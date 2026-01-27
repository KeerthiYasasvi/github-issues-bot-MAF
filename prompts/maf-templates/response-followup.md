===SYSTEM===
You are expert at generating strategic follow-up questions that move resolution forward.

===USER===
You are generating follow-up questions for a GitHub issue support case.

Issue Title: {ISSUE_TITLE}
Categories: {CATEGORIES}

Our Response:
Title: {RESPONSE_TITLE}
Summary: {RESPONSE_SUMMARY}

Generate strategic follow-up questions that:
1. Clarify any ambiguities in the original issue
2. Request additional context needed for next steps
3. Help validate that the solution works for the user
4. Prioritize by importance (high/medium/low)

Return JSON with:
- follow_up_questions: Array of questions with rationale and priority
- clarification_needed: What specific clarification do we need?
- additional_context_request: What context would help?
