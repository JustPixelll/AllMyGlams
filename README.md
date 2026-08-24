# AllMyGlams

AllMyGlams is an experimental Dalamud glamour workstation for FFXIV: one place to build equipment-only Glamourer looks, reuse existing designs, keep a local wardrobe/favourites library, inspect equipment-related Penumbra mods, and bundle selected mod state into saved looks.

## v0.1 feature set

### Dresser

- All Glamourer equipment slots in one equipment-focused editor.
- Slot-aware vanilla item search backed by FFXIV game data.
- In-game item icon display.
- Dye 1 / Dye 2 selection.
- Per-slot Apply flag so unused slots leave the current appearance untouched.
- Detect the local player's current Glamourer equipment/dyes.
- Apply the working outfit through `Glamourer.SetItem.V3`.
- Save an equipment-only design through `Glamourer.AddDesign`.
- Save to the local Wardrobe or directly to Favorites.

### Glamourer workspace

- Browse/search existing Glamourer designs.
- Preserve Glamourer folder/path information in the browser.
- Load an existing design's equipment/dyes into the AllMyGlams Dresser.
- Apply only the equipment portion of an existing design.
- Copy existing Glamourer designs into the local Wardrobe/Favorites as equipment-only looks.

### Wardrobe / Favorites

- Persistent local saved outfits.
- Separate Favorites view.
- Duplicate/delete/load/apply/save-to-Glamourer actions.
- A saved outfit can also carry attached Penumbra mod recipes.
- `Apply Look` applies the gear and then the attached Penumbra mod state.

### Penumbra workspace

- Browse every installed mod using supported Penumbra IPC.
- Read the local player's effective Penumbra collection.
- Show Penumbra Changed Items for every mod.
- Flag changed-item names that map to wearable FFXIV items and display their game icons.
- Optional equipment-related-only filter.
- Enable/disable mods from AllMyGlams.
- Read and change mod priority.
- Lazily load Penumbra option groups only when requested.
- Edit single-choice and multi-choice option groups.
- Attach an individual mod, or all currently enabled equipment-related mods, to the working outfit.
- Attached recipes store enabled state, priority, and current option selections.
- Applying an outfit's mod recipe intentionally leaves unrelated mods untouched.

### Sourced wardrobe foundation

The local data model already keeps:

- source/provider name,
- external glamour ID,
- source URL,
- author,
- rating snapshot,
- last metadata refresh time.

This is intended for an openly disclosed, cached Eorzea Collection provider (and potentially other opt-in sources) without coupling the core dresser to a scraper or hotlinking screenshots.

## Design principles

1. **No config-file hacks.** Glamourer and Penumbra are controlled via their published IPC surfaces.
2. **Equipment-first designs.** AllMyGlams does not intentionally modify race, face, hair, body customization, materials, or other character customization when creating an outfit.
3. **Local-first wardrobe.** Once an external look is imported, its resolved item/dye recipe should be stored locally and reusable without repeatedly requesting the source.
4. **Source attribution stays attached.** Imported looks retain source/author/link metadata.
5. **Explicit network refresh.** External wardrobe providers should fetch only on user action and cache resolved designs.
6. **Do not rewrite unrelated mods.** Applying an outfit's attached Penumbra recipe configures the listed mods and leaves the rest of the collection alone.

## Dependencies

- Dalamud API / `Dalamud.NET.Sdk` 15
- Glamourer for apply/capture/design features
- Penumbra for mod-management features

The plugin UI can still open if Glamourer or Penumbra is missing; actions that require the missing plugin report the IPC failure instead of editing plugin files directly.

## Commands

- `/allmyglams`
- `/amg`

## Known v0.1 limits

- Equipment-mod detection currently uses Penumbra's Changed Items names and exact matching against wearable FFXIV item names. The full Mods list remains available so false negatives do not hide mods unless the equipment-only filter is enabled.
- Attached mod recipes do not disable unrelated mods. A future isolated outfit-collection mode may provide stricter reproducibility without touching the user's normal collection.
- The Eorzea Collection/source-provider UI is only the local cache/attribution foundation for now; no automatic crawler is shipped in this first build.
- This is an experimental plugin and the storage/UI schema may change while the workflow is tested.

## Build status

The current v0.1 tree is compiled and packaged in GitHub Actions against the Dalamud D17 development files.
