# QOLCharSelector

Silverpine 1.7.3 BepInEx plugin that adds a scrollable list of every built-in
and custom player character to the left of the start-screen character
selector. Each row shows the character's small world sprite and name; clicking
a row selects the same character as the original arrow controls. A native
search field filters character names as you type.

Created by **Saelac and ChatGPT**.

**Current version:** 1.0.1

Version 1.0.1 safely handles custom Player Character fields being removed at
runtime, including QOLNPCSelector SILC enable/disable operations.

## Requirements

- Silverpine 1.7.3
- BepInEx 5
- Modding Tools 1.9.3 or newer

## Installation

Place `QOLCharSelector.dll` under
`BepInEx/plugins/QOLCharSelector/`. Keep exactly one current copy of
`ModdingTools.dll` installed under `BepInEx/plugins/ModdingTools/`.

## Lifecycle

The Harmony attachment hook is installed once and intentionally remains active
for the process lifetime. Silverpine destroys the initial BepInEx plugin host
during its menu-to-game bootstrap transition, so that host's `OnDestroy` does
not unpatch Harmony. The scene-local list component still unsubscribes from the
selector event when the character-creation UI is destroyed.

## Building

Build `QOLCharSelector.csproj` in Release. It targets
`netstandard2.1` and references the local Silverpine, BepInEx, Unity,
TextMeshPro, and Modding Tools assemblies.
