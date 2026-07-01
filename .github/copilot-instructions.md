# Repository Agent Instructions

This repository must be maintained as a spec-driven and documentation-aware project.

## Required Behavior

When changing code, check whether these files need updates:

- specs/*/spec.md
- specs/*/plan.md
- specs/*/tasks.md
- docs/architecture.md
- docs/changelog.md
- docs/decisions.md
- evals/spec-consistency-eval.md

## Documentation Rule

Do not invent project claims. All documentation must be grounded in actual files,
code, commits, issues, tests, or specs.

## Spec Rule

Implementation must trace back to a spec, task, or GitHub issue. If no spec exists,
create or update one before implementing major features.

## Evaluation Rule

Before finalizing changes, check whether documentation, specs, and evaluation
material are consistent with the actual repository.

## Security Rule

Never write secret values to the repository. Only reference environment variable
names and setup instructions.

Forbidden:

- API keys
- GitHub tokens
- SSH private keys
- PGP private keys
- cloud credentials
- database passwords
- .env files

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
<!-- SPECKIT END -->
