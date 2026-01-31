# Issue #13 Multi-User Bug Analysis

## 🐛 Critical Bug Found: Bot Confuses Users with Bot Identity

### Timeline of Events

1. **12:18 PM** - Issue #13 opened by @KeerthiYasasvi
2. **12:19 PM** - Bot (github-actions[bot]) responds with 3 questions (Run #76) ✅
3. **12:49 PM** - @yk617 (different account) posts answers (Run #77 triggered) ⚠️  
4. **12:50 PM** - Bot responds: "/stop comment posted" to @KeerthiYasasvi ❌ BUG!
5. **12:59 PM** - @KeerthiYasasvi posts same answers (Run #78 triggered) ⚠️
6. **1:00 PM** - Bot again responds: "/stop comment posted" ❌ BUG!

---

## 🔍 Root Cause Analysis

### The Bug (Run #77 Logs - Line 24):

```
[MAF] LoadState: Looking for bot comments from: 
    preferred=github-actions[bot], 
    actual=yk617  ← WRONG! This is the user who triggered the workflow
```

### The Faulty Logic (GuardrailsExecutor.cs lines 33-35):

```csharp
var preferredBotUsername = Environment.GetEnvironmentVariable("SUPPORTBOT_USERNAME") ?? "github-actions[bot]";
var actualBotUsername = Environment.GetEnvironmentVariable("GITHUB_ACTOR") ?? preferredBotUsername;
```

**Problem:** `GITHUB_ACTOR` represents the **user who triggered the workflow**, NOT the bot identity!

When yk617 commented:
- `GITHUB_ACTOR` = `"yk617"` (the commenter)
- `actualBotUsername` = `"yk617"` ← WRONG!

### The Incorrect Bot Check (GuardrailsExecutor.cs lines 37-43):

```csharp
if (input.EventName == "issue_comment" && 
    (incomingCommentAuthor == preferredBotUsername || incomingCommentAuthor == actualBotUsername))
{
    Console.WriteLine($"[MAF] Guardrails: Comment from bot ({incomingCommentAuthor}). Ignoring to prevent loop.");
    input.ShouldStop = true;
    input.StopReason = "Bot comment - ignoring to prevent infinite loop";
    return new ValueTask<RunContext>(input);
}
```

**What happened:**
- `incomingCommentAuthor` = `"yk617"` (from the comment)
- `actualBotUsername` = `"yk617"` (from GITHUB_ACTOR - WRONG!)
- Match! ✅ Bot thinks yk617 IS the bot
- Result: Bot ignores yk617's comment and posts "/stop" message

---

## 🎯 Why This is Wrong

### GITHUB_ACTOR Semantics:

`GITHUB_ACTOR` = **the user who triggered the workflow event**

- Issue opened → `GITHUB_ACTOR` = issue opener
- Comment created → `GITHUB_ACTOR` = commenter  
- Bot comment → `GITHUB_ACTOR` = bot identity (github-actions[bot])

**The bot should ONLY look at `SUPPORTBOT_USERNAME` for its identity, NOT `GITHUB_ACTOR`!**

---

## 📊 Evidence from Logs

### Run #77 (yk617's comment):
```
[MAF] LoadState: Looking for bot comments from: preferred=github-actions[bot], actual=yk617
[MAF] Guardrails: Comment from bot (yk617). Ignoring to prevent loop.
[MAF Final Decision] stop
```

Bot posted:
```
@KeerthiYasasvi

You've opted out with /stop. I won't ask further questions on this issue.
```

**Problems:**
1. ✅ Mentioned @KeerthiYasasvi (correct - issue author)
2. ❌ Said "/stop was posted" (FALSE - nobody posted /stop)
3. ❌ Treated yk617 as the bot itself

### Run #78 (KeerthiYasasvi's comment):
Same behavior - bot thinks KeerthiYasasvi is the bot because `GITHUB_ACTOR` = `"KeerthiYasasvi"`

---

## 🔧 The Fix

### Remove GITHUB_ACTOR from Bot Identity Check

**Current (WRONG):**
```csharp
var preferredBotUsername = Environment.GetEnvironmentVariable("SUPPORTBOT_USERNAME") ?? "github-actions[bot]";
var actualBotUsername = Environment.GetEnvironmentVariable("GITHUB_ACTOR") ?? preferredBotUsername;  // ← REMOVE THIS

if (incomingCommentAuthor == preferredBotUsername || incomingCommentAuthor == actualBotUsername)
```

**Fixed (CORRECT):**
```csharp
var botUsername = Environment.GetEnvironmentVariable("SUPPORTBOT_USERNAME") ?? "github-actions[bot]";

if (input.EventName == "issue_comment" && incomingCommentAuthor == botUsername)
{
    Console.WriteLine($"[MAF] Guardrails: Comment from bot ({incomingCommentAuthor}). Ignoring to prevent loop.");
    input.ShouldStop = true;
    input.StopReason = "Bot comment - ignoring to prevent infinite loop";
    return new ValueTask<RunContext>(input);
}
```

### Same Fix Needed in LoadStateExecutor.cs

**Current (WRONG):**
```csharp
var preferredBotUsername = Environment.GetEnvironmentVariable("SUPPORTBOT_USERNAME") ?? "github-actions[bot]";
var actualBotUsername = Environment.GetEnvironmentVariable("GITHUB_ACTOR") ?? preferredBotUsername;  // ← REMOVE THIS

Console.WriteLine($"[MAF] LoadState: Looking for bot comments from: preferred={preferredBotUsername}, actual={actualBotUsername}");

var isBotComment = commentAuthor == preferredBotUsername || commentAuthor == actualBotUsername;  // ← FIX THIS
```

**Fixed (CORRECT):**
```csharp
var botUsername = Environment.GetEnvironmentVariable("SUPPORTBOT_USERNAME") ?? "github-actions[bot]";

Console.WriteLine($"[MAF] LoadState: Looking for bot comments from: {botUsername}");

var isBotComment = commentAuthor == botUsername;
```

---

## 🚨 Impact Assessment

### What Broke:
1. ❌ Multi-user support completely non-functional
2. ❌ Bot treats every user as "the bot" when they comment
3. ❌ `/stop` message posted when nobody used `/stop`
4. ❌ Allow list logic bypassed (all users seen as bot)
5. ❌ No conversations possible from any user except issue opener on first response

### Why Workflow Filter Alone Isn't Enough:
The workflow filter prevents infinite loops:
```yaml
if: github.event_name == 'issues' || github.event.comment.user.login != 'github-actions[bot]'
```

But the **code-level bot identity check** is still needed for:
- Loading state from bot comments (not user comments)
- Multi-user allow list logic
- /diagnose command handling

---

## 🎬 Historical Context: Why This Bug Exists

### Original Intent (commit 4ac4276):
The fix was meant to handle cases where the bot might post under different usernames (PAT vs built-in token).

**Flawed Assumption:**
> "GITHUB_ACTOR might be the bot's actual username when it posts"

**Reality:**
- When workflow uses `secrets.GITHUB_TOKEN`, bot posts as `github-actions[bot]`
- `GITHUB_ACTOR` is ALWAYS the workflow trigger user, never changes based on token type
- The PAT issue was solved by switching tokens, not by checking GITHUB_ACTOR

### The Correct Approach:
- Bot identity = `SUPPORTBOT_USERNAME` environment variable (set in workflow)
- Workflow token = `secrets.GITHUB_TOKEN` (built-in)
- `GITHUB_ACTOR` = workflow trigger context (NOT bot identity)

---

## ✅ Validation Tests After Fix

### Test 1: Issue Opener (KeerthiYasasvi)
1. Open issue
2. Bot responds with questions ✅
3. User answers
4. Bot asks follow-ups (Loop 2) ✅
5. User answers
6. Bot finalizes or escalates (Loop 3) ✅

### Test 2: Different User (yk617)  
1. yk617 comments on issue ❌ Should be ignored
2. Bot does NOT respond ✅
3. yk617 posts "/diagnose" ✅ Added to allow list
4. yk617 comments again
5. Bot responds (yk617 Loop 1) ✅
6. Verify yk617 and KeerthiYasasvi have separate loop counters ✅

### Test 3: Bot Self-Comment
1. Bot posts comment ✅
2. Workflow triggers BUT...
3. Workflow filter prevents run ✅ (workflow-level defense)
4. If filter bypassed, code checks bot identity ✅ (code-level defense)

---

## 📋 Files to Fix

1. **src/SupportConcierge.Core/Modules/Workflows/Executors/GuardrailsExecutor.cs**
   - Lines 33-35: Remove GITHUB_ACTOR fallback
   - Lines 37-43: Check only SUPPORTBOT_USERNAME

2. **src/SupportConcierge.Core/Modules/Workflows/Executors/LoadStateExecutor.cs**  
   - Lines 59-62: Remove GITHUB_ACTOR fallback
   - Lines 68-73: Check only SUPPORTBOT_USERNAME

---

## 🔬 Why Critique Scores Are Deterministic (Separate Issue)

This is **NOT** related to the multi-user bug. The deterministic scores (5, 2, 2) are likely due to:

1. **Low-quality first-pass generation** - Agents not using context effectively
2. **Critique agent working but refinement not helping** - Even with real gpt-4o-mini critique, if the base model isn't improving, scores stay low
3. **Possible temperature=0 in wrong places** - Need to check agent generation temperatures

**To investigate separately:**
- Check OPENAI_MODEL and OPENAI_CRITIQUE_MODEL settings
- Review temperature settings in agent LLM calls
- Verify prompt quality in triage/research/response prompts

---

## 🎯 Summary

**Root Cause:** Bot uses `GITHUB_ACTOR` (workflow trigger user) as fallback bot identity, causing it to think every commenter is the bot itself.

**Fix:** Remove `GITHUB_ACTOR` from bot identity logic entirely. Only use `SUPPORTBOT_USERNAME`.

**Why It Wasn't Caught:** The infinite loop fix (workflow filter) masked this bug. Multi-user testing revealed it.

**Severity:** CRITICAL - Multi-user support completely broken

**ETA to Fix:** 15 minutes (2 file changes, remove 4 lines, update 4 lines)

