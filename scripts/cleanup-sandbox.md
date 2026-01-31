# Cleanup Sandbox Repo

Use `gh` CLI to clean up test artifacts.

## Close test issues

```bash
gh issue list --state open --limit 100
# Close by number (repeat as needed)
gh issue close <issue-number>
```

## Delete bot comments

```bash
# List comments for an issue
gh api repos/<owner>/<repo>/issues/<issue-number>/comments --paginate
# Delete by comment id
gh api -X DELETE repos/<owner>/<repo>/issues/comments/<comment-id>
```

## Remove labels created for testing

```bash
gh label list
# Delete by name
gh label delete "supportbot:build" --yes
```
