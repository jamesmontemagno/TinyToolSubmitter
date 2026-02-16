import type { CopilotSession } from "@github/copilot-sdk";
import type { ToolMetadata } from "./issue-url-builder.js";

/**
 * Uses the Copilot SDK to analyze a README and extract tool metadata.
 */
export async function generateMetadata(
    session: CopilotSession,
    readmeContent: string,
    repoName: string,
): Promise<ToolMetadata | null> {
    const prompt = `You extract metadata from a GitHub README for Tiny Tool Town.

Return ONLY valid JSON (no markdown, no code fences) with this exact schema:
{
    "name": "string",
    "tagline": "string <= 100 chars",
    "description": "string, 2-4 sentences",
    "tags": ["lowercase-tag", "another-tag", "3-6 tags total"]
}

Rules:
- Name must match the tool name from README; do not invent a different product.
- Tagline must be concise and clear.
- Description should be enthusiastic but honest.
- Tags must be lowercase, relevant, and contain 3-6 entries.

Repository name: ${repoName}

README content:
${readmeContent}`;

    const raw = await sendPrompt(session, prompt);
    let metadata = parseResponse(raw);
    if (!metadata) return null;

    normalizeGeneratedFields(metadata);

    const issues = getGeneratedFieldIssues(metadata);
    if (issues.length > 0) {
        const repairPrompt = `Fix this metadata JSON so it satisfies ALL constraints.
Return ONLY valid JSON with keys: name, tagline, description, tags.

Constraints:
- name: non-empty
- tagline: non-empty and <= 100 characters
- description: non-empty, 2-4 sentences
- tags: array of 3-6 lowercase, relevant tags

Issues found:
${issues.join("; ")}

Current JSON:
${JSON.stringify({
            name: metadata.name,
            tagline: metadata.tagline,
            description: metadata.description,
            tags: metadata.tags.split(",").map((t) => t.trim()).filter(Boolean),
        })}`;

        const repairedRaw = await sendPrompt(session, repairPrompt);
        const repaired = parseResponse(repairedRaw);
        if (repaired) {
            normalizeGeneratedFields(repaired);
            if (getGeneratedFieldIssues(repaired).length === 0) {
                metadata = repaired;
            }
        }
    }

    applyFallbacks(metadata, repoName);
    normalizeGeneratedFields(metadata);

    return metadata;
}

async function sendPrompt(session: CopilotSession, prompt: string): Promise<string> {
    const response = await session.sendAndWait({ prompt });
    return response?.data?.content?.trim() ?? "";
}

function parseResponse(raw: string): ToolMetadata | null {
    if (!raw) return null;

    try {
        const json = extractJsonObject(raw);
        const parsed = JSON.parse(json);

        let tagsValue = "";
        if (Array.isArray(parsed.tags)) {
            tagsValue = parsed.tags
                .filter((t: unknown) => typeof t === "string" && t.trim())
                .join(", ");
        } else if (typeof parsed.tags === "string") {
            tagsValue = parsed.tags;
        }

        return {
            name: (parsed.name ?? "").trim(),
            tagline: (parsed.tagline ?? "").trim(),
            description: (parsed.description ?? "").trim(),
            githubUrl: "",
            author: "",
            authorGitHub: "",
            tags: tagsValue,
        };
    } catch {
        return null;
    }
}

function extractJsonObject(raw: string): string {
    const start = raw.indexOf("{");
    const end = raw.lastIndexOf("}");
    return start >= 0 && end > start ? raw.slice(start, end + 1) : raw;
}

function normalizeGeneratedFields(metadata: ToolMetadata): void {
    metadata.name = metadata.name.trim();
    metadata.tagline = metadata.tagline.trim();
    metadata.description = metadata.description.trim();

    const tags = metadata.tags
        .split(",")
        .map((t) => t.trim().toLowerCase())
        .filter(Boolean);

    metadata.tags = [...new Set(tags)].join(", ");
}

function getGeneratedFieldIssues(metadata: ToolMetadata): string[] {
    const issues: string[] = [];

    if (!metadata.name) issues.push("name is missing");
    if (!metadata.tagline) issues.push("tagline is missing");
    else if (metadata.tagline.length > 100) issues.push("tagline exceeds 100 characters");
    if (!metadata.description) issues.push("description is missing");

    const tags = metadata.tags.split(",").map((t) => t.trim()).filter(Boolean);
    if (tags.length < 3 || tags.length > 6) issues.push("tags must contain 3 to 6 entries");
    if (tags.some((t) => t !== t.toLowerCase())) issues.push("tags must be lowercase");

    return issues;
}

function applyFallbacks(metadata: ToolMetadata, repoName: string): void {
    if (!metadata.name) metadata.name = repoName;
    if (!metadata.tagline) metadata.tagline = `A tiny tool called ${repoName}.`;
    if (metadata.tagline.length > 100) metadata.tagline = metadata.tagline.slice(0, 100).trimEnd();
    if (!metadata.description) metadata.description = `${repoName} is a helpful open source tool from this repository.`;
    if (!metadata.tags) metadata.tags = "cli, developer-tools, productivity";
}
