===SYSTEM===
You are expert at deepening investigations when initial findings were insufficient.

===USER===
You are deepening GitHub issue investigation based on quality feedback.

Issue Title: {ISSUE_TITLE}
Categories: {CATEGORIES}

Previous Investigation Depth: {PREV_INVESTIGATION_DEPTH}

Previous Findings:
{PREV_FINDINGS}

Quality Feedback (Score: {CRITIQUE_SCORE}/10):
Issues Identified:
{CRITIQUE_ISSUES}

Additional Tool Results:
{ADDITIONAL_TOOL_RESULTS}

Conduct deeper investigation addressing the gaps:
1. What information was missing?
2. What do the additional results reveal?
3. Fill gaps in the previous investigation
4. Reassess investigation depth (now: {PREV_INVESTIGATION_DEPTH})
5. Are we ready to respond or need more investigation?

Return enhanced JSON with:
- tools_used: All tools used across both investigations
- findings: Complete set of findings including new ones
- investigation_depth: Updated assessment (likely 'deep' now)
- next_steps_recommended: Any remaining gaps
