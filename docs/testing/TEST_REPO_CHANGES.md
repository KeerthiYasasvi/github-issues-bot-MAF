# Test Repository Required Changes

## ⚠️ CRITICAL: Apply These Changes First

Before the bot code changes will work, you must update your test repository workflow file.

### File to Edit
`yt-music-ELT-pipeline/.github/workflows/supportbot.yml`

### Three Required Changes

#### Change 1: Fix Bot Token (Lines ~30-35)
**FIND:**
```yaml
env:
  GITHUB_TOKEN: ${{ secrets.BOT_GITHUB_TOKEN }}
```

**REPLACE WITH:**
```yaml
env:
  GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
  SUPPORTBOT_USERNAME: github-actions[bot]
```

**Why:** Using a PAT makes the bot post as your account. Built-in `GITHUB_TOKEN` makes it post as "github-actions[bot]".

---

#### Change 2: Add Bot Comment Filter (Lines ~45-50)
**FIND:**
```yaml
    - name: Run Support Concierge
      run: dotnet run --project bot/src/SupportConcierge.Cli -- --event-file "$GITHUB_EVENT_PATH"
```

**REPLACE WITH:**
```yaml
    - name: Run Support Concierge
      if: github.event_name == 'issues' || github.event.comment.user.login != 'github-actions[bot]'
      run: dotnet run --project bot/src/SupportConcierge.Cli -- --event-file "$GITHUB_EVENT_PATH"
```

**Why:** Prevents infinite loop by filtering out comments from the bot itself.

---

#### Change 3: Verify Trigger Configuration (Lines ~5-10)
**ENSURE IT LOOKS LIKE:**
```yaml
on:
  issues:
    types: [opened, edited]
  issue_comment:
    types: [created]
```

**Why:** This is correct - just verify no extra filters are present.

---

## Quick Copy-Paste Version

Navigate to: https://github.com/KeerthiYasasvi/yt-music-ELT-pipeline/blob/main/.github/workflows/supportbot.yml

Click "Edit" and make these exact changes:

1. **Line with `GITHUB_TOKEN:`**
   - Change: `${{ secrets.BOT_GITHUB_TOKEN }}`
   - To: `${{ secrets.GITHUB_TOKEN }}`
   
2. **After that line, add:**
   ```yaml
   SUPPORTBOT_USERNAME: github-actions[bot]
   ```

3. **Line with "name: Run Support Concierge"**
   - Add this line right after it (before the `run:` line):
   ```yaml
   if: github.event_name == 'issues' || github.event.comment.user.login != 'github-actions[bot]'
   ```

## Testing After Changes

1. **Cancel Existing Runs:**
   - Go to Actions tab
   - Cancel any running/queued workflows (Runs #52+)

2. **Post Test Comment:**
   - Comment on Issue #12: "Testing fixed workflow"
   - Check Actions tab - should see ONE run only
   - Check comment author - should be "github-actions[bot]"

3. **Verify No Loop:**
   - Bot should respond once
   - No new workflow runs should trigger from bot comment
   - Success = only one Run #53 (or next number)

## What This Fixes

✅ Bot posts as "github-actions[bot]" instead of your account  
✅ No infinite loop (bot comments don't trigger workflow)  
✅ Bot can detect its own comments correctly  
✅ Multi-user state changes will work once code deployed

---

**After confirming these work, the multi-user code changes I'm implementing will be ready to deploy.**
