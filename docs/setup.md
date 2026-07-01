# Setup

## Prerequisites

- .NET 8 SDK
- GitHub repository with issue and issue-comment events enabled
- `GITHUB_TOKEN`
- `OPENAI_API_KEY`
- `OPENAI_MODEL`
- Optional `OPENAI_CRITIQUE_MODEL`

## Local Development

1. Restore, build, and test the solution.
	 `dotnet restore`
	 `dotnet build`
	 `dotnet test`
2. Run the CLI locally for smoke or dry-run execution.
	 `dotnet run --project src/SupportConcierge.Cli -- --smoke`
	 `dotnet run --project src/SupportConcierge.Cli -- --event-file test-event.json --dry-run`
3. Run evaluation scenarios when needed.
	 `dotnet run --project src/SupportConcierge.Cli -- --eval`
4. Review or customize the `.supportbot/` spec pack for categories,
	 checklists, validators, routing, and playbooks.

## Deployment

- Use `.github/workflows/supportbot.yml` for repository-triggered execution.
- Use `action.yml` if you want to consume the bot as a reusable composite
	action.

## Configuration

- Required environment variable names:
	`GITHUB_TOKEN`
	`OPENAI_API_KEY`
	`OPENAI_MODEL`
- Optional environment variable names:
	`OPENAI_CRITIQUE_MODEL`
	`SUPPORTBOT_DRY_RUN`
	`SUPPORTBOT_WRITE_MODE`
	`SUPPORTBOT_SPEC_DIR`
	`SUPPORTBOT_EVALS_DIR`
	`SUPPORTBOT_METRICS_DIR`
	`SUPPORTBOT_TELEMETRY_DIR`
	`SUPPORTBOT_USERNAME`
- Do not commit secret values.
