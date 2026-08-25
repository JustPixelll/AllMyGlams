using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace AllMyGlams.Windows;

public sealed partial class MainWindow
{
    private sealed record ResolvedModPiece(string ChangedName, ItemRecord Item, IReadOnlyList<GlamSlot> CompatibleSlots);

    private string glamourerSearch = string.Empty;

    private void DrawGlamourerTab()
    {
        if (ImGui.Button("Refresh Glamourer Designs"))
            plugin.Glamourer.RefreshDesigns(out status);

        ImGui.SameLine();
        ImGui.TextDisabled($"{plugin.Glamourer.Designs.Count} design(s)");

        ImGui.SetNextItemWidth(420 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##glamourerSearch", "Search name or folder...", ref glamourerSearch, 100);
        ImGui.Spacing();
        ImGui.TextDisabled("Wear / Edit expands partial Glamourer designs against what you currently wear so the Dresser stays a complete look.");
        ImGui.Spacing();

        var designs = plugin.Glamourer.Designs.Where(x =>
            string.IsNullOrWhiteSpace(glamourerSearch)
            || x.DisplayName.Contains(glamourerSearch, StringComparison.CurrentCultureIgnoreCase)
            || x.FullPath.Contains(glamourerSearch, StringComparison.CurrentCultureIgnoreCase));

        foreach (var design in designs)
        {
            ImGui.PushID(design.Id.ToString());
            ImGui.TextUnformatted(design.DisplayName);
            if (!string.Equals(design.FullPath, design.DisplayName, StringComparison.Ordinal))
            {
                ImGui.SameLine();
                ImGui.TextDisabled(design.FullPath);
            }

            if (design.ShownInQuickDesignBar)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("[Quick Design]");
            }

            if (ImGui.SmallButton("Wear / Edit"))
            {
                if (TryPlayerIndex(out var index)
                    && plugin.Glamourer.LoadDesignIntoOutfit(design, working, index, out status))
                {
                    workingNameIsSource = true;
                    workingDirty = false;
                    ApplyWholeLook(working);
                }
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Save to Wardrobe"))
                SaveGlamourerDesignLocal(design);

            ImGui.Separator();
            ImGui.PopID();
        }
    }

    private void SaveGlamourerDesignLocal(GlamourerDesignEntry design)
    {
        if (!TryPlayerIndex(out var index))
            return;

        var local = OutfitRecord.CreateBlank(design.DisplayName);
        if (!plugin.Glamourer.LoadDesignIntoOutfit(design, local, index, out status))
            return;

        working = local;
        workingNameIsSource = true;
        workingDirty = false;
        RequestSaveWorkingToWardrobe();
    }

    private void ApplyWholeLook(OutfitRecord outfit)
    {
        if (!TryPlayerIndex(out var index))
            return;

        string? modMessage = null;
        if (outfit.Mods.Count > 0 && !plugin.Penumbra.ApplyRecipes(outfit.Mods, index, out modMessage))
            modMessage = $"Mod recipe failed: {modMessage}";

        if (!plugin.Glamourer.ApplyOutfit(outfit, index, out var gearMessage))
        {
            status = modMessage is null ? gearMessage : $"{gearMessage} {modMessage}";
            return;
        }

        nextLiveEquipmentSyncUtc = DateTime.UtcNow.AddMilliseconds(350);
        status = modMessage is null ? gearMessage : $"{gearMessage} {modMessage}";
    }

    private void ApplyWorkingSlot(GlamSlot slot)
    {
        if (!TryPlayerIndex(out var index))
            return;

        working.EnsureSlots();
        if (plugin.Glamourer.ApplySlot(slot, working.Slots[slot], index, out var message))
            nextLiveEquipmentSyncUtc = DateTime.UtcNow.AddMilliseconds(350);
        status = message;
    }

    private void DrawWardrobePiece(OutfitRecord outfit, GlamSlot slot)
    {
        var value = outfit.Slots[slot];
        var item = plugin.GameData.GetItem(value.ItemId);
        var itemName = item?.Name ?? (value.ItemId == 0 ? "None" : $"Unknown item #{value.ItemId}");

        ImGui.PushID($"wardrobe-piece-{slot}");
        DrawItemIcon(value.ItemId, 44);
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextUnformatted($"{slot.DisplayName()}: {itemName}");
        if (value.Stain1 != 0 || value.Stain2 != 0)
        {
            var dye1 = plugin.GameData.GetStain(value.Stain1).Name;
            var dye2 = plugin.GameData.GetStain(value.Stain2).Name;
            ImGui.TextDisabled($"Dye 1: {dye1} · Dye 2: {dye2}");
        }
        else
        {
            ImGui.TextDisabled("Undyed");
        }

        if (ImGui.SmallButton("Apply This Piece"))
            ApplyWardrobePiece(outfit, slot);
        ImGui.EndGroup();
        ImGui.Spacing();
        ImGui.PopID();
    }

    private List<PenumbraModRecipe> RelevantRecipesForPiece(OutfitRecord outfit, GlamSlot slot)
    {
        var item = plugin.GameData.GetItem(outfit.Slots[slot].ItemId);
        if (item is null || outfit.Mods.Count == 0)
            return [];

        var relevantDirectories = plugin.Penumbra.Mods
            .Where(mod => mod.ChangedItems.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
            .Select(mod => mod.Directory)
            .ToHashSet(StringComparer.Ordinal);

        return outfit.Mods
            .Where(recipe => recipe.Enabled && relevantDirectories.Contains(recipe.Directory))
            .Select(recipe => recipe.Clone())
            .ToList();
    }

    private void ApplyWardrobePiece(OutfitRecord outfit, GlamSlot slot)
    {
        if (!TryPlayerIndex(out var index))
            return;

        var relevantRecipes = RelevantRecipesForPiece(outfit, slot);
        string? modMessage = null;
        if (relevantRecipes.Count > 0 && !plugin.Penumbra.ApplyRecipes(relevantRecipes, index, out modMessage))
            modMessage = $"Relevant mod recipe failed: {modMessage}";

        working.EnsureSlots();
        var source = outfit.Slots[slot];
        var target = working.Slots[slot];
        CopySlot(source, target);
        MergeWorkingMods(relevantRecipes);
        MarkWorkingChanged();

        if (!plugin.Glamourer.ApplySlot(slot, target, index, out var gearMessage))
        {
            status = modMessage is null ? gearMessage : $"{gearMessage} {modMessage}";
            return;
        }

        nextLiveEquipmentSyncUtc = DateTime.UtcNow.AddMilliseconds(350);
        status = modMessage is null
            ? $"Applied only {slot.DisplayName()} from '{outfit.Name}'."
            : $"Applied only {slot.DisplayName()} from '{outfit.Name}'. {modMessage}";
    }

    private PenumbraModRecipe ToRecipe(PenumbraModEntry mod)
        => new()
        {
            Directory = mod.Directory,
            Name = mod.Name,
            Enabled = mod.Enabled,
            Priority = mod.Priority,
            Settings = mod.Settings.ToDictionary(x => x.Key, x => x.Value.ToList(), StringComparer.Ordinal),
        };

    private void AttachModToWorking(PenumbraModEntry mod)
    {
        working.Mods.RemoveAll(x => string.Equals(x.Directory, mod.Directory, StringComparison.Ordinal));
        working.Mods.Add(ToRecipe(mod));
        MarkWorkingChanged();
        status = $"Attached {mod.Name} to the Dresser.";
    }

    private void MergeWorkingMods(IEnumerable<PenumbraModRecipe> recipes)
    {
        foreach (var recipe in recipes)
        {
            working.Mods.RemoveAll(x => string.Equals(x.Directory, recipe.Directory, StringComparison.Ordinal));
            working.Mods.Add(recipe.Clone());
        }
    }

    private static void CopySlot(OutfitSlot source, OutfitSlot target)
    {
        target.ItemId = source.ItemId;
        target.Stain1 = source.Stain1;
        target.Stain2 = source.Stain2;
        target.Apply = true;
    }

    private bool EnsureModEnabled(PenumbraModEntry mod)
    {
        if (mod.Enabled)
            return true;

        if (plugin.Penumbra.SetEnabled(mod, true, out var message))
            return true;

        status = message;
        return false;
    }

    private ResolvedModPiece? ResolveChangedPiece(string changed)
    {
        foreach (var item in plugin.GameData.GetItemsByName(changed))
        {
            var slots = GlamSlots.Ordered.Where(slot => plugin.GameData.ItemFitsSlot(item.Id, slot)).ToArray();
            if (slots.Length > 0)
                return new ResolvedModPiece(changed, item, slots);
        }

        return null;
    }

    private List<ResolvedModPiece> ResolveModPieces(PenumbraModEntry mod)
    {
        var result = new List<ResolvedModPiece>();
        foreach (var changed in mod.ChangedItems)
        {
            var piece = ResolveChangedPiece(changed);
            if (piece is not null)
                result.Add(piece);
        }

        return result;
    }

    private void ApplyModLook(PenumbraModEntry mod)
    {
        if (!EnsureModEnabled(mod))
            return;

        var pieces = ResolveModPieces(mod);
        if (pieces.Count == 0)
        {
            status = $"{mod.Name} has no changed item names that AllMyGlams can map to wearable FFXIV equipment.";
            return;
        }

        var outfit = OutfitRecord.CreateBlank($"Mod · {mod.Name}");
        outfit.EnsureSlots();
        outfit.Mods.Add(ToRecipe(mod));

        var occupied = new HashSet<GlamSlot>();
        var appliedPieces = 0;
        var skippedAlternatives = 0;
        foreach (var piece in pieces)
        {
            GlamSlot? targetSlot = piece.CompatibleSlots.FirstOrDefault(slot => !occupied.Contains(slot));
            if (targetSlot is null || targetSlot.Value == 0)
            {
                skippedAlternatives++;
                continue;
            }

            var target = outfit.Slots[targetSlot.Value];
            target.ItemId = piece.Item.Id;
            target.Stain1 = 0;
            target.Stain2 = 0;
            target.Apply = true;
            occupied.Add(targetSlot.Value);
            appliedPieces++;
        }

        working = outfit;
        workingNameIsSource = true;
        workingDirty = false;
        ApplyWholeLook(working);
        if (skippedAlternatives > 0)
            status += $" {skippedAlternatives} additional changed item(s) shared an already-used slot; use their individual Apply button to choose them instead.";
        else
            status += $" Loaded {appliedPieces} changed equipment piece(s); every other Dresser slot was set to None.";
    }

    private void ApplyModPiece(PenumbraModEntry mod, ItemRecord item, GlamSlot slot)
    {
        if (!EnsureModEnabled(mod))
            return;

        working.EnsureSlots();
        var target = working.Slots[slot];
        target.ItemId = item.Id;
        target.Stain1 = 0;
        target.Stain2 = 0;
        target.Apply = true;

        working.Mods.RemoveAll(x => string.Equals(x.Directory, mod.Directory, StringComparison.Ordinal));
        working.Mods.Add(ToRecipe(mod));
        MarkWorkingChanged();

        if (!TryPlayerIndex(out var index))
            return;

        if (plugin.Glamourer.ApplySlot(slot, target, index, out var message))
        {
            nextLiveEquipmentSyncUtc = DateTime.UtcNow.AddMilliseconds(350);
            status = $"Applied {item.Name} from {mod.Name} to {slot.DisplayName()}. No other equipment slot was changed.";
        }
        else
        {
            status = message;
        }
    }

    private void DrawModOptionsEditor(PenumbraModEntry mod)
    {
        if (mod.AvailableSettings.Count == 0)
        {
            ImGui.TextDisabled("This mod exposes no option groups.");
            return;
        }

        if (!ImGui.TreeNode("Editable option groups"))
            return;

        foreach (var group in mod.AvailableSettings)
        {
            ImGui.PushID(group.Name);
            ImGui.TextUnformatted(group.Name);
            ImGui.SameLine();
            ImGui.TextDisabled(group.IsSingle ? "single choice" : "multi choice");

            mod.Settings.TryGetValue(group.Name, out var currentValues);
            currentValues ??= [];

            if (group.IsSingle)
            {
                var current = currentValues.FirstOrDefault() ?? "(none)";
                ImGui.SetNextItemWidth(360 * ImGuiHelpers.GlobalScale);
                if (ImGui.BeginCombo("##single", current))
                {
                    foreach (var option in group.Options)
                    {
                        if (ImGui.Selectable($"{option}##option", string.Equals(option, current, StringComparison.Ordinal)))
                            plugin.Penumbra.SetOptionGroup(mod, group.Name, new[] { option }, out status);
                    }
                    ImGui.EndCombo();
                }
            }
            else
            {
                foreach (var option in group.Options)
                {
                    var selected = currentValues.Contains(option, StringComparer.Ordinal);
                    if (!ImGui.Checkbox($"{option}##multi", ref selected))
                        continue;

                    var next = currentValues.ToList();
                    if (selected)
                    {
                        if (!next.Contains(option, StringComparer.Ordinal))
                            next.Add(option);
                    }
                    else
                    {
                        next.RemoveAll(x => string.Equals(x, option, StringComparison.Ordinal));
                    }

                    plugin.Penumbra.SetOptionGroup(mod, group.Name, next, out status);
                    mod.Settings.TryGetValue(group.Name, out currentValues);
                    currentValues ??= [];
                }
            }

            ImGui.PopID();
        }

        ImGui.TreePop();
    }

    private void DrawChangedItem(PenumbraModEntry mod, string changed)
    {
        var piece = ResolveChangedPiece(changed);
        if (piece is null)
        {
            ImGui.BulletText(changed);
            return;
        }

        ImGui.PushID($"changed-{changed}");
        DrawItemIcon(piece.Item.Id, 52);
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextUnformatted(piece.Item.Name);
        ImGui.TextDisabled(string.Join(" / ", piece.CompatibleSlots.Select(x => x.DisplayName())));

        if (piece.CompatibleSlots.Count == 1)
        {
            if (ImGui.SmallButton("Apply This Piece"))
                ApplyModPiece(mod, piece.Item, piece.CompatibleSlots[0]);
        }
        else
        {
            for (var i = 0; i < piece.CompatibleSlots.Count; i++)
            {
                if (i > 0)
                    ImGui.SameLine();
                var slot = piece.CompatibleSlots[i];
                if (ImGui.SmallButton($"Apply {slot.DisplayName()}"))
                    ApplyModPiece(mod, piece.Item, slot);
            }
        }

        ImGui.EndGroup();
        ImGui.Spacing();
        ImGui.PopID();
    }
}
