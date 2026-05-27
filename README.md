# PiSignalWatch
Modular .NET worker for Raspberry Pi topic monitoring...

## Setup
- copy `appsettings.example.json` to `appsettings.json`
- set env vars: X_BEARER_TOKEN, OPENAI_API_KEY, DISCORD_WEBHOOK_URL, TELEGRAM_BOT_TOKEN, EMAIL_SMTP_PASSWORD

## Run
`dotnet restore && dotnet build && dotnet run --project src/PiSignalWatch`

## Publish ARM64
`dotnet publish src/PiSignalWatch -c Release -r linux-arm64 --self-contained false`

## systemd
Create service pointing to published DLL; use `journalctl -u pisignalwatch -f`.

## Security
Do not commit secrets, keep Pi behind firewall, use SSH keys.

## Future
SQLite, dashboard, improved Reddit, better dedupe, trend charts, local admin UI.
