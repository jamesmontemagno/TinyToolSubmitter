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
- `.github/workflows/release-submitter.yml` — NuGet release workflow
- `LICENSE` — MIT license

## Local Development

```bash
dotnet restore src/TinyToolSubmitter.csproj
dotnet build src/TinyToolSubmitter.csproj
```

Run locally:

```bash
dotnet run --project src/TinyToolSubmitter.csproj -- [path-to-repo]
```

## Release Workflow

This repo includes a tag-triggered workflow:

- Workflow: `.github/workflows/release-submitter.yml`
- Trigger tag format: `submitter-v*` (example: `submitter-v1.0.0`)
- Required secret: `NUGET_API_KEY`

The workflow builds and packs `src/TinyToolSubmitter.csproj`, pushes to NuGet.org, and creates a GitHub release with the package artifact.

## Tool Usage

Options:

```text
tiny-tool-submit [path-to-repo]
	--readme <path>     Path to README file (skip auto-detection)
	--headless          Skip interactive prompts, open URL directly
	--model <name>      Copilot model to use (default: gpt-4.1)
	--cli-path <path>   Path to Copilot CLI executable (default: auto-detect on PATH, then copilot)
	--verbose           Print Copilot CLI startup diagnostics
```

## License

MIT — see [LICENSE](LICENSE).
