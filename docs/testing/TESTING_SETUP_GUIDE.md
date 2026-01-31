# Testing Setup Guide: Action Versioning + Test Branch

## Overview

This guide walks you through testing the MAF-integrated Support Bot using:
- **Method 1: Action Versioning** - Bot as reusable GitHub Action (no code copying)
- **Option A: Test Branch** - Isolated testing with easy cleanup

## What "Dry Run" Actually Means

**SUPPORTBOT_DRY_RUN=true:**
- Bot processes **real GitHub events** from your repository
- Executes complete MAF workflow (Parse → Guardrails → Triage → Research → Response → Orchestrator → Persist)
- All agents run (OrchestratorAgent, CriticAgent, EnhancedTriageAgent, EnhancedResearchAgent, EnhancedResponseAgent)
- Generates actual response content you can see in logs
- **BUT:** Does not post comment back to the issue

**NOT a scripted test** - this is the real bot handling real events, just with output muted for safety.

## Testing Flow

```
┌─────────────────────────────────────────────────────────┐
│ Phase 1: Dry Run (Safe - No Comments Posted)           │
├─────────────────────────────────────────────────────────┤
│ 1. Create issue: "Build fails with ModuleNotFoundError"│
│ 2. Workflow triggers automatically                      │
│ 3. Check Actions tab for run logs                       │
│ 4. Verify MAF stages in logs:                           │
│    - [MAF] ParseEvent: Issue #...                       │
│    - [MAF] Guardrails: No stop commands detected        │
│    - [MAF] Triage: Category=build-error, Confidence=0.9 │
│    - [MAF] Research: Selected 2 tools                   │
│    - [MAF] Response: Generated brief                    │
│    - [MAF] Orchestrator: Decision=finalize              │
│    - [MAF Final Decision] ShouldFinalize=true           │
│ 5. No comment posted to issue ✓                         │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│ Phase 2: Write Mode (Validate - Real Comments)         │
├─────────────────────────────────────────────────────────┤
│ 1. Edit workflow: dry-run='false', write-mode='true'   │
│ 2. Create another issue                                 │
│ 3. Bot posts actual comment                             │
│ 4. Verify:                                               │
│    - Comment appears on issue                           │
│    - Formatting correct                                 │
│    - Content quality acceptable                         │
│    - Response matches category                          │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│ Phase 3: Cleanup (Remove All Test Artifacts)           │
├─────────────────────────────────────────────────────────┤
│ 1. git checkout main                                    │
│ 2. git branch -D test-support-bot                       │
│ 3. git push origin --delete test-support-bot            │
│ 4. Test issues remain (can close manually if desired)   │
└─────────────────────────────────────────────────────────┘
```

## Branch Testing: How It Works

**Key Concept:** Issue events are **repository-level**, not branch-level.

When you create an issue in `yt-music-simulator`:
- Issue exists at repository level (visible on main, all branches)
- GitHub triggers `issues.opened` event for the repository
- **Workflows in your current branch respond to the event**
- If you're on `test-support-bot` branch, the workflow in that branch runs
- Main branch workflow doesn't run (not merged yet)

**This lets you test without affecting main!**

## Step-by-Step Setup

### 1. Push Bot Repository (With Action Definition)

```powershell
# Already done: action.yml created and committed
cd "d:\Projects\agents\ms-quickstart\ghithub-issues-bot-MAF\Github-issues-bot-with-MAF"

# Set up remote (if not already set)
git remote add origin https://github.com/KeerthiYasasvi/ghithub-issues-bot-MAF.git

# Push main branch and tag
git push origin main
git push origin v0.1.0-test

# Verify on GitHub:
# - https://github.com/KeerthiYasasvi/ghithub-issues-bot-MAF
# - Check "Releases" section for v0.1.0-test tag
```

### 2. Create Test Branch in Test Repository

```powershell
cd "D:\Projects\ytm\yt-music-simulator"

# Create and switch to test branch
git checkout -b test-support-bot

# Workflow file already created at .github/workflows/support-bot-test.yml

# Commit and push
git add .github/workflows/support-bot-test.yml
git commit -m "Add support bot test workflow (action versioning)"
git push origin test-support-bot

# Verify on GitHub:
# - Branch 'test-support-bot' appears
# - File visible at: .github/workflows/support-bot-test.yml
```

### 3. Configure Secrets in Test Repository

**On GitHub (yt-music-simulator repository):**

