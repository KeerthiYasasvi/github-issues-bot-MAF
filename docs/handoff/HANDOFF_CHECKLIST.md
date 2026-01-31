# Handoff Checklist

Use this as a fast, one‑page guide for the next agent.

## 1) Repo context
- Project root: `D:\Projects\agents\ms-quickstart\ghithub-issues-bot-MAF\Github-issues-bot-with-MAF`
- Language/runtime: C# (.NET 8)
- Workflow engine: Microsoft Agent Framework (MAF) Workflows

## 2) Confirm MAF usage
- Packages: `Directory.Packages.props` includes `Microsoft.Agents.AI` + `Microsoft.Agents.AI.Workflows`
- Executors: `src/SupportConcierge.Core/Modules/Workflows/Executors/*.cs` inherit `Executor<TInput,TOutput>`
- Workflow DAG: `src/SupportConcierge.Core/Modules/Workflows/SupportConciergeWorkflow.cs` uses `WorkflowBuilder`
- Execution: `src/SupportConcierge.Core/Modules/Workflows/SupportConciergeRunner.cs` uses `InProcessExecution.RunAsync`

## 3) Workflow DAG summary
ParseEvent → LoadSpecPack → LoadPriorState → ApplyGuardrails → ExtractCasePacket → ScoreCompleteness
- Stop: AcknowledgeStop → PersistState
- NeedsInfo: GenerateFollowUps → ValidateFollowUps → PostFollowUpComment → PersistState
- Actionable: SearchDuplicates → FetchGroundingDocs → GenerateEngineerBrief → ValidateBrief
  → PostFinalBriefComment → ApplyRouting → PersistState
- Loops exhausted: Escalate → PersistState

## 4) Key design rules
- Deterministic extraction first, LLM only fills gaps.
- Secret redaction before any LLM call.
- Max follow‑up loops: `SUPPORTBOT_MAX_FOLLOWUP_LOOPS` (default 3).
- Brief regeneration cap: `SUPPORTBOT_MAX_BRIEF_REGEN` (default 2).
- State persisted in hidden HTML comment.

## 5) Run and verify
From repo root:
```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/SupportConcierge.Cli -- --smoke
```

Deterministic evals:
```powershell
SUPPORTBOT_NO_LLM=true dotnet run --project src/SupportConcierge.Cli -- --eval
```

## 6) Config variables
Required:
- `GITHUB_TOKEN`
- `OPENAI_API_KEY`
- `OPENAI_MODEL`

Optional:
- `SUPPORTBOT_DRY_RUN` (true/false)
- `SUPPORTBOT_WRITE_MODE` (true/false)
- `SUPPORTBOT_SPEC_DIR` (default `.supportbot`)
- `SUPPORTBOT_METRICS_DIR` (default `artifacts/metrics`)

## 7) Spec pack paths
- `.supportbot/categories.yaml`
- `.supportbot/checklists.yaml`
- `.supportbot/validators.yaml`
- `.supportbot/routing.yaml`
- `.supportbot/playbooks/*.md`

## 8) Deployment options
- Reusable workflow: `.github/workflows/supportbot.yml`
- CI gates: `.github/workflows/ci.yml`
- See `docs/deployment/DEPLOYMENT.md`

