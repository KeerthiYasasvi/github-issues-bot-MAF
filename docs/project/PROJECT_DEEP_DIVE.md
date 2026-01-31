# GitHub Issues Support Bot - Deep Dive Analysis

**Project**: GitHub Issues Support Bot using Microsoft Agent Framework (MAF) Workflows  
**Language**: C# (.NET 8)  
**Purpose**: Automated GitHub issue triage, information gathering, and engineer brief generation using deterministic-first extraction + LLM agents

---

## 1. PROJECT OVERVIEW

### High-Level Goal
This bot automates GitHub issue triage by:
1. **Deterministically extracting** structured information from issue forms
2. **Asking targeted follow-up questions** (max 3 loops) to fill gaps
3. **Generating engineer briefs** with routing labels/assignees
4. **Persisting state** across bot interactions via hidden HTML comments

### Core Philosophy
- **Deterministic-first**: Extract via form parsing before invoking any LLM
- **Secret-safe**: Redact secrets before any LLM prompt
- **Schema-validated**: All LLM outputs validated against JSON schemas with repair retry
- **Stateful**: Full workflow state persisted for resumability

### Key Differentiator
Uses **Microsoft Agent Framework (MAF) Workflows** as the orchestration engine, not a generic orchestrator. This provides structured, DAG-based workflow definition with type-safe executors.

---

## 2. WORKFLOW DAG (THE CORE)

### Visual Flow

```
ParseEvent → LoadSpecPack → LoadPriorState → ApplyGuardrails → ExtractCasePacket → ScoreCompleteness
  ├─ STOP: AcknowledgeStop → PersistState
  ├─ NEEDS_INFO: GenerateFollowUps → ValidateFollowUps → PostFollowUpComment → PersistState
  ├─ ACTIONABLE: SearchDuplicates → FetchGroundingDocs → GenerateEngineerBrief → ValidateBrief
  │              → PostFinalBriefComment → ApplyRouting → PersistState
  └─ LOOPS_EXHAUSTED: Escalate → PersistState
```

### Decision Points (Conditional Edges)

1. **ApplyGuardrails → [AcknowledgeStop vs Extract]**
   - If `/stop` command detected → stop workflow
   - Otherwise → proceed to extraction

2. **ScoreCompleteness → [Escalate vs GenFollowups vs PostBrief vs SearchDupes]**
   - If `ShouldEscalate` → escalation path
   - Else if `ShouldAskFollowUps` → follow-up loop
   - Else if off-topic category → direct to finalize
   - Else if `ShouldFinalize` → brief generation path

3. **PostFinalBriefComment → [ApplyRouting vs Persist]**
   - If off-topic → skip routing, go directly to persist
   - Otherwise → apply routing (labels/assignees)

### Executor Architecture

Each step inherits from `Executor<TInput, TOutput>` and overrides `HandleAsync()`:
- **TInput**: Typically `RunContext` (the state bag)
- **TOutput**: Usually `RunContext` (mutated)
- All executors receive `WorkflowServices` (dependency container)
- Timing automatically captured via `_services.Metrics.StartStep()` / `EndStep()`

---

## 3. KEY EXECUTORS (IN SEQUENCE)

### Phase 1: Setup & Context
| Executor | Purpose | Input | Output |
|----------|---------|-------|--------|
| **ParseEventExecutor** | Extract GitHub event into RunContext | `EventInput` | `RunContext` |
| **LoadSpecPackExecutor** | Load repo's spec pack (categories, checklists, validators) | `RunContext` | `RunContext` + `SpecPack` |
| **LoadPriorStateExecutor** | Restore state from prior bot comment | `RunContext` | `RunContext` + `BotState` |
| **ApplyGuardrailsExecutor** | Check `/stop`, author gating, limits | `RunContext` | `RunContext` + `ShouldStop` flag |

### Phase 2: Deterministic Extraction
| Executor | Purpose |
|----------|---------|
| **ExtractCasePacketExecutor** | Parse issue form, classify category, merge deterministic fields, call LLM to fill gaps |
| **ScoreCompletenessExecutor** | Score extracted packet against checklist, decide if actionable/needs-info/escalate |

### Phase 3a: Follow-Up Loop (if NeedsInfo)
| Executor | Purpose |
|----------|---------|
| **GenerateFollowUpsExecutor** | LLM generates targeted follow-up questions |
| **ValidateFollowUpsExecutor** | Validate follow-up schema, check for duplicates from prior loops |
| **PostFollowUpCommentExecutor** | Post comment with questions, increment loop counter |
| **PersistStateExecutor** | Save state to HTML comment |

### Phase 3b: Brief Generation (if Actionable)
| Executor | Purpose |
|----------|---------|
| **SearchDuplicatesExecutor** | Find similar open issues |
| **FetchGroundingDocsExecutor** | Fetch relevant repo docs, playbook for category |
| **GenerateEngineerBriefExecutor** | LLM produces structured brief (summary, symptoms, repro, env, evidence, next steps, duplicates) |
| **ValidateBriefExecutor** | Schema validate brief, handle disagreement detection |
| **PostFinalBriefCommentExecutor** | Post brief comment, check for disagreement, decide if regen needed |
| **ApplyRoutingExecutor** | Apply labels/assignees per routing rules |

