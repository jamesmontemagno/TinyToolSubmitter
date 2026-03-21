import { execSync } from "child_process";
import * as fs from "fs";
import * as path from "path";

const README_NAMES = [
    "README.md", "readme.md", "Readme.md",
    "README", "readme",
    "README.rst", "readme.rst",
    "README.txt", "readme.txt",
];

const WEBSITE_HINT_KEYWORDS = ["demo", "try", "website", "live", "app", "preview", "play"];

/**
 * Finds a README file in the given directory.
 */
export function findReadme(directory: string): string | null {
    for (const name of README_NAMES) {
        const filePath = path.join(directory, name);
        if (fs.existsSync(filePath)) {
            return filePath;
        }
    }
    return null;
}

/**
 * Detects the GitHub repository URL from git remotes.
 */
export function detectGitHubUrl(directory: string): string | null {
    try {
        const output = execSync("git remote get-url origin", {
            cwd: directory,
            encoding: "utf-8",
            stdio: ["pipe", "pipe", "pipe"],
        }).trim();

        if (!output) return null;
        return normalizeGitUrl(output);
    } catch {
        return null;
    }
}

/**
 * Detects the license type from the repo.
 */
export function detectLicense(directory: string): string | null {
    const licenseFiles = ["LICENSE", "LICENSE.md", "LICENSE.txt", "LICENCE", "LICENCE.md", "LICENCE.txt"];

    for (const name of licenseFiles) {
        const filePath = path.join(directory, name);
        if (!fs.existsSync(filePath)) continue;

        const content = fs.readFileSync(filePath, "utf-8");
        const detected = detectLicenseFromText(content);
        if (detected) return detected;
        return "Unknown";
    }

    // Fallback: check README
    const readmePath = findReadme(directory);
    if (readmePath && fs.existsSync(readmePath)) {
        const content = fs.readFileSync(readmePath, "utf-8");
        const detected = detectLicenseFromText(content);
        if (detected) return detected;
    }

    return null;
}

/**
 * Detects a likely public website/demo URL from README markdown.
 */
export function detectWebsiteOrDemoUrl(readmeContent: string, githubUrl?: string | null): string | null {
    if (!readmeContent?.trim()) return null;

    const githubRepo = tryParseUrl(githubUrl ?? undefined);
    const candidates: Array<{ url: string; score: number; order: number }> = [];
    const seen = new Set<string>();
    let order = 0;

    const markdownLinkRegex = /\[(?<label>[^\]]+)\]\((?<url>https?:\/\/[^\s)]+)\)/gi;
    for (const match of readmeContent.matchAll(markdownLinkRegex)) {
        const label = match.groups?.label;
        const rawUrl = match.groups?.url;
        if (!rawUrl) continue;

        const candidate = buildWebsiteCandidate(rawUrl, label, githubRepo);
        if (candidate && !seen.has(candidate.url)) {
            seen.add(candidate.url);
            candidates.push({ url: candidate.url, score: candidate.score + 2, order: order++ });
        }
    }

    const bareUrlRegex = /https?:\/\/[^\s<>()]+/gi;
    for (const match of readmeContent.matchAll(bareUrlRegex)) {
        const rawUrl = match[0];
        const candidate = buildWebsiteCandidate(rawUrl, undefined, githubRepo);
        if (candidate && !seen.has(candidate.url)) {
            seen.add(candidate.url);
            candidates.push({ url: candidate.url, score: candidate.score, order: order++ });
        }
    }

    candidates.sort((a, b) => b.score - a.score || a.order - b.order);
    return candidates[0]?.url ?? null;
}

function detectLicenseFromText(content: string): string | null {
    const lower = content.toLowerCase();

    if (lower.includes("mit license") || lower.includes("licensed under the mit") || lower.includes("license: mit"))
        return "MIT";
    if (lower.includes("apache license") || lower.includes("apache-2.0") || lower.includes("license: apache"))
        return "Apache-2.0";
    if (lower.includes("gnu general public license") || lower.includes("gpl-3.0") || lower.includes("gplv3"))
        return "GPL-3.0";
    if (lower.includes("gpl-2.0") || lower.includes("gplv2"))
        return "GPL-2.0";
    if (lower.includes("bsd 2-clause"))
        return "BSD-2-Clause";
    if (lower.includes("bsd 3-clause"))
        return "BSD-3-Clause";
    if (lower.includes("mozilla public license") || lower.includes("mpl-2.0"))
        return "MPL-2.0";
    if (lower.includes("isc license") || lower.includes("license: isc"))
        return "ISC";
    if (lower.includes("the unlicense") || lower.includes("unlicense"))
        return "Unlicense";

    return null;
}

