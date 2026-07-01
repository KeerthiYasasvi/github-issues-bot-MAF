# Decisions

## Decision Log

- Use a multi-agent workflow with explicit agent responsibilities instead of a
	single monolithic prompt chain.
- Parse issue forms and build deterministic case packets before LLM reasoning
	so the workflow can rely on stable extracted fields.
- Validate agent outputs with a critic stage before labels, research, or final
	responses are trusted.
- Persist bot state in GitHub comments because GitHub Actions does not provide
	long-lived in-process memory between runs.
- Limit repeated follow-up loops per user and escalate when the agent has asked
	enough questions without reaching a confident resolution.
- Split primary and critique model roles so the main workflow can use a stronger
	generation model while critique uses a cheaper validation model.