### Phase 3c: Escalation & Finalization
| Executor | Purpose |
|----------|---------|
| **EscalateExecutor** | Mark issue for escalation (e.g., too many loops) |
| **PersistStateExecutor** | Save final state (IsFinalized=true, prevents duplicate posts) |

---

## 4. CORE DATA MODELS

### RunContext (The State Bag)
Located: [src/SupportConcierge.Core/Modules/Models/RunContext.cs](src/SupportConcierge.Core/Modules/Models/RunContext.cs)

```csharp
public sealed class RunContext
{
    // GitHub data
    public string? EventName { get; set; }
    public GitHubIssue Issue { get; set; }
    public GitHubRepository Repository { get; set; }
    public GitHubComment? IncomingComment { get; set; }

    // Spec pack & state
    public SpecPackConfig SpecPack { get; set; }
    public BotState? State { get; set; }

    // Decision flags
    public bool ShouldStop { get; set; }
    public bool ShouldAskFollowUps { get; set; }
    public bool ShouldFinalize { get; set; }
    public bool ShouldEscalate { get; set; }

    // Results from agents
    public CategoryDecision? CategoryDecision { get; set; }
    public CasePacket CasePacket { get; set; }          // Extracted fields
    public ScoringResult? Scoring { get; set; }         // Completeness score
    public List<FollowUpQuestion> FollowUpQuestions { get; set; }
    public EngineerBrief? Brief { get; set; }           // Final brief
    public List<DuplicateReference> Duplicates { get; set; }

    // Decision path (for audit)
    public Dictionary<string, string> DecisionPath { get; set; }
}
```

### BotState (Persisted Across Runs)
```csharp
public sealed class BotState
{
    public string Category { get; set; }
    public int LoopCount { get; set; }                  // Follow-up loop counter
    public List<string> AskedFields { get; set; }       // History to avoid re-asking
    public bool IsActionable { get; set; }
    public int CompletenessScore { get; set; }
    public bool IsFinalized { get; set; }               // Prevents duplicate brief posts
    public long? EngineerBriefCommentId { get; set; }   // For disagreement detection
    public int BriefIterationCount { get; set; }        // Regen counter
}
```

### CasePacket (Extracted Information)
```csharp
public sealed class CasePacket
{
    public Dictionary<string, string> Fields { get; set; }
    public HashSet<string> DeterministicFields { get; set; }  // Mark deterministically extracted fields
}
```

### EngineerBrief (Final Output)
```csharp
public sealed class EngineerBrief
{
    public string Summary { get; set; }
    public List<string> Symptoms { get; set; }
    public List<string> ReproSteps { get; set; }
    public Dictionary<string, string> Environment { get; set; }
    public List<string> KeyEvidence { get; set; }
    public List<string> NextSteps { get; set; }
    public List<string> ValidationConfirmations { get; set; }
    public List<DuplicateReference> PossibleDuplicates { get; set; }
}
```

---

## 5. AGENTS (LLM-POWERED COMPONENTS)

Located: [src/SupportConcierge.Core/Modules/Agents/](src/SupportConcierge.Core/Modules/Agents/)

### ClassifierAgent
**File**: [ClassifierAgent.cs](src/SupportConcierge.Core/Modules/Agents/ClassifierAgent.cs)
- **When**: Category detected by keywords has low confidence OR no keyword match
- **Input**: Issue title + body + available categories
- **Output**: `CategoryDecision` (category, confidence, reasoning)
- **Schema**: Structured JSON response

### ExtractorAgent
**File**: [ExtractorAgent.cs](src/SupportConcierge.Core/Modules/Agents/ExtractorAgent.cs)
- **When**: Deterministic extraction leaves gaps
- **Input**: Issue body + comments + required field names
- **Output**: `Dictionary<string, string>` (field → value)
- **Key Feature**: Won't override deterministically extracted fields
- **Schema Repair**: Auto-repairs malformed JSON responses

### FollowUpAgent
**File**: [FollowUpAgent.cs](src/SupportConcierge.Core/Modules/Agents/FollowUpAgent.cs)
- **When**: Issue completeness below threshold
- **Input**: Category + existing fields + missing fields
- **Output**: `List<FollowUpQuestion>` (field, question, why_needed)
- **Limits**: Max 3 questions per loop, max 3 loops total

### BriefAgent
**File**: [BriefAgent.cs](src/SupportConcierge.Core/Modules/Agents/BriefAgent.cs)
- **When**: Issue deemed actionable
- **Input**: Full case packet + playbook + repo docs + duplicates
- **Output**: `EngineerBrief` (structured)
- **Regeneration**: `RegenerateAsync()` called if user disagrees

