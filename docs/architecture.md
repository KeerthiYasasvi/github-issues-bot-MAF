# Architecture

## Overview

github-issues-bot-MAF is a multi-agent GitHub issue triage and follow-up system
built on Microsoft Agent Framework workflows. It listens to issue and comment
events, extracts structured information from issue content, coordinates a set of
specialized agents, validates their outputs through a critic pass, posts either
targeted follow-up questions or an engineer-ready brief, and persists workflow
state inside GitHub comments so the system can continue across stateless GitHub
Actions runs.

## Main Components

### Workflow Orchestrator

- `src/SupportConcierge.Core/Modules/Workflows/SupportConciergeWorkflow.cs`
	defines the main executable flow.
- The workflow routes through parse, state loading, guardrails, triage,
	research, response, decision, comment posting, and state persistence steps.

### Agent Layer

- `OrchestratorAgent` plans investigations and decides whether to ask questions,
	finalize, or escalate.
- `EnhancedTriageAgent` classifies issues and extracts issue details.
- `EnhancedResearchAgent` selects tools and investigates.
- `EnhancedResponseAgent` writes follow-up questions and engineer briefs.
- `CriticAgent` scores outputs before they propagate.
- `OffTopicAgent` handles off-topic detection.
- `CasePacketAgent` builds structured issue packets.

### Executor and Tool Layer

- Workflow executors under
	`src/SupportConcierge.Core/Modules/Workflows/Executors/` perform the concrete
	steps of the DAG.
- Tools under `src/SupportConcierge.Core/Modules/Tools/` cover GitHub API
	access, state persistence, issue form parsing, comment formatting, scoring,
	schema validation, and metrics.

### Spec Pack Configuration

- `.supportbot/` contains repository-specific configuration for categories,
	checklists, validators, routing, and playbooks.
- This allows behavior changes without modifying core orchestration code.

### Runtime and Deployment Surface

- `src/SupportConcierge.Cli/Program.cs` is the CLI entry point.
- `.github/workflows/supportbot.yml` runs the bot for GitHub issue and comment
	events.
- `action.yml` exposes the bot as a composite GitHub Action.

## Workflow

1. Parse the GitHub event into a runtime input.
2. Load prior workflow state from embedded comment state if it exists.
3. Apply guardrails, including stop handling and loop control.
4. Detect off-topic content and exit early when needed.
5. Load the `.supportbot` spec pack.
6. Run triage and validate the result through the critic.
7. Add labels and build a deterministic case packet.
8. Decide whether research is needed.
9. Run research and validate findings.
10. Generate a response or follow-up questions and validate the output.
11. Let the orchestrator decide whether to ask questions, finalize, or
		escalate.
12. Post the comment back to GitHub and persist updated state.

The workflow tracks loop counts per user so multiple participants in the same
issue can have independent conversation progress.

## External Dependencies

- GitHub API for reading comments, posting comments, and managing labels
- OpenAI models for agent generation and critique
- Microsoft Agent Framework for workflow and agent orchestration
- YamlDotNet for spec pack loading
- JsonSchema.Net for schema validation
- GitHub Actions as the default execution environment

## Decisions

- Prefer deterministic extraction before LLM reasoning so issue form data is
	captured in a stable way before any model call.
- Run a critic pass over triage, research, and response outputs to keep quality
	gates explicit.
- Persist workflow state in GitHub comments because GitHub Actions executions
	are stateless.
- Track loop counts per user and cap repeated follow-up loops before escalating
	to a human maintainer.
- Use a stronger primary model and a cheaper critique model to balance output
	quality and runtime cost.
