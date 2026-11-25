#!/usr/bin/env node

import * as fs from "fs";
import * as path from "path";
import { execSync } from "child_process";
import { CopilotClient, RuntimeConnection, approveAll } from "@microsoft/copilot-sdk";
import type { CopilotSession } from "@microsoft/copilot-sdk";
import { select, confirm, input } from "@inquirer/prompts";
import {
    findReadme,
    detectGitHubUrl,
    detectLicense,
    detectLanguage,
    detectGitUserName,
    detectGitHubUsername,
    detectWebsiteOrDemoUrl,
} from "./repo-detector.js";
import { generateMetadata } from "./metadata-generator.js";
import { buildIssueUrl } from "./issue-url-builder.js";
import type { ToolMetadata } from "./issue-url-builder.js";

const THEME_OPTIONS = [
    "None (site default)",
    "terminal",
    "neon",
    "minimal",
    "pastel",
    "matrix",
    "sunset",
    "ocean",
    "forest",
    "candy",
    "synthwave",
    "newspaper",
    "retro",
] as const;

const THEME_PALETTES: Array<{ name: string; colors: [string, string, string, string] }> = [
    { name: "terminal", colors: ["#0A0A0A", "#111111", "#00FF41", "#39FF14"] },
    { name: "neon", colors: ["#0D0221", "#150535", "#FF2A6D", "#05D9E8"] },
    { name: "minimal", colors: ["#FAFAFA", "#FFFFFF", "#333333", "#555555"] },
    { name: "pastel", colors: ["#FEF6F9", "#FFFFFF", "#E8829A", "#82B4E8"] },
    { name: "matrix", colors: ["#000800", "#001200", "#00FF00", "#00CC00"] },
    { name: "sunset", colors: ["#1A0A2E", "#251244", "#FF6B35", "#FF9F1C"] },
    { name: "ocean", colors: ["#0A1628", "#0F2035", "#00B4D8", "#0096C7"] },
    { name: "forest", colors: ["#1A2416", "#243020", "#82B74B", "#C4A35A"] },
    { name: "candy", colors: ["#FF69B4", "#FF91CB", "#FFFF00", "#00FFCC"] },
    { name: "synthwave", colors: ["#1A1033", "#241546", "#FF71CE", "#01CDFE"] },
    { name: "newspaper", colors: ["#F2EFE6", "#FFFDF7", "#B91C1C", "#1A1A1A"] },
    { name: "retro", colors: ["#1A1200", "#2A1F00", "#FFB000", "#FF8C00"] },
];

const RECOMMENDED_MODEL_ID = "claude-haiku-4.5";

class CliExitError extends Error {
    constructor(readonly exitCode: number) {
        super();
    }
}

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
const flagThemeName = getFlagValue("--theme");

console.log("\x1b[36m╔══════════════════════════════════════════════════════════════╗");
console.log("║       Tiny Tool Town — Submission Helper 🏘️📋                 ║");
console.log("║       Powered by Microsoft Copilot SDK (Node.js)             ║");
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

const fullReadmeContent = fs.readFileSync(readmePath, "utf-8");
let readmeContent = fullReadmeContent;
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
const websiteUrl = detectWebsiteOrDemoUrl(fullReadmeContent, githubUrl);

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
if (websiteUrl) console.log(`   Website:    ${websiteUrl}`);
console.log("\x1b[0m");

// --- Step 3: Use Copilot to generate metadata ---
console.log("🤖 Starting Copilot to analyze your README...");

let client: CopilotClient | null = null;
let session: CopilotSession | null = null;

