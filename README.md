# TinyToolSubmitter

Standalone repo for the Tiny Tool Town submitter CLI.

![TinyClips 2026-02-14 at 22 55 08](https://github.com/user-attachments/assets/a5f2e541-9b27-48d3-a369-a3d96ae5e5e0)

[![NuGet version](https://img.shields.io/nuget/v/TinyToolSubmitter?style=flat-square&logo=nuget)](https://www.nuget.org/packages/TinyToolSubmitter)
[![NuGet prerelease](https://img.shields.io/nuget/vpre/TinyToolSubmitter?style=flat-square&logo=nuget&label=prerelease)](https://www.nuget.org/packages/TinyToolSubmitter)
[![NuGet downloads](https://img.shields.io/nuget/dt/TinyToolSubmitter?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/TinyToolSubmitter)

## Quick Start

Prerequisites:

- .NET 10 SDK
- GitHub Copilot access configured for the Microsoft Copilot SDK call
- No separate Copilot CLI install is required; the SDK supplies its bundled runtime

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

Requires Node.js `^20.19.0` or `>=22.12.0`.

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
- Authentication: NuGet.org GitHub trusted-publishing policy for `jamesmontemagno/TinyToolSubmitter` and `release-submitter.yml`

### Node.js / npm
- Workflow: `.github/workflows/publish-npm.yml`
- Trigger tag format: `npm-v*` (example: `npm-v1.0.0`)
- Also supports `workflow_dispatch` with a version input
- Authentication: npm trusted publisher for `jamesmontemagno/TinyToolSubmitter` and `publish-npm.yml`

Both workflows build, publish, and handle versioning automatically.

Trusted-publishing policies must be configured once for the package owners:

- NuGet.org: repository owner `jamesmontemagno`, repository `TinyToolSubmitter`, workflow file `release-submitter.yml`, package owner `JamesMontemagno`, and no GitHub environment.
- npm: GitHub Actions publisher for owner `jamesmontemagno`, repository `TinyToolSubmitter`, workflow file `publish-npm.yml`, and no environment.

## Tool Usage

Options:

```text
tiny-tool-submit [path-to-repo]
	--readme <path>     Path to README file (skip auto-detection)
	--headless          Skip interactive prompts, open URL directly
	--model <name>      Copilot model to use (default: claude-haiku-4.5)
	--cli-path <path>   Path to a compatible Copilot runtime executable
	--theme <name>      Tiny Tool Town page theme (or 'none' for site default)
	--verbose           Print Copilot runtime startup diagnostics
```

Runtime selection order:

1. `--cli-path`
2. `COPILOT_CLI_PATH`
3. Microsoft Copilot SDK's bundled runtime (in-process by default)

Both the .NET and Node CLIs now support Tiny Tool Town's optional `theme` field.
In interactive mode, they show a theme picker and a terminal color swatch preview
for each available page theme before submission.

## License

MIT — see [LICENSE](LICENSE).
