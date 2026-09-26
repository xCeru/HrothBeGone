# HrothBeGone

A local-only Dalamud plugin that changes the appearance of loaded Hrothgar actors on your client.

## Features

- Detects loaded Hrothgar player characters and human NPCs (players, battle NPCs, event NPCs, retainers).
- Replaces them locally with a selectable target race: Hyur, Elezen, Lalafell, Miqo'te, Roegadyn, Au Ra, or Viera.
- Random mode assigns a random non-Hrothgar race independently to each Hrothgar actor.
- Optional "include my character" toggle.
- Clamps Hrothgar-specific customization (face, hairstyle, tail) into ranges valid for the target race, and preserves clan parity so the resulting race/clan/gender combination is valid.
- Remaps racial starter gear to the target race's equivalent so starter outfits do not render invisible.
- Restores the original 26-byte customize block and equipment when disabled or when the plugin unloads.
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

## Install in game

1. In XIVLauncher, enable Dalamud "get plugins from an experimental third-party repository" if you have not already.
2. Add this repository URL in the plugin installer's "Experimental" settings:

   ```
   https://raw.githubusercontent.com/xCeru/HrothBeGone/main/repo.json
   ```

3. Find **HrothBeGone** in the Experimental tab and install it.

Requires a release asset to exist at the `v0.1.0.0` tag (see Releases).

## Important

This is a client-side visual modification. It is intentionally not designed to alter character data on the server.

The implementation uses direct `Character.DrawData.CustomizeData` writes with a hide/re-show redraw cycle. The project is intended for personal/dev use; do not assume it is suitable for public plugin-repository submission without checking current Dalamud policies.
