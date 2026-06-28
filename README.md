# Pal Local Manager

Pal Local Manager is a native Windows desktop app for running and managing a local Palworld dedicated server from one place.

It is built in C#/.NET WinForms and ships as a standalone `.exe`, so you do not need Python or a separate launcher.

## What it does

- Starts and stops the Palworld dedicated server headlessly.
- Runs Playit alongside the server so friends can join without opening ports manually.
- Shows server status, players, uptime, and logs in a single dashboard.
- Edits Palworld server settings from the UI.
- Installs SteamCMD and downloads the dedicated server on first setup.
- Imports and manages common mod files, including UE4SS and PalDefender-related folders.
- Supports backups, RCON commands, scheduler actions, and Discord webhook notifications.

## Requirements

- Windows 10 or 11.
- Palworld Dedicated Server installed locally, or SteamCMD access so the app can install it for you.
- Docker Desktop if you want to run the Playit agent from the app.
- A valid Playit agent secret if you want Playit tunneling to work.

## First-time setup

1. Launch `PalLocalManager.exe`.
2. Create a new profile or add an existing Palworld server folder.
3. If needed, use the SteamCMD installer button to download the dedicated server.
4. Set your Playit container name and public address in the profile page.
5. Start the Playit agent.
6. Start the server.

## How it works

The app keeps the server and Playit tunnel under one dashboard:

- The server is launched headlessly, so no command window is shown.
- Playit is managed as a Docker container by container name.
- Logs are captured into the server folder so you can debug startup issues without hunting for console windows.
- Server settings are edited in the UI and written back to the local config files.

## Default paths

These are the defaults used by the app:

```text
C:\PalworldServer\SakuraSweetheart_99795188
C:\PalworldServer\steamcmd
```

## Release build

The published Windows executable is in:

```text
publish\PalLocalManager.exe
```

## Build from source

```powershell
dotnet restore -r win-x64
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

## Notes

- The app uses `app.ico` for the EXE icon and `palworld-logo.png` for the splash screen and dashboard logo.
- If Windows shows an older taskbar icon after updating, unpin and reopen the app once so the shell cache refreshes.
