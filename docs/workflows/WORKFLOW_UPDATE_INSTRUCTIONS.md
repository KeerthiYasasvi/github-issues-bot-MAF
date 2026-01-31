# Fix for Test Repository Workflow

## Problem
The test repository's workflow file uses `ref: main` which can be cached by GitHub Actions. Even though commits c6c829d and c58c825 are in the main branch, the workflow might be using a cached version of the repository.

## Solution Options

### Option 1: Use Specific Commit SHA (Recommended)

Update `.github/workflows/supportbot.yml` in the test repository:

```yaml
- name: Checkout
  uses: actions/checkout@v4
  with:
    repository: KeerthiYasasvi/github-issues-bot-MAF
    path: bot
    ref: d6c40a1  # Latest commit with all fixes (multi-user, loop display, allow list)
```

### Option 2: Force Fresh Pull

Update `.github/workflows/supportbot.yml` in the test repository:

```yaml
- name: Checkout
  uses: actions/checkout@v4
  with:
    repository: KeerthiYasasvi/github-issues-bot-MAF
    path: bot
    ref: main
    
- name: Pull Latest Changes
  run: |
    cd bot
    git pull origin main
```

### Option 3: Clear Cache and Use Main

Update `.github/workflows/supportbot.yml` to disable caching:

```yaml
- name: Checkout
  uses: actions/checkout@v4
  with:
    repository: KeerthiYasasvi/github-issues-bot-MAF
    path: bot
    ref: main
    clean: true  # Force clean checkout
```

## Recommended Fix

Use **Option 1** - it's the most reliable. Change line 21 in the workflow file from:

```yaml
ref: main
```

to:

```yaml
ref: d6c40a1
```

This will force GitHub Actions to checkout the exact commit with all the fixes.

## After Applying Fix

1. Commit and push the workflow change to test repository
2. Wait for workflow to run on any issue comment
3. Verify logs show: `[MAF] LoadState: Looking for bot comments from: github-actions[bot]` 
   (NOT `preferred=..., actual=...`)
4. Test with yk617 account commenting on Issue #14

## Commits That Need to Be Pulled

- **c6c829d**: "CRITICAL FIX: Remove GITHUB_ACTOR from bot identity check"
- **c58c825**: "CRITICAL FIX: Parse only incoming comment for commands, not issue body + all comments"
- **2064fd4**: "Add comprehensive debug logging to LoadStateExecutor and StateStoreTool"
- **e52f946**: "Fix loop count display bug in PostCommentExecutor"
- **d6c40a1**: "Fix multi-user comment addressing and allow list behavior" (LATEST)
