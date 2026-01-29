# GitHub Issues Support Bot (MAF Workflows)

A GitHub Action that triages issues and comments using Microsoft Agent Framework (MAF) style workflows in C#. It loads repo-defined Spec Packs, extracts a structured case packet deterministically first, asks targeted follow-ups (max 3 loops), and produces an engineer-ready brief with routing labels/assignees. State persists across runs via a hidden HTML comment.

## Why this exists

- Deterministic-first extraction before any LLM call
- Schema-validated JSON outputs with repair retry
- Guardrails for author gating, /stop, and disagreement handling
- E2E eval suite + CI gates for quality and budget control

## Workflow at a glance

```
ParseEvent -> LoadSpecPack -> LoadPriorState -> ApplyGuardrails -> ExtractCasePacket -> ScoreCompleteness
  -> Stop: AcknowledgeStop -> PersistState
  -> NeedsInfo: GenerateFollowUps -> ValidateFollowUps -> PostFollowUpComment -> PersistState
  -> Actionable: SearchDuplicates -> FetchGroundingDocs -> GenerateEngineerBrief -> ValidateBrief
              -> PostFinalBriefComment -> ApplyRouting -> PersistState
  -> LoopsExhausted: Escalate -> PersistState
```

More details: `docs/ARCHITECTURE.md`

## Quick start

### Option A: Deploy as Submodule (Recommended)

For test repositories that need version-controlled bot deployments:

1. **Add bot as submodule to your test repo:**
   ```bash
   cd your-test-repo
   git submodule add https://github.com/KeerthiYasasvi/github-issues-bot-MAF.git bot
   git submodule update --init --recursive
   ```

2. **Create workflow file** (`.github/workflows/supportbot.yml`):
   ```yaml
   name: Support Concierge Bot
   
   on:
     issues:
       types: [opened, edited]
     issue_comment:
       types: [created]
   
   permissions:
     contents: write
     issues: write
   
   jobs:
     supportbot:
       runs-on: ubuntu-latest
       steps:
         - name: Checkout
           uses: actions/checkout@v4
           with:
             submodules: recursive  # ← Gets pinned bot version
   
         - name: Setup .NET
           uses: actions/setup-dotnet@v4
           with:
             dotnet-version: 8.0.x
   
         - name: Run Support Concierge
           if: github.event_name == 'issues' || github.event.comment.user.login != 'github-actions[bot]'
           run: dotnet run --project bot/src/SupportConcierge.Cli -- --event-file "$GITHUB_EVENT_PATH"
           env:
             GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
             SUPPORTBOT_USERNAME: github-actions[bot]
             OPENAI_API_KEY: ${{ secrets.API_KEY }}
             OPENAI_MODEL: ${{ vars.PRIMARY_MODEL }}
             OPENAI_CRITIQUE_MODEL: ${{ vars.SECONDARY_MODEL }}
             SUPPORTBOT_DRY_RUN: ${{ vars.SUPPORTBOT_DRY_RUN || 'false' }}
             SUPPORTBOT_WRITE_MODE: ${{ vars.SUPPORTBOT_WRITE_MODE || 'true' }}
   
         - name: Upload metrics
           if: always()
           uses: actions/upload-artifact@v4
           with:
             name: supportbot-metrics
             path: artifacts/metrics
   ```

3. **Configure repo variables/secrets:**
   - `PRIMARY_MODEL` (repo variable, e.g., "gpt-4o")
   - `SECONDARY_MODEL` (repo variable, e.g., "gpt-4o-mini")
   - `API_KEY` (secret, your OpenAI API key)
   - `SUPPORTBOT_DRY_RUN` (repo variable, "true"/"false")
   - `SUPPORTBOT_WRITE_MODE` (repo variable, "true"/"false")

4. **Update bot version:**
   ```bash
   cd bot
   git pull origin main
   cd ..
   git add bot
   git commit -m "Update bot to [commit-sha]"
   git push
   ```

**Why Submodules?**
- ✅ Version locking - each test commit pins bot version
- ✅ Reproducibility - recreate historical scenarios
- ✅ No race conditions - submodule pointer is committed
- ✅ Clear deployment - explicit version updates

### Option B: Direct Deployment (Bot Repo Only)

For deploying the bot in its own repository:

1) Configure repo variables/secrets
- `OPENAI_MODEL` (repo variable)
- `OPENAI_API_KEY` (secret)
- `SUPPORTBOT_DRY_RUN` (repo variable, true/false)
- `SUPPORTBOT_WRITE_MODE` (repo variable, true/false)

2) Add the runtime workflow
- Copy `.github/workflows/supportbot.yml` into your repo, or use the reusable workflow described in `docs/DEPLOYMENT.md`.

3) Optional spec pack override
- Defaults to `.supportbot`. Set `SUPPORTBOT_SPEC_DIR` to point elsewhere.

## Local run

Simulate a run from a saved GitHub event JSON:

```bash
dotnet run --project src/SupportConcierge.Cli -- --event-file path/to/event.json --dry-run
```

Run a quick smoke workflow using the sample fixture:

```bash
dotnet run --project src/SupportConcierge.Cli -- --smoke
```

## Evals

The previous CLI eval runner has been removed during the MAF migration. Current status:
- CLI eval entrypoint: **removed** (EvalRunner.cs deleted)
- Scenarios and fixtures remain in `evals/` for future reuse
- To re-enable evals, wire a new MAF-based runner that loads scenarios from `evals/` and executes the MAF workflow with `SUPPORTBOT_NO_LLM=true` for deterministic validation.

Details: `docs/EVALS.md`

## Repo layout

- `src/SupportConcierge.Core` - workflow, agents, tools, models (MAF DAG)
- `src/SupportConcierge.Cli` - runtime entrypoint (MAF workflow execution)
- `evals/` - fixtures and reports
- `tests/` - deterministic plumbing tests
- `.supportbot/` - spec pack config and playbooks

## Proof we use MAF

Packages:
- `Microsoft.Agents.AI.Workflows` (1.0.0-preview.260108.1)
- `Microsoft.Agents.AI` (1.0.0-preview.260108.1)

Current workflow wiring (MAF DAG):

```csharp
// SupportConciergeWorkflow.Build(...)
ParseEvent → Guardrails → (if /stop → PersistState)
                       → Triage → Research → Response → OrchestratorEvaluate
OrchestratorEvaluate → PersistState (if finalize/escalate/follow_up or loop>=3)
OrchestratorEvaluate → Triage (if continue and loop<3)
```

Runtime execution:

```csharp
var workflow = SupportConciergeWorkflow.Build(triage, research, response, critic, orchestrator, tools);
var run = await InProcessExecution.RunAsync(workflow, eventInput);
```

## License

MIT (replace as needed)
