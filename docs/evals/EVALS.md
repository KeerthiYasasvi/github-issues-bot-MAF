# Evals

This repo includes four eval families:

1) E2E workflow suite (full DAG in DRY_RUN mode)
2) Follow-up question quality
3) Engineer brief quality
4) Cost/latency budgets

Deterministic plumbing tests live in `tests/SupportConcierge.Tests`.

## Run evals with an LLM

```bash
OPENAI_API_KEY=... \
OPENAI_MODEL=... \
dotnet run --project src/SupportConcierge.Cli -- --eval
```

## Run evals without an LLM

This mode skips the follow-up and brief eval suites and runs the E2E suite deterministically.

```bash
SUPPORTBOT_NO_LLM=true \
dotnet run --project src/SupportConcierge.Cli -- --eval
```

## Outputs

- `artifacts/evals/eval_report.json`
- `artifacts/evals/EVAL_REPORT.md`
- `artifacts/evals/followup_eval_report.json`
- `artifacts/evals/FOLLOWUP_EVAL_REPORT.md`
- `artifacts/evals/brief_eval_report.json`
- `artifacts/evals/BRIEF_EVAL_REPORT.md`

## Thresholds

Edit `evals/eval_config.json` to change thresholds:

- `minFollowUpScore`
- `minBriefScore`
- `maxAverageLatencyMs`
- `maxAverageTokens`
- `failOnHallucinations`

## Fixtures

- E2E scenarios: `evals/scenarios/e2e/*.json`
- Follow-up scenarios: `evals/scenarios/followups/*.json`
- Brief scenarios: `evals/scenarios/briefs/*.json`