function buildWebsiteCandidate(
    rawUrl: string,
    label: string | undefined,
    githubRepo: URL | null,
): { url: string; score: number } | null {
    const cleaned = rawUrl.trim().replace(/[.,;:!?\)\]"']+$/g, "");
    const parsed = tryParseUrl(cleaned);
    if (!parsed) return null;

    const protocol = parsed.protocol.toLowerCase();
    if (protocol !== "https:" && protocol !== "http:") return null;

    const host = parsed.hostname.toLowerCase();
    if (host === "github.com" || host === "raw.githubusercontent.com" || host === "img.shields.io") {
        return null;
    }

    const lowerPath = parsed.pathname.toLowerCase();
    if (/(\.png|\.jpe?g|\.gif|\.svg|\.webp)$/.test(lowerPath)) {
        return null;
    }

    if (
        githubRepo &&
        parsed.hostname.toLowerCase() === githubRepo.hostname.toLowerCase() &&
        parsed.pathname.toLowerCase().startsWith(githubRepo.pathname.toLowerCase())
    ) {
        return null;
    }

    let score = 0;
    const hintText = `${label ?? ""} ${parsed.hostname} ${parsed.pathname}`.toLowerCase();
    for (const keyword of WEBSITE_HINT_KEYWORDS) {
        if (hintText.includes(keyword)) score += 3;
    }

    if (/(vercel\.app|netlify\.app|pages\.dev|github\.io)$/.test(host)) {
        score += 2;
    }

    return { url: parsed.href.replace(/\/$/, ""), score };
}

function tryParseUrl(value?: string): URL | null {
    if (!value?.trim()) return null;
    try {
        return new URL(value);
    } catch {
        return null;
    }
}

/**
 * Detects the primary programming language by scanning file extensions.
 */
export function detectLanguage(directory: string): string | null {
    const extensionMap: Record<string, string> = {
        ".cs": "C#",
        ".fs": "F#",
        ".vb": "VB.NET",
        ".py": "Python",
        ".rs": "Rust",
        ".go": "Go",
        ".ts": "TypeScript",
        ".js": "JavaScript",
        ".java": "Java",
        ".kt": "Kotlin",
        ".swift": "Swift",
        ".rb": "Ruby",
        ".cpp": "C++",
        ".c": "C",
        ".zig": "Zig",
        ".lua": "Lua",
        ".php": "PHP",
        ".dart": "Dart",
        ".ex": "Elixir",
        ".exs": "Elixir",
        ".hs": "Haskell",
        ".scala": "Scala",
        ".r": "R",
        ".jl": "Julia",
        ".pl": "Perl",
        ".sh": "Shell",
        ".ps1": "PowerShell",
    };

    const skipDirs = new Set([".git", "node_modules", "bin", "obj", "vendor", "target", "dist", "__pycache__"]);
    const counts: Record<string, number> = {};

    function walk(dir: string, depth: number): void {
        if (depth > 5) return;
        try {
            const entries = fs.readdirSync(dir, { withFileTypes: true });
            for (const entry of entries) {
                if (entry.isDirectory()) {
                    if (!skipDirs.has(entry.name)) {
                        walk(path.join(dir, entry.name), depth + 1);
                    }
                } else if (entry.isFile()) {
                    const ext = path.extname(entry.name).toLowerCase();
                    const lang = extensionMap[ext];
                    if (lang) {
                        counts[lang] = (counts[lang] || 0) + 1;
                    }
                }
            }
        } catch {
            // Ignore enumeration errors
        }
    }

    walk(directory, 0);

    const entries = Object.entries(counts);
    if (entries.length === 0) return null;

    entries.sort((a, b) => b[1] - a[1]);
    return entries[0][0];
}

/**
 * Detects the git user name from git config.
 */
export function detectGitUserName(directory: string): string | null {
    return runGit(directory, "config user.name");
}

/**
 * Detects the GitHub username from the remote URL.
 */
export function detectGitHubUsername(githubUrl: string | null): string | null {
    if (!githubUrl) return null;
    try {
        const url = new URL(githubUrl);
        const segments = url.pathname.replace(/^\//, "").split("/");
        return segments.length >= 1 ? segments[0] : null;
    } catch {
        return null;
    }
}

function normalizeGitUrl(url: string): string {
    // SSH: git@github.com:owner/repo.git -> https://github.com/owner/repo
    if (url.startsWith("git@github.com:")) {
        let repoPath = url.slice("git@github.com:".length).replace(/\/$/, "");
        if (repoPath.endsWith(".git")) {
            repoPath = repoPath.slice(0, -4);
        }
        return `https://github.com/${repoPath}`;
    }

    // HTTPS
    if (url.toLowerCase().startsWith("https://github.com/")) {
        let trimmed = url.replace(/\/$/, "");
        if (trimmed.endsWith(".git")) {
            trimmed = trimmed.slice(0, -4);
        }
        return trimmed;
    }

    return url;
}

function runGit(directory: string, args: string): string | null {
    try {
        const output = execSync(`git ${args}`, {
            cwd: directory,
            encoding: "utf-8",
            stdio: ["pipe", "pipe", "pipe"],
        }).trim();

        return output || null;
    } catch {
        return null;
    }
}
