# PiSignalWatch
Modular .NET worker for Raspberry Pi topic monitoring...

## IDE + Build Dependencies
Install these first so the project loads cleanly in an IDE and builds without missing-reference errors:

- **.NET 8 SDK** (required): install from https://dotnet.microsoft.com/download/dotnet/8.0
- **.NET 8 Runtime** (usually installed with SDK, but required on deployment hosts)
- **An IDE with C# support**:
  - Visual Studio 2022 17.8+ with the **.NET desktop development** workload, or
  - JetBrains Rider 2024.1+, or
  - VS Code + **C# Dev Kit** extension + official **.NET Install Tool** extension
- **Git** (for source control + restoring solution context)

Quick verification commands after install:

```bash
dotnet --info
dotnet --list-sdks
```

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
