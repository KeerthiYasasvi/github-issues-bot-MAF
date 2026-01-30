# Evaluation Framework

This project includes production‑style evaluations to measure:
- Information gathering quality (primary goal)
- Decision correctness (follow‑up vs finalize)
- Off‑topic handling
- Safety (no secrets requested)
- Tool selection and investigation
- Multi‑user loop correctness
- Latency and token usage

## How to run (offline, deterministic)
Uses a heuristic LLM so evals run without network access.

```
dotnet run --project src/SupportConcierge.Cli -- --eval --scenarios-dir evals/scenarios --output-dir artifacts/evals
```

## How to run (live LLM)
Requires `OPENAI_API_KEY` and `OPENAI_MODEL`.

```
set SUPPORTBOT_EVAL_USE_LLM=true
dotnet run --project src/SupportConcierge.Cli -- --eval --scenarios-dir evals/scenarios --output-dir artifacts/evals
```

## Scenario types
- `evals/scenarios/e2e`: End‑to‑end workflow evals (golden set)
- `evals/scenarios/followups`: Follow‑up question quality
- `evals/scenarios/briefs`: Engineer brief quality

## Output artifacts
- `eval_e2e_results.json` — pass/fail per scenario
- `eval_followup_results.json` — follow‑up scores + notes
- `eval_brief_results.json` — brief scores + notes
- `eval_summary.json` — aggregate stats (pass rate, avg tokens, avg latency)

## CI subset
Add `tags: ["ci"]` to a scenario to include it in PR evals:

```
dotnet run --project src/SupportConcierge.Cli -- --eval --subset ci
```

