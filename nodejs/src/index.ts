#!/usr/bin/env node

import * as fs from "fs";
import * as path from "path";
import { execSync } from "child_process";
import { CopilotClient } from "@github/copilot-sdk";
import { select, confirm, input } from "@inquirer/prompts";
import {
    findReadme,
    detectGitHubUrl,
    detectLicense,
    detectLanguage,
    detectGitUserName,
    detectGitHubUsername,
} from "./repo-detector.js";
import { generateMetadata } from "./metadata-generator.js";
import { buildIssueUrl } from "./issue-url-builder.js";
import type { ToolMetadata } from "./issue-url-builder.js";

// --- Parse CLI flags ---
const args = process.argv.slice(2);
const flagArgs = args.filter((a) => a.startsWith("-"));
const positionalArgs = args.filter((a) => !a.startsWith("-"));

const headless = flagArgs.includes("--headless");
const verbose = flagArgs.includes("--verbose");

function getFlagValue(flag: string): string | undefined {
    const idx = args.indexOf(flag);
    return idx >= 0 && idx < args.length - 1 ? args[idx + 1] : undefined;
}

const flagReadmePath = getFlagValue("--readme");
const flagModelName = getFlagValue("--model");
const flagCliPath = getFlagValue("--cli-path");

console.log("\x1b[36m╔══════════════════════════════════════════════════════════════╗");
console.log("║       Tiny Tool Town — Submission Helper 🏘️📋                 ║");
console.log("║       Powered by GitHub Copilot SDK (Node.js)                 ║");
console.log("╚══════════════════════════════════════════════════════════════╝\x1b[0m");
console.log();

// Resolve the repo directory
const repoDir = positionalArgs.length > 0
    ? path.resolve(positionalArgs[0])
    : process.cwd();

if (!fs.existsSync(repoDir) || !fs.statSync(repoDir).isDirectory()) {
    console.error(`\x1b[31m❌ Directory not found: ${repoDir}\x1b[0m`);
    process.exit(1);
}

console.log(`📂 Repository: ${repoDir}`);

// --- Step 1: Find README ---
let readmePath: string;

if (flagReadmePath) {
    readmePath = path.resolve(flagReadmePath);
} else {
    const detected = findReadme(repoDir);
    if (detected) {
        console.log(`\x1b[32m📄 Found README: ${path.relative(repoDir, detected)}\x1b[0m`);

        if (headless) {
            readmePath = detected;
        } else {
            const useDetected = await confirm({ message: "Use this README?", default: true });
            readmePath = useDetected ? detected : await promptForReadmePath(repoDir);
        }
    } else if (!headless) {
        console.log("\x1b[33m📄 No README found automatically. Let's pick one.\x1b[0m");
        readmePath = await promptForReadmePath(repoDir);
    } else {
        console.error("\x1b[31m❌ No README found and running in headless mode. Use --readme <path>.\x1b[0m");
        process.exit(1);
    }
}

if (!fs.existsSync(readmePath)) {
    console.error(`\x1b[31m❌ README not found: ${readmePath}\x1b[0m`);
    process.exit(1);
}

let readmeContent = fs.readFileSync(readmePath, "utf-8");
if (readmeContent.length > 4000) {
    readmeContent = readmeContent.slice(0, 4000) + "\n\n[truncated]";
}

console.log(`📏 README length: ${readmeContent.length} chars`);

// --- Step 2: Detect repo metadata ---
console.log("\n🔍 Detecting repository metadata...");

const githubUrl = detectGitHubUrl(repoDir);
const license = detectLicense(repoDir);
const language = detectLanguage(repoDir);
const authorName = detectGitUserName(repoDir);
const authorGitHub = detectGitHubUsername(githubUrl);

