namespace SupportConcierge.Core.Agents;

public static class Schemas
{
    public static readonly string CategoryClassificationSchema = """
{
  "type": "object",
  "properties": {
    "category": { "type": "string" },
    "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
    "reasoning": { "type": "string" }
  },
  "required": ["category", "confidence", "reasoning"],
  "additionalProperties": false
}
""";

    public static readonly string CasePacketSchema = """
{
  "type": "object",
  "properties": {
    "operating_system": { "type": "string" },
    "version": { "type": "string" },
    "runtime_version": { "type": "string" },
    "build_tool_version": { "type": "string" },
    "error_message": { "type": "string" },
    "stack_trace": { "type": "string" },
    "steps_to_reproduce": { "type": "string" },
    "expected_behavior": { "type": "string" },
    "actual_behavior": { "type": "string" },
    "build_command": { "type": "string" },
    "build_log": { "type": "string" },
    "installation_method": { "type": "string" },
    "input_data": { "type": "string" },
    "screenshots": { "type": "string" },
    "doc_location": { "type": "string" },
    "issue_description": { "type": "string" },
    "suggested_fix": { "type": "string" }
  },
  "required": [
    "operating_system",
    "version",
    "runtime_version",
    "build_tool_version",
    "error_message",
    "stack_trace",
    "steps_to_reproduce",
    "expected_behavior",
    "actual_behavior",
    "build_command",
    "build_log",
    "installation_method",
    "input_data",
    "screenshots",
    "doc_location",
    "issue_description",
    "suggested_fix"
  ],
  "additionalProperties": false
}
""";

    public static readonly string FollowUpQuestionsSchema = """
{
  "type": "object",
  "properties": {
    "questions": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "field": { "type": "string" },
          "question": { "type": "string" },
          "why_needed": { "type": "string" }
        },
        "required": ["field", "question", "why_needed"],
        "additionalProperties": false
      },
      "maxItems": 3
    }
  },
  "required": ["questions"],
  "additionalProperties": false
}
""";

    public static readonly string EngineerBriefSchema = """
{
  "type": "object",
  "properties": {
    "summary": { "type": "string" },
    "symptoms": { "type": "array", "items": { "type": "string" } },
    "repro_steps": { "type": "array", "items": { "type": "string" } },
    "environment": { "type": "object", "additionalProperties": true },
    "key_evidence": { "type": "array", "items": { "type": "string" } },
    "next_steps": { "type": "array", "items": { "type": "string" } },
    "validation_confirmations": { "type": "array", "items": { "type": "string" }, "minItems": 2, "maxItems": 3 },
    "possible_duplicates": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "issue_number": { "type": "integer", "minimum": 1 },
          "similarity_reason": { "type": "string", "minLength": 1 }
        },
        "required": ["issue_number", "similarity_reason"],
        "additionalProperties": false
      },
      "maxItems": 5
    }
  },
  "required": ["summary", "symptoms", "environment", "key_evidence", "next_steps", "validation_confirmations"],
  "additionalProperties": false
}
""";
}
