# Dev tooling

Scripts for iterating on Playnite Queue against a copy of your real Playnite
library, so the production library stays untouched while you experiment.

## Overview

Playnite already supports a `--userdatadir <path>` CLI flag (defined in
[`source/Playnite/App/CmdLineOptions.cs`](../source/Playnite/App/CmdLineOptions.cs))
that rebases the entire data layout (library, config, plugins, themes) under a
custom root. We use this to point Debug builds at a separate copy of the real
library so any in-progress code can read and write freely without risk.

## One-time setup

1. Build the solution in **Debug | x86** at least once. Either:
   - Open [`source/Playnite.sln`](../source/Playnite.sln) in Visual Studio 2022,
     set **Playnite.DesktopApp** as the startup project, switch the toolbar to
     **Debug / x86**, and **Build > Build Solution**.
   - Or from the repo root in PowerShell, let `Run-Dev.ps1 -Build` do it the
     first time you launch.
2. (Optional) In Visual Studio, set the project's **Properties > Debug > Start
   arguments** to `--userdatadir %APPDATA%\Playnite-Dev` so plain `F5`
   debugging also targets the dev DB. The setting is saved to
   `Playnite.DesktopApp.csproj.user`, which is gitignored.

## Daily workflow

```powershell
# Refresh the dev snapshot from your real library (close Playnite first).
.\tools\Snapshot-DevLibrary.ps1

# Build (if needed) and launch against the snapshot.
.\tools\Run-Dev.ps1 -Build
```

`Snapshot-DevLibrary.ps1` will refuse to run if any Playnite instance is open
to avoid corrupting the in-flight LiteDB file. `Run-Dev.ps1` does the same
check before launching.

## Scripts

### [`Snapshot-DevLibrary.ps1`](Snapshot-DevLibrary.ps1)

Copies the real Playnite data directory to a dev location.

| Parameter | Default                       | Description                              |
| --------- | ----------------------------- | ---------------------------------------- |
| `Source`  | `%APPDATA%\Playnite`          | Real library to copy from.               |
| `Dest`    | `%APPDATA%\Playnite-Dev`      | Dev location to copy into.               |
| `Force`   | `$false`                      | Skip the overwrite-confirmation prompt.  |

### [`Run-Dev.ps1`](Run-Dev.ps1)

Launches `Playnite.DesktopApp.exe` with `--userdatadir` pointed at the dev
snapshot.

| Parameter | Default                                                                  | Description                                           |
| --------- | ------------------------------------------------------------------------ | ----------------------------------------------------- |
| `DataDir` | `%APPDATA%\Playnite-Dev`                                                 | User data directory the dev build will use.           |
| `ExePath` | `source\Playnite.DesktopApp\bin\Debug\Playnite.DesktopApp.exe`           | Override if your build output is somewhere else.      |
| `Build`   | `$false`                                                                 | Run MSBuild against `Playnite.DesktopApp.csproj` first. |

## Reverting / cleaning up

The real library is never modified by these scripts. To wipe the dev snapshot,
simply delete `%APPDATA%\Playnite-Dev` (or whatever path you used).
