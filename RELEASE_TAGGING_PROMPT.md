# Release Tagging Prompt

Use this prompt with Copilot Chat to create both release tags for TinyToolSubmitter.

```text
In /Users/jamesmontemagno/Projects/TinyToolSubmitter, do a safe dual-release tagging flow for version <VERSION>:

1) Verify branch and cleanliness
- Run: git status --short --branch
- Confirm working tree is clean before tagging.

2) Verify target commit
- Run: git rev-parse --short HEAD
- Tag current HEAD unless I specify a different commit.

3) Check existing tags
- Run: git tag --list "submitter-v<VERSION>"
- Run: git tag --list "npm-v<VERSION>"

4) Create missing tags as annotated tags
- If missing: git tag -a submitter-v<VERSION> -m "Release .NET tool <VERSION>"
- If missing: git tag -a npm-v<VERSION> -m "Release npm tool <VERSION>"
- Do not recreate tags that already exist.

5) Verify tags
- Run: git show --no-patch --pretty=fuller submitter-v<VERSION>
- Run: git show --no-patch --pretty=fuller npm-v<VERSION>

6) Ask before pushing
- Suggest: git push origin submitter-v<VERSION> npm-v<VERSION>
- Do not push unless I explicitly approve.

Finally, summarize:
- Whether the repo was clean
- HEAD SHA tagged
- Which tags already existed vs were created
- Exact push command to run
```

## Example

For version 1.0.7, replace `<VERSION>` with `1.0.7`.
