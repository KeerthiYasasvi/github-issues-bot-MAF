# GitHub Issues Support Bot (MAF Workflows)

A **multi-agent AI system** built with Microsoft Agent Framework (MAF) that automatically triages GitHub issues, gathers missing information through iterative conversations, and produces actionable engineer briefs. The bot uses a coordinated team of specialized agents (Orchestrator, Triage, Research, Response, Critic) to deliver intelligent issue handling.

## Key Features

- **Multi-Agent Architecture** - Orchestrator plans, specialists execute, Critic validates
- **Deterministic-First Extraction** - Parse issue forms before any LLM call
- **Quality Gates** - Every agent output goes through critique validation
- **Multi-User Support** - Independent conversations per user on same issue
- **State Persistence** - Survives stateless GitHub Actions via HTML comments
- **Configurable Behavior** - YAML-based Spec Packs for categories, checklists, routing
- **Comprehensive Evals** - Rubric-based evaluation framework for quality measurement

## How It Works

```
┌─────────────────────────────────────────────────────────────────┐
│                   Orchestrator Agent (Planner)                  │
│         • Plans investigation • Evaluates progress              │
│         • Decides: ask questions / finalize / escalate          │
└─────────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
    ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
    │  Triage Agent   │ │ Research Agent  │ │ Response Agent  │
    │  • Classify     │ │ • Select tools  │ │ • Generate brief│
    │  • Extract info │ │ • Investigate   │ │ • Follow-ups    │
    └────────┬────────┘ └────────┬────────┘ └────────┬────────┘
             │                   │                   │
             └───────────────────┴───────────────────┘
                                 │
                    ┌────────────▼────────────┐
                    │      Critic Agent       │
                    │    • Validate outputs   │
                    │    • Score quality      │
                    │    • Trigger refinement │
                    └─────────────────────────┘
```

### Workflow Flow

```
ParseEvent → LoadState → Guardrails → OffTopicCheck → LoadSpecPack → Triage 
    → AddLabels → CasePacket → ResearchGate → Research → Response 
    → OrchestratorEvaluate → [Ask Questions | Finalize | Escalate] 
    → PostComment → PersistState
```

### Loop Behavior (Per User)

| Loop | Action |
|------|--------|
| 1 | Initial triage + targeted questions |
| 2 | With answers, more questions or response |
| 3 | Final response or prepare escalation |
| 4+ | Escalate to human maintainer |

## Quick Start

### Option A: Deploy as Submodule (Recommended)

```bash
# Add bot as submodule
cd your-repo
git submodule add https://github.com/your-org/github-issues-bot-MAF.git bot
git submodule update --init --recursive
```

Create `.github/workflows/supportbot.yml`:

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
      - uses: actions/checkout@v4
        with:
          submodules: recursive

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - name: Run Support Concierge
        if: github.event_name == 'issues' || github.event.comment.user.login != 'github-actions[bot]'
        run: dotnet run --project bot/src/SupportConcierge.Cli -- --event-file "$GITHUB_EVENT_PATH"
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          OPENAI_API_KEY: ${{ secrets.API_KEY }}
          OPENAI_MODEL: ${{ vars.PRIMARY_MODEL || 'gpt-5.4-mini' }}
          OPENAI_CRITIQUE_MODEL: ${{ vars.SECONDARY_MODEL || 'gpt-5.4-nano' }}
          SUPPORTBOT_USERNAME: github-actions[bot]
```

Configure repository:
- **Secrets**: `API_KEY` (OpenAI API key)
- **Variables**: `PRIMARY_MODEL` (gpt-5.4-mini), `SECONDARY_MODEL` (gpt-5.4-nano)

### Option B: Direct Deployment

```bash
# Clone and configure
git clone https://github.com/your-org/github-issues-bot-MAF.git
cd github-issues-bot-MAF

# Set environment variables
export GITHUB_TOKEN="your-token"
export OPENAI_API_KEY="your-key"
export OPENAI_MODEL="gpt-5.4-mini"
export OPENAI_CRITIQUE_MODEL="gpt-5.4-nano"
```

## Local Development

```bash
# Build and test
dotnet restore
dotnet build
dotnet test

# Smoke test
dotnet run --project src/SupportConcierge.Cli -- --smoke

# Run with event file
dotnet run --project src/SupportConcierge.Cli -- --event-file test-event.json --dry-run
```

## Evaluations

Run quality evaluations to measure agent performance:

```bash
# Offline (no LLM)
dotnet run --project src/SupportConcierge.Cli -- --eval

# With LLM
SUPPORTBOT_EVAL_USE_LLM=true dotnet run --project src/SupportConcierge.Cli -- --eval
```

Eval outputs: `artifacts/evals/eval_summary.json`

## Configuration

### Spec Pack (`.supportbot/`)

| File | Purpose |
|------|---------|
| `categories.yaml` | Issue categories and keywords |
| `checklists.yaml` | Required fields per category |
| `validators.yaml` | Validation rules, secret patterns |
| `routing.yaml` | Labels and assignees per category |
| `playbooks/*.md` | Category-specific guidance |

### Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `GITHUB_TOKEN` | Yes | GitHub API token |
| `OPENAI_API_KEY` | Yes | OpenAI API key |
| `OPENAI_MODEL` | Yes | Primary model (e.g., gpt-5.4-mini) |
| `OPENAI_CRITIQUE_MODEL` | No | Critic model (default: primary) |
| `SUPPORTBOT_DRY_RUN` | No | Don't post to GitHub |
| `SUPPORTBOT_WRITE_MODE` | No | Enable posting |

## User Commands

| Command | Effect |
|---------|--------|
| `/stop` | Opt out of conversation |
| `/diagnose` | Join conversation or restart |

## Repository Structure

```
├── src/
│   ├── SupportConcierge.Core/     # Core bot logic
│   │   └── Modules/
│   │       ├── Agents/            # AI agents (Orchestrator, Triage, etc.)
│   │       ├── Workflows/         # MAF workflow + executors
│   │       ├── Tools/             # GitHub API, state management
│   │       ├── Guardrails/        # Security, command parsing
│   │       └── Models/            # Data models
│   └── SupportConcierge.Cli/      # CLI entry point
├── prompts/maf-templates/         # Prompt templates
├── evals/                         # Evaluation scenarios + rubrics
├── tests/                         # Unit tests
└── .supportbot/                   # Spec pack configuration
```


