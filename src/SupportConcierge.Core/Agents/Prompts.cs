namespace SupportConcierge.Core.Agents;

public static class Prompts
{
    public static string CategoryClassification(string title, string body, string categories)
    {
        return $@"You are a GitHub issue triage assistant. Classify this issue into one of the following categories and respond in JSON format:

{categories}

Issue Title: {title}

Issue Body:
{body}

Return your classification in JSON format with confidence score (0-1) and brief reasoning.";
    }

    public static string ExtractCasePacket(string issueBody, string comments, string requiredFields)
    {
        return $@"You are a precise information extraction assistant. Extract structured fields from this GitHub issue and respond in JSON format.

Required fields to extract (extract ONLY what is explicitly present, leave fields empty if not found):
{requiredFields}

Issue Body:
{issueBody}

Additional Comments:
{comments}

Extract the information into JSON format. Use exact text from the issue/comments. If a field is not present or unclear, leave it as an empty string. Do not invent or infer information that isn't explicitly stated.";
    }

    public static string GenerateFollowUpQuestions(string issueBody, string category, List<string> missingFields, List<string> askedBefore)
    {
        var askedList = askedBefore.Count > 0
            ? $"\n\nFields already asked about (do NOT ask again): {string.Join(", ", askedBefore)}"
            : string.Empty;

        return $@"You are a helpful GitHub support bot. The user has submitted a {category} issue, but it's missing critical information. Respond with a JSON formatted list of questions.

Issue so far:
{issueBody}

Missing fields that need to be collected: {string.Join(", ", missingFields)}{askedList}

Generate up to 3 targeted, friendly follow-up questions to gather the missing information in JSON format. Be specific about what format you need (e.g., ""full error message including stack trace"", ""exact version number""). Make questions concise and actionable.

IMPORTANT GUARDRAILS - NEVER DO THIS:
- Never ask for passwords, API keys, tokens, secrets, or credentials
- Never ask users to share actual credentials even if field names suggest it (e.g., if field is 'reddit_credentials', ask for CONFIRMATION it's set, not the actual credentials)
- Never ask for connection strings, database URLs, or other sensitive connection details with actual values
- Never ask for authentication tokens, bearer tokens, or authorization codes
- Focus on diagnostic information only (logs, errors, versions, configuration names, not values)
- Be friendly and respectful

If a missing field appears to be credentials-related (e.g., 'credentials', 'api_key', 'token', 'password', 'secret', 'auth'), ask for CONFIRMATION that it's configured correctly, NOT for the actual value.";
    }

    public static string GenerateEngineerBrief(string issueBody, string comments, string category, Dictionary<string, string> extractedFields, string playbook, string repoDocs, string duplicateIssues)
    {
        var fieldsText = string.Join("\n", extractedFields.Select(kvp => $"- {kvp.Key}: {kvp.Value}"));
        var duplicatesSection = !string.IsNullOrEmpty(duplicateIssues)
            ? $"\n\nPotentially Related Issues:\n{duplicateIssues}"
            : string.Empty;

        return $@"You are an expert technical support engineer. Create a concise, actionable brief in JSON format for engineers to investigate this {category} issue.

Original Issue:
{issueBody}

Additional Information from Follow-ups:
{comments}

Extracted Fields:
{fieldsText}

Relevant Playbook Guidance:
{playbook}

Repository Documentation Context:
{repoDocs}{duplicatesSection}

Generate a comprehensive engineer brief in JSON format with:
1. One-sentence summary
2. Key symptoms
3. Reproduction steps (if available)
4. Environment details
5. Critical evidence snippets (keep short - max 2-3 lines per snippet)
6. Suggested next steps (grounded in the playbook and repo docs - do NOT suggest steps that contradict repo documentation)
7. validation_confirmations: 2-3 quick yes/no questions that confirm the suggested steps apply to this specific issue

IMPORTANT:
- Include validation_confirmations as a list of strings with specific yes/no questions that check if the recommended steps apply
- Base next_steps ONLY on the provided playbook and documentation
- Do NOT invent commands, file paths, or procedures not mentioned in the context
- If duplicate issues are provided, include them in possible_duplicates as an array of objects with issue_number (int) and similarity_reason (string). Otherwise, omit the possible_duplicates field entirely
- Keep evidence snippets short and relevant
- Be factual and precise
- Format response as JSON
- Include all required fields: summary, symptoms, environment, key_evidence, next_steps, validation_confirmations";
    }

    public static string RegenerateEngineerBrief(string previousBrief, string userFeedback, Dictionary<string, string> extractedFields, string playbook, string category)
    {
        var fieldsText = string.Join("\n", extractedFields.Select(kvp => $"- {kvp.Key}: {kvp.Value}"));

        return $@"You are an expert technical support engineer. The user indicated the previous brief didn't match their situation. Generate a REVISED engineer brief in JSON format based on their feedback.

Previous Brief (that didn't work):
{previousBrief}

User's Feedback/Clarification:
{userFeedback}

Updated Fields (from user):
{fieldsText}

Playbook Reference:
{playbook}

Generate a REVISED brief in JSON format focusing on:
1. Updated summary (reflecting user's clarification)
2. Revised key symptoms (aligned with actual error from user)
3. Alternative reproduction steps
4. Updated environment details
5. Different critical evidence snippets based on user feedback
6. Alternative suggested next steps (different approach from first brief)
7. validation_confirmations: 2-3 NEW yes/no questions to confirm THIS revised brief applies

IMPORTANT:
- This is a REVISION based on user disagreement - propose fundamentally different next steps than before
- Focus on the user's specific clarification in their feedback
- Include 2-3 validation questions that test if this revised approach is correct
- Format response as JSON with all required fields: summary, symptoms, environment, key_evidence, next_steps, validation_confirmations
- Be precise and grounded in the playbook
- Do NOT invent commands or procedures not in the playbook";
    }
}
