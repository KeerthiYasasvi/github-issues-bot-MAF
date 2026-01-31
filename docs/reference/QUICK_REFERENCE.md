# Quick Reference: Bot Testing Commands

## Setup Commands

```powershell
# 1. Push bot repository
cd "d:\Projects\agents\ms-quickstart\ghithub-issues-bot-MAF\Github-issues-bot-with-MAF"
git remote add origin https://github.com/KeerthiYasasvi/ghithub-issues-bot-MAF.git
git push origin main
git push origin v0.1.0-test

# 2. Create test branch in test repo
cd "D:\Projects\ytm\yt-music-simulator"
git checkout -b test-support-bot
git add .github/workflows/support-bot-test.yml
git commit -m "Add support bot test workflow"
git push origin test-support-bot
```

## GitHub Configuration

```
Repository: https://github.com/KeerthiYasasvi/yt-music-ELT-pipeline
Path: Settings → Secrets and variables → Actions

Secrets:
├─ OPENAI_API_KEY = sk-...

Variables (optional):
├─ OPENAI_MODEL = gpt-4o
└─ OPENAI_CRITIQUE_MODEL = gpt-4o-mini
```

## Testing Flow

```powershell
# Phase 1: Dry Run (no comments posted)
# - Create issue on GitHub
# - Check Actions tab for logs
# - Verify MAF execution in logs

# Phase 2: Enable write mode
cd "D:\Projects\ytm\yt-music-simulator"
# Edit .github/workflows/support-bot-test.yml:
#   dry-run: 'false'
#   write-mode: 'true'
git add .github/workflows/support-bot-test.yml
git commit -m "Enable write mode"
git push origin test-support-bot
# Create another issue
# Verify comment posted

# Phase 3: Cleanup
git checkout main
git branch -D test-support-bot
git push origin --delete test-support-bot
```

## What to Look For in Logs

```
✓ [MAF] ParseEvent: Issue #X
✓ [MAF] Guardrails: No stop commands
✓ [MAF] Triage: Category=..., Confidence=...
✓ [MAF] Research: Selected N tools
✓ [MAF] Response: Generated brief
✓ [MAF] Orchestrator: Decision=...
✓ [MAF Final Decision] Should...=true
✓ [DRY RUN] Would have posted comment (in dry run)
✓ [Posted comment to issue #X] (in write mode)
```

## Common Issues

| Problem | Solution |
|---------|----------|
| Workflow doesn't trigger | Check Actions enabled in repo settings |
| Bot fails to run | Verify OPENAI_API_KEY secret is set |
| Tag not found | Push tag: `git push origin v0.1.0-test` |
| No comment posted | Enable write-mode AND disable dry-run |
| Rate limit error | Wait or use different API key |

## Files Created

```
Bot Repository (ghithub-issues-bot-MAF):
├─ action.yml (NEW - makes bot reusable)
├─ Tag: v0.1.0-test (NEW)
└─ docs/testing/TESTING_SETUP_GUIDE.md (NEW)

Test Repository (yt-music-simulator):
└─ .github/workflows/support-bot-test.yml (NEW)
```

## Next Steps

- [ ] Push bot repo + tag to GitHub
- [ ] Configure secrets in test repo
- [ ] Create test issue (dry run)
- [ ] Check logs for MAF execution
- [ ] Enable write mode
- [ ] Create another issue
- [ ] Verify comment posted
- [ ] Clean up test branch

