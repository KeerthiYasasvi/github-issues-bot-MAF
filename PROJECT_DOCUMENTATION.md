# Support Concierge Bot - Project Documentation

## 📋 Table of Contents
- [Project Overview](#project-overview)
- [Purpose & Goals](#purpose--goals)
- [Architecture](#architecture)
- [How It Works](#how-it-works)
- [Workspace Structure](#workspace-structure)
- [Test Repository Setup](#test-repository-setup)
- [Multi-Agent Framework (MAF)](#multi-agent-framework-maf)
- [State Management](#state-management)
- [Implemented Features](#implemented-features)
- [Bugs Fixed](#bugs-fixed)
- [Known Issues](#known-issues)
- [Future Enhancements](#future-enhancements)

---

## 🎯 Project Overview

**Support Concierge Bot** is an intelligent GitHub issue triage and support assistant built using Microsoft's Multi-Agent Framework (MAF). It automatically engages with issue reporters through iterative conversations to gather sufficient information, classify issues, and either provide resolutions or escalate to maintainers with comprehensive context.

**Repository Structure:**
- **Bot Repository**: `github-issues-bot-MAF` - Contains the bot source code
- **Test Repository**: `ytm-stream-analytics` - Real-world test bed for bot interactions

**Technology Stack:**
- .NET 8.0 (C#)
- Microsoft Agents AI Framework
- OpenAI GPT-4 (via Azure OpenAI or OpenAI API)
- GitHub Actions for deployment
- Octokit.NET for GitHub API integration

---

## 🎓 Purpose & Goals

### Primary Purpose
Transform raw, incomplete GitHub issues into **actionable, well-documented problems** by:
1. **Automatically triaging** issues using AI-powered classification
2. **Iteratively gathering** missing information through natural language conversations
3. **Researching context** from documentation, previous issues, and codebase
4. **Providing resolutions** or escalating with comprehensive briefs for maintainers

### Design Goals
- ✅ **Multi-user support**: Multiple users can interact with the bot independently on the same issue
- ✅ **State persistence**: Conversation context survives across bot restarts
- ✅ **Iterative refinement**: Up to 3 loops per user to gather complete information
- ✅ **Security gating**: Only authorized users (issue author + `/diagnose` users) can interact
- ✅ **Context awareness**: Bot maintains separate conversation state for each user
- ✅ **Graceful degradation**: Fallback mechanisms when agents fail

### Success Metrics
- **Actionability Rate**: % of issues escalated with complete information
- **Loop Efficiency**: Average loops needed to gather sufficient details
- **False Positive Rate**: Issues incorrectly marked as actionable
- **User Satisfaction**: Measured through disagreement detection

---

## 🏗️ Architecture

### Multi-Agent System

The bot uses a **specialized multi-agent architecture** where each agent has a distinct responsibility:

```
┌─────────────────────────────────────────────────────────────────┐
│                      MAF Workflow Orchestration                  │
└─────────────────────────────────────────────────────────────────┘
                                │
                    ┌───────────┴───────────┐
                    ▼                       ▼
        ┌─────────────────────┐   ┌─────────────────────┐
        │   Triage Agent      │   │   Research Agent    │
        │   (Classifier)      │   │   (Tool Executor)   │
        │                     │   │                     │
        │ - Category          │   │ - Web search        │
        │ - Severity          │   │ - Doc lookup        │
        │ - Actionability     │   │ - Issue search      │
        └──────────┬──────────┘   └──────────┬──────────┘
                   │                          │
                   └────────┬─────────────────┘
                            ▼
                   ┌─────────────────────┐
                   │   Response Agent    │
                   │   (Question Gen)    │
                   │                     │
                   │ - Follow-ups        │
                   │ - Brief generation  │
                   └──────────┬──────────┘
                              │
                    ┌─────────┴─────────┐
                    ▼                   ▼
        ┌─────────────────────┐   ┌─────────────────────┐
        │   Critic Agent      │   │  Orchestrator Agent │
        │   (Quality Check)   │   │  (Decision Maker)   │
        │                     │   │                     │
        │ - Validates output  │   │ - Loop control      │
        │ - Scores quality    │   │ - Flow decisions    │
        └─────────────────────┘   └─────────────────────┘
```

### Agent Responsibilities

**1. Triage Agent** (`ClassifierAgent`)
- **Input**: Issue title, body, user comments
- **Output**: Classification (category, severity, actionability)
- **Purpose**: Initial categorization of the problem
- **Critic**: Validates classification reasoning and confidence scores

**2. Research Agent** (`ExtractorAgent`)
- **Input**: Issue context + available tools
- **Output**: Contextual findings from documentation, web, previous issues
- **Purpose**: Gather external context to better understand the problem
- **Tools**: Web search, documentation lookup, issue search, code search
- **Critic**: Validates research relevance and completeness

**3. Response Agent** (`FollowUpAgent`)
- **Input**: Classification + research findings + conversation history
- **Output**: Follow-up questions OR final brief
- **Purpose**: Generate targeted questions to fill information gaps
- **Critic**: Validates question quality, relevance, and non-redundancy

**4. Critic Agent** (Embedded in all agents)
- **Input**: Agent's output + input context
- **Output**: Quality score (1-5) + critique reasoning
- **Purpose**: Validate and score each agent's output quality
- **Impact**: Low scores trigger fallbacks or agent retries

**5. Orchestrator Agent** (`OrchestratorAgent`)
- **Input**: Full conversation state + agent outputs
- **Output**: Decision (ask questions / finalize / escalate / continue loop)
- **Purpose**: High-level flow control and convergence detection
- **Logic**: 
  - Loop < 3 + actionable → finalize
  - Loop < 3 + not actionable → ask follow-ups
  - Loop = 3 → escalate
  - Disagreement detected → brief regeneration

### Workflow DAG (Directed Acyclic Graph)

```
ParseEvent → LoadState → Guardrails → [Decision: Stop?]
                                        │
                           NO ──────────┘
                           │
                           ▼
                      Triage → Research → Response → Orchestrator
                           │                              │
                           │              ┌───────────────┴────────────┐
                           │              ▼                            ▼
                           │      [Ask Follow-ups]              [Finalize/Escalate]
                           │              │                            │
                           │              ▼                            ▼
                           │         PostComment ←───────────────────┘
                           │              │
                           │              ▼
                           │        PersistState → Exit
                           │
                           └──[Loop < 3]──┘ (max 3 iterations per user)
```

---

## ⚙️ How It Works

### Step-by-Step Flow

**1. Issue Created or Comment Posted**
- GitHub triggers workflow via `issue_comment` or `issues` event
- Workflow checks out bot code and runs `dotnet run`

**2. ParseEvent Executor**
- Extracts event type (`issue_comment`, `issues.opened`, etc.)
- Identifies active participant (commenter or issue author)
- Parses issue title, body, and comment text

**3. LoadState Executor**
- Searches bot's previous comments for embedded state
- Extracts state from HTML comment: `<!-- supportbot_state:{...} -->`
- Initializes `BotState` with `UserConversations` dictionary
- Loads each user's loop count, asked fields, and case packet
- **Migration**: Converts legacy single-user state to multi-user format

**4. Guardrails Executor** (Security & Flow Control)
- **Bot Comment Detection**: Ignores bot's own comments to prevent loops
- **Active Participant Assignment**: Sets `ActiveParticipant` to commenter or issue author
- **Command Parsing**: Detects `/stop`, `/diagnose` commands in current comment ONLY
- **Allow List Enforcement**:
  - Issue author: Always allowed
  - Other users: Only if they're in `UserConversations` OR used `/diagnose`
  - Unknown users: Silently ignored (no bot response)
- **Per-User Finalization Check**: If user's conversation already finalized → stop
- **Loop Limit Check**: If user's loop count ≥ 3 → escalate
- **Disagreement Detection**: If finalized user posts disagreement → allow regeneration

**5. Triage Agent** (Classification)
- **Prompt Engineering**: 
  - Issue context (title, body, comments)
  - User's conversation history (previous Q&A)
  - Shared findings from other users (if any)
- **Output**: 
  - Category (e.g., "bug", "feature", "question")
  - Severity (e.g., "critical", "medium", "low")
  - Actionability assessment (is there enough info?)
- **Critic Validation**: Scores triage reasoning (1-5)
- **Fallback**: If critic score < 3 → retry once, then use last valid triage

**6. Research Agent** (Context Gathering)
- **Available Tools**:
  - `SearchWeb`: Bing search for external documentation
  - `SearchDocumentation`: Project docs (if configured)
  - `SearchIssues`: Previous GitHub issues with similar problems
  - `SearchCode`: Code snippets from repository
- **Execution**: Agent decides which tools to invoke based on triage
- **Output**: `SharedFindings` with `DiscoveredBy` attribution
- **Critic Validation**: Scores research relevance (1-5)
- **Fallback**: If critic score < 3 → skip research, proceed with triage only

**7. Response Agent** (Question Generation / Brief Creation)
- **Decision Path**:
  - **Actionable** → Generate `Brief` (case packet for maintainer)
  - **Not Actionable** → Generate `FollowUpQuestions` (3-5 questions)
- **Intelligence**:
  - Avoids asking same question twice (checks `AskedFields`)
  - References research findings in questions
  - Tailors questions to user's previous answers
  - Generates specific, non-generic questions
- **Critic Validation**: Scores question quality (1-5)
- **Fallback**: If critic score < 3 → use generic fallback questions

**8. Orchestrator Agent** (Decision Making)
- **Input**: Full `RunContext` (triage, research, response, user state)
- **Decision Logic**:
  ```
  IF actionable AND brief exists AND loop < 3:
      → ShouldFinalize = true (post brief, mark done)
  ELSE IF loop >= 3:
      → ShouldEscalate = true (post "need help", mark done)
  ELSE IF follow-up questions exist:
      → ShouldAskFollowUps = true (post questions, increment loop)
  ELSE:
      → Continue loop (go back to triage)
  ```
- **Convergence Detection**: Prevents infinite loops with max 3 iterations
- **Output**: Sets ONE flag (mutually exclusive): `ShouldFinalize`, `ShouldEscalate`, `ShouldAskFollowUps`

**9. PostComment Executor**
- **Comment Composition**:
  - **Mention**: `@{ActiveParticipant}` (actual commenter, not always issue author)
  - **Content**: Questions, brief, or /stop acknowledgment
  - **Footer**: "Loop X of 3" + interaction instructions
- **State Embedding**:
  - Serializes `BotState` to JSON
  - Embeds in HTML comment: `<!-- supportbot_state:{...} -->`
  - **Compression**: If JSON > 2000 bytes → gzip + base64
- **GitHub API Call**: Posts comment with embedded state
- **Silent Skip**: If `StopReason` contains "not in allow list" → no comment posted

**10. PersistState Executor**
- Saves execution metrics to `artifacts/metrics/run_{id}_{timestamp}.json`
- Records loop counts, agent scores, execution time

### Multi-User State Management

**Key Concept**: Each user has an independent conversation tracked in `BotState.UserConversations[username]`.

**UserConversation Structure**:
```csharp
{
  "LoopCount": 2,           // 0-3, increments each follow-up round
  "IsExhausted": false,     // true when loop reaches 3
  "IsFinalized": false,     // true when /stop or brief sent
  "FinalizedAt": null,      // timestamp of finalization
  "AskedFields": [...],     // questions already asked to this user
  "CasePacket": {           // their contribution to the case
    "Category": "bug",
    "Severity": "high",
    "UserInputs": [...]
  }
}
```

**SharedFindings Structure**:
```csharp
{
  "Findings": [
    {
      "Content": "API rate limit is 100 req/hour per IP",
      "Source": "web",
      "DiscoveredBy": "yk617"  // Attribution
    }
  ]
}
```

**State Flow Example**:
```
Issue #15: "Python ETL fails with 429 errors"

┌─────────────────────────────────────────────────────────────┐
│ Run 1: KeerthiYasasvi (issue author) opens issue           │
│ LoadState: No previous state → Initialize empty            │
│ Triage: "Rate limiting issue, need error details"          │
│ PostComment: "Can you share logs?" (KeerthiYasasvi Loop 1) │
│ State: {KeerthiYasasvi: {LoopCount: 1, AskedFields: [...]}}│
└─────────────────────────────────────────────────────────────┘
         ↓ (State embedded in comment)
┌─────────────────────────────────────────────────────────────┐
│ Run 2: KeerthiYasasvi comments with logs                   │
│ LoadState: Extract state from bot's comment                │
│ Triage: "Still need retry logic details"                   │
│ PostComment: "What retry strategy?" (Loop 2)               │
│ State: {KeerthiYasasvi: {LoopCount: 2, AskedFields: [...]}}│
└─────────────────────────────────────────────────────────────┘
         ↓ (State embedded)
┌─────────────────────────────────────────────────────────────┐
│ Run 3: yk617 comments (different user!)                    │
│ LoadState: Extract state (has KeerthiYasasvi's data)       │
│ Guardrails: yk617 not in allow list → STOP, silent ignore  │
│ NO COMMENT POSTED                                           │
└─────────────────────────────────────────────────────────────┘
         ↓ (No state change)
┌─────────────────────────────────────────────────────────────┐
│ Run 4: yk617 comments "/diagnose"                          │
│ LoadState: Extract state (still has only KeerthiYasasvi)   │
│ Guardrails: /diagnose detected → add yk617 to allow list   │
│ Triage: Start fresh triage for yk617's perspective         │
│ PostComment: "@yk617 Can you reproduce?" (yk617 Loop 1)    │
│ State: {KeerthiYasasvi: {...}, yk617: {LoopCount: 1, ...}} │
└─────────────────────────────────────────────────────────────┘
         ↓ (State now has 2 users)
┌─────────────────────────────────────────────────────────────┐
│ Run 5: KeerthiYasasvi comments with retry details          │
│ LoadState: Extract state (has both users)                  │
│ ActiveParticipant: KeerthiYasasvi                           │
│ Triage: "Actionable! Have enough info"                     │
│ PostComment: "@KeerthiYasasvi Brief posted" (finalized)    │
│ State: {KeerthiYasasvi: {IsFinalized: true}, yk617: {...}} │
└─────────────────────────────────────────────────────────────┘
         ↓
┌─────────────────────────────────────────────────────────────┐
│ Run 6: yk617 comments with more info                       │
│ LoadState: Extract state (both users, K finalized)         │
│ ActiveParticipant: yk617                                    │
│ PostComment: "@yk617 More questions?" (yk617 Loop 2)       │
│ State: {KeerthiYasasvi: {finalized}, yk617: {Loop: 2}}     │
└─────────────────────────────────────────────────────────────┘
```

---

## 📁 Workspace Structure

### Bot Repository (`github-issues-bot-MAF`)

```
Github-issues-bot-with-MAF/
├── src/
│   ├── SupportConcierge.Cli/           # Entry point (Program.cs)
│   │   ├── Program.cs                  # Main orchestration logic
│   │   └── Evals/                      # Evaluation runners
│   │       ├── EvalConfig.cs
│   │       └── EvalRunner.cs
│   │
│   └── SupportConcierge.Core/          # Core bot logic
│       ├── Agents/                     # LLM-powered agents
│       │   ├── ClassifierAgent.cs      # Triage
│       │   ├── ExtractorAgent.cs       # Research
│       │   ├── FollowUpAgent.cs        # Response
│       │   ├── JudgeAgent.cs           # Critic
│       │   ├── OrchestratorAgent.cs    # Orchestrator
│       │   ├── OpenAiClient.cs         # GPT-4 wrapper
│       │   ├── Prompts.cs              # Prompt templates
│       │   └── Schemas.cs              # Structured output schemas
│       │
│       ├── Guardrails/                 # Security & validation
│       │   ├── CommandParser.cs        # /stop, /diagnose parsing
│       │   ├── DisagreementDetector.cs # User disagreement detection
│       │   ├── SecretRedactor.cs       # PII/secret removal
│       │   └── Validators.cs           # Input validation
│       │
│       ├── Models/                     # Data models
│       │   ├── BotState.cs             # UserConversations, SharedFindings
│       │   ├── CaseModels.cs           # CasePacket, Brief
│       │   ├── EventInput.cs           # GitHub webhook payload
│       │   ├── GitHubModels.cs         # Issue, Comment DTOs
│       │   ├── JudgeModels.cs          # Critic scores
│       │   ├── MetricsModels.cs        # Performance tracking
│       │   └── RunContext.cs           # Shared execution context
│       │
│       ├── SpecPack/                   # Spec-based evaluation
│       │   ├── SpecPackLoader.cs
│       │   └── SpecPackModels.cs
│       │
│       ├── Tools/                      # External integrations
│       │   ├── GitHubTool.cs           # Octokit wrapper
│       │   ├── StateStoreTool.cs       # State serialization
│       │   └── ToolRegistry.cs         # Research tool registry
│       │
│       └── Workflows/                  # MAF workflow definitions
│           ├── SupportConciergeWorkflow.cs  # DAG builder
│           └── Executors/              # Workflow steps
│               ├── GuardrailsExecutor.cs
│               ├── LoadStateExecutor.cs
│               ├── OrchestratorEvaluateExecutor.cs
│               ├── ParseEventExecutor.cs
│               ├── PersistStateExecutor.cs
│               ├── PostCommentExecutor.cs
│               ├── ResearchExecutor.cs
│               ├── ResponseExecutor.cs
│               └── TriageExecutor.cs
│
├── tests/
│   └── SupportConcierge.Tests/
│       ├── GuardrailsTests.cs
│       ├── SecretRedactorTests.cs
│       ├── StateStoreTests.cs
│       └── TestHelpers.cs
│
├── evals/                              # Evaluation configurations
│   ├── eval_config.json                # Eval settings
│   ├── specpack.json                   # Spec file
│   ├── scenarios/                      # Test scenarios
│   │   ├── briefs/
│   │   ├── e2e/
│   │   └── followups/
│   └── goldens/                        # Expected outputs
│
├── artifacts/                          # Generated outputs
│   ├── evals/                          # Eval reports
│   │   ├── eval_report.json
│   │   └── EVAL_REPORT.md
│   └── metrics/                        # Run metrics
│       └── run_{id}_{timestamp}.json
│
├── docs/                               # Documentation
│   ├── ARCHITECTURE.md
│   ├── DEPLOYMENT.md
│   ├── EVALS.md
│   └── HANDOFF_CHECKLIST.md
│
├── scripts/                            # Helper scripts
│   ├── capture-event.md
│   └── cleanup-sandbox.md
│
├── Directory.Build.props               # Shared MSBuild props
├── Directory.Packages.props            # Central package versions
├── SupportConcierge.slnx               # Solution file
├── WORKFLOW_UPDATE_INSTRUCTIONS.md     # Deployment guide
└── PROJECT_DOCUMENTATION.md            # This file
```

---

## 🧪 Test Repository Setup

### Test Repository: `ytm-stream-analytics`

**Purpose**: Real-world test environment for bot interactions

**Workflow Configuration**: `.github/workflows/supportbot.yml`

```yaml
name: Support Concierge Bot
on:
  issues:
    types: [opened, reopened, edited]
  issue_comment:
    types: [created]

jobs:
  supportbot:
    runs-on: ubuntu-latest
    permissions:
      issues: write
      contents: read
    
    steps:
      - name: Checkout bot code
        uses: actions/checkout@v4
        with:
          repository: KeerthiYasasvi/github-issues-bot-MAF
          path: bot
          ref: main  # OR specific commit: d6c40a1
      
      - name: Pull Latest Changes (Force Fresh Pull)
        run: |
          cd bot
          git pull origin main
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      
      - name: Run Support Concierge
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          OPENAI_API_KEY: ${{ secrets.OPENAI_API_KEY }}
          OPENAI_ENDPOINT: ${{ secrets.OPENAI_ENDPOINT }}
          OPENAI_MODEL: ${{ secrets.OPENAI_MODEL }}
          SUPPORTBOT_USERNAME: github-actions[bot]
        run: |
          dotnet run --project bot/src/SupportConcierge.Cli \
            -- --event-file "$GITHUB_EVENT_PATH"
      
      - name: Upload metrics
        uses: actions/upload-artifact@v4
        with:
          name: supportbot-metrics
          path: bot/artifacts/metrics/*.json
```

**Environment Variables**:
- `GITHUB_TOKEN`: Automatic token for posting comments
- `OPENAI_API_KEY`: OpenAI or Azure OpenAI API key
- `OPENAI_ENDPOINT`: API endpoint (optional for Azure)
- `OPENAI_MODEL`: Model name (e.g., `gpt-4`, `gpt-4-turbo`)
- `SUPPORTBOT_USERNAME`: Bot's username (default: `github-actions[bot]`)

**Workflow Trigger Logic**:
- **Issue opened**: Bot responds with initial triage + questions
- **Issue comment**: Bot responds if commenter is in allow list
- **Issue reopened**: Bot starts fresh conversation
- **Issue edited**: Bot re-triages with updated information

---

## 🔧 Multi-Agent Framework (MAF)

### What is MAF?

**Microsoft Agents AI Framework** (MAF) is a .NET library for building **stateful, multi-agent workflows** with:
- **Directed Acyclic Graphs (DAGs)**: Define agent execution order
- **Conditional Edges**: Dynamic routing based on context
- **Executor Pattern**: Each step is an `IExecutor<RunContext>`
- **Shared Context**: `RunContext` flows through all executors
- **Dependency Injection**: Built-in DI for agents and tools

### Workflow Builder Pattern

```csharp
var builder = new WorkflowBuilder(parseEvent)
    .BindExecutor(loadState)
    .BindExecutor(guardrails)
    .BindExecutor(triage)
    .BindExecutor(research)
    .BindExecutor(response)
    .BindExecutor(orchestratorEvaluate)
    .BindExecutor(postComment)
    .BindExecutor(persistState);

// Conditional edges
builder.AddEdge<RunContext>(
    guardrails, 
    postComment, 
    ctx => ctx?.ShouldStop ?? false  // Stop early if needed
);

builder.AddEdge<RunContext>(
    guardrails, 
    triage, 
    ctx => !(ctx?.ShouldStop ?? false)  // Continue if not stopped
);
```

### Executor Interface

```csharp
public interface IExecutor<TInput>
{
    ValueTask<TInput> ExecuteAsync(TInput input, CancellationToken ct);
}
```

Each executor:
1. Receives `RunContext` with current state
2. Performs its operation (call LLM, update state, post comment, etc.)
3. Returns updated `RunContext`
4. Next executor receives the updated context

### RunContext Structure

```csharp
public class RunContext
{
    // Input data
    public string EventName { get; set; }
    public Issue Issue { get; set; }
    public Comment? IncomingComment { get; set; }
    
    // State management
    public BotState? State { get; set; }
    public string ActiveParticipant { get; set; }
    public UserConversation? ActiveUserConversation { get; set; }
    
    // Agent outputs
    public ClassificationResult? Classification { get; set; }
    public ResearchResult? ResearchResult { get; set; }
    public ResponseResult? ResponseResult { get; set; }
    
    // Flow control
    public bool ShouldStop { get; set; }
    public bool ShouldAskFollowUps { get; set; }
    public bool ShouldFinalize { get; set; }
    public bool ShouldEscalate { get; set; }
    public string? StopReason { get; set; }
    
    // Commands
    public bool IsStopCommand { get; set; }
    public bool IsDiagnoseCommand { get; set; }
    public bool IsDisagreement { get; set; }
    
    // Generated content
    public List<FollowUpQuestion> FollowUpQuestions { get; set; }
    public Brief? Brief { get; set; }
    
    // Metrics
    public ExecutionState? ExecutionState { get; set; }
}
```

---

## 💾 State Management

### State Persistence Strategy

**Challenge**: GitHub Actions are stateless - each workflow run starts fresh.

**Solution**: Embed state in bot's comments using HTML comments (invisible to users).

**Marker Format**:
```html
<!-- supportbot_state:{"UserConversations":{"KeerthiYasasvi":{"LoopCount":2,...}}} -->
```

**Compression**: If JSON > 2000 bytes:
```html
<!-- supportbot_state:compressed:H4sIAAAAAAAA/6tWKkktLsnPTVWyUlAqz... -->
```

### StateStoreTool Implementation

**Embedding State**:
```csharp
public string EmbedState(string commentBody, BotState state)
{
    var json = JsonSerializer.Serialize(state);
    
    if (json.Length > CompressionThresholdBytes)
    {
        json = "compressed:" + CompressString(json);
    }
    
    var marker = $"{StateMarkerPrefix}{json}{StateMarkerSuffix}";
    return commentBody + "\n\n" + marker;
}
```

**Extracting State**:
```csharp
public BotState? ExtractState(string commentBody)
{
    var pattern = Regex.Escape(StateMarkerPrefix) + @"(.+?)" + Regex.Escape(StateMarkerSuffix);
    var matches = Regex.Matches(commentBody, pattern, RegexOptions.Singleline);
    
    if (matches.Count == 0) return null;
    
    var data = matches[^1].Groups[1].Value.Trim();
    
    if (data.StartsWith("compressed:"))
    {
        data = DecompressString(data.Substring("compressed:".Length));
    }
    
    return JsonSerializer.Deserialize<BotState>(data);
}
```

### LoadStateExecutor Logic

```csharp
1. Fetch all comments on the issue (via GitHub API)
2. Iterate comments in reverse chronological order (newest first)
3. For each comment:
   a. Check if author is "github-actions[bot]"
   b. If yes, call StateStoreTool.ExtractState(comment.Body)
   c. If state found → load it, BREAK
4. If no state found → initialize new BotState
5. If state has UserConversations → use it
6. If state has old format (single user) → migrate to multi-user
7. If ActiveParticipant not in UserConversations → add them
8. Set RunContext.ActiveUserConversation = UserConversations[ActiveParticipant]
```

### State Migration

Converts legacy single-user state to multi-user format:

```csharp
if (loadedState.UserConversations == null && loadedState.LoopCount > 0)
{
    // Old format: single user tracked at root level
    var issueAuthor = input.Issue?.User?.Login ?? string.Empty;
    
    loadedState.UserConversations = new Dictionary<string, UserConversation>
    {
        [issueAuthor] = new UserConversation
        {
            LoopCount = loadedState.LoopCount,
            IsFinalized = loadedState.IsFinalized,
            AskedFields = loadedState.AskedFields ?? new(),
            CasePacket = loadedState.CasePacket
        }
    };
    
    Console.WriteLine("[MAF] LoadState: Migrated legacy single-user state");
}
```

---

## ✅ Implemented Features

### Phase 1: Core Bot Functionality
- ✅ **GitHub Actions Integration**: Triggered on issue open/comment events
- ✅ **Multi-Agent Workflow**: Triage → Research → Response → Orchestrator
- ✅ **State Persistence**: Embedded state in HTML comments for survival across runs
- ✅ **LLM Integration**: OpenAI GPT-4 for all agents
- ✅ **Structured Output**: JSON schema enforcement for agent responses
- ✅ **Critic System**: Quality validation for each agent's output

### Phase 2: Multi-User Support (Commits dd5a1d8, 84cec4d)
- ✅ **UserConversations Dictionary**: Per-user state tracking
- ✅ **ActiveParticipant Logic**: Dynamic user identification
- ✅ **Separate Loop Counters**: Each user has independent loop count (0-3)
- ✅ **SharedFindings**: Cross-user research attribution
- ✅ **Per-User Finalization**: Users can /stop independently
- ✅ **Allow List**: Issue author + /diagnose users can interact

### Phase 3: Command System
- ✅ **`/stop` Command**: User opts out, conversation finalized
- ✅ **`/diagnose` Command**: New user joins conversation
- ✅ **Command Parsing**: Regex-based detection in comments
- ✅ **Disagreement Detection**: Allows brief regeneration after finalization

### Phase 4: Security & Guardrails
- ✅ **Bot Comment Detection**: Prevents infinite loops from bot's own comments
- ✅ **Allow List Enforcement**: Only authorized users trigger bot responses
- ✅ **Secret Redaction**: PII/API key removal from logs
- ✅ **Input Validation**: Sanitize user inputs before LLM calls
- ✅ **Loop Limit**: Max 3 iterations per user, auto-escalate after

### Phase 5: Research Tools
- ✅ **Tool Registry**: Pluggable research tools
- ✅ **Web Search**: Bing integration for external docs
- ✅ **Issue Search**: Previous issues with similar problems
- ✅ **Documentation Lookup**: Project-specific doc search
- ✅ **Tool Execution Tracking**: Logs which tools agents used

### Phase 6: Testing & Evaluation
- ✅ **Unit Tests**: StateStoreTool, SecretRedactor, Guardrails
- ✅ **Spec-Based Evals**: Golden test scenarios
- ✅ **Metrics Collection**: Per-run performance tracking
- ✅ **Eval Reports**: JSON + Markdown outputs

---

## 🐛 Bugs Fixed

### Bug #1: GITHUB_ACTOR Misuse (Commit c6c829d) ✅
- **Problem**: Used `GITHUB_ACTOR` env var for bot identity, but it represents workflow **trigger user** (e.g., "yk617"), not bot
- **Impact**: Bot couldn't load state because it looked for comments from "yk617" instead of "github-actions[bot]"
- **Fix**: Removed GITHUB_ACTOR, use only `SUPPORTBOT_USERNAME` env var
- **Evidence**: Logs changed from `preferred=..., actual=...` to `Looking for bot comments from: github-actions[bot]`

### Bug #2: Command Parsing Contamination (Commit c58c825) ✅
- **Problem**: GuardrailsExecutor concatenated issue body + ALL comment bodies, then parsed for commands
- **Impact**: Bot detected "/stop" from its own previous responses, causing false /stop triggers
- **Fix**: Parse ONLY the incoming comment body for commands
- **Code Change**: 
  ```csharp
  // BEFORE: input.Issue?.Body + all comments
  // AFTER: input.IncomingComment?.Body (current comment only)
  ```

### Bug #3: State Loading Debug Gaps (Commit 2064fd4) ✅
- **Problem**: When state didn't load, no visibility into why (no logs showing comment count, authors, regex matches)
- **Impact**: Couldn't diagnose why state loading failed
- **Fix**: Added comprehensive debug logging in LoadStateExecutor and StateStoreTool
- **Logs Added**:
  - Total comment count on issue
  - Each comment's ID, author, body preview
  - Whether state marker found in each comment
  - Regex match success/failure
  - Deserialization success/failure

### Bug #4: Loop Count Display Bug (Commit e52f946) ✅
- **Problem**: PostCommentExecutor used obsolete `State.LoopCount` (always 0) instead of `ActiveUserConversation.LoopCount`
- **Impact**: Bot showed "Loop 0 of 3" in every comment, even though internal state tracked correct loop counts
- **Fix**: Changed line 105 to use `ActiveUserConversation?.LoopCount`
- **Evidence**: Run #86 logs showed correct internal state (Loop=1) but comment displayed "Loop 0"

### Bug #5: Wrong User Mentioned (Commit d6c40a1) ✅
- **Problem**: PostCommentExecutor always mentioned issue author, not the actual commenter
- **Impact**: Bot said "@KeerthiYasasvi" when replying to yk617
- **Fix**: Changed to mention `ActiveParticipant` instead of `issue.User.Login`

### Bug #6: False /stop Messages (Commit d6c40a1) ✅
- **Problem**: When non-allowed user commented, bot posted "You've opted out with /stop" to issue author
- **Impact**: Confusing false /stop messages to wrong users
- **Fix**: 
  1. Only post /stop message when `IsStopCommand=true` (user explicitly typed /stop)
  2. Silently skip comment when `StopReason` contains "not in allow list"

### Bug #7: Non-Allowed Users Trigger Responses (Commit d6c40a1) ✅
- **Problem**: Guardrails set `ShouldStop=true` for non-allowed users, but PostCommentExecutor still posted comments
- **Impact**: Random users could spam bot interactions
- **Fix**: Added check in PostCommentExecutor to return early (no comment) when stop reason is "not in allow list"

---

## ⚠️ Known Issues

### 1. Critique Scoring Bug 🔴 CRITICAL PRIORITY
**Problem**: Critic agent returns **identical scores for every agent in every run**, exhibiting completely deterministic behavior.

**Specific Observed Scores**:
```json
// EVERY RUN (12, 34, 86, etc.):
"TriageScore": 5,      // Always 5
"ResearchScore": 2,    // Always 2
"ResponseScore": 2     // Always 2
```

**Impact**:
- Fallback mechanisms never trigger (triage always "excellent", research/response always "poor")
- No signal for actual output quality variations
- Metrics completely useless for evaluation
- Agent improvements can't be measured
- System appears to have learned a fixed scoring pattern

**Root Cause Hypothesis**:
1. **Deterministic Prompt**: Critic prompt likely produces identical reasoning every time
2. **No Context Variation**: Critic sees similar inputs every run, memorizes pattern
3. **Temperature Too Low**: LLM temperature may be 0 (fully deterministic)
4. **Prompt Structure**: May explicitly or implicitly bias toward these exact scores
5. **Schema Constraint**: Possibly forcing certain score ranges per agent type

**Why This Matters**:
The scoring is not just "similar" - it's **byte-for-byte identical** across all runs, suggesting the critic is not actually evaluating quality but following a fixed template response. This means the entire critique system is non-functional.

**Debug Steps**:
- [ ] Add logging of full critic prompt + response
- [ ] Add examples of bad outputs (score 1-2) to critic prompt
- [ ] Vary temperature (try 0.7 instead of 0.3)
- [ ] Test critic with known bad inputs (empty, nonsensical)
- [ ] Check if critic reasoning is actually different (not just scores)

### 2. Agent Reasoning Not Logged � HIGH PRIORITY
**Problem**: Workflow logs don't show **any agent reasoning, tool usage, or decision-making process** for any of the 5 agents.

**Missing Visibility**:
- **Triage Agent**: Why it classified as bug/feature, why actionable/not actionable
- **Research Agent**: Which tools selected, search queries used, why those tools
- **Response Agent**: Why these specific questions, what information gaps identified
- **Critic Agent**: Full reasoning for scores (though scores are broken - see bug #1)
- **Orchestrator Agent**: Decision logic (finalize vs ask follow-ups vs escalate)

**Impact**:
- Impossible to debug why bot asked certain questions (see Issue #16 - nonsensical questions)
- Can't understand research agent's tool selection rationale
- No visibility into orchestrator's decision logic
- Can't audit triage classification reasoning
- Can't determine if agents are hallucinating or making logical decisions
- Cannot validate if multi-agent system is actually working as designed

**Desired Logs** (Human-Readable Format):
```
[Triage] Classification:
  Category: bug
  Severity: high
  Actionability: 3/5
  Reasoning: Issue mentions "429 error" + "rate limit" + "Azure Functions" → Rate limiting bug
  Missing Info: retry logic implementation details, exact request volume/patterns

[Research] Tool Selection:
  Selected Tools: SearchWeb, SearchIssues
  SearchWeb Query: "Azure Functions rate limiting 429 error"
  SearchIssues Query: "label:bug 429 rate limit"
  Reasoning: Need external docs on Azure rate limits + check if similar issues exist
  
[Research] Findings:
  - Found 3 related issues (#8, #12, #14)
  - Found 2 doc pages: Azure Functions limits, API best practices
  - Key insight: Azure outbound IP may be shared, causing aggregate rate limiting

[Response] Question Generation:
  Question 1: "What retry logic do you have?" (Not asked before, critical for diagnosis)
  Question 2: "How many requests per minute?" (New, helps quantify volume)
  Question 3: "Using shared or dedicated App Service Plan?" (New, Azure-specific)
  Reasoning: Need retry implementation to assess if exponential backoff is correct
            Need request volume to validate if hitting documented limits
            Need plan type because shared IPs have aggregate limits

[Critic] Evaluation:
  Triage Score: 5/5 - Clear categorization, identified key missing info
  Research Score: 2/5 - Found issues but didn't extract actionable insights from them
  Response Score: 2/5 - Questions too generic, didn't reference research findings
  Reasoning: [Full reasoning text from critic prompt response]

[Orchestrator] Decision:
  Input State: Loop 1, not actionable, 3 questions generated
  Decision: Ask follow-ups (ShouldAskFollowUps = true)
  Reasoning: Issue not actionable yet, have targeted questions, under loop limit
  Next Action: Post questions, increment loop counter, wait for user response
```

**Implementation**:
- [ ] Add `Console.WriteLine` for agent reasoning in each executor
- [ ] Log structured output JSON before critic validation
- [ ] Show which fields are missing for actionability
- [ ] Display tool execution results (not just "research complete")

### 3. Loop Counter Not Incrementing 🔴 CRITICAL PRIORITY
**Problem**: Loop counter **does not increment** between bot interactions. Bot always shows "Loop 1 of 3" even after multiple follow-up rounds.

**Evidence** (Issue #16):
```
Comment 1 (Bot): "Loop 1 of 3" + questions
Comment 2 (User): Answers
Comment 3 (Bot): "Loop 1 of 3" + more questions  ← WRONG, should be Loop 2
Comment 4 (User): More answers  
Comment 5 (Bot): "Loop 1 of 3" + more questions  ← WRONG, should be Loop 3
```

**Impact**:
- Bot never escalates (thinks it's always on loop 1)
- Users answer same questions repeatedly
- No convergence toward finalization
- Max loop limit (3) never enforced
- Bot appears broken to users

**Root Cause Hypothesis**:
1. **Loop Counter Not Saved**: `UserConversation.LoopCount` incremented but not persisted in state
2. **State Not Loaded**: Loop counter resets because state extraction fails
3. **Wrong Property Used**: Code may be reading/writing different loop counter properties
4. **Increment Timing**: Counter incremented AFTER state embedding instead of BEFORE

**Where to Debug**:
- LoadStateExecutor: Check if `ActiveUserConversation.LoopCount` is loaded correctly
- OrchestratorEvaluateExecutor: Check if loop counter is incremented
- PostCommentExecutor: Verify state is embedded AFTER counter increment
- Run logs: Check "Embedded state" message shows correct loop count

**Related**: This may be why Issue #16 shows nonsensical questions - bot forgets context because it thinks every interaction is Loop 1.

### 4. Multi-User Loop Counter Carries Over 🔴 CRITICAL PRIORITY
**Problem**: When a second user comments, their loop counter **starts at the first user's loop count** instead of 0. Loop counters are not independent per user.

**Evidence** (Issue #16):
```
KeerthiYasasvi (issue author):
  Comment 1 (Bot): "@KeerthiYasasvi Loop 1 of 3"
  Comment 2 (KeerthiYasasvi): Answers
  Comment 3 (Bot): "@KeerthiYasasvi Loop 1 of 3" (should be 2, but see bug #3)

yk617 (second user with /diagnose):
  Comment 4 (yk617): "/diagnose" + question
  Comment 5 (Bot): "@yk617 Loop 1 of 3"  ← WRONG, why does yk617 start at loop 1?
                                         ← Should be Loop 1, but only by coincidence
  
Actual Expected Flow:
  yk617 should start at Loop 1 (correct)
  But if KeerthiYasasvi was at Loop 2, yk617 would start at Loop 2 (broken)
```

**Impact**:
- Second user inherits first user's progress
- Second user may hit loop limit immediately if first user was at Loop 3
- Multi-user conversations are fundamentally broken
- Users can't have independent conversations on same issue
- Bot behavior is unpredictable and user-dependent

**Root Cause Hypothesis**:
1. **Shared Loop Counter**: Code may use `State.LoopCount` (obsolete) instead of `UserConversations[user].LoopCount`
2. **Wrong User Key**: When adding new user to `UserConversations`, copies existing user's data
3. **Initialization Bug**: New `UserConversation` initialized with wrong loop count (copies from state instead of 0)
4. **Reference vs Copy**: New user gets reference to existing user's object instead of fresh copy

**Where to Debug**:
- LoadStateExecutor: Check logic when adding new user to `UserConversations`
- GuardrailsExecutor: Check /diagnose command handling - does it initialize loop to 0?
- PostCommentExecutor: Check if loop increment applies to correct user's conversation
- State JSON: Manually inspect embedded state to see if each user has separate loop counts

**Why This is Critical**:
This bug makes the entire multi-user feature non-functional. Until fixed, the bot cannot support multiple users on the same issue, which was a primary design goal (Phase 2 implementation).

### 5. Nonsensical Questions Generated 🔴 HIGH PRIORITY
**Problem**: Response agent sometimes generates questions that don't make sense given the context or previous answers (see Issue #16).

**Impact**:
- Users get confused by irrelevant questions
- Bot appears to not "listen" to previous answers
- Wastes user time answering questions that don't help diagnosis
- Reduces trust in bot's intelligence

**Possible Causes**:
1. **Context Loss**: Related to bug #3 - if loop counter doesn't increment, bot forgets previous Q&A
2. **AskedFields Not Checked**: Response agent may not properly check `UserConversation.AskedFields`
3. **Poor Prompt**: Response agent prompt may not emphasize "avoid redundant questions"
4. **No Reasoning Logs**: Can't debug without seeing why agent selected those questions (see bug #2)

**Debug Strategy**:
1. Fix bug #3 (loop counter) first - may resolve this automatically
2. Add logging for bug #2 (agent reasoning) - see why questions selected
3. Review Response agent prompt for redundancy prevention
4. Check if `AskedFields` is properly populated and queried

### 6. Response Agent Ignores Triage Classification ✅ FIXED (Commit 5b0ce74)
**Problem**: Response agent generated **generic technical debugging questions** (error logs, OS, runtime) for ALL issue types, including documentation issues and feature requests. This made the bot appear "dumb" and frustrated users.

**Evidence** (Issue #16):
```
Issue: "README clone instructions point to a different repo/directory (ytm-stream-analytics)"
Triage: Correctly classified as "documentation_issue"
Questions Generated:
  1. "Please share the exact error message and any relevant logs or stack traces."
  2. "What OS and versions are you using (runtime/build tool)?"
  3. "What steps lead to the failure?"

User Response: "This is more of a README consistency issue than a runtime failure..."
Bot Comment 2: Asked IDENTICAL questions again (completely ignored correction)
yk617 uses /diagnose: Restates it's a documentation issue
Bot Comment 3: Asked SAME technical questions to yk617
```

**Impact**:
- Bot fundamentally broken for ~50% of issues (documentation, feature requests, configuration)
- Users waste time answering irrelevant questions
- Bot appears to not "listen" or understand basic issue types
- Makes triage agent's classification work completely useless
- Reduces user trust in bot's intelligence

**Root Cause**:
The `response-followup.md` prompt template was too generic. It mentioned `{CATEGORIES}` in the prompt but didn't provide **category-specific guidance** for question generation. The LLM defaulted to technical debugging questions (most common pattern in training data).

**Fix (Commit 5b0ce74)**:
Enhanced [prompts/maf-templates/response-followup.md](prompts/maf-templates/response-followup.md) with explicit category-specific guidelines:
- **documentation_issue** → Ask about files, content, what should change (NEVER error logs)
- **feature_request** → Ask about use case, design, alternatives (NEVER debugging steps)
- **runtime_error/bug_report** → Ask about errors, environment, reproduction steps
- **build_issue** → Ask about build tools, configuration, output
- **configuration_error** → Ask about config files, settings, expected behavior
- **dependency_conflict** → Ask about package manager, lock files, conflicts
- **environment_setup** → Ask about setup steps, prerequisites, setup logs

Added critical instruction: "**Match the category - documentation issues should NOT get debugging questions!**"

**Validation**:
- Issue #16 should now get: "Which file needs updating?" "What's incorrect?" "What should it say?"
- Technical issues still get: "What error message?" "What environment?" "How to reproduce?"
- Feature requests get: "What problem does this solve?" "How should it work?" "Alternatives?"

### 7. Comment Parsing Edge Cases 🟡 MEDIUM PRIORITY
**Problem**: Current command detection is simple regex, may miss edge cases.

**Edge Cases**:
- `/stop` in code blocks should be ignored
- `/diagnose` in quoted text should be ignored
- Multiple commands in one comment (which wins?)
- Typos: `/stoop`, `/diagnoze` (fuzzy matching?)

**Current Regex**: `@"(?:^|\s)/stop(?:\s|$)"` (word boundary)

**Improvements**:
- [ ] Ignore commands in Markdown code blocks (` ``` ` or ` ` `)
- [ ] Ignore commands in blockquotes (`> /stop`)
- [ ] Priority order if multiple commands: /stop > /diagnose
- [ ] Fuzzy matching with Levenshtein distance ≤ 2

### 7. State Size Growth 🟢 LOW PRIORITY
**Problem**: As more users join, `UserConversations` grows. With compression, this is mitigated but not eliminated.

**Current Limits**:
- Compression threshold: 2000 bytes
- GitHub comment max: 65536 chars
- Typical state size: 500-2000 bytes per user

**Potential Issues**:
- 30+ users on one issue → state may exceed comment limit
- Long conversation histories bloat `SharedFindings`

**Mitigations**:
- [ ] Prune finalized users after 7 days
- [ ] Limit `SharedFindings` to 10 most recent
- [ ] Move large state to GitHub Gists (link in comment)

### 9. Loop Exhaustion Messaging 🟢 LOW PRIORITY
**Problem**: When user reaches loop 3, bot says "I'll escalate to maintainer" but doesn't actually tag anyone.

**Current Behavior**:
```
Loop 3 of 3. I'll escalate to maintainer after 3 attempts if issue remains unclear.
```

**Improvement**:
- [ ] Detect repository maintainers (CODEOWNERS, admin collaborators)
- [ ] Mention maintainers in escalation comment: "@maintainer FYI"
- [ ] Create "needs-triage" label automatically
- [ ] Post summary of what was gathered so far

---

## 🚀 Future Enhancements

### Critical Bug Fixes (Must Fix First)

#### 1. Fix Loop Counter Not Incrementing (Bug #3)
- **Goal**: Make loop counter actually increment between interactions
- **Approach**:
  - Add debug logging to show loop count before/after increment
  - Verify `OrchestratorEvaluateExecutor` increments `ActiveUserConversation.LoopCount`
  - Ensure increment happens BEFORE `PostCommentExecutor` embeds state
  - Check that state embedding captures the incremented value
  - Test with Issue #16 to verify "Loop 1 → Loop 2 → Loop 3" progression

#### 2. Fix Multi-User Loop Counter Carryover (Bug #4)
- **Goal**: Each user starts at Loop 1, independent counters
- **Approach**:
  - Debug `LoadStateExecutor` - when adding new user to `UserConversations`, ensure loop starts at 1
  - Check if using `State.LoopCount` (obsolete) instead of per-user loop count
  - Verify `/diagnose` command creates new `UserConversation` with `LoopCount = 1`
  - Test with Issue #16: KeerthiYasasvi at Loop 2, yk617 uses `/diagnose` → yk617 should be Loop 1

### High Priority

#### 3. Fix Critique Scoring System (Bug #1)
- **Goal**: Make critic scores meaningful and variable
- **Approach**: 
  - Add negative examples to critic prompt (bad classifications, irrelevant questions)
  - Log full critic reasoning, not just scores
  - Adjust temperature for more variability
  - Add test cases with known bad inputs

#### 2. Enhanced Logging & Observability
- **Goal**: Full transparency into agent reasoning
- **Features**:
  - Structured logging of all agent prompts + responses
  - Tool execution traces (which tools, what queries, results)
  - Decision tree visualization for orchestrator logic
  - Per-agent execution time metrics
  - Token usage tracking (cost estimation)

#### 3. Smarter Research Agent
- **Goal**: Reduce hallucinations, improve context gathering
- **Features**:
  - RAG-based documentation search (vector embeddings)
  - GitHub code search with semantic ranking
  - Stack Overflow integration (via API)
  - Previous issue similarity scoring (cosine similarity)
  - Fact verification against multiple sources

#### 4. Brief Quality Improvements
- **Goal**: Actionable, maintainer-friendly case packets
- **Features**:
  - Structured brief templates (steps to reproduce, expected vs actual)
  - Severity scoring with justification
  - Auto-link related issues and PRs
  - Code snippet extraction from user descriptions
  - Environment matrix (OS, runtime versions, dependencies)

### Medium Priority

#### 5. Adaptive Questioning
- **Goal**: Fewer loops, smarter questions
- **Features**:
  - Category-specific question banks (bug vs feature vs question)
  - Conditional follow-ups based on previous answers
  - Avoid generic questions ("What OS?") when context provides it
  - Pre-fill answers from issue body/comments (ask for confirmation)

#### 6. Maintainer Handoff
- **Goal**: Smooth escalation to humans
- **Features**:
  - Auto-detect CODEOWNERS and mention them
  - Create "needs-triage" or "help-wanted" labels
  - Post brief summary with all gathered info
  - Allow maintainer override: `/bot finalize` or `/bot loop-again`

#### 7. Multi-Issue Learning
- **Goal**: Bot improves over time
- **Features**:
  - Track which questions lead to actionable briefs
  - Learn common issue patterns (frequent bugs, FAQs)
  - Suggest issue templates based on category
  - Build knowledge base from resolved issues

#### 8. Better Multi-User Coordination
- **Goal**: Leverage multiple perspectives
- **Features**:
  - Merge complementary findings from different users
  - Detect conflicting information (user A says X, user B says Y)
  - Prioritize issue author's answers over external users
  - Show "N users helping" in status messages

### Low Priority

#### 9. Localization & Accessibility
- **Goal**: Support non-English issues
- **Features**:
  - Detect issue language (langdetect)
  - Translate questions to user's language (GPT-4 multilingual)
  - Post responses in original language
  - English-only logging/state (for maintainers)

#### 10. Interactive UI
- **Goal**: Web dashboard for bot management
- **Features**:
  - View all bot interactions (filterable by repo, user, status)
  - Manually override bot decisions (mark as actionable, reopen loop)
  - Review and edit briefs before maintainer sees them
  - Analytics dashboard (avg loops, actionability rate, top categories)

#### 11. Advanced Guardrails & Security
- **Goal**: Prevent abuse, misuse, and security vulnerabilities
- **Features**:
  - **Prompt Injection Defense**: Detect and block attempts to manipulate agent prompts
    - Input sanitization (remove markdown injection, code fence escaping)
    - System prompt isolation (user input never mixed with system instructions)
    - Jailbreak pattern detection ("ignore previous instructions", "you are now...", etc.)
    - Content validation before LLM calls (check for adversarial patterns)
  - **Rate Limiting**: Max 5 comments per user per hour, max 20 bot interactions per issue
  - **Spam Detection**: Repeated messages, gibberish, excessive length
  - **Profanity Filter**: Reject toxic/abusive comments (return silent ignore)
  - **Auto-Lock Issues**: After 10 bot interactions → require human review before continuation
  - **PII Detection**: Enhanced secret redaction (SSN, credit cards, API keys, passwords)
  - **Malicious Link Blocking**: Prevent phishing links in issue bodies/comments

#### 12. Integration Ecosystem
- **Goal**: Connect with other tools
- **Features**:
  - Jira ticket creation from actionable briefs
  - Slack notifications for escalated issues
  - Linear/Asana task generation
  - Sentry/Datadog error log lookup
  - Discord/Telegram bot variants (same backend)

---

## 📝 Development Workflow

### Local Testing
```bash
# Build
cd Github-issues-bot-with-MAF
dotnet build

# Run tests
dotnet test

# Run evaluations
dotnet run --project src/SupportConcierge.Cli -- --eval

# Test on specific GitHub event
dotnet run --project src/SupportConcierge.Cli -- --event-file path/to/event.json
```

### Debugging Tips
1. **Capture GitHub Event**: Use `scripts/capture-event.md` to save webhook payload
2. **Run Locally**: Test with captured event (no need to trigger GitHub Actions)
3. **Check Logs**: All executors log with `[MAF]` prefix for easy filtering
4. **Dry Run Mode**: Set env var `DRY_RUN=true` to prevent comment posting

### Deployment Process
1. **Test Locally**: Verify changes with captured events
2. **Commit to `main`**: Push to bot repository
3. **Update Test Repo** (if using Option 1): Change `ref:` in workflow to new commit SHA
4. **Verify in Actions**: Check workflow logs for errors
5. **Monitor Live Issues**: Watch bot responses on real issues

---

## 🔍 Troubleshooting

### Common Issues

**Bot Not Responding**
- ✅ Check workflow is enabled in test repo (Actions tab)
- ✅ Verify GITHUB_TOKEN has `issues: write` permission
- ✅ Check OPENAI_API_KEY is set correctly
- ✅ Review workflow logs for errors
- ✅ Ensure user is in allow list (issue author or used /diagnose)

**State Not Persisting**
- ✅ Check bot comment has `<!-- supportbot_state:... -->` marker
- ✅ Verify StateStoreTool.ExtractState logs (should see "Found 1 matches")
- ✅ Check for JSON deserialization errors
- ✅ Ensure bot username matches (SUPPORTBOT_USERNAME env var)

**Wrong Loop Count Displayed**
- ✅ Verify commit e52f946 is deployed (loop display fix)
- ✅ Check PostCommentExecutor uses `ActiveUserConversation.LoopCount`
- ✅ Review workflow logs for "Embedded state" message with correct loop count

**False /stop Messages**
- ✅ Verify commit c58c825 is deployed (command parsing fix)
- ✅ Check GuardrailsExecutor parses only incoming comment
- ✅ Ensure commit d6c40a1 is deployed (multi-user addressing fix)

**Non-Allowed User Triggers Bot**
- ✅ Verify commit d6c40a1 is deployed (allow list fix)
- ✅ Check logs for "Silently ignoring" message
- ✅ Ensure PostCommentExecutor has "not in allow list" check

---

## 📊 Metrics & Evaluation

### Collected Metrics
- **Loop Statistics**: Avg loops per user, max loops reached, finalization rate
- **Agent Scores**: Critic scores for triage, research, response (WARNING: currently bugged)
- **Execution Time**: Total workflow time, per-agent time
- **Tool Usage**: Which research tools used, how often
- **Actionability Rate**: % of issues marked actionable after loops

### Evaluation Framework
- **Spec-Based Evals**: Golden test scenarios in `evals/scenarios/`
- **Human Review**: Manual audit of bot responses
- **A/B Testing**: Compare different prompts/models (future)

---

## 🤝 Contributing

### Adding New Agents
1. Create agent class in `src/SupportConcierge.Core/Agents/`
2. Implement structured output schema in `Schemas.cs`
3. Add executor in `src/SupportConcierge.Core/Workflows/Executors/`
4. Bind executor in `SupportConciergeWorkflow.cs`
5. Add to `RunContext` for passing data between executors

### Adding New Tools
1. Create tool class in `src/SupportConcierge.Core/Tools/`
2. Implement `ITool` interface
3. Register in `ToolRegistry.cs`
4. Add to research agent's prompt

### Adding New Commands
1. Add regex pattern to `CommandParser.cs`
2. Add command flag to `RunContext`
3. Handle in `GuardrailsExecutor.cs`
4. Update `PostCommentExecutor.cs` for response message

---

## 📚 References

- [Microsoft Agents AI Framework](https://github.com/microsoft/agents)
- [GitHub Actions Workflows](https://docs.github.com/en/actions)
- [OpenAI Structured Outputs](https://platform.openai.com/docs/guides/structured-outputs)
- [Octokit.NET](https://github.com/octokit/octokit.net)

---

## ❓ Questions & Gaps

Please review this documentation and let me know if:

1. **Missing Information**: Any sections need more detail?
2. **Technical Accuracy**: Are the architecture diagrams correct?
3. **Use Case Coverage**: Are there user scenarios not documented?
4. **Future Plans**: Any features I missed for the roadmap?
5. **Deployment Details**: Need more specific instructions for test repo setup?

---

**Last Updated**: January 28, 2026  
**Bot Version**: Commit d6c40a1  
**Status**: Active Development
