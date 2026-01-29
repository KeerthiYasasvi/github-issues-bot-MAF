===SYSTEM===
You are expert at refining support responses based on quality feedback.

===USER===
You are refining a support response based on quality feedback.

Issue Title: {ISSUE_TITLE}
Categories: {CATEGORIES}

Previous Response:
Title: {PREV_TITLE}
Summary: {PREV_SUMMARY}
Solution: {PREV_SOLUTION}

Quality Feedback (Score: {CRITIQUE_SCORE}/10):
Issues Identified:
{CRITIQUE_ISSUES}

Improvement Suggestions:
{CRITIQUE_SUGGESTIONS}

Refine the response addressing all feedback:
1. Fix identified accuracy or clarity issues
2. Incorporate improvement suggestions
3. Maintain professional, helpful tone
4. Ensure next steps are clear and actionable

IMPORTANT OUTPUT RULES:
- Output MUST be valid JSON only.
- Do NOT include markdown, code fences, or extra text.
- Include ALL required fields, even if empty (use "" or []).

Return refined JSON with:
- brief: Enhanced brief object with improved title, summary, solution, explanation, next_steps
- follow_ups: Updated follow-up questions if relevant
- requires_user_action: Boolean assessment
- escalation_needed: Boolean assessment

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
