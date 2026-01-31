# Support Concierge Bot - Test Plan (Runnable)

This test plan is designed to validate that the bot gathers enough information (primary goal) and, when possible, provides a useful response (secondary goal).

Use the **Issue Templates** section to copy/paste into GitHub issues and comments. Each case includes:
- Expected bot behavior
- Quick verification checklist

---

## How to Use
1) Open a new issue in the test repo.
2) Paste the "Issue Template" for the test case.
3) Watch the bot reply.
4) Use the checklist to confirm the correct behavior.

Notes:
- For multi-user tests, comment from a second account.
- If you need to reset the conversation for a user, comment `/diagnose`.

---

# Test Cases

## 1) Docs - Missing Info (well formatted)
**Goal:** Bot asks clarifying questions.

**Issue Template:**
Title: README missing Spark output path

Body:
The README says Spark writes Parquet files, but it does not say where the files are written.
Please clarify the output path.

**Expected:**
- Bot asks: which file/section, what’s missing, what it should say.
- Loop 1/3.

**Checklist:**
- [ ] Asks docs‑specific questions (file/section, missing info, replacement text)
- [ ] Mentions Loop 1/3
- [ ] Does not finalize

---

## 2) Docs - Complete Info (well formatted)
**Goal:** Bot finalizes with summary.

**Issue Template:**
Title: README should specify Spark output directory

Body:
File: README.md  
Section: Running the Pipeline  
Problem: The README says Spark writes Parquet but does not specify the exact output directory.  
Fix: Add “Spark writes Parquet files under spark_output/ (project root).”

**Expected:**
- Bot produces final summary + next steps.
- No follow‑up questions.

**Checklist:**
- [ ] Produces summary and recommended next steps
- [ ] No follow‑up questions
- [ ] Marks issue as actionable

---

## 3) Runtime Error - Missing Info (not well formatted)
**Goal:** Bot asks for OS/version/logs/steps.

**Issue Template (messy):**
App crashes. pls fix. error somewhere. running it and boom. need help asap

**Expected:**
- Bot asks for exact error/logs, OS/runtime versions, repro steps.

**Checklist:**
- [ ] Asks for error logs
- [ ] Asks for OS/runtime versions
- [ ] Asks for steps to reproduce

---

## 4) Runtime Error - Partial Info (well formatted)
**Goal:** Ask only missing info, not repeat.

**Issue Template:**
Title: Crash on startup

Body:
Error: "TypeError: Cannot read property 'duration' of undefined"
Steps:
1. npm install
2. npm run analyze -- --input data/sample-stream.json
Missing: I did not note OS or Node version.

**Expected:**
- Asks for OS + Node/npm versions.
- Does not re‑ask for error or steps.

**Checklist:**
- [ ] Asks for OS and version details only
- [ ] Does not repeat error/steps request

---

## 5) Bug Report - Complete Info (well formatted)
**Goal:** Finalize with summary + suggested fix.

**Issue Template:**
Title: Analyzer fails for live streams without duration

Body:
Error: TypeError: Cannot read property 'duration' of undefined
File: src/analyzer.js:45
Env: Ubuntu 22.04, Node 18.17.0, npm 9.6.7
Repro:
1) npm install
2) npm run analyze -- --input data/live-streams.json
Expected: Analyzer should skip missing durations
Actual: Crashes on first item

**Expected:**
- Bot finalizes.
- Includes summary + suggested fix + next steps.

**Checklist:**
- [ ] Summary matches issue
- [ ] Suggested fix is reasonable (null check)
- [ ] No follow‑up questions

---

## 6) Feature Request - Missing Info (not well formatted)
**Goal:** Ask for problem + desired behavior.

**Issue Template (messy):**
pls add feature to export everything

**Expected:**
- Bot asks: problem it solves, expected behavior, alternatives.

**Checklist:**
- [ ] Asks why feature is needed
- [ ] Asks how it should work
- [ ] Asks for alternatives or similar solutions

---

## 7) Config Issue - Partial Info (well formatted)
**Goal:** Ask for config file/setting and current behavior.

**Issue Template:**
Title: Config not picked up

Body:
I edited a config but the app still uses defaults.
I don’t remember which config file it reads from.

**Expected:**
- Bot asks which config file/setting, expected vs actual.

**Checklist:**
- [ ] Asks which config file or setting
- [ ] Asks expected vs actual behavior

---

## 8) Off‑Topic Comment (needs second account)
**Goal:** Redirect off‑topic comment to new issue.

**Setup:**
- Open an issue about submodule/state testing.

**Comment (from another user):**
/diagnose The README says Spark writes Parquet but doesn’t say where. Please add the path.

**Expected:**
- Bot says comment is off‑topic and asks to open a new issue.

**Checklist:**
- [ ] Off‑topic redirect message appears
- [ ] Suggests opening new issue or replying in correct thread

---

## 9) Non‑Author Comment without /diagnose
**Goal:** Guardrails ignore comment.

**Comment (from another user):**
I have this issue too. Any updates?

**Expected:**
- No bot response.
- Workflow logs show “not in allow list.”

**Checklist:**
- [ ] No bot comment
- [ ] Guardrails log shows “not in allow list”

---

## 10) /diagnose Allow‑List Join (second account)
**Goal:** Allow non‑author to interact.

**Comment:**
/diagnose I can reproduce this. Here are my steps...

**Expected:**
- Bot responds and starts Loop 1/3 for that user.

**Checklist:**
- [ ] Bot responds to second user
- [ ] Loop starts at 1/3

---

## 11) /stop Command
**Goal:** Stop conversation cleanly.

**Comment:**
/stop

**Expected:**
- Bot replies it will stop asking questions.

**Checklist:**
- [ ] Stop confirmation message
- [ ] No more follow‑ups for that user

---

## 12) Loop Exhaustion Escalation
**Goal:** Escalate after 3 low‑info replies.

**Flow:**
- Provide vague answers 3 times.

**Expected:**
- Bot escalates to maintainer.

**Checklist:**
- [ ] Escalation message after 3 loops
- [ ] Mentions maintainer review

---

# Paste‑Ready Checklist (for each issue)
Copy into the issue as a comment after the bot replies:

- [ ] Bot understood the category correctly
- [ ] Bot requested only missing info
- [ ] Bot avoided asking for secrets/tokens
- [ ] Loop count is correct
- [ ] Final response is clean if info is complete
- [ ] Redirects off‑topic comments (if applicable)

