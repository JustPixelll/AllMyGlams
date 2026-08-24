# AllMyGlams

AllMyGlams is an experimental Dalamud glamour workstation for FFXIV: the Dresser is the editor, your character is the viewer, and Glamourer/Penumbra/public glamour recipes are brought together in one outfit workflow.

## v0.2 feature set

### Live Dresser

- The Dresser represents a **complete equipment look**, not a partial overlay.
- Every Glamourer equipment slot is explicit, including `None`.
- The current character equipment/dyes are captured into the Dresser on load/login.
- While the window is open, clean (non-edited) Dresser state follows external gear/Glamourer changes automatically.
- Unsaved Dresser edits pause automatic synchronization so they are not overwritten.
- Slot-aware vanilla item search backed by FFXIV game data with in-game item icons.
- Dye 1 / Dye 2 selection.
- Top-level actions:
  - **Apply Dresser to Character**
  - **Revert to Game**
  - **Save to Wardrobe**
  - **Save to Glamourer**
- `Revert to Game` uses Glamourer's supported equipment-only revert IPC; it does not reconstruct game gear manually.
- Glamourer's slot-specific and weapon-type synthetic Nothing IDs are normalized back to `None` in the editor.

### Naming and wardrobe

- A freshly captured current state is called `Game Look`.
- Editing gear/dyes/mod recipes changes a source-derived name to `Custom` unless the user already supplied their own name.
- Saving `Custom` uses the next available Custom name without prompting on that automatic sequence.
- Saving any other duplicate name prompts before overriding the existing wardrobe entry.
- Favorites were removed; saved local looks live in one **My Wardrobe** collection.
- Wardrobe looks can be worn/edited immediately and saved onward to Glamourer.

### Glamourer workspace

- Glamourer designs refresh automatically when the plugin loads/opens, with a manual refresh button remaining available.
- Browse/search existing Glamourer designs and preserve their folder/path display.
- **Wear / Edit** turns even a partial Glamourer design into the complete resulting look by overlaying it on the character's current equipment before placing it in the Dresser.
- Save expanded Glamourer designs to the local Wardrobe.
- Saving from AllMyGlams creates equipment/dye-only Glamourer designs and leaves character customization untouched.

### Penumbra workspace

- Penumbra state refreshes automatically when the player is available, with a manual refresh button remaining available.
- Browse installed mods using supported Penumbra IPC only.
- Read the local player's effective collection.
- Show Changed Items for mods and identify names that map to wearable FFXIV items.
- Enable/disable mods, change priorities, and lazily load/edit option groups.
- Attach one mod or the currently enabled equipment-related mods to the Dresser.
- Saved mod recipes carry enabled state, priority, and option selections.
- Applying a look configures only the mods explicitly attached to it; unrelated mods are intentionally left alone.

### Public Wardrobe — Eorzea Collection

The Wardrobe contains a separate collapsible **Public Wardrobe — Eorzea Collection** area.

- Import an individual `https://ffxiv.eorzeacollection.com/glamour/...` URL or numeric glamour ID on demand.
- Resolve Eorzea Collection's English item/dye names against FFXIV's English game-data sheets, then use the local client's item IDs/icons.
- Parse supported gear slots plus both dye channels when present.
- Keep creator attribution, original source URL, external glamour ID, and fetch time with the cached look.
- Store the resolved recipe locally so wearing it later requires **zero** Eorzea Collection requests.
- Explicit **Refresh Source** is available for cached public entries.
- No EC screenshots are downloaded or hotlinked.
- No catalogue/background crawler is shipped.
- If Eorzea Collection returns `403`, AllMyGlams reports it and does **not** attempt to bypass the site's access controls.

The broad Eorzea Collection browse endpoint has shown automated-access restrictions in testing, so v0.2 deliberately starts with individual glamour imports rather than pretending to provide a reliable full-site browser.

## Design principles

1. **No config-file hacks.** Glamourer and Penumbra are controlled through their IPC surfaces.
2. **Character as viewer.** The Dresser edits a complete outfit and the player character displays the result.
3. **Equipment-only Glamourer output.** AllMyGlams does not intentionally modify race, face, hair, body customization, materials, or other avatar customization when saving an outfit.
4. **Local-first public wardrobe.** Imported public recipes are resolved and cached locally.
5. **Source attribution stays attached.** Imported looks retain provider/author/link metadata.
6. **Explicit network access.** External sources are fetched on user action, not through background crawling.
7. **Do not rewrite unrelated mods.** Outfit mod recipes configure their listed mods only.

## Dependencies

- Dalamud API / `Dalamud.NET.Sdk` 15
- Glamourer for equipment state/design integration
- Penumbra for mod-management integration

The UI can still open if Glamourer or Penumbra is unavailable; related actions report the IPC failure rather than editing either plugin's files.

## Commands

- `/allmyglams`
- `/amg`

## Custom Dalamud repository

Add this URL under Dalamud **Settings → Experimental → Custom Plugin Repositories**:

`https://raw.githubusercontent.com/JustPixelll/AllMyGlams/main/pluginmaster.json`

## Current experimental limits

- Equipment-mod detection uses Penumbra Changed Items names and matching against wearable FFXIV item names. The full Mods list remains available so the heuristic is not the only way to access a mod.
- Eorzea Collection v0.2 integration imports individual glamour pages only. Full browse/search is intentionally not implemented while automated access to the broad catalogue is uncertain/restricted.
- Public-source page markup can change. Import failures are surfaced instead of silently generating incomplete outfits.
- This remains an experimental plugin; storage/UI details may evolve as the workflow is tested in-game.

## Build status

The v0.2 tree is compiled and packaged in GitHub Actions against the Dalamud D17 development files before merge/release.
