# Roshi Waifu Tool — to test

Rewritten 2026-10-04. Replaces the old list (it described the layout from 2 October, which no longer exists).
Covers everything built since your last in-game session that you haven't reported on.

Mark each box Y or N. For an N, note what happened or paste the red/yellow lines from the SMAPI console.
Rebuild first, launch through MO2, load a save on a normal day, press F6.

## 0. Start-up
- [ ] 0.1 No red errors. No line saying "Couldn't patch ..." or "Couldn't hook ...".

## 1. Controls (Settings: "Game controls: off" is the default)
- [ ] 1.1 Left-click on the world: the farmer does not swing a tool.
- [ ] 1.2 Mouse wheel: the hotbar selection does not change.
- [ ] 1.3 The interact key still talks to an NPC. Dialogue advances with clicks as usual.
- [ ] 1.4 Number keys: `1` plays at x1; `1` again pauses; `2` = x2; `3` = x4; at x4, `1` goes to x1, `1` again pauses.
- [ ] 1.5 Set "Game controls: on": clicks and the wheel reach the game again, number keys pick hotbar slots.

## 2. Camera
- [ ] 2.1 Hold the left button on the world and move the mouse: the map moves with it. A plain click does nothing.
- [ ] 2.2 After dragging, WASD still walks the farmer; the arrow keys move the view.
- [ ] 2.3 Camera off (hub, Test): the view goes back to the farmer.

## 3. Follow across maps
- [ ] 3.1 Right-click Haley, Follow: your farmer disappears, the camera stays on her.
- [ ] 3.2 Press Play and let her walk into another map: after a short fade the camera is on her again.
- [ ] 3.3 At a map's edge the view stops (no black outside the map).
- [ ] 3.4 Nothing odd while following: no error spam, no stuck screen, she isn't blocked.
- [ ] 3.5 Her ring: **Stop following** puts you back on the exact tile and facing you started from.
- [ ] 3.6 Follow again, then **Stay here**: your farmer appears next to her.
- [ ] 3.7 Follow, then switch Camera off: same as Stop following.
- [ ] 3.8 Which sounds are missing while following (footsteps? positional audio?). Note which mod each is from.

## 4. Selection and marker
- [ ] 4.1 Click a face in the column: she is highlighted, no window opens. Click again: unselected.
- [ ] 4.2 A bobbing emerald floats above the selected character. It doesn't crowd the label box over her head.
- [ ] 4.3 Settings, "Marker: off": the emerald is gone.

## 5. The column of faces
- [ ] 5.1 Always one column. In town with many NPCs: the mouse wheel scrolls it, `+N` shows how many more.
- [ ] 5.2 Witch / Scarlett copies and the Xtardew Haleys are not in the column.
- [ ] 5.3 Squirrels and other animals are not in the column. Is anyone missing who should be there?
- [ ] 5.4 Settings: "Alts in column: shown" and "Animals in column: shown" bring them back.
- [ ] 5.5 Hover a face: the tooltip is drawn on top of the hub, not behind it.

## 6. NPCs drawer
- [ ] 6.1 Hub, NPCs: filters on the left, two rows of cards, `<` `>` and the wheel turn the page.
- [ ] 6.2 Click the search box, type part of a name: the cards filter. Esc / Enter / a click elsewhere stops typing.
- [ ] 6.3 While typing, the farmer doesn't walk and no menu opens.
- [ ] 6.4 Right-click a card, Favourite: a gold star appears on it. Non-favourites show nothing. Click the star: gone.
- [ ] 6.5 Haley's card shows `+N`; right-click gives a ring of her versions; picking one opens that one's ring;
      clicking the head there goes back.
- [ ] 6.6 Hidden copies are named after their mod, e.g. "Abigail (Xtardew Valley 1)" (may say "alt" for the first
      few seconds). Note any that stay "alt".

## 7. Rings
- [ ] 7.1 With a ring open, nothing behind it highlights or shows a tooltip.
- [ ] 7.2 Expressions: vanilla ones read `$3 Vanilla`, the rest `$40 Custom`. Clicking one shows that expression.
- [ ] 7.3 Right-click a tile: your farmer's head in the middle, map and tile on the plate; Warp here works.
- [ ] 7.4 Labels read "Warp to me", "Warp to <map>", "Warp to <name>".

## 8. Settings
- [ ] 8.1 Labels cycles: shown, on mouse over, off; each does what it says.
- [ ] 8.2 "All doors: unlocked": enter a shop before opening time and a bedroom without hearts.
- [ ] 8.3 HUD on/off works from Settings.

## 9. World controls (hub, Test)
- [ ] 9.1 Switch season: the map changes, and someone with seasonal outfits changes outfit right away.
- [ ] 9.2 Switch weather to Rain: rain starts, rain outfits apply.
- [ ] 9.3 Day - / Day +: the date in the hub's title changes.
- [ ] 9.4 Exit the workspace: the save reloads and season, weather and day are back to normal.

## 10. If something with names looks wrong
- [ ] 10.1 Run `rwt_npcs` in the SMAPI console and paste the output to Claude.
