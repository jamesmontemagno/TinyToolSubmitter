# TinyToolSubmitter

Standalone repo for the Tiny Tool Town submitter CLI.

![TinyClips 2026-02-14 at 22 55 08](https://github.com/user-attachments/assets/a5f2e541-9b27-48d3-a369-a3d96ae5e5e0)

[![NuGet version](https://img.shields.io/nuget/v/TinyToolSubmitter?style=flat-square&logo=nuget)](https://www.nuget.org/packages/TinyToolSubmitter)
[![NuGet prerelease](https://img.shields.io/nuget/vpre/TinyToolSubmitter?style=flat-square&logo=nuget&label=prerelease)](https://www.nuget.org/packages/TinyToolSubmitter)
[![NuGet downloads](https://img.shields.io/nuget/dt/TinyToolSubmitter?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/TinyToolSubmitter)

## Quick Start

Prerequisites:

- .NET 10 SDK
- GitHub Copilot access configured for the SDK call
- GitHub Copilot CLI installed and available on PATH (`copilot`)

Run directly with dnx:

```bash
dnx TinyToolSubmitter
```

Run against a specific repository path:

```bash
dnx TinyToolSubmitter /path/to/repo
```

Global tool install alternative:

```bash
dotnet tool install --global TinyToolSubmitter
tiny-tool-submit [path-to-repo]
```

## Project Layout

- `src/` — .NET tool source (`TinyToolSubmitter.csproj` and app code)
- `nodejs/` — Node.js/TypeScript version of the tool
- `.github/workflows/release-submitter.yml` — NuGet release workflow
- `.github/workflows/publish-npm.yml` — npm release workflow
- `LICENSE` — MIT license

## Local Development

### .NET

```bash
dotnet restore src/TinyToolSubmitter.csproj
dotnet build src/TinyToolSubmitter.csproj
```

Run locally:

```bash
dotnet run --project src/TinyToolSubmitter.csproj -- [path-to-repo]
```

### Node.js

```bash
cd nodejs
npm install
npm run build
```

Run locally:

```bash
node nodejs/dist/index.js [path-to-repo]
```

Or during development:

```bash
cd nodejs
npm run dev -- [path-to-repo]
```

### npm install alternative

```bash
npm install -g tiny-tool-submitter
tiny-tool-submit [path-to-repo]
```

## Release Workflow

This repo includes tag-triggered workflows for both NuGet and npm:

### .NET / NuGet
- Workflow: `.github/workflows/release-submitter.yml`
- Trigger tag format: `submitter-v*` (example: `submitter-v1.0.0`)
- Required secret: `NUGET_API_KEY`

### Node.js / npm
- Workflow: `.github/workflows/publish-npm.yml`
- Trigger tag format: `npm-v*` (example: `npm-v1.0.0`)
- Also supports `workflow_dispatch` with a version input
- Required secret: `NPM_TOKEN`

Both workflows build, publish, and handle versioning automatically.

## Tool Usage

Options:

```text
tiny-tool-submit [path-to-repo]
	--readme <path>     Path to README file (skip auto-detection)
	--headless          Skip interactive prompts, open URL directly
	--model <name>      Copilot model to use (default: gpt-4.1)
	--cli-path <path>   Path to Copilot CLI executable (default: auto-detect on PATH, then copilot)
	--theme <name>      Tiny Tool Town page theme (or 'none' for site default)
	--verbose           Print Copilot CLI startup diagnostics
```

Both the .NET and Node CLIs now support Tiny Tool Town's optional `theme` field.
In interactive mode, they show a theme picker and a terminal color swatch preview
for each available page theme before submission.

## License

MIT — see [LICENSE](LICENSE).
