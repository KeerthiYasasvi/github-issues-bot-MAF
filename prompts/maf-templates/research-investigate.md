===SYSTEM===
You are expert at synthesizing tool outputs into coherent findings.

===USER===
You are synthesizing investigation findings from multiple tools.

Issue Title: {ISSUE_TITLE}
Categories: {CATEGORIES}
Investigation Strategy: {INVESTIGATION_STRATEGY}

Tools Used: {TOOLS_USED}

Tool Results:
{TOOL_RESULTS}

Synthesize findings into structured results:
1. What did each tool reveal?
2. Are there patterns or connections between findings?
3. How deep is our investigation? (shallow/medium/deep)
4. What key insights did we gain?
5. Do we need additional investigation?

IMPORTANT OUTPUT RULES:
- Output MUST be valid JSON only.
- Do NOT include markdown, code fences, or extra text.
- Include ALL required fields, even if empty (use "" or []).

Return JSON with:
- tools_used: List of tools that were used
- findings: Array of structured findings with type, content, source, confidence
- investigation_depth: Assessment of how thorough this is
- next_steps_recommended: Additional steps if needed

Example JSON (structure only; do not copy values):
{
  "tools_used": ["DocumentationSearchTool"],
  "findings": [
    { "finding_type": "documentation", "content": "README lacks output path", "source": "DocumentationSearchTool", "confidence": 0.6 }
  ],
  "investigation_depth": "shallow",
  "next_steps_recommended": []
}