1. Go to: `https://github.com/KeerthiYasasvi/yt-music-ELT-pipeline/settings/secrets/actions`

2. Add Repository Secret:
   - Name: `OPENAI_API_KEY`
   - Value: `sk-...` (your OpenAI API key)
   - Click "Add secret"

3. Add Repository Variables (Optional):
   - Go to: Settings → Secrets and variables → Actions → Variables tab
   - Name: `OPENAI_MODEL`
   - Value: `gpt-4o`
   - Click "Add variable"
   
   - Name: `OPENAI_CRITIQUE_MODEL`
   - Value: `gpt-4o-mini`
   - Click "Add variable"

**Note:** `GITHUB_TOKEN` is automatically provided by GitHub Actions.

### 4. Enable GitHub Actions (If Not Already)

```
Repository → Settings → Actions → General
✓ Allow all actions and reusable workflows
```

### 5. Create Test Issue (Dry Run)

**On GitHub (yt-music-simulator repository):**

1. Go to Issues tab
2. Click "New issue"
3. Title: `Build fails with ModuleNotFoundError`
4. Body:
   ```
   When running `python main.py`, I get:
   
   ModuleNotFoundError: No module named 'spotipy'
   
   The error happens at line 15 in main.py. I've already run pip install -r requirements.txt but it still fails.
   ```
5. Click "Submit new issue"

### 6. Monitor Workflow Execution

**On GitHub:**

1. Go to Actions tab
2. You should see workflow run: "Support Bot Test (MAF)"
3. Click on the run to see logs
4. Expand steps to see MAF execution:
   ```
   Run Support Bot via Action
   ├─ Setup .NET
   ├─ Restore dependencies
   └─ Run Support Bot
      ├─ [Event] Received issues.opened event for issue #1
      ├─ [MAF] ParseEvent: Issue #1 - "Build fails..."
      ├─ [MAF] Guardrails: No stop commands detected
      ├─ [MAF] Triage: Category=dependency-issue, Confidence=0.95
      ├─ [MAF] Research: Selected 2 tools (SearchDocs, SearchIssues)
      ├─ [MAF] Response: Generated brief with solution
      ├─ [MAF] Orchestrator: Decision=finalize
      └─ [MAF Final Decision] ShouldFinalize=true
   ```

5. **Verify:** No comment posted on the issue (dry run mode)

### 7. Switch to Write Mode

**Edit workflow file:**

```yaml
# In .github/workflows/support-bot-test.yml
with:
  dry-run: 'false'  # Changed from 'true'
  write-mode: 'true'  # Changed from 'false'
```

**Commit and push:**

```powershell
cd "D:\Projects\ytm\yt-music-simulator"
git add .github/workflows/support-bot-test.yml
git commit -m "Enable write mode for bot testing"
git push origin test-support-bot
```

**Create another test issue:**

Title: `Runtime error: AttributeError in data processing`

Body:
```
Getting this error when processing YouTube data:

AttributeError: 'NoneType' object has no attribute 'get'

Stack trace shows it's in process_data() function. How do I fix this?
```

**Verify:** Bot should post a comment on this issue.

### 8. Cleanup Test Branch

**After validation complete:**

```powershell
cd "D:\Projects\ytm\yt-music-simulator"

# Switch back to main
git checkout main

# Delete local test branch
git branch -D test-support-bot

# Delete remote test branch
git push origin --delete test-support-bot
```

**Result:** All test artifacts removed from repository.

## What You'll See in Logs (Dry Run)

