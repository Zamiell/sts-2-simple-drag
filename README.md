# Simple Drag

A Slay the Spire 2 mod that removes unnecessary aiming when only one living enemy remains.

Drag a single-target attack or skill out of your hand and release it in the play zone above your hand to play it on the remaining enemy. The card follows your mouse instead of switching to a targeting arrow, while still showing the target's highlight and preview.

## Behavior

- Only applies to mouse-dragged, single-target **Attack** and **Skill** cards when exactly one living enemy remains and it is a valid target.
- Leaves normal targeting unchanged for multiple living enemies, other card types, other target types, click targeting, keyboard shortcuts, and controllers.
- A fast flick that releases above the play-zone line still counts as a drag, even if the game would otherwise have switched to click targeting. Clicking a card and releasing in the hand keeps the normal click targeting.
- Preserves the game's normal card-play and cancellation behavior. Releasing outside the play zone cancels the play.
- Rechecks the target on release. If it is no longer valid or another living enemy has appeared, the play is cancelled.

## Logging

Each drag writes a few `[SimpleDrag]` lines to the game log (`godot.log`): the left-button release, whether the mod took over the drag (`decision=simple-drag` or `decision=vanilla` with a `reason`), and the result on release. On macOS the log is in `~/Library/Application Support/SlayTheSpire2/logs/`.

## Build

Requires a local Slay the Spire 2 installation and the [.NET SDK](https://dotnet.microsoft.com/download) with support for `net9.0`. The mod manifest declares a minimum game version of `0.111.0`; later updates may change the private APIs used by the mod.

From the repository root:

```sh
dotnet build -c Release -p:GameDataDir="/path/to/game/data_sts2_platform"
```

Replace the path with the game's data directory containing `sts2.dll`, `GodotSharp.dll`, and `0Harmony.dll`. These assemblies are supplied by the game, not this repository.

`Directory.Build.props` picks default paths for the OS running the build, so for a standard Steam installation `dotnet build -c Release` works without a path override:

| Build OS | Default game location |
| --- | --- |
| macOS | `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2` (arm64 or x86_64 data matching the Mac) |
| Windows | `C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2` |
| Linux | `/mnt/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2` (Windows install through WSL) |

Build output is packaged in `dist/SimpleDrag/`.

## Install

After building, copy `SimpleDrag.dll` and `SimpleDrag.json` from `dist/SimpleDrag/` into the game's `mods/SimpleDrag/` directory:

```text
Slay the Spire 2/
+-- mods/
    +-- SimpleDrag/
        +-- SimpleDrag.dll
        +-- SimpleDrag.json
```

On macOS the `mods/` directory is inside the app bundle, at `SlayTheSpire2.app/Contents/MacOS/mods/`.

Alternatively, build and install directly to the default location for your OS:

```sh
dotnet build -c Release -t:InstallMod
```

`./install.sh` runs the same command.

For a non-default installation, pass `-p:GameDir="/path/to/Slay the Spire 2"`, and `-p:GameDataDir=...` or `-p:GameModsDir=...` if those are not in the usual places.

Restart the game after installing or updating. To uninstall, remove the `mods/SimpleDrag/` directory.

## Development checks

The behavior checks use game stubs to exercise targeting, release-time validation, cancellation, and preview cleanup. They require .NET 10 and the game's `0Harmony.dll`:

```sh
dotnet run --project tests/Behavior \
  -p:GameDataDir="/path/to/game/data_sts2_platform"
```

After building the mod, check that its private API bindings and Harmony patches work against your installed game:

```sh
dotnet run --project tests/Compatibility -- \
  "/path/to/game/data_sts2_platform" \
  "dist/SimpleDrag/SimpleDrag.dll"
```

The compatibility check targets .NET 9. These checks do not replace in-game testing of the drag interaction.