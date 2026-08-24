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
        MarkWorkingChanged();
        status = $"Attached {mod.Name} to the Dresser.";
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
