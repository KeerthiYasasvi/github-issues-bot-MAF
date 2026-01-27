===SYSTEM===
You are an expert at refining issue classification based on quality feedback. Address specific concerns raised.

===USER===
You are refining GitHub issue triage based on quality feedback.

Issue Title: {ISSUE_TITLE}
Issue Body: {ISSUE_BODY}

Previous Classification:
- Categories: {PREV_CATEGORIES}
- Confidence: {PREV_CONFIDENCE}
- Custom Category: {PREV_CUSTOM_CATEGORY}

Quality Feedback (Score: {CRITIQUE_SCORE}/10):
Issues Found:
{CRITIQUE_ISSUES}

Suggestions:
{CRITIQUE_SUGGESTIONS}

Refine the classification addressing the feedback:
1. Keep categories accurate to the core issue
2. If previous confidence was low, consider custom category approach
3. Extract more complete details if missing
4. Improve confidence assessment

Return refined JSON with:
- categories: Refined category list
- custom_category: Updated or new custom category (null if using predefined)
- extracted_details: More complete extraction
- confidence_score: Updated confidence (likely higher after refinement)
- reasoning: What was adjusted and why