### JudgeAgent
**File**: [JudgeAgent.cs](src/SupportConcierge.Core/Modules/Agents/JudgeAgent.cs)
- **When**: Eval mode, scoring follow-ups and briefs
- **Input**: Scenario + generated follow-ups/brief
- **Output**: Score + rubric evaluation
- **Usage**: Deterministic tests only (eval framework)

---

## 6. TOOLS (Non-LLM Utilities)

Located: [src/SupportConcierge.Core/Modules/Tools/](src/SupportConcierge.Core/Modules/Tools/)

### GitHubTool
**Interface**: [IGitHubTool.cs](src/SupportConcierge.Core/Modules/Tools/IGitHubTool.cs)
**Implementation**: [GitHubTool.cs](src/SupportConcierge.Core/Modules/Tools/GitHubTool.cs)

**Capabilities**:
- `GetIssueCommentsAsync()` - Fetch all comments on an issue
- `PostCommentAsync()` - Post a new comment (respects dry-run, write-mode flags)
- `AddLabelsAsync()` - Apply labels to issue
- `AddAssigneesAsync()` - Assign users to issue
- `GetFileContentAsync()` - Fetch file contents (for repo docs, playbooks)
- `SearchIssuesAsync()` - Find similar issues

### StateStoreTool
**File**: [StateStoreTool.cs](src/SupportConcierge.Core/Modules/Tools/StateStoreTool.cs)

**Key Features**:
- Stores `BotState` as a hidden HTML comment: `<!-- supportbot_state:{json} -->`
- Auto-compression if JSON > 2KB (GZip + Base64)
- `ExtractState()` - Pull state from prior bot comments
- `EmbedState()` - Append state to new comment
- `PruneState()` - Trim old fields to keep comment size manageable

**Example**: State persists across multiple runs, enabling loop counting and "already asked" tracking.

### SpecPackTool / SpecPackLoader
**File**: [SpecPackLoader.cs](src/SupportConcierge.Core/Modules/SpecPack/SpecPackLoader.cs)

**Loads from `.supportbot/`**:
- `categories.yaml` - Category definitions + keywords
- `checklists.yaml` - Required fields per category + weights + threshold
- `validators.yaml` - Junk patterns, format validators (regex), secret patterns, contradiction rules
- `routing.yaml` - Label/assignee routing per category
- `playbooks/*.md` - Category-specific guidance (fetched and passed to agents)

### IssueFormParser
**File**: [IssueFormParser.cs](src/SupportConcierge.Core/Modules/Tools/IssueFormParser.cs)

