# Test Issue Content

Copy and paste this into a new issue at:
https://github.com/KeerthiYasasvi/yt-music-simulator/issues/new

---

**Title:**
```
Test Issue - Workflow Fix Validation
```

**Body:**
```markdown
## Test Issue for Workflow Fixes

This issue tests:
1. ✅ Bot identity (should post as github-actions[bot])
2. ✅ Infinite loop fix (should only trigger once)  
3. ✅ Multi-user state model (new code deployed)

### Scenario
My Python ETL pipeline fails when extracting data from YouTube Music API.

**Error:**
```
HTTPError: 429 Too Many Requests
Retry-After: 3600
```

**What I've tried:**
- Added retry logic with exponential backoff
- Reduced request rate to 10 req/min
- Still getting rate limited after ~50 requests

**Environment:**
- Python 3.11
- ytmusicapi v1.4.2
- Running on Azure Functions (Consumption plan)

Any ideas what could be causing this?
```

---

## What to Check After Posting

### 1. Actions Tab
Go to: https://github.com/KeerthiYasasvi/yt-music-simulator/actions

**Expected:**
- ✅ Only ONE workflow run should trigger (e.g., Run #53)
- ✅ NO Run #54, #55, #56... (infinite loop fixed)

**If you see multiple runs:**
- ❌ Infinite loop NOT fixed - workflow filter not working

### 2. Bot Comment Author
Check the bot's response comment.

**Expected:**
- ✅ Author should be "github-actions[bot]"
- ✅ Avatar should be GitHub Actions bot icon

**If you see your username:**
- ❌ Bot identity NOT fixed - token still using PAT

### 3. Bot Questions
Check if bot asks the expected questions.

**Expected:**
- ✅ Should ask 3 clarifying questions (Loop 1)
- ✅ Questions about error patterns, logs, configuration

**Example questions:**
1. Error message details
2. When the error occurs
3. Stack trace or logs

### 4. Workflow Run Details
Click on the workflow run → "Run Support Concierge" step

**Expected logs:**
```
[MAF] Guardrails: All checks passed for KeerthiYasasvi. Loop 0/3
[MAF] LoadState: No embedded state found in comments; starting fresh
[MAF] LoadState: Creating conversation for issue author KeerthiYasasvi
[MAF] Orchestrator: KeerthiYasasvi Loop 1/3
[MAF] PostComment: Embedded state (KeerthiYasasvi Loop=1, 1 users, Category=runtime)
```

---

## Quick Test Checklist

After creating the issue, wait 1-2 minutes and check:

- [ ] Only ONE workflow run in Actions tab
- [ ] Bot comment author is "github-actions[bot]"
- [ ] Bot asks 3 clarifying questions
- [ ] No workflow Run #54 appears (no infinite loop)
- [ ] Bot comment includes hidden state marker

If all checks pass ✅ → Workflow fixes successful!

Then we can proceed with multi-user testing.