```
[Event] Received issues.opened event for issue #1
[Event] Repository: KeerthiYasasvi/yt-music-ELT-pipeline
[Event] Issue: #1 - "Build fails with ModuleNotFoundError"

[MAF] Building workflow with 7 executors...
[MAF] ParseEvent: Converted event to RunContext
[MAF] Issue: #1
[MAF] Repository: yt-music-ELT-pipeline

[MAF] Guardrails: Checking for stop commands...
[MAF] Guardrails: No /stop detected, proceeding

[MAF] Triage: Starting classification...
[MAF] Triage: Category=dependency-issue (predefined)
[MAF] Triage: Confidence=0.95
[MAF] Triage: Technologies detected: Python, pip, requirements.txt
[MAF] Critic (Triage): Score=8/10 (pass)

[MAF] Research: Selecting tools...
[MAF] Research: Selected 2 tools: SearchDocs, SearchIssues
[MAF] Research: Executing tool: SearchDocs with {"keywords":["ModuleNotFoundError","spotipy"]}
[MAF] Research: Tool result: [3 documents found]
[MAF] Research: Executing tool: SearchIssues with {"keywords":["spotipy","install"]}
[MAF] Research: Tool result: [2 similar issues]
[MAF] Research: Synthesizing findings...
[MAF] Critic (Research): Score=7/10 (pass)

[MAF] Response: Generating brief...
[MAF] Response: Brief type=solution
[MAF] Response: Solution steps: 3
[MAF] Critic (Response): Score=9/10 (pass)

[MAF] Orchestrator: Evaluating progress...
[MAF] Orchestrator: Loop count: 1
[MAF] Orchestrator: Decision: finalize (actionable and high confidence)
[MAF] Orchestrator: ShouldFinalize=true

[MAF] PersistState: Workflow completed
[MAF Final Decision] ShouldFinalize=true, ShouldEscalate=false, ShouldAskFollowUps=false

[DRY RUN] Would have posted comment:
---
## Dependency Installation Issue

The error occurs because the `spotipy` package is not installed in your environment.

**Solution:**

1. Install spotipy:
   ```bash
   pip install spotipy
   ```

2. Verify installation:
   ```bash
   pip list | grep spotipy
   ```

3. If using virtual environment, ensure it's activated before running main.py

**Related:** See issue #42 for similar spotipy installation problems.

---

[INFO] Dry run mode: Comment not posted to issue
```

## What You'll See on Issue (Write Mode)

After enabling `write-mode: 'true'`, the bot will post the generated comment directly on the issue.

## Troubleshooting

### Workflow Doesn't Trigger

**Check:**
- Workflow file is in `test-support-bot` branch
- You're creating issues while on that branch (or after pushing branch)
- Actions are enabled in repository settings
- Workflow syntax is valid (check Actions tab for errors)

### Bot Doesn't Run

**Check:**
- `OPENAI_API_KEY` secret is set correctly
- Tag `v0.1.0-test` exists in bot repository
- Bot repository is public (or you have access)
- Check workflow logs for error messages

### Bot Runs But Fails

**Check logs for:**
- API key invalid: "401 Unauthorized"
- Model not available: "404 Model not found"
- Rate limit: "429 Too Many Requests"
- Spec pack missing: "Failed to load spec pack"

### Comments Not Posted (Write Mode)

**Check:**
- `dry-run: 'false'` in workflow
- `write-mode: 'true'` in workflow
- `GITHUB_TOKEN` has `issues: write` permission (should be automatic)
- Bot has permission to post on issue (not locked)

## Next Steps After Testing

Once testing is successful:

1. **Deploy to Production:**
   - Add workflow to main branch OR
   - Add to production repository using same action reference

2. **Monitor First Few Runs:**
   - Check comment quality
   - Verify categorization accuracy
   - Track costs (OpenAI API usage)

3. **Iterate on Spec Pack:**
   - Add more categories
   - Refine playbooks
   - Add validators

4. **Scale Up:**
   - Remove dry-run mode
   - Enable on all repositories
   - Configure cost alerts

## Why This Approach Works

**Action Versioning Benefits:**
- ✅ Bot code stays separate from test repository
- ✅ Multiple repositories can use same bot without duplication
- ✅ Update bot once, all repositories get updates
- ✅ Clear versioning (v0.1.0-test → v1.0.0)
- ✅ Easy to roll back (pin to specific version)

**Test Branch Benefits:**
- ✅ Zero impact on main branch
- ✅ Can test with real events (not mocks)
- ✅ Easy cleanup (delete branch)
- ✅ Can iterate quickly (push changes to test branch)
- ✅ Safe experimentation environment

**Dry Run Benefits:**
- ✅ Validate bot logic without posting comments
- ✅ Check response quality before going live
- ✅ Debug issues without affecting users
- ✅ Safe to test on production-like data
- ✅ Can review generated content first

## Summary

You now have:
- ✅ `action.yml` in bot repository (makes it reusable)
- ✅ Tag `v0.1.0-test` for versioning
- ✅ Workflow in test repository (references action)
- ✅ Test branch for isolation
- ✅ Dry run mode for safety

**To start testing:**
1. Push bot repository with tag
2. Push test branch with workflow
3. Configure secrets
4. Create test issue
5. Check logs
6. Enable write mode
7. Validate comments
8. Clean up branch

Good luck! 🚀
