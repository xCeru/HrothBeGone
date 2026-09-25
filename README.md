# HrothBeGone

A local-only Dalamud plugin that changes the appearance of loaded Hrothgar actors on your client.

## Features

- Detects loaded Hrothgar player characters and human NPCs.
- Replaces them locally with a selectable target race.
- Random mode chooses a different target race independently for each Hrothgar actor.
- Optional local-player inclusion.
- Restores the original 26-byte customize block when disabled or when the plugin unloads.
- Reapplies after territory changes and when new actors appear.
- Does not send race changes to the FFXIV server.

## Build

This project follows the current Dalamud SDK style used by the official SamplePlugin.

Requirements:
- XIVLauncher + Dalamud
- .NET SDK supported by your installed Dalamud SDK
- `DALAMUD_HOME` set if your Dalamud development environment is not in its default location.

Build:

    dotnet build -c Release

Then add the resulting DLL directory as a Dalamud Dev Plugin Location and load it from:
`/xlplugins` -> Dev Tools -> Installed Dev Plugins.

## Important

This is a client-side visual modification. It is intentionally not designed to alter character data on the server.

The implementation uses the same direct `Character.DrawData.CustomizeData` + native human `UpdateDrawData` technique currently used by the open-source Krangler plugin for local race changes. The project is intended for personal/dev use; do not assume it is suitable for public plugin-repository submission without checking current Dalamud policies.