let repoName: string;
if (githubUrl) {
    try {
        const url = new URL(githubUrl);
        const segments = url.pathname.replace(/^\//, "").split("/");
        repoName = segments[segments.length - 1] || path.basename(repoDir);
    } catch {
        repoName = path.basename(repoDir);
    }
} else {
    repoName = path.basename(repoDir);
}

console.log("\x1b[90m");
if (githubUrl) console.log(`   GitHub URL: ${githubUrl}`);
if (license) console.log(`   License:    ${license}`);
if (language) console.log(`   Language:   ${language}`);
if (authorName) console.log(`   Author:     ${authorName}`);
if (authorGitHub) console.log(`   Username:   ${authorGitHub}`);
console.log("\x1b[0m");

// --- Step 3: Use Copilot to generate metadata ---
console.log("🤖 Starting Copilot to analyze your README...");

let client: CopilotClient | null = null;

try {
    const cliPath = flagCliPath || resolveCliPath();

    if (verbose) {
        console.log(`\x1b[90m   Copilot CLI path: ${cliPath}\x1b[0m`);
    }

    // Check CLI availability
    try {
        execSync(`${cliPath} --version`, { encoding: "utf-8", stdio: ["pipe", "pipe", "pipe"] });
    } catch {
        console.error(`\x1b[31m❌ Failed to execute GitHub Copilot CLI at '${cliPath}'.\x1b[0m`);
        console.log("   Install Copilot CLI and ensure `copilot` is available on your PATH.");
        process.exit(1);
    }

    client = new CopilotClient({
        cliPath,
        autoStart: false,
    });

    try {
        await client.start();
    } catch (ex: unknown) {
        const msg = ex instanceof Error ? ex.message : String(ex);
        if (msg.includes("not found") || msg.includes("No such file")) {
            console.error(`\x1b[31m❌ GitHub Copilot CLI was not found at '${cliPath}'.\x1b[0m`);
            console.log("   Install Copilot CLI and ensure `copilot` is available on your PATH.");
        } else {
            console.error(`\x1b[31m❌ Could not connect to GitHub Copilot CLI.\x1b[0m`);
            console.log("   Try: `copilot --version` and `copilot auth status`.");
            if (verbose) console.log(`   Error: ${msg}`);
        }
        process.exit(1);
    }

    console.log("\x1b[32m✅ Copilot client started\x1b[0m");

    // Model selection
    let selectedModel: string;
    if (flagModelName) {
        selectedModel = flagModelName;
    } else if (headless) {
        selectedModel = "gpt-4.1";
    } else {
        console.log("\x1b[90m   Fetching available models...\x1b[0m");
        try {
            const models = await client.listModels();
            if (models && models.length > 0) {
                const defaultModel = models.find((m: { id: string }) =>
                    m.id.toLowerCase() === "gpt-4.1"
                ) || models[0];

                const choices = models.map((m: { id: string; name: string }) => ({
                    name: m.id === defaultModel.id ? `${m.name} (recommended)` : m.name,
                    value: m.id,
                }));

                // Move recommended to top
                choices.sort((a: { name: string }, b: { name: string }) =>
                    a.name.includes("(recommended)") ? -1 : b.name.includes("(recommended)") ? 1 : 0
                );

                selectedModel = await select({
                    message: "🤖 Select a model",
                    choices,
                });
            } else {
                selectedModel = "gpt-4.1";
            }
        } catch {
            selectedModel = "gpt-4.1";
        }
    }

    console.log(`🤖 Using model: ${selectedModel}\n`);

    const session = await client.createSession({
        model: selectedModel,
        streaming: true,
        infiniteSessions: { enabled: false },
    });

    console.log("\x1b[90m   Analyzing README with AI...\x1b[0m");
    const metadata = await generateMetadata(session, readmeContent, repoName);

    if (!metadata) {
        console.error("\x1b[31m❌ Failed to generate metadata from README.\x1b[0m");
        process.exit(1);
    }

    // Fill in detected values
    metadata.githubUrl = githubUrl ?? "";
    metadata.language = language ?? undefined;
    metadata.license = license ?? undefined;
    metadata.author = authorName ?? "";
    metadata.authorGitHub = authorGitHub ?? "";

    // --- Step 4: Review metadata ---
    console.log("\n\x1b[36m📋 Generated Submission Metadata\x1b[0m");
    printMetadataTable(metadata);

    if (!headless) {
        let editing = true;
        while (editing) {
            const fieldChoice = await select({
                message: "✏️  Edit metadata fields (choose Done to continue)",
                choices: [
                    { name: "Done", value: "done" },
                    { name: "name", value: "name" },
                    { name: "tagline", value: "tagline" },
                    { name: "description", value: "description" },
                    { name: "github_url", value: "github_url" },
                    { name: "website", value: "website" },
                    { name: "author", value: "author" },
                    { name: "author_github", value: "author_github" },
                    { name: "tags", value: "tags" },
                    { name: "language", value: "language" },
                    { name: "license", value: "license" },
                ],
            });

            if (fieldChoice === "done") {
                editing = false;
                break;
            }

            const currentValue = getFieldValue(metadata, fieldChoice);
            const newValue = await input({
                message: `New value for ${fieldChoice}`,
                default: currentValue,
            });

            setFieldValue(metadata, fieldChoice, newValue.trim());
            console.log(`\x1b[32m✅ Updated ${fieldChoice}\x1b[0m`);
            printMetadataTable(metadata);
        }
    }

    // --- Step 5: Build and open the URL ---
    const issueUrl = buildIssueUrl(metadata);

    console.log("\n\x1b[36m═══════════════════════════════════════════════");
    console.log("  🚀 Ready to Submit!");
    console.log("═══════════════════════════════════════════════\x1b[0m");

    console.log("\n📋 Open this URL to submit your tool to Tiny Tool Town:\n");
    console.log(`\x1b[32m${issueUrl}\x1b[0m`);

    // Try to open the URL in the default browser
    if (!headless) {
        const openInBrowser = await confirm({ message: "🌐 Open in browser?", default: true });
        if (openInBrowser) {
            openUrl(issueUrl);
            console.log("\x1b[32m✅ Opened in browser!\x1b[0m");
        }
    } else {
        openUrl(issueUrl);
    }

    console.log("\n🏘️  Thanks for submitting to Tiny Tool Town!");

    await session.destroy();
    await client.stop();
    process.exit(0);
} catch (ex: unknown) {
    if (ex instanceof Error && ex.message.includes("cancelled")) {
        console.log("\n\x1b[33m⚠️ Cancelled by user.\x1b[0m");
        process.exit(0);
    }

    const msg = ex instanceof Error ? ex.message : String(ex);
    console.error(`\n\x1b[31m❌ Fatal error: ${msg}\x1b[0m`);
    process.exit(1);
} finally {
    if (client) {
        try {
            await client.stop();
        } catch {
            // ignore
        }
    }
    console.log("\n🛑 Done.");
}

// --- Helpers ---

function printMetadataTable(metadata: ToolMetadata): void {
    const rows: [string, string][] = [
        ["Tool Name", metadata.name],
        ["Tagline", metadata.tagline],
        ["Description", metadata.description],
        ["GitHub URL", metadata.githubUrl],
        ["Website", metadata.websiteUrl ?? "(none)"],
        ["Author", metadata.author],
        ["GitHub User", metadata.authorGitHub],
        ["Tags", metadata.tags],
        ["Language", metadata.language ?? "(not detected)"],
        ["License", metadata.license ?? "(not detected)"],
    ];

    console.log("┌──────────────┬──────────────────────────────────────────────────┐");
    for (const [field, value] of rows) {
        const paddedField = field.padEnd(12);
        const truncatedValue = value.length > 48 ? value.slice(0, 45) + "..." : value;
        const paddedValue = truncatedValue.padEnd(48);
        console.log(`│ \x1b[33m${paddedField}\x1b[0m │ ${paddedValue} │`);
    }
    console.log("└──────────────┴──────────────────────────────────────────────────┘");
}

function openUrl(url: string): void {
    try {
        const platform = process.platform;
        if (platform === "darwin") {
            execSync(`open "${url}"`, { stdio: "ignore" });
        } else if (platform === "win32") {
            execSync(`cmd /c start "" "${url}"`, { stdio: "ignore" });
        } else {
            execSync(`xdg-open "${url}"`, { stdio: "ignore" });
        }
    } catch {
        // Silently fail — URL is already printed
    }
}

async function promptForReadmePath(repoDir: string): Promise<string> {
    let currentDir = repoDir;

    while (true) {
        const relativeCurrentDir = path.relative(repoDir, currentDir);
        const currentDirDisplay = relativeCurrentDir ? relativeCurrentDir : ".";

        let entries: fs.Dirent[] = [];
        try {
            entries = fs.readdirSync(currentDir, { withFileTypes: true });
        } catch {
            entries = [];
        }

        const readmeCandidates = entries
            .filter((entry) => entry.isFile() && isReadmeCandidate(entry.name))
            .map((entry) => entry.name)
            .sort((a, b) => a.localeCompare(b));

        const subDirectories = entries
            .filter((entry) => entry.isDirectory() && entry.name !== ".git")
            .map((entry) => entry.name)
            .sort((a, b) => a.localeCompare(b));

        const choices: Array<{ name: string; value: string }> = [
            { name: "✍ Enter full path manually", value: "manual" },
            ...readmeCandidates.map((fileName) => ({
                name: `📄 ${fileName}`,
                value: `file:${fileName}`,
            })),
            ...subDirectories.map((dirName) => ({
                name: `📁 ${dirName}`,
                value: `dir:${dirName}`,
            })),
        ];

        const parentDirectory = path.dirname(currentDir);
        if (parentDirectory !== currentDir) {
            choices.push({ name: "↩ Go up", value: "up" });
        }
        choices.push({ name: "❌ Cancel", value: "cancel" });

        const choice = await select({
            message: `Select README in ${currentDirDisplay} or browse:`,
            choices,
        });

        if (choice === "cancel") {
            throw new Error("README selection cancelled.");
        }

        if (choice === "up") {
            if (parentDirectory !== currentDir) {
                currentDir = parentDirectory;
            }
            continue;
        }

        if (choice === "manual") {
            const selectedPath = await input({
                message: "README path",
                default: path.join(repoDir, "README.md"),
                validate: (val) => fs.existsSync(val) || "README file does not exist",
            });

            return path.resolve(selectedPath);
        }

        if (choice.startsWith("dir:")) {
            const dirName = choice.slice("dir:".length);
            currentDir = path.join(currentDir, dirName);
            continue;
        }

        if (choice.startsWith("file:")) {
            const fileName = choice.slice("file:".length);
            return path.join(currentDir, fileName);
        }
    }
}

function isReadmeCandidate(fileName: string): boolean {
    const lower = fileName.toLowerCase();
    return (
        lower === "readme.md" ||
        lower === "readme" ||
        lower === "readme.rst" ||
        lower === "readme.txt" ||
        lower.startsWith("readme.")
    );
}

function resolveCliPath(): string {
    try {
        const cmd = process.platform === "win32" ? "where" : "which";
        const result = execSync(`${cmd} copilot`, { encoding: "utf-8", stdio: ["pipe", "pipe", "pipe"] }).trim();
        const first = result.split("\n")[0]?.trim();
        return first || "copilot";
    } catch {
        return "copilot";
    }
}

function getFieldValue(metadata: ToolMetadata, field: string): string {
    switch (field) {
        case "name": return metadata.name;
        case "tagline": return metadata.tagline;
        case "description": return metadata.description;
        case "github_url": return metadata.githubUrl;
        case "website": return metadata.websiteUrl ?? "";
        case "author": return metadata.author;
        case "author_github": return metadata.authorGitHub;
        case "tags": return metadata.tags;
        case "language": return metadata.language ?? "";
        case "license": return metadata.license ?? "";
        default: return "";
    }
}

function setFieldValue(metadata: ToolMetadata, field: string, value: string): void {
    switch (field) {
        case "name": metadata.name = value; break;
        case "tagline": metadata.tagline = value; break;
        case "description": metadata.description = value; break;
        case "github_url": metadata.githubUrl = value; break;
        case "website": metadata.websiteUrl = value; break;
        case "author": metadata.author = value; break;
        case "author_github": metadata.authorGitHub = value; break;
        case "tags": metadata.tags = value; break;
        case "language": metadata.language = value; break;
        case "license": metadata.license = value; break;
    }
}
