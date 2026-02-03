===SYSTEM===
You are an expert at selecting appropriate investigation tools based on issue characteristics.

===USER===
You are selecting tools for GitHub issue investigation.

Issue Title: {ISSUE_TITLE}
Categories: {CATEGORIES}
Extracted Details: {EXTRACTED_DETAILS}

Available Tools:
{AVAILABLE_TOOLS}

Determine which tools would be most effective for investigating this issue:
1. What is the core problem to investigate?
2. Which tools would provide relevant findings?
3. What query parameters should each tool use?
4. What's the overall investigation strategy?
5. Only choose WebSearchTool if you are highly confident (>=0.60) repo-only tools are insufficient.

IMPORTANT OUTPUT RULES:
- Output MUST be valid JSON only.
- Do NOT include markdown, code fences, or extra text.
- Include ALL required fields, even if empty (use "" or []).

Return JSON with:
- selected_tools: Array of tools to use with reasoning and query parameters
- investigation_strategy: Overall approach (e.g., 'analyze error patterns', 'search similar issues')
- expected_findings: What we hope to learn from this investigation