try {
    const cliLaunch = resolveCliLaunch(flagCliPath);

    if (verbose) {
        console.log(`\x1b[90m   Copilot runtime: ${cliLaunch.displayPath} (${cliLaunch.source})\x1b[0m`);
    }

    client = new CopilotClient({
        mode: "coding-agent",
        runtime: cliLaunch.cliPath
            ? RuntimeConnection.forSpawnedStdio({
                path: cliLaunch.cliPath,
                args: cliLaunch.cliArgs,
            })
            : undefined,
    });

    try {
        await client.start();
    } catch (ex: unknown) {
        const msg = ex instanceof Error ? ex.message : String(ex);
        if (msg.includes("not found") || msg.includes("No such file")) {
            console.error(`\x1b[31m❌ Copilot runtime was not found at '${cliLaunch.displayPath}'.\x1b[0m`);
            printCliResolutionGuidance(cliLaunch.source);
        } else {
            console.error("\x1b[31m❌ Could not start the Copilot runtime.\x1b[0m");
            if (cliLaunch.cliPath) {
                console.log("   Try: `copilot --version` and `copilot auth status`.");
            }
            if (verbose) console.log(`   Error: ${msg}`);
        }
        throw new CliExitError(1);
    }

    console.log("\x1b[32m✅ Copilot client started\x1b[0m");

    // Model selection
    let selectedModel: string;
    if (flagModelName) {
        selectedModel = flagModelName;
    } else if (headless) {
        selectedModel = RECOMMENDED_MODEL_ID;
    } else {
        console.log("\x1b[90m   Fetching available models...\x1b[0m");
        try {
            const models = await client.listModels();
            if (models && models.length > 0) {
                const defaultModel = models.find((m: { id: string }) =>
                    m.id.toLowerCase() === RECOMMENDED_MODEL_ID
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
                selectedModel = RECOMMENDED_MODEL_ID;
            }
        } catch {
            selectedModel = RECOMMENDED_MODEL_ID;
        }
    }

    console.log(`🤖 Using model: ${selectedModel}\n`);

    session = await client.createSession({
        model: selectedModel,
        permissions: {
            onRequest: approveAll,
        },
    });

    console.log("\x1b[90m   Analyzing README with AI...\x1b[0m");
    const metadata = await generateMetadata(session, readmeContent, repoName);

    if (!metadata) {
        console.error("\x1b[31m❌ Failed to generate metadata from README.\x1b[0m");
        throw new CliExitError(1);
    }

    // Fill in detected values
    metadata.githubUrl = githubUrl ?? "";
    metadata.language = language ?? undefined;
    metadata.license = license ?? undefined;
    metadata.author = authorName ?? "";
    metadata.authorGitHub = authorGitHub ?? "";
    metadata.websiteUrl ??= websiteUrl ?? undefined;
    metadata.theme = normalizeThemeSelection(metadata.theme);

    if (flagThemeName) {
        const parsedTheme = parseThemeFlagValue(flagThemeName);
        if (parsedTheme === undefined && !isNoThemeFlagValue(flagThemeName)) {
            console.error(`\x1b[31m❌ Invalid theme '${flagThemeName}'.\x1b[0m`);
            console.log(`   Valid values: ${getThemeFlagValues().join(", ")}`);
            throw new CliExitError(1);
        }

        metadata.theme = parsedTheme;
    }

    // --- Step 4: Review metadata ---
    console.log("\n\x1b[36m📋 Generated Submission Metadata\x1b[0m");
    printMetadataTable(metadata);

    if (!headless) {
        displayThemePaletteTable();

        const selectThemeNow = await confirm({ message: "Select a page theme now?", default: false });
        if (selectThemeNow) {
            metadata.theme = await promptForTheme(metadata.theme);
            console.log(`\x1b[32m✅ Updated theme to ${displayTheme(metadata.theme)}\x1b[0m`);
            printMetadataTable(metadata);
        }

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
                    { name: "theme", value: "theme" },
                ],
            });

            if (fieldChoice === "done") {
                editing = false;
                break;
            }

            if (fieldChoice === "theme") {
                displayThemePaletteTable();
                metadata.theme = await promptForTheme(metadata.theme);
                console.log(`\x1b[32m✅ Updated theme to ${displayTheme(metadata.theme)}\x1b[0m`);
                printMetadataTable(metadata);
                continue;
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
} catch (ex: unknown) {
    if (ex instanceof CliExitError) {
        process.exitCode = ex.exitCode;
    } else if (ex instanceof Error && ex.message.includes("cancelled")) {
        console.log("\n\x1b[33m⚠️ Cancelled by user.\x1b[0m");
    } else {
        const msg = ex instanceof Error ? ex.message : String(ex);
        console.error(`\n\x1b[31m❌ Fatal error: ${msg}\x1b[0m`);
        process.exitCode = 1;
    }
} finally {
    if (session) {
        try {
            await session.disconnect();
        } catch (ex: unknown) {
            if (verbose) {
                console.error(`   Session cleanup failed: ${ex instanceof Error ? ex.message : String(ex)}`);
            }
        }
    }

    if (client) {
        try {
            await client.stop();
        } catch (ex: unknown) {
            if (verbose) {
                console.error(`   Client cleanup failed: ${ex instanceof Error ? ex.message : String(ex)}`);
            }
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
        ["Theme", displayTheme(metadata.theme)],
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

type CliLaunchConfig = {
    cliPath?: string;
    cliArgs?: string[];
    displayPath: string;
    source: string;
};

function resolveCliLaunch(explicitCliPath?: string): CliLaunchConfig {
    if (explicitCliPath?.trim()) {
        return createCliLaunchConfig(explicitCliPath.trim(), "argument");
    }

    if (process.env.COPILOT_CLI_PATH?.trim()) {
        return createCliLaunchConfig(process.env.COPILOT_CLI_PATH.trim(), "environment");
    }

    return {
        displayPath: "SDK-managed bundled runtime",
        source: "default",
    };
}

function createCliLaunchConfig(discoveredPath: string, source: string): CliLaunchConfig {
    if (process.platform === "win32") {
        const extension = path.extname(discoveredPath).toLowerCase();
        if (extension === ".cmd" || extension === ".bat") {
            return {
                cliPath: process.env.ComSpec || "cmd.exe",
                cliArgs: ["/d", "/s", "/c", `"${discoveredPath}"`],
                displayPath: discoveredPath,
                source,
            };
        }

        if (extension === ".ps1") {
            return {
                cliPath: "powershell.exe",
                cliArgs: ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", discoveredPath],
                displayPath: discoveredPath,
                source,
            };
        }
    }

    return {
        cliPath: discoveredPath,
        displayPath: discoveredPath,
        source,
    };
}

function printCliResolutionGuidance(source: string): void {
    switch (source) {
        case "argument":
            console.log("   Verify the provided --cli-path value points to a compatible Copilot runtime executable.");
            break;
        case "environment":
            console.log("   Verify COPILOT_CLI_PATH points to a compatible Copilot runtime executable.");
            break;
        default:
            console.log("   Reinstall dependencies so the SDK-managed Copilot runtime is restored locally.");
            break;
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
        case "theme": return displayTheme(metadata.theme);
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
        case "theme": metadata.theme = normalizeThemeSelection(value); break;
    }
}

async function promptForTheme(currentTheme?: string): Promise<string | undefined> {
    const defaultTheme = displayTheme(currentTheme);
    const theme = await select({
        message: "🎨 Select page theme",
        default: defaultTheme,
        choices: THEME_OPTIONS.map((option) => ({ name: option, value: option })),
    });

    return normalizeThemeSelection(theme);
}

function normalizeThemeSelection(value?: string): string | undefined {
    if (!value) return undefined;

    const trimmed = value.trim();
    if (trimmed.toLowerCase() === "none (site default)") return undefined;

    const match = THEME_OPTIONS
        .filter((option) => option !== "None (site default)")
        .find((option) => option.toLowerCase() === trimmed.toLowerCase());

    return match;
}

function isNoThemeFlagValue(value: string): boolean {
    const lower = value.trim().toLowerCase();
    return lower === "none" || lower === "default" || lower === "site-default";
}

function parseThemeFlagValue(value: string): string | undefined {
    if (isNoThemeFlagValue(value)) return undefined;

    const trimmed = value.trim();
    const options = THEME_OPTIONS.filter((option) => option !== "None (site default)");
    const match = options.find((option) => option.toLowerCase() === trimmed.toLowerCase());
    return match;
}

function getThemeFlagValues(): string[] {
    return [
        "none",
        "terminal",
        "neon",
        "minimal",
        "pastel",
        "matrix",
        "sunset",
        "ocean",
        "forest",
        "candy",
        "synthwave",
        "newspaper",
        "retro",
    ];
}

function displayTheme(value?: string): string {
    return value ?? "None (site default)";
}

function displayThemePaletteTable(): void {
    console.log("\n\x1b[36m🎨 Theme Preview\x1b[0m");
    console.log("   None (site default)  uses Tiny Tool Town default");

    for (const palette of THEME_PALETTES) {
        const swatches = palette.colors.map((hex) => colorizeSwatch(hex)).join(" ");
        console.log(`   ${palette.name.padEnd(12)} ${swatches}  ${palette.colors.join(", ")}`);
    }
}

function colorizeSwatch(hex: string): string {
    const rgb = hexToRgb(hex);
    if (!rgb) return "■";
    return `\x1b[38;2;${rgb.r};${rgb.g};${rgb.b}m■\x1b[0m`;
}

function hexToRgb(hex: string): { r: number; g: number; b: number } | null {
    const normalized = hex.replace("#", "");
    if (!/^[0-9A-Fa-f]{6}$/.test(normalized)) return null;
    return {
        r: Number.parseInt(normalized.slice(0, 2), 16),
        g: Number.parseInt(normalized.slice(2, 4), 16),
        b: Number.parseInt(normalized.slice(4, 6), 16),
    };
}
