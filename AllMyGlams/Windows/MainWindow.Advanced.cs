using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace AllMyGlams.Windows;

public sealed partial class MainWindow
{
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
        ImGui.TextDisabled("Load strips the design down to equipment/dyes for editing here; Apply Equipment tells Glamourer to apply only the equipment portion of the original design.");
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

            if (ImGui.SmallButton("Load into Dresser"))
                plugin.Glamourer.LoadDesignIntoOutfit(design, working, out status);

            ImGui.SameLine();
            if (ImGui.SmallButton("Apply Equipment"))
            {
                if (TryPlayerIndex(out var index))
                    plugin.Glamourer.ApplyExistingDesign(design, index, out status);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Save Local Copy"))
                SaveGlamourerDesignLocal(design, false);

            ImGui.SameLine();
            if (ImGui.SmallButton("Favorite Local Copy"))
                SaveGlamourerDesignLocal(design, true);

            ImGui.Separator();
            ImGui.PopID();
        }
    }

    private void SaveGlamourerDesignLocal(GlamourerDesignEntry design, bool favorite)
    {
        var local = OutfitRecord.CreateBlank(design.DisplayName);
        if (!plugin.Glamourer.LoadDesignIntoOutfit(design, local, out status))
            return;

        local.Favorite = favorite;
        plugin.Configuration.Library.Add(local);
        plugin.Configuration.Save();
        status = $"Saved equipment from '{design.DisplayName}' to {(favorite ? "Favorites" : "Wardrobe")}.";
    }

    private void ApplyWholeLook(OutfitRecord outfit)
    {
        if (!TryPlayerIndex(out var index))
            return;

        if (!plugin.Glamourer.ApplyOutfit(outfit, index, out var gearMessage))
        {
            status = gearMessage;
            return;
        }

        if (outfit.Mods.Count == 0)
        {
            status = gearMessage;
            return;
        }

        if (!plugin.Penumbra.ApplyRecipes(outfit.Mods, index, out var modMessage))
        {
            status = $"Gear applied. Mod recipe failed: {modMessage}";
            return;
        }

        status = $"{gearMessage} {modMessage}";
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
        status = $"Attached {mod.Name} to '{working.Name}'.";
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

    private void DrawChangedItem(string changed)
    {
        var matchedItems = plugin.GameData.GetItemsByName(changed);
        if (matchedItems.Count > 0)
        {
            DrawItemIcon(matchedItems[0].Id, 24);
            ImGui.SameLine();
            ImGui.TextUnformatted(changed);
            ImGui.SameLine();
            ImGui.TextDisabled("[wearable item]");
        }
        else
        {
            ImGui.BulletText(changed);
        }
    }
}
