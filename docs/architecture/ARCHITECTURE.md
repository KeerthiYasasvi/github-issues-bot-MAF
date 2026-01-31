# Architecture

## Workflow DAG

```
ParseEvent -> LoadSpecPack -> LoadPriorState -> ApplyGuardrails -> ExtractCasePacket -> ScoreCompleteness
  -> Stop: AcknowledgeStop -> PersistState
  -> NeedsInfo: GenerateFollowUps -> ValidateFollowUps -> PostFollowUpComment -> PersistState
  -> Actionable: SearchDuplicates -> FetchGroundingDocs -> GenerateEngineerBrief -> ValidateBrief
              -> PostFinalBriefComment -> ApplyRouting -> PersistState
  -> LoopsExhausted: Escalate -> PersistState
```

The workflow is defined with `WorkflowBuilder` and each step is implemented as an `Executor<TInput, TOutput>` that overrides `HandleAsync`.

## Agents

- `ClassifierAgent` - category prediction when keyword heuristics are inconclusive
- `ExtractorAgent` - fills missing case packet fields after deterministic parsing
- `FollowUpAgent` - generates targeted questions (max 3)
- `BriefAgent` - produces structured engineer brief with schema validation
- `JudgeAgent` - optional rubric scoring in eval mode

## Tools

- `GitHubTool` - read/write issues, comments, labels, assignees, and repo docs
- `SpecPackTool` - loads and validates `.supportbot` YAML and playbooks
- `StateStoreTool` - embeds/extracts state blobs via HTML comments, with pruning + compression
- `MetricsTool` - step timers, token usage, decision path logging

## Deterministic-first extraction

1) Parse issue form headings (`## Heading`) into normalized field keys.
2) Extract key:value lines from the issue body.
3) Merge deterministic fields and mark them as authoritative.
4) Redact secrets before any LLM call.
5) Call the LLM only to fill missing fields; do not override deterministic fields.

## State persistence

- Stored as an HTML comment appended to bot comments.
- Extracts the latest state blob even if the comment is quoted.
- Prunes large lists (ex: asked fields) and compresses if size exceeds threshold.

## Guardrails

- Only issue author drives the workflow, except `/diagnose` and `/stop`.
- Follow-up loop cap and brief regeneration cap.
- Finalization flag prevents duplicate posting.
- Disagreement detection allows a limited brief regeneration before escalation.
- Secret redaction occurs before any LLM prompt; redaction stats are logged.
