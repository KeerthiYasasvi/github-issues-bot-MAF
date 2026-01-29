# Deployment

This bot runs as a GitHub Action on `issues` and `issue_comment` events. Use a sandbox repo first.

## Sandbox testing guide

1) Create or pick a sandbox repo (ex: `ytm-stream-analytics`).
2) Add labels referenced by `.supportbot/routing.yaml` (or edit routing to match existing labels).
3) Add repository variables:
   - `PRIMARY_MODEL` (e.g., "gpt-4o")
   - `SECONDARY_MODEL` (e.g., "gpt-4o-mini")
   - `SUPPORTBOT_DRY_RUN` (true/false)
   - `SUPPORTBOT_WRITE_MODE` (true/false)
4) Add repository secrets:
   - `API_KEY` (your OpenAI API key)
5) Install the workflow (Option A or B below).
6) Open a test issue and add comments from the author to exercise the follow-up loop.

## Option A: Submodule Deployment (Recommended for Testing)

**Best for:** Version-controlled bot deployments with reproducibility

1) **Add bot as submodule to your test repo:**

```bash
cd your-test-repo
git submodule add https://github.com/KeerthiYasasvi/github-issues-bot-MAF.git bot
git submodule update --init --recursive
```

2) **Create workflow file** (`.github/workflows/supportbot.yml`):

```yaml
name: Support Concierge Bot

on:
  issues:
    types: [opened, edited]
  issue_comment:
    types: [created]

permissions:
  contents: write
  issues: write

jobs:
  supportbot:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4
        with:
          submodules: recursive  # Gets the pinned bot version

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - name: Run Support Concierge
        if: github.event_name == 'issues' || github.event.comment.user.login != 'github-actions[bot]'
        run: dotnet run --project bot/src/SupportConcierge.Cli -- --event-file "$GITHUB_EVENT_PATH"
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          SUPPORTBOT_USERNAME: github-actions[bot]
          OPENAI_API_KEY: ${{ secrets.API_KEY }}
          OPENAI_MODEL: ${{ vars.PRIMARY_MODEL }}
          OPENAI_CRITIQUE_MODEL: ${{ vars.SECONDARY_MODEL }}
          SUPPORTBOT_DRY_RUN: ${{ vars.SUPPORTBOT_DRY_RUN || 'false' }}
          SUPPORTBOT_WRITE_MODE: ${{ vars.SUPPORTBOT_WRITE_MODE || 'true' }}
          SUPPORTBOT_SPEC_DIR: ${{ vars.SUPPORTBOT_SPEC_DIR || '.supportbot' }}
          SUPPORTBOT_METRICS_DIR: artifacts/metrics

      - name: Upload metrics
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: supportbot-metrics
          path: artifacts/metrics
```

3) **Update bot version when needed:**

```bash
cd bot
git pull origin main
cd ..
git add bot
git commit -m "Update bot to [commit-sha]"
git push
```

4) **Check current bot version:**

```bash
cd bot
git log -1 --oneline
```

### Why Submodules?

- ✅ **Version Locking**: Each test repo commit explicitly declares which bot version it uses
- ✅ **Reproducibility**: Can recreate any historical test scenario by checking out old commits
- ✅ **No Race Conditions**: The submodule pointer is committed, not fetched at runtime
- ✅ **Clear Deployment**: `git submodule update` becomes your explicit deployment step
- ✅ **Debuggability**: Know exactly which bot code ran for any issue
- ✅ **Testing**: Test new bot versions before updating the pointer

## Option B: Action Versioning (Reusable Workflow)

**Best for:** Production deployments with semantic versioning

1) Use the reusable workflow in this repo:

```yaml
jobs:
  supportbot:
    uses: KeerthiYasasvi/github-issues-bot-MAF/.github/workflows/supportbot.yml@v1.2.3
    secrets: inherit
```

2) Tag releases (`v1.2.3`) and update the sandbox repo reference when you upgrade.

## Comparison checklist

- Submodule
  - Pros: exact SHA pin, easy local edits
  - Cons: more repo maintenance, manual updates

- Reusable workflow
  - Pros: clean consumer repo, easy upgrades by tag
  - Cons: versioning discipline required

## Cleanup

See `scripts/cleanup-sandbox.md` for removing test issues, labels, and bot comments.
