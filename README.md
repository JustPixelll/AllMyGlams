# AllMyGlams

AllMyGlams is an experimental Dalamud glamour workstation for FFXIV. The goal is one place to build equipment-only Glamourer looks, capture the current Glamourer state, keep a local outfit/favourites library, inspect equipment-related Penumbra mods, and apply Penumbra mod settings without editing either plugin's files.

## v0.1 scope

- Dresser with all Glamourer equipment slots.
- Search vanilla game items by slot and show their in-game icons.
- Dye 1 / Dye 2 selection.
- Apply the working outfit directly through `Glamourer.SetItem.V3`.
- Capture the current player appearance through `Glamourer.GetState`.
- Save equipment-only designs through `Glamourer.AddDesign`.
- Local library and separate favourites.
- Penumbra mod browser using supported IPC only.
- Show each mod's changed-item list and flag mods whose changed-item names match wearable FFXIV items.
- Read the player's effective Penumbra collection.
- Enable/disable mods and set mod priority through Penumbra IPC.
- Data model for sourced wardrobe entries (source name, URL, author, external ID/rating, last refresh), ready for Eorzea Collection or other opt-in sources later.

## Design principles

1. **No config-file hacks.** Glamourer and Penumbra are controlled via their published IPC surfaces.
2. **Equipment-first designs.** AllMyGlams does not intentionally modify race, face, hair, body customization, materials, or other character customization when creating an outfit.
3. **Local-first wardrobe.** Once an external look is imported, its resolved item/dye recipe can be stored locally and reused without repeatedly requesting the source.
4. **Source attribution stays attached.** Imported looks retain source/author/link metadata.
5. **Explicit network refresh.** External wardrobe providers should fetch only on user action and cache resolved designs.

## Dependencies

- Dalamud API / `Dalamud.NET.Sdk` 15
- Glamourer (for apply/capture/save features)
- Penumbra (for mod-management features)

The plugin should still open if Glamourer or Penumbra is missing; the related actions will report that IPC is unavailable.

## Command

`/allmyglams` or `/amg`

## Status

Very early experimental scaffold. Expect API/UX changes while the dresser and mod-workspace concepts are tested.
