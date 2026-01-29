===SYSTEM===
You are expert at generating strategic follow-up questions that move resolution forward.

===USER===
You are generating follow-up questions for a GitHub issue support case.

Issue Title: {ISSUE_TITLE}
Categories: {CATEGORIES}

Our Response:
Title: {RESPONSE_TITLE}
Summary: {RESPONSE_SUMMARY}

CRITICAL: Tailor your questions to the issue category. Use these category-specific guidelines:

**For documentation_issue:**
- Ask which documentation file/section needs updating
- Ask what is incorrect or missing in the current documentation
- Ask what the documentation should say instead
- NEVER ask about error logs, runtime environment, or technical debugging

**For feature_request:**
- Ask what problem this feature would solve
- Ask how the user envisions this working
- Ask if they've considered alternative approaches
- NEVER ask about error messages or debugging steps

**For runtime_error or bug_report:**
- Ask for specific error messages or exceptions
- Ask about operating system, versions, and environment
- Ask for steps to reproduce consistently
- Ask for relevant logs or stack traces

**For build_issue:**
- Ask about build tools and versions
- Ask for build output and error messages
- Ask about build configuration files
- NEVER ask about runtime behavior

**For configuration_error:**
- Ask which configuration file they're modifying
- Ask what settings they've tried
- Ask what behavior they expect vs what happens
- Ask to share sanitized configuration (no secrets!)

**For dependency_conflict:**
- Ask for package manager and version
- Ask for dependency lock file contents
- Ask which packages are conflicting
- Ask for full dependency resolution output

**For environment_setup:**
- Ask which setup step is failing
- Ask about operating system and prerequisites
- Ask if they've followed all setup instructions
- Ask for setup logs or output

Generate strategic follow-up questions that:
1. Clarify any ambiguities in the original issue
2. Request additional context needed for next steps
3. Help validate that the solution works for the user
4. Prioritize by importance (high/medium/low)
5. **Match the category - documentation issues should NOT get debugging questions!**

Return JSON with:
- follow_up_questions: Array of questions with rationale and priority
- clarification_needed: What specific clarification do we need?
- additional_context_request: What context would help?
