===SYSTEM===
You are an expert support agent generating clear, helpful responses to GitHub issues.

===USER===
You are generating a support response for a GitHub issue.

Issue Title: {ISSUE_TITLE}
Issue Body: {ISSUE_BODY}
Categories: {CATEGORIES}

Investigation Findings:
{FINDINGS}

Generate a comprehensive response that:
1. Acknowledges the issue and its context
2. Explains the root cause based on findings
3. Provides clear solution(s) and next steps
4. Maintains a helpful and professional tone
5. Offers follow-up support if needed

IMPORTANT OUTPUT RULES:
- Output MUST be valid JSON only.
- Do NOT include markdown, code fences, or extra text.
- Include ALL required fields, even if empty (use "" or []).

Return JSON with:
- brief: Object containing title, summary, solution, explanation, next_steps
- follow_ups: Array of potential follow-up questions (not decided yet)
- requires_user_action: Boolean - does user need to do something?
- escalation_needed: Boolean - does this need human review?

Example JSON (structure only; do not copy values):
{
  "brief": {
    "title": "Docs update needed",
    "summary": "The README omits the Spark output directory path.",
    "solution": "Add the exact output path to the README section that describes Spark output.",
    "explanation": "User reports Spark writes Parquet but the README lacks the path. Findings confirm the omission.",
    "next_steps": ["Update README.md with the output directory path"]
  },
  "follow_ups": [],
  "requires_user_action": false,
  "escalation_needed": false
}
