# Roshi Waifu Tool: ideas not built yet

Kept up to date: new ideas are added, built ones are removed (they move to the README and the design doc).
Last updated 2026-10-03.

## Interface
- Hub polish: icons or faces instead of text on the mode and favourite buttons; hotkeys for the modes.
- Farmer's head in the tile ring turning left/right like the NPC heads (the game's small portrait only faces front).
- Uses for the freed mouse: wheel = camera zoom.
- A second follow style: the farmer visibly walks a tile or two behind her (Settings choice).
- **One tabbed character window** (Create-a-Sim style): Outfits, Expressions, Inspector, Schedule as tabs instead of
  separate windows.
- **Message bubbles** for the tool's own messages instead of the game's notifications.
- Anything the game's art lacks for the parchment look gets drawn for the tool (decided: parchment all the way).
- Tile and frame pickers through the right-click ring.

## Characters
- Per-expression source in the Expressions ring (`$40 Harem Valley`): APF only knows the pack that owns the whole
  portrait, so this needs APF to record where each expression comes from (handoff to the APF chat).
- Sound while following: mods that measure distance from the player (positional audio, footsteps) go quiet because
  the hidden farmer is parked off the map. Fix in those mods (measure from the screen centre), via a handoff.
- **Per-mod tags by a real scan** (which mod added which NPC), instead of only the name prefix. Undecided.

## Editing (stage D)
- **Detour editor**: a guided flow for making LAF detours (pick NPC, tile, time, pose) in the world.
- **Recipes** for common setups ("modest situations": who may see her in which outfit, where and when).
- Forms built from LAF's / APF's data models, drafts kept in memory, export to clipboard or a folder outside Mods.

## Other
- **Modern Config Menu** support for the Settings buttons (needs its source or Nexus page).
- **Flight recorder**: a log of what changed on an NPC over the day (outfit, pose, detour) to scroll back through.
- **Browser panel**: the desktop half of the hybrid GUI.
- Mid-day snapshot instead of a full day restart (stage E).

## Housekeeping
- Delete `TO-TEST.md` once he says the tests are done.
