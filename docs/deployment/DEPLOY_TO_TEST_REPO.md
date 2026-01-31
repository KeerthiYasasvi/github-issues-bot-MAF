# Deploy Support Bot Workflow to Test Repo (yt-music-simulator)

## Files to Copy

Copy this file from the bot repo:
- `.github/workflows/supportbot-for-test-repo.yml`

To the test repo at:
- `D:\Projects\ytm\yt-music-simulator\.github\workflows\supportbot.yml`

## Secrets Already Configured ✓

You've already added these to the test repo:
- `BOT_GITHUB_TOKEN` (secret)
- `API_KEY` (secret)
- `PRIMARY_MODEL` (variable)
- `SECONDARY_MODEL` (variable)

## Manual Steps

1. Open File Explorer and navigate to:
   ```
   D:\Projects\agents\ms-quickstart\Github-issues-bot-with-MAF\Github-issues-bot-with-MAF\.github\workflows\
   ```

2. Copy `supportbot-for-test-repo.yml`

3. Navigate to:
   ```
   D:\Projects\ytm\yt-music-simulator\.github\workflows\
   ```
   (Create `.github/workflows/` folder if it doesn't exist)

4. Paste and rename to `supportbot.yml`

5. Commit and push to the test repo:
   ```powershell
   cd "D:\Projects\ytm\yt-music-simulator"
   git add .github/workflows/supportbot.yml
   git commit -m "feat: add support bot workflow"
   git push origin main
   ```

## Testing

Once deployed, create a test issue in yt-music-simulator with minimal info:
```
Build failing
```

The bot should respond asking for more details (OS, error message, version, etc.).