**Deterministic Extraction**:
1. Split issue body by `## Heading` (form field headers)
2. Parse each section for `key: value` lines
3. Return dict of field → value
4. Mark all extracted fields as deterministic (won't be overridden by LLM)

### SchemaValidator
**File**: [SchemaValidator.cs](src/SupportConcierge.Core/Modules/Tools/SchemaValidator.cs)

**Capabilities**:
- Validates JSON against schema (using `JsonSchema.Net`)
- `TryValidate()` returns errors if schema mismatch
- Used to validate LLM responses before processing
- Agents retry with repair prompts if validation fails

### MetricsTool
**File**: [MetricsTool.cs](src/SupportConcierge.Core/Modules/Tools/MetricsTool.cs)

**Tracks**:
- Step timings (start → end)
- Token usage per LLM call
- Warnings & decision path
- Outputs to `artifacts/metrics/{run_id}.json`

---

## 7. GUARDRAILS

Located: [src/SupportConcierge.Core/Modules/Guardrails/](src/SupportConcierge.Core/Modules/Guardrails/)

### CommandParser
**File**: [CommandParser.cs](src/SupportConcierge.Core/Modules/Guardrails/CommandParser.cs)

**Detects**:
- `/stop` - User can halt the workflow
- `/diagnose` - Triggers special diagnostic flow (future feature)

**Gate**: Only issue author + special commands exempt from gating; most operations require author.

### SecretRedactor
**File**: [SecretRedactor.cs](src/SupportConcierge.Core/Modules/Guardrails/SecretRedactor.cs)

**Features**:
- Configurable regex patterns (from spec pack validators)
- Replaces matches with `[REDACTED]`
- Logs redaction findings (type: API Key, Password, etc.)
- **Called before any LLM prompt** (safety first)

**Patterns Detected**: API keys, passwords, secrets, credentials, bearer tokens

### Validators
**File**: [Validators.cs](src/SupportConcierge.Core/Modules/Guardrails/Validators.cs)

**Checks**:
- **Junk patterns**: Identifies placeholder text (e.g., "N/A", "TBD")
- **Format validators**: Regex per field (e.g., version format, URL format)
- **Contradiction detection**: Warns if two fields contradict (e.g., "version 1" + "version 2")

### DisagreementDetector
**File**: [DisagreementDetector.cs](src/SupportConcierge.Core/Modules/Guardrails/DisagreementDetector.cs)

**Purpose**: Detects if user explicitly disagrees with generated brief (e.g., comment reaction or text pattern).  
**Action**: Allows one regeneration attempt before escalating.

---

## 8. WORKFLOW SERVICES (Dependency Container)

**File**: [WorkflowServices.cs](src/SupportConcierge.Core/Modules/Workflows/WorkflowServices.cs)

```csharp
public sealed class WorkflowServices
{
    public ISpecPackLoader SpecPackLoader { get; }
    public StateStoreTool StateStore { get; }
    public IssueFormParser IssueFormParser { get; }
    public CommentComposer CommentComposer { get; }
    public CategoryScorer CategoryScorer { get; }
    public SchemaValidator SchemaValidator { get; }
    public SecretRedactor SecretRedactor { get; }
    public Validators Validators { get; }
    public CompletenessScorer CompletenessScorer { get; }
    public IGitHubTool GitHub { get; }
    public MetricsTool Metrics { get; }
    
    // Agents
    public ClassifierAgent ClassifierAgent { get; }
    public ExtractorAgent ExtractorAgent { get; }
    public FollowUpAgent FollowUpAgent { get; }
    public BriefAgent BriefAgent { get; }
    public JudgeAgent JudgeAgent { get; }
}
```

**Key Method**: `UpdateForSpecPack()` - Re-initializes validators, redactor, scorer when spec pack loaded.

---

## 9. WORKFLOW EXECUTION

### Entry Point: SupportConciergeRunner
**File**: [SupportConciergeRunner.cs](src/SupportConcierge.Core/Modules/Workflows/SupportConciergeRunner.cs)

```csharp
public async Task<RunContext> RunAsync(EventInput input, CancellationToken cancellationToken = default)
{
    _services.LastRunContext = null;
    await InProcessExecution.RunAsync(_workflow, input, runId: null, cancellationToken);
    return _services.LastRunContext ?? new RunContext { ... };
}
```

**Key Detail**: Uses `InProcessExecution.RunAsync()` from MAF framework. The workflow passes context through executors sequentially/conditionally.

### CLI Entry Point
**File**: [src/SupportConcierge.Cli/Program.cs](src/SupportConcierge.Cli/Program.cs)

**Usage Modes**:
1. **Runtime** (default): `dotnet run -- --event-file <path>` → Parse GitHub event, run workflow
2. **Smoke test**: `dotnet run -- --smoke` → Run against fixture
3. **Eval**: `dotnet run -- --eval` → Run full eval suite

**Environment Variables**:
- Required: `GITHUB_TOKEN`, `OPENAI_API_KEY`, `OPENAI_MODEL`
- Optional: `SUPPORTBOT_DRY_RUN`, `SUPPORTBOT_WRITE_MODE`, `SUPPORTBOT_SPEC_DIR`, `SUPPORTBOT_METRICS_DIR`

---

## 10. SPEC PACK CONFIGURATION

Located: [.supportbot/](./supportbot/)

### categories.yaml
```yaml
categories:
  - name: bug
    description: "Issue is a bug"
    keywords: [bug, error, crash, fail]
  - name: feature
    description: "Feature request"
    keywords: [feature, enhancement, request]
  - name: off_topic
    keywords: [spam, advertisement]
```

### checklists.yaml
```yaml
checklists:
  bug:
    completeness_threshold: 70
    required_fields:
      - name: "environment"
        description: "OS, version, etc."
        weight: 20
        optional: false
      - name: "repro_steps"
        description: "Steps to reproduce"
        weight: 20
        optional: false
```

### validators.yaml
```yaml
validators:
  junk_patterns:
    - "^(N/A|TBD|unknown|?)$"
  format_validators:
    version: "^v?\\d+\\.\\d+(\\.\\d+)?$"
  secret_patterns:
    - "AKIA[0-9A-Z]{16}"  # AWS key
    - "(token|api.key)\\s*=\\s*[\\w-]+"
  contradiction_rules:
    - field1: "os"
      field2: "browser"
      condition: "if os == 'linux' and browser == 'safari' then warn"
```

### routing.yaml
```yaml
routing:
  routes:
    - category: bug
      labels: ["bug", "triage"]
      assignees: ["engineer1", "engineer2"]
    - category: feature
      labels: ["enhancement"]
      assignees: ["product-owner"]
  escalation_mentions:
    - "@security-team"
```

### playbooks/
```
playbooks/
  bug.md       → Guidance for bug categorization/investigation
  feature.md   → Guidance for feature requests
```

---

## 11. TEST SUITE

Located: [tests/SupportConcierge.Tests/](tests/SupportConcierge.Tests/)

### Test Files
- **GuardrailsTests.cs** - Tests for `CommandParser`, `SecretRedactor`, `Validators`
- **SecretRedactorTests.cs** - Detailed secret redaction tests
- **StateStoreTests.cs** - Tests for state embedding/extraction, compression, pruning

### Run Tests
```bash
dotnet test
```

---

## 12. EVALUATION FRAMEWORK

Located: [evals/](./evals/)

### Scenario Structure
**Files**: `evals/scenarios/e2e/*.json`, `followups/*.json`, `briefs/*.json`

```json
{
  "name": "bug_with_env",
  "eventName": "issues",
  "issue": { "number": 1, "title": "...", "body": "..." },
  "comments": [...],
  "specPackPath": ".supportbot",
  "expectations": {
    "expectedCategory": "bug",
    "minCompleteness": 70,
    "minFollowUpQuestions": 2
  }
}
```

### Eval Config
**File**: [evals/eval_config.json](evals/eval_config.json)

```json
{
  "minFollowUpScore": 0.75,
  "minBriefScore": 0.80,
  "maxAverageLatencyMs": 5000,
  "maxAverageTokens": 2000,
  "failOnHallucinations": true
}
```

### Run Evals
```bash
# With LLM
OPENAI_API_KEY=... OPENAI_MODEL=gpt-4 dotnet run --project src/SupportConcierge.Cli -- --eval

# Deterministic only (no LLM)
SUPPORTBOT_NO_LLM=true dotnet run --project src/SupportConcierge.Cli -- --eval
```

### Output
- `artifacts/evals/eval_report.json` - E2E results
- `artifacts/evals/EVAL_REPORT.md` - Markdown summary
- `artifacts/evals/followup_eval_report.json` - Follow-up quality
- `artifacts/evals/brief_eval_report.json` - Brief quality

---

## 13. DEPLOYMENT

### GitHub Actions Integration

**File**: [.github/workflows/supportbot.yml](.github/workflows/supportbot.yml)

**Trigger**: `on: [issues, issue_comment]`

**Steps**:
1. Checkout repo
2. Download published bot artifact
3. Set environment variables
4. Run: `dotnet run --project SupportConcierge.Cli -- --event-file "$GITHUB_EVENT_PATH"`

### Deployment Options

1. **Submodule + Local Workflow** (Option A)
   - Add repo as submodule: `.github/supportbot`
   - Point workflow at submodule project
   - Pro: Exact SHA control
   - Con: Manual updates

2. **Reusable Workflow** (Option B)
   - Call reusable workflow from this repo
   - Specify version tag
   - Pro: Clean consumer repo
   - Con: Versioning discipline required

### Configuration (Sandbox Repo)

1. Add labels matching `.supportbot/routing.yaml`
2. Add repository variables:
   - `OPENAI_MODEL` (e.g., gpt-4)
   - `SUPPORTBOT_DRY_RUN` (true/false)
   - `SUPPORTBOT_WRITE_MODE` (true/false)
3. Add repository secret:
   - `OPENAI_API_KEY`

---

## 14. EXECUTION FLOW EXAMPLE

### Scenario: New GitHub Issue Opened

```
1. GitHub sends issue event
2. ParseEventExecutor: Extract issue title, body, author
3. LoadSpecPackExecutor: Load .supportbot/ configs
4. LoadPriorStateExecutor: Check if prior comments exist with state blob
   → No prior state found → create new BotState
5. ApplyGuardrailsExecutor: Check author, /stop command, limits
   → Pass (issue author, no /stop, first loop)
6. ExtractCasePacketExecutor:
   - Deterministically parse form fields
   - Classify category (bug/feature/etc.)
   - Call ExtractorAgent to fill missing fields
   → CasePacket: { environment: "...", repro_steps: "..." }
7. ScoreCompletenessExecutor: Score against bug checklist (70%)
   → Score = 65% → Below threshold
   → ShouldAskFollowUps = true
8. GenerateFollowUpsExecutor: Call FollowUpAgent
   → Questions: ["What version?", "What OS?"]
9. ValidateFollowUpsExecutor: Schema check, dedup
10. PostFollowUpCommentExecutor: Post comment with questions
    → Embed BotState (LoopCount=1, AskedFields=[version, os])
11. PersistStateExecutor: Done (state already embedded in comment)

→ Workflow exits. User responds with answers.

--- Later, user comments with answers ---

1. GitHub sends issue_comment event
2. ParseEventExecutor: Author = commenter
3-5. Load state, apply guardrails
6. ExtractCasePacketExecutor: Re-extract + new comment content
   → CasePacket: { environment: "...", version: "2.1", os: "Ubuntu" }
7. ScoreCompletenessExecutor: Score = 85%
   → ShouldAskFollowUps = false, ShouldFinalize = true
8. SearchDuplicatesExecutor: Find similar bugs
   → Duplicates = [#123, #456]
9. FetchGroundingDocsExecutor: Fetch bug.md playbook + README.md
10. GenerateEngineerBriefExecutor: Call BriefAgent
    → Brief: { summary: "...", symptoms: [...], reproSteps: [...], nextSteps: [...] }
11. ValidateBriefExecutor: Schema valid
12. PostFinalBriefCommentExecutor: Post brief
    → Embed BotState (IsFinalized=true)
13. ApplyRoutingExecutor: Add labels [bug, triage], assign [@engineer1]
14. PersistStateExecutor: Done

→ Issue now has brief + routing
```

---

## 15. KEY DESIGN PATTERNS

### 1. Executor Pattern (MAF)
- Each workflow step is an `Executor<TInput, TOutput>`
- Inherently async with cancellation support
- Metrics auto-captured via decorator pattern

### 2. State Bag Pattern
`RunContext` is threaded through entire workflow, mutated at each step. No external state machine.

### 3. Deterministic-First
- Extraction never relies solely on LLM
- Form parsing + heuristics come first
- LLM fills gaps only; never overrides deterministic fields

### 4. Schema Validation with Retry
```csharp
var response = await _llmClient.CompleteAsync(request);
if (!validator.TryValidate(response.Content, schema, out errors))
{
    // Auto-retry with repair prompt
    var repaired = await RepairJsonAsync(response.Content, schema);
}
```

### 5. HTML Comment State Persistence
```
Bot Comment:
"Here are your follow-up questions:\n\n1. Version?\n\n<!-- supportbot_state:{compressed or raw json} -->"
```
- Survives quoting, editing
- Auto-extracted on next event
- Tracks: loop count, asked fields, brief status, etc.

### 6. Conditional Edges
Edges in workflow are predicated on `RunContext` flags:
```csharp
builder.AddEdge<RunContext>(score, genFollowups, ctx => ctx != null && ctx.ShouldAskFollowUps);
builder.AddEdge<RunContext>(score, searchDupes, ctx => ctx != null && ctx.ShouldFinalize && !ShouldFinalizeOffTopic(ctx));
```

---

## 16. DIRECTORY MAP

```
Github-issues-bot-with-MAF/
├── README.md                                    # Quick start guide
├── SupportConcierge.slnx                        # Solution file
├── Directory.Build.props                        # Common props
├── Directory.Packages.props                     # Central package versions
│
├── docs/
│   ├── docs/architecture/ARCHITECTURE.md                          # High-level design
│   ├── docs/deployment/DEPLOYMENT.md                            # Sandbox setup, options
│   ├── docs/evals/EVALS.md                                 # Evaluation framework
│   ├── docs/handoff/HANDOFF_CHECKLIST.md                     # Next agent guide
│
├── .supportbot/                                 # Spec pack (deployable config)
│   ├── categories.yaml
│   ├── checklists.yaml
│   ├── validators.yaml
│   ├── routing.yaml
│   └── playbooks/
│       ├── bug.md
│       ├── feature.md
│
├── .github/
│   └── workflows/
│       ├── supportbot.yml                       # Runtime workflow (GitHub Actions)
│       └── ci.yml                               # CI gates (test, build)
│
├── src/
│   ├── SupportConcierge.Core/                   # Core logic
│   │   ├── Workflows/
│   │   │   ├── SupportConciergeWorkflow.cs      # DAG definition (WorkflowBuilder)
│   │   │   ├── SupportConciergeRunner.cs        # Execution entry point
│   │   │   ├── WorkflowServices.cs              # DI container
│   │   │   ├── ExecutorDefaults.cs
│   │   │   └── Executors/
│   │   │       ├── ParseEventExecutor.cs
│   │   │       ├── LoadSpecPackExecutor.cs
│   │   │       ├── LoadPriorStateExecutor.cs
│   │   │       ├── ApplyGuardrailsExecutor.cs
│   │   │       ├── ExtractCasePacketExecutor.cs
│   │   │       ├── ScoreCompletenessExecutor.cs
│   │   │       ├── AcknowledgeStopExecutor.cs
│   │   │       ├── GenerateFollowUpsExecutor.cs
│   │   │       ├── ValidateFollowUpsExecutor.cs
│   │   │       ├── PostFollowUpCommentExecutor.cs
│   │   │       ├── SearchDuplicatesExecutor.cs
│   │   │       ├── FetchGroundingDocsExecutor.cs
│   │   │       ├── GenerateEngineerBriefExecutor.cs
│   │   │       ├── ValidateBriefExecutor.cs
│   │   │       ├── PostFinalBriefCommentExecutor.cs
│   │   │       ├── ApplyRoutingExecutor.cs
│   │   │       ├── EscalateExecutor.cs
│   │   │       └── PersistStateExecutor.cs
│   │   │
│   │   ├── Agents/
│   │   │   ├── ClassifierAgent.cs                # Category classification
│   │   │   ├── ExtractorAgent.cs                 # Field extraction
│   │   │   ├── FollowUpAgent.cs                  # Follow-up generation
│   │   │   ├── BriefAgent.cs                     # Brief generation + regen
│   │   │   ├── JudgeAgent.cs                     # Eval scoring
│   │   │   ├── LlmModels.cs                      # ILlmClient, request/response
│   │   │   ├── OpenAiClient.cs                   # OpenAI impl
│   │   │   ├── NullLlmClient.cs                  # Stub for no-LLM mode
│   │   │   ├── MetricsLlmClient.cs               # Decorator (timing + tokens)
│   │   │   ├── Prompts.cs                        # Prompt templates
│   │   │   └── Schemas.cs                        # JSON schema defs
│   │   │
│   │   ├── Tools/
│   │   │   ├── IGitHubTool.cs                    # GitHub API interface
│   │   │   ├── GitHubTool.cs                     # HTTP implementation
│   │   │   ├── IssueFormParser.cs                # Deterministic form parsing
│   │   │   ├── ISpecPackLoader.cs                # Spec pack interface
│   │   │   ├── SpecPackLoader.cs                 # YAML loading
│   │   │   ├── StateStoreTool.cs                 # State blob (de)serialization
│   │   │   ├── SchemaValidator.cs                # JSON schema validation
│   │   │   ├── MetricsTool.cs                    # Timing + token tracking
│   │   │   ├── CommentComposer.cs                # Comment formatting
│   │   │   ├── CategoryScorer.cs                 # Heuristic category match
│   │   │   ├── CompletenessScorer.cs             # Checklist scoring
│   │   │   ├── FakeGitHubTool.cs                 # Mock for testing
│   │   │   └── FakeSpecPackLoader.cs             # Mock for testing
│   │   │
│   │   ├── Guardrails/
│   │   │   ├── CommandParser.cs                  # /stop, /diagnose detection
│   │   │   ├── SecretRedactor.cs                 # Regex-based secret redaction
│   │   │   ├── Validators.cs                     # Field validation (junk, format, contradiction)
│   │   │   └── DisagreementDetector.cs           # User disagreement detection
│   │   │
│   │   ├── Models/
│   │   │   ├── RunContext.cs                     # Core state bag
│   │   │   ├── CaseModels.cs                     # CasePacket, EngineerBrief, etc.
│   │   │   ├── EvalModels.cs                     # Eval-specific models
│   │   │   ├── GitHubModels.cs                   # GitHub issue/comment structures
│   │   │   ├── JudgeModels.cs                    # Judge output structures
│   │   │   ├── MetricsModels.cs                  # Metrics tracking structures
│   │   │   ├── EventInput.cs                     # GitHub event input
│   │   │   └── SpecPack/
│   │   │       ├── SpecPackModels.cs             # Category, Checklist, Routing defs
│   │   │       └── SpecPackLoader.cs             # YAML → Models
│   │   │
│   │   └── SupportConcierge.Core.csproj
│   │
│   └── SupportConcierge.Cli/                     # CLI runner
│       ├── Program.cs                            # Entry point (runtime, smoke, eval)
│       ├── SupportConcierge.Cli.csproj
│       └── Evals/
│           ├── EvalConfig.cs                     # Eval thresholds
│           └── EvalRunner.cs                     # Orchestrates all eval suites
│
├── tests/
│   └── SupportConcierge.Tests/
│       ├── GuardrailsTests.cs
│       ├── SecretRedactorTests.cs
│       ├── StateStoreTests.cs
│       ├── TestHelpers.cs
│       └── SupportConcierge.Tests.csproj
│
├── evals/
│   ├── eval_config.json                         # Thresholds
│   ├── specpack.json                            # Spec pack for evals
│   ├── scenarios/
│   │   ├── e2e/
│   │   │   └── *.json                            # End-to-end scenarios
│   │   ├── followups/
│   │   │   └── *.json                            # Follow-up quality scenarios
│   │   └── briefs/
│   │       └── *.json                            # Brief quality scenarios
│   ├── goldens/                                  # Expected outputs
│   │   └── *.json
│   └── results/                                  # Generated reports
│
├── artifacts/
│   ├── evals/
│   │   ├── eval_report.json
│   │   ├── EVAL_REPORT.md
│   │   └── followup_eval_report.json
│   └── metrics/
│       └── run_*.json                            # Per-run timings, tokens
│
├── scripts/
│   ├── capture-event.md                         # How to save GitHub event
│   └── cleanup-sandbox.md                       # Sandbox cleanup guide
│
└── .supportbot/                                  # (also root)
    └── [spec pack files]
```

---

## 17. CONFIGURATION & ENVIRONMENT

### Required Environment Variables
- `GITHUB_TOKEN` - GitHub API token (repo scope)
- `OPENAI_API_KEY` - OpenAI API key
- `OPENAI_MODEL` - Model name (e.g., gpt-4)

### Optional Variables
- `SUPPORTBOT_DRY_RUN=true` - Don't write to GitHub
- `SUPPORTBOT_WRITE_MODE=true` - Actually post (default: false)
- `SUPPORTBOT_SPEC_DIR=.supportbot` - Override spec pack location
- `SUPPORTBOT_METRICS_DIR=artifacts/metrics` - Where to save metrics
- `SUPPORTBOT_MAX_FOLLOWUP_LOOPS=3` - Max follow-up rounds
- `SUPPORTBOT_MAX_BRIEF_REGEN=2` - Max brief regenerations
- `SUPPORTBOT_NO_LLM=true` - Run deterministic only (evals)
- `SUPPORTBOT_BOT_USERNAME=github-actions[bot]` - Filter bot comments

---

## 18. QUICK REFERENCE: WHERE WHAT IS

| Component | File(s) |
|-----------|---------|
| **Workflow DAG** | [SupportConciergeWorkflow.cs](src/SupportConcierge.Core/Modules/Workflows/SupportConciergeWorkflow.cs) |
| **Execution Runner** | [SupportConciergeRunner.cs](src/SupportConcierge.Core/Modules/Workflows/SupportConciergeRunner.cs) |
| **18 Executors** | [Workflows/Executors/*.cs](src/SupportConcierge.Core/Modules/Workflows/Executors/) |
| **5 Agents (LLM)** | [Agents/*.cs](src/SupportConcierge.Core/Modules/Agents/) |
| **GitHub API** | [GitHubTool.cs](src/SupportConcierge.Core/Modules/Tools/GitHubTool.cs) |
| **State Persistence** | [StateStoreTool.cs](src/SupportConcierge.Core/Modules/Tools/StateStoreTool.cs) |
| **Spec Pack Loading** | [SpecPackLoader.cs](src/SupportConcierge.Core/Modules/SpecPack/SpecPackLoader.cs) |
| **Secret Redaction** | [SecretRedactor.cs](src/SupportConcierge.Core/Modules/Guardrails/SecretRedactor.cs) |
| **Field Validation** | [Validators.cs](src/SupportConcierge.Core/Modules/Guardrails/Validators.cs) |
| **Deterministic Parsing** | [IssueFormParser.cs](src/SupportConcierge.Core/Modules/Tools/IssueFormParser.cs) |
| **Metrics & Timing** | [MetricsTool.cs](src/SupportConcierge.Core/Modules/Tools/MetricsTool.cs) |
| **CLI Entry Point** | [src/SupportConcierge.Cli/Program.cs](src/SupportConcierge.Cli/Program.cs) |
| **Unit Tests** | [tests/SupportConcierge.Tests/*.cs](tests/SupportConcierge.Tests/) |
| **Eval Framework** | [src/SupportConcierge.Cli/Evals/EvalRunner.cs](src/SupportConcierge.Cli/Evals/EvalRunner.cs) |
| **Eval Scenarios** | [evals/scenarios/](evals/scenarios/) |
| **Spec Pack Config** | [.supportbot/](./supportbot/) (categories, checklists, validators, routing) |
| **GitHub Actions** | [.github/workflows/](./github/workflows/) |

---

## 19. KEY TAKEAWAYS FOR NEXT DEVELOPER

1. **Workflow is DAG-based via MAF**: All logic flows through `SupportConciergeWorkflow` builders and executors. No hidden state machines.

2. **State persistence is simple**: JSON blob in HTML comment. Survives quoting, threading.

3. **Deterministic-first is fundamental**: Never rely solely on LLM for critical data. Parse forms, check heuristics first.

4. **Secret redaction is early**: Before *any* LLM call, redact secrets. Safety > convenience.

5. **Agents are swappable**: Each agent (Classifier, Extractor, FollowUp, Brief) uses `ILlmClient`. Easy to plug in new LLM providers.

6. **Spec packs make config easy**: Repo maintainers define categories, checklists, routing in YAML. No code changes needed.

7. **Schema validation with repair**: LLM responses validated; failed responses auto-repaired and retried.

8. **Metrics baked in**: Every step timed, token usage tracked. Decision path logged for audit.

9. **Tests are deterministic**: No LLM mocking needed; use `SUPPORTBOT_NO_LLM=true` and spec pack fixtures.

10. **Deployment is standard GitHub Actions**: Reusable workflow or submodule. Clear sandbox testing guide.

---

## 20. NEXT STEPS FOR DEVELOPMENT

To extend this project:

1. **Add a new executor**: Inherit `Executor<TInput, TOutput>`, implement `HandleAsync()`, add to `WorkflowBuilder` in `SupportConciergeWorkflow.cs`.

2. **Add a new agent**: Implement against `ILlmClient`, add prompts to `Prompts.cs`, schemas to `Schemas.cs`.

3. **Customize spec pack**: Edit `.supportbot/` YAML files (categories, validators, routing, playbooks).

4. **Add validation rules**: Update `validators.yaml` with new junk patterns, format regexes, secret patterns.

5. **Extend guardrails**: Add new command detection or contradiction rules.

6. **Add more evals**: Create scenarios in `evals/scenarios/` subdirectories, run `--eval` mode.

7. **Integrate new LLM**: Implement `ILlmClient`, wire in `Program.cs` dependency container.

---

## Summary

This is a **production-grade, schema-validated, stateful GitHub issue triage bot** using Microsoft Agent Framework (MAF) Workflows. The architecture prioritizes:

- **Determinism** (form-first extraction)
- **Safety** (secret redaction, schema validation)
- **Auditability** (decision path, metrics, state history)
- **Extensibility** (spec packs, configurable agents, pluggable tools)
- **Testability** (mock tools, eval framework, deterministic mode)

Perfect for teams wanting intelligent issue routing with full control over logic and configuration.

