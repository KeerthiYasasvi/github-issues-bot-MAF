# Capture GitHub Event Fixtures

Use this to capture real GitHub event payloads for eval fixtures.

1) Add a temporary step in `.github/workflows/supportbot.yml`:

```yaml
- name: Upload event payload
  uses: actions/upload-artifact@v4
  with:
    name: event-payload
    path: ${{ github.event_path }}
```

2) Trigger the workflow by opening or editing an issue (or posting a comment).
3) Download the `event-payload` artifact from the workflow run.
4) Save it under `evals/scenarios/e2e/` and use `--event-file` to replay locally.
