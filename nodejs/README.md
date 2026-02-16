# tiny-tool-submitter

CLI tool to help submit your project to Tiny Tool Town using the GitHub Copilot SDK.

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
- `--model <name>` Copilot model to use (default: `gpt-4.1`)
- `--cli-path <path>` path to Copilot CLI executable
- `--verbose` print Copilot CLI startup diagnostics

## Requirements

- Node.js 18+
- GitHub Copilot CLI installed and authenticated (`copilot`)

## Release

This package is published by GitHub Actions from this repository:

- Workflow: `/.github/workflows/publish-npm.yml`
- Tag trigger: `npm-v*` (example: `npm-v1.0.1`)
- Manual trigger: `workflow_dispatch` with `version` input
- Required secret: `NPM_TOKEN`

Tag-based release example:

```bash
git tag -a npm-v1.0.1 -m "Release Node.js tool v1.0.1"
git push origin npm-v1.0.1
```
