# tiny-tool-submitter

CLI tool to help submit your project to Tiny Tool Town using the Microsoft Copilot SDK.

## Install

```bash
npm install -g tiny-tool-submitter
```

## Usage

```bash
tiny-tool-submit [path-to-repo]
```

Options:

- `--readme <path>` path to README file (skip auto-detection)
- `--headless` skip interactive prompts
- `--model <name>` Copilot model to use (default: `claude-haiku-4.5`)
- `--cli-path <path>` path to a compatible Copilot runtime executable
- `--theme <name>` Tiny Tool Town page theme (or `none` for site default)
- `--verbose` print Copilot runtime startup diagnostics

In interactive mode, the CLI includes Tiny Tool Town's optional page `theme`
picker and prints a color swatch preview for each available theme.

## Requirements

- Node.js `^20.19.0` or `>=22.12.0`
- GitHub Copilot access configured for the SDK call
- No separate Copilot CLI install is required; the SDK supplies its bundled runtime

Runtime selection order:

1. `--cli-path`
2. `COPILOT_CLI_PATH`
3. Microsoft Copilot SDK's bundled runtime (in-process by default)

## Release

This package is published by GitHub Actions from this repository:

- Workflow: `/.github/workflows/publish-npm.yml`
- Tag trigger: `npm-v*` (example: `npm-v1.0.1`)
- Manual trigger: `workflow_dispatch` with `version` input
- Authentication: npm trusted publisher for `jamesmontemagno/TinyToolSubmitter` and `publish-npm.yml`

Tag-based release example:

```bash
git tag -a npm-v1.0.1 -m "Release Node.js tool v1.0.1"
git push origin npm-v1.0.1
```
