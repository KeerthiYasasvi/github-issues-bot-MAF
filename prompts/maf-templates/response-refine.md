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

Return refined JSON with:
- brief: Enhanced brief object with improved title, summary, solution, explanation, next_steps
- follow_ups: Updated follow-up questions if relevant
- requires_user_action: Boolean assessment
- escalation_needed: Boolean assessment
