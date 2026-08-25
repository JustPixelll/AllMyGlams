using System.Numerics;
using AllMyGlams.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace AllMyGlams.Windows;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private OutfitRecord working = OutfitRecord.CreateBlank("Game Look");
    private string status = "Ready.";
    private GlamSlot? pickerSlot;
    private string itemSearch = string.Empty;
    private string librarySearch = string.Empty;
    private string modSearch = string.Empty;
    private string eorzeaInput = string.Empty;
    private bool requestOpenItemPicker;
    private bool workingDirty;
    private bool workingNameIsSource = true;
    private bool requestOverwritePopup;
    private OutfitRecord? pendingOverwrite;
    private Guid pendingOverwriteId;
    private Task<EorzeaImportResult>? eorzeaImportTask;

    public MainWindow(Plugin plugin)
        : base("All My Glams##AllMyGlams")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(820, 580),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose()
    {
    }

    public void RefreshFromIntegrations(bool forceDresser)
    {
        var messages = new List<string>();
        if (plugin.Glamourer.RefreshDesigns(out var glamourerMessage))
            messages.Add(glamourerMessage);

        if (TryPlayerIndex(out var index))
        {
            if (plugin.Penumbra.Refresh(index, plugin.GameData, out var penumbraMessage))
                messages.Add(penumbraMessage);

            if (forceDresser)
                CaptureLiveLook("Game Look", index);
        }

        if (messages.Count > 0 && !forceDresser)
            status = string.Join(" ", messages);
    }

    public override void Draw()
    {
        ProcessEorzeaImport();
        DrawHeader();
        ImGui.Separator();

        if (ImGui.BeginTabBar("##AllMyGlamsTabs"))
        {
            if (ImGui.BeginTabItem("Dresser"))
            {
                DrawDresserTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Glamourer"))
            {
                DrawGlamourerTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Wardrobe"))
            {
                DrawWardrobeTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Mods"))
            {
                DrawModsTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        DrawItemPicker();
        DrawOverwritePopup();
    }

    private void DrawHeader()
    {
        ImGui.TextWrapped("The Dresser mirrors your character's current equipment appearance. Item, dye and None edits apply immediately; Apply Dresser is only a fallback full-look reapply.");
        ImGui.Spacing();
        ImGui.TextDisabled(status);
    }

    private void DrawDresserTab()
    {
        working.EnsureSlots();

        var outfitName = working.Name;
        ImGui.SetNextItemWidth(360 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputTextWithHint("##outfitName", "Outfit name", ref outfitName, 100))
        {
            working.Name = outfitName;
            workingNameIsSource = false;
        }

        if (ImGui.Button("Apply Dresser to Character"))
            ApplyWholeLook(working);

        ImGui.SameLine();
        if (ImGui.Button("Revert to Game"))
            RevertToGame();

        ImGui.SameLine();
        if (ImGui.Button("Save to Wardrobe"))
            RequestSaveWorkingToWardrobe();

        ImGui.SameLine();
        if (ImGui.Button("Save to Glamourer"))
            SaveWorkingToGlamourer();

        ImGui.Spacing();
        ImGui.TextDisabled($"Live complete look · {working.Mods.Count} attached Penumbra mod recipe(s){(workingDirty ? " · unsaved changes" : string.Empty)}");
        ImGui.Spacing();

        if (ImGui.BeginTable("##dresserTable", 6,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY,
                new Vector2(0, -1)))
        {
            ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 95 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, 50 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Dye 1", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Dye 2", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 55 * ImGuiHelpers.GlobalScale);
            ImGui.TableHeadersRow();

            foreach (var slot in GlamSlots.Ordered)
            {
                var value = working.Slots[slot];
                ImGui.PushID($"dresser-{slot}");
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(slot.DisplayName());

                ImGui.TableNextColumn();
                DrawItemIcon(value.ItemId, 36);

                ImGui.TableNextColumn();
                var item = plugin.GameData.GetItem(value.ItemId);
                var label = item?.Name ?? (value.ItemId == 0 ? "None" : $"Unknown item #{value.ItemId}");
                if (ImGui.Button($"{label}##pick", new Vector2(-1, 0)))
                {
                    pickerSlot = slot;
                    itemSearch = item?.Name ?? string.Empty;
                    requestOpenItemPicker = true;
                }

                ImGui.TableNextColumn();
                var oldStain1 = value.Stain1;
                value.Stain1 = DrawStainCombo("##stain1", value.Stain1);
                if (oldStain1 != value.Stain1)
                {
                    MarkWorkingChanged();
                    ApplyWorkingSlot(slot);
                }

                ImGui.TableNextColumn();
                var oldStain2 = value.Stain2;
                value.Stain2 = DrawStainCombo("##stain2", value.Stain2);
                if (oldStain2 != value.Stain2)
                {
                    MarkWorkingChanged();
                    ApplyWorkingSlot(slot);
                }

                ImGui.TableNextColumn();
                if (ImGui.SmallButton("None"))
                {
                    value.ItemId = 0;
                    value.Stain1 = 0;
                    value.Stain2 = 0;
                    value.Apply = true;
                    MarkWorkingChanged();
                    ApplyWorkingSlot(slot);
                }

                ImGui.PopID();
            }

            ImGui.EndTable();
        }
    }

    private void DrawWardrobeTab()
    {
        ImGui.SetNextItemWidth(360 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##librarySearch", "Search wardrobe, author or source...", ref librarySearch, 100);
        ImGui.Spacing();

        if (ImGui.CollapsingHeader("My Wardrobe"))
        {
            var local = plugin.Configuration.Library
                .Where(x => !string.Equals(x.SourceName, "Eorzea Collection", StringComparison.OrdinalIgnoreCase))
                .Where(MatchesLibrarySearch)
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            ImGui.TextDisabled($"{local.Count} saved look(s)");
            DrawWardrobeEntries(local, false);
        }

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Public Wardrobe — Eorzea Collection"))
        {
            ImGui.TextWrapped("On-demand import only: paste an individual Eorzea Collection glamour URL. All My Glams downloads no screenshots, stores the resolved item/dye recipe locally, keeps creator/source attribution, and only contacts EC again when you explicitly import or refresh. It will not bypass a 403 or other access control.");
            ImGui.Spacing();

            ImGui.SetNextItemWidth(560 * ImGuiHelpers.GlobalScale);
            var enter = ImGui.InputTextWithHint("##ecUrl", "https://ffxiv.eorzeacollection.com/glamour/123456/name", ref eorzeaInput, 512, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            var importing = eorzeaImportTask is not null;
            if (importing)
            {
                ImGui.BeginDisabled();
                ImGui.Button("Importing...");
                ImGui.EndDisabled();
            }
            else if (ImGui.Button("Import") || enter)
            {
                StartEorzeaImport(eorzeaInput);
            }

            ImGui.Spacing();
            var publicEntries = plugin.Configuration.Library
                .Where(x => string.Equals(x.SourceName, "Eorzea Collection", StringComparison.OrdinalIgnoreCase))
                .Where(MatchesLibrarySearch)
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            ImGui.TextDisabled($"{publicEntries.Count} cached public look(s)");
            DrawWardrobeEntries(publicEntries, true);
        }
    }

    private void DrawWardrobeEntries(List<OutfitRecord> outfits, bool publicSource)
    {
        Guid? delete = null;
        foreach (var outfit in outfits)
        {
            outfit.EnsureSlots();
            ImGui.PushID(outfit.Id.ToString());

            var open = ImGui.TreeNode($"{outfit.Name}##wardrobe-tree");
            if (!string.IsNullOrWhiteSpace(outfit.SourceAuthor))
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"by {outfit.SourceAuthor}");
            }

            if (open)
            {
                if (!string.IsNullOrWhiteSpace(outfit.SourceName) && !string.Equals(outfit.SourceName, "Local", StringComparison.OrdinalIgnoreCase))
                    ImGui.TextDisabled($"Source: {outfit.SourceName}{(string.IsNullOrWhiteSpace(outfit.SourceUrl) ? string.Empty : $" · {outfit.SourceUrl}")}");
                if (outfit.SourceLastRefreshed is not null)
                    ImGui.TextDisabled($"Fetched: {outfit.SourceLastRefreshed:yyyy-MM-dd HH:mm} UTC");
                if (outfit.Mods.Count > 0)
                    ImGui.TextDisabled($"{outfit.Mods.Count} attached Penumbra mod recipe(s)");

                if (ImGui.SmallButton("Apply Outfit"))
                {
                    working = outfit.Clone();
                    workingNameIsSource = true;
                    workingDirty = false;
                    ApplyWholeLook(working);
                }

                ImGui.SameLine();
                if (ImGui.SmallButton("Save to Glamourer"))
                    SaveOutfitToGlamourer(outfit);

                if (publicSource && !string.IsNullOrWhiteSpace(outfit.SourceUrl))
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Refresh Source"))
                        StartEorzeaImport(outfit.SourceUrl!);
                }

                ImGui.SameLine();
                if (ImGui.SmallButton(publicSource ? "Remove Cache" : "Delete"))
                    delete = outfit.Id;

                ImGui.Spacing();
                foreach (var slot in GlamSlots.Ordered)
                    DrawWardrobePiece(outfit, slot);

                ImGui.TreePop();
            }

            ImGui.Separator();
            ImGui.PopID();
        }

        if (delete is { } id)
        {
            plugin.Configuration.Library.RemoveAll(x => x.Id == id);
            plugin.Configuration.Save();
            status = publicSource ? "Removed cached public look." : "Removed wardrobe look.";
        }
    }

    private bool MatchesLibrarySearch(OutfitRecord outfit)
        => string.IsNullOrWhiteSpace(librarySearch)
           || outfit.Name.Contains(librarySearch, StringComparison.CurrentCultureIgnoreCase)
           || outfit.SourceName.Contains(librarySearch, StringComparison.CurrentCultureIgnoreCase)
           || (outfit.SourceAuthor?.Contains(librarySearch, StringComparison.CurrentCultureIgnoreCase) ?? false);

    private void DrawModsTab()
    {
        if (ImGui.Button("Refresh Penumbra"))
            RefreshPenumbra();

        ImGui.SameLine();
        ImGui.TextDisabled($"Effective collection: {plugin.Penumbra.EffectiveCollectionName}");

        ImGui.SetNextItemWidth(330 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##modSearch", "Search mods or changed items...", ref modSearch, 100);
        ImGui.SameLine();
        var equipmentOnly = plugin.Configuration.ModsEquipmentOnly;
        if (ImGui.Checkbox("Equipment-related only", ref equipmentOnly))
        {
            plugin.Configuration.ModsEquipmentOnly = equipmentOnly;
            plugin.Configuration.Save();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Collapsed rows stay intentionally minimal. Expand a mod for priority, options, diagnostics and per-piece apply controls.");
        ImGui.Spacing();

        var visible = plugin.Penumbra.Mods.Where(x =>
            (!plugin.Configuration.ModsEquipmentOnly || x.AffectsEquipment)
            && (string.IsNullOrWhiteSpace(modSearch)
                || x.Name.Contains(modSearch, StringComparison.CurrentCultureIgnoreCase)
                || x.Directory.Contains(modSearch, StringComparison.CurrentCultureIgnoreCase)
                || x.ChangedItems.Any(y => y.Contains(modSearch, StringComparison.CurrentCultureIgnoreCase))));

        foreach (var mod in visible)
        {
            ImGui.PushID(mod.Directory);
            var enabled = mod.Enabled;
            if (ImGui.Checkbox("##enabled", ref enabled))
                plugin.Penumbra.SetEnabled(mod, enabled, out status);

            ImGui.SameLine();
            var treeOpen = ImGui.TreeNode($"{mod.Name}##tree");

            if (treeOpen)
            {
                ImGui.TextDisabled($"{(mod.AffectsEquipment ? "Equipment-related" : "Other changes")} · {(mod.Inherited ? "inherited" : mod.Temporary ? "temporary" : "direct")} · {mod.ChangedItems.Count} changed item/object(s)");
                ImGui.TextDisabled($"Directory: {mod.Directory}");

                if (ImGui.SmallButton("Apply Mod Look"))
                    ApplyModLook(mod);

                ImGui.SameLine();
                if (ImGui.SmallButton("Attach this mod to Dresser"))
                    AttachModToWorking(mod);

                ImGui.Spacing();
                ImGui.TextUnformatted("Priority");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(90 * ImGuiHelpers.GlobalScale);
                var priority = mod.Priority;
                if (ImGui.InputInt("##priority", ref priority, 1, 10))
                    mod.Priority = priority;
                ImGui.SameLine();
                if (ImGui.SmallButton("Set"))
                    plugin.Penumbra.SetPriority(mod, mod.Priority, out status);

                ImGui.SameLine();
                if (!mod.AvailableSettingsLoaded && ImGui.SmallButton("Load / edit options"))
                    plugin.Penumbra.LoadAvailableSettings(mod, out status);

                if (mod.AvailableSettingsLoaded)
                    DrawModOptionsEditor(mod);
                else if (mod.Settings.Count > 0 && ImGui.TreeNode("Current option settings"))
                {
                    foreach (var (group, options) in mod.Settings)
                        ImGui.BulletText($"{group}: {(options.Count == 0 ? "(none)" : string.Join(", ", options))}");
                    ImGui.TreePop();
                }

                ImGui.Spacing();
                ImGui.TextUnformatted("Changed items / objects");
                ImGui.Separator();
                if (mod.ChangedItems.Count == 0)
                    ImGui.TextDisabled("Penumbra reports no named changed items for this mod.");
                else
                    foreach (var changed in mod.ChangedItems)
                        DrawChangedItem(mod, changed);

                ImGui.TreePop();
            }

            ImGui.Separator();
            ImGui.PopID();
        }
    }

    private void DrawItemPicker()
    {
        if (requestOpenItemPicker)
        {
            ImGui.OpenPopup("Choose Item##AllMyGlamsItemPicker");
            requestOpenItemPicker = false;
        }

        var open = true;
        if (!ImGui.BeginPopupModal("Choose Item##AllMyGlamsItemPicker", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        if (pickerSlot is not { } slot)
        {
            ImGui.TextDisabled("No slot selected.");
            ImGui.EndPopup();
            return;
        }

        ImGui.TextUnformatted($"Choose {slot.DisplayName()}");
        ImGui.SetNextItemWidth(480 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##itemSearch", "Search item name...", ref itemSearch, 100);

        if (ImGui.Button("None", new Vector2(-1, 0)))
        {
            var target = working.Slots[slot];
            target.ItemId = 0;
            target.Stain1 = 0;
            target.Stain2 = 0;
            target.Apply = true;
            MarkWorkingChanged();
            ApplyWorkingSlot(slot);
            ImGui.CloseCurrentPopup();
        }

        ImGui.BeginChild("##itemResults", new Vector2(620 * ImGuiHelpers.GlobalScale, 430 * ImGuiHelpers.GlobalScale), true);
        var results = plugin.GameData.SearchItems(slot, itemSearch, 150);
        foreach (var item in results)
        {
            ImGui.PushID(unchecked((int)item.Id));
            DrawItemIcon(item.Id, 30);
            ImGui.SameLine();
            if (ImGui.Selectable($"{item.Name}##select", false, ImGuiSelectableFlags.None, new Vector2(520 * ImGuiHelpers.GlobalScale, 30 * ImGuiHelpers.GlobalScale)))
            {
                var target = working.Slots[slot];
                target.ItemId = item.Id;
                target.Apply = true;
                MarkWorkingChanged();
                ApplyWorkingSlot(slot);
                ImGui.CloseCurrentPopup();
            }
            ImGui.PopID();
        }
        ImGui.EndChild();

        ImGui.TextDisabled(results.Count >= 150 ? "Showing first 150 matches. Refine the search for more." : $"{results.Count} match(es)");
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private void DrawItemIcon(ulong itemId, float size)
    {
        var item = plugin.GameData.GetItem(itemId);
        if (item is null || item.IconId == 0)
        {
            ImGui.Dummy(new Vector2(size, size) * ImGuiHelpers.GlobalScale);
            return;
        }

        try
        {
            var texture = Plugin.TextureProvider.GetFromGameIcon(item.IconId).GetWrapOrDefault();
            if (texture is not null)
                ImGui.Image(texture.Handle, new Vector2(size, size) * ImGuiHelpers.GlobalScale);
            else
                ImGui.Dummy(new Vector2(size, size) * ImGuiHelpers.GlobalScale);
        }
        catch
        {
            ImGui.Dummy(new Vector2(size, size) * ImGuiHelpers.GlobalScale);
        }
    }

    private byte DrawStainCombo(string id, byte stainId)
    {
        var current = plugin.GameData.GetStain(stainId);
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo(id, current.Name))
            return stainId;

        foreach (var stain in plugin.GameData.Stains)
        {
            if (ImGui.Selectable($"{stain.Name}##{stain.Id}", stain.Id == stainId))
                stainId = stain.Id;
        }

        ImGui.EndCombo();
        return stainId;
    }

    private void MarkWorkingChanged()
    {
        workingDirty = true;
        if (workingNameIsSource)
        {
            working.Name = "Custom";
            workingNameIsSource = false;
        }
    }

    private void CaptureLiveLook(string name, int objectIndex)
    {
        var live = OutfitRecord.CreateBlank(name);
        if (!plugin.Glamourer.CaptureCurrent(live, objectIndex, out var captureMessage))
        {
            status = captureMessage;
            return;
        }

        live.Name = name;
        live.Mods = CaptureActiveModRecipes();
        working = live;
        workingDirty = false;
        workingNameIsSource = true;
        status = $"{captureMessage} Loaded as {name}.";
    }

    private List<PenumbraModRecipe> CaptureActiveModRecipes()
        => plugin.Penumbra.Mods
            .Where(x => x.Enabled && x.AffectsEquipment)
            .Select(ToRecipe)
            .ToList();

    private void RevertToGame()
    {
        if (!TryPlayerIndex(out var index))
            return;

        if (!plugin.Glamourer.RevertEquipmentToGame(index, out var revertMessage))
        {
            status = revertMessage;
            return;
        }

        plugin.Penumbra.Refresh(index, plugin.GameData, out _);
        CaptureLiveLook("Game Look", index);
        status = $"{revertMessage} Dresser refreshed from the game look.";
    }

    private void SaveWorkingToGlamourer()
    {
        if (string.IsNullOrWhiteSpace(working.Name))
            working.Name = "Custom";

        if (plugin.Glamourer.SaveDesign(working, out _, out status))
            plugin.Glamourer.RefreshDesigns(out _);
    }

    private void SaveOutfitToGlamourer(OutfitRecord outfit)
    {
        if (plugin.Glamourer.SaveDesign(outfit, out _, out status))
            plugin.Glamourer.RefreshDesigns(out _);
    }

    private void RequestSaveWorkingToWardrobe()
    {
        var clone = working.Clone();
        clone.Favorite = false;
        clone.Name = string.IsNullOrWhiteSpace(clone.Name) ? "Custom" : clone.Name.Trim();

        // Public imports remain cached in the Public Wardrobe. Saving one from the Dresser
        // creates a local copy while retaining its attribution URL/author metadata.
        if (string.Equals(clone.SourceName, "Eorzea Collection", StringComparison.OrdinalIgnoreCase))
            clone.SourceName = "Local copy · Eorzea Collection";

        if (clone.Name.Equals("Custom", StringComparison.OrdinalIgnoreCase))
            clone.Name = NextCustomName();

        var existing = plugin.Configuration.Library.FirstOrDefault(x =>
            !string.Equals(x.SourceName, "Eorzea Collection", StringComparison.OrdinalIgnoreCase)
            && x.Name.Equals(clone.Name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            pendingOverwrite = clone;
            pendingOverwriteId = existing.Id;
            requestOverwritePopup = true;
            return;
        }

        CommitWardrobeSave(clone);
    }

    private string NextCustomName()
    {
        var names = plugin.Configuration.Library
            .Where(x => !string.Equals(x.SourceName, "Eorzea Collection", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!names.Contains("Custom"))
            return "Custom";

        for (var i = 2; ; i++)
        {
            var candidate = $"Custom{i}";
            if (!names.Contains(candidate))
                return candidate;
        }
    }

    private void CommitWardrobeSave(OutfitRecord clone)
    {
        plugin.Configuration.Library.Add(clone);
        plugin.Configuration.Save();
        working.Name = clone.Name;
        workingNameIsSource = false;
        workingDirty = false;
        status = $"Saved '{clone.Name}' to My Wardrobe.";
    }

    private void DrawOverwritePopup()
    {
        if (requestOverwritePopup)
        {
            ImGui.OpenPopup("Overwrite Wardrobe Look?##AllMyGlams");
            requestOverwritePopup = false;
        }

        var open = true;
        if (!ImGui.BeginPopupModal("Overwrite Wardrobe Look?##AllMyGlams", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextWrapped($"My Wardrobe already contains '{pendingOverwrite?.Name}'. Override that saved look?");
        ImGui.Spacing();

        if (ImGui.Button("Override", new Vector2(120 * ImGuiHelpers.GlobalScale, 0)) && pendingOverwrite is not null)
        {
            var index = plugin.Configuration.Library.FindIndex(x => x.Id == pendingOverwriteId);
            if (index >= 0)
            {
                pendingOverwrite.Id = pendingOverwriteId;
                plugin.Configuration.Library[index] = pendingOverwrite;
                plugin.Configuration.Save();
                working.Name = pendingOverwrite.Name;
                workingNameIsSource = false;
                workingDirty = false;
                status = $"Overrode '{pendingOverwrite.Name}' in My Wardrobe.";
            }

            pendingOverwrite = null;
            pendingOverwriteId = Guid.Empty;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120 * ImGuiHelpers.GlobalScale, 0)))
        {
            pendingOverwrite = null;
            pendingOverwriteId = Guid.Empty;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void StartEorzeaImport(string input)
    {
        if (eorzeaImportTask is not null)
            return;

        status = "Fetching one Eorzea Collection glamour recipe...";
        eorzeaImportTask = plugin.EorzeaCollection.ImportAsync(input, plugin.GameData);
    }

    private void ProcessEorzeaImport()
    {
        if (eorzeaImportTask is null || !eorzeaImportTask.IsCompleted)
            return;

        EorzeaImportResult result;
        try
        {
            result = eorzeaImportTask.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            status = $"Eorzea Collection import failed: {ex.Message}";
            eorzeaImportTask = null;
            return;
        }

        eorzeaImportTask = null;
        if (!result.Success || result.Outfit is null)
        {
            status = result.Message;
            return;
        }

        var imported = result.Outfit;
        var existingIndex = plugin.Configuration.Library.FindIndex(x =>
            string.Equals(x.SourceName, "Eorzea Collection", StringComparison.OrdinalIgnoreCase)
            && x.SourceExternalId == imported.SourceExternalId);

        if (existingIndex >= 0)
        {
            imported.Id = plugin.Configuration.Library[existingIndex].Id;
            plugin.Configuration.Library[existingIndex] = imported;
        }
        else
        {
            plugin.Configuration.Library.Add(imported);
        }

        plugin.Configuration.Save();
        status = result.Warnings.Count == 0
            ? result.Message
            : $"{result.Message} {result.Warnings.Count} field(s) could not be resolved; see the log for details.";

        foreach (var warning in result.Warnings)
            Plugin.Log.Warning("[Eorzea Collection] {Warning}", warning);
    }

    private void RefreshPenumbra()
    {
        if (!TryPlayerIndex(out var index))
            return;

        plugin.Penumbra.Refresh(index, plugin.GameData, out status);
    }

    private bool TryPlayerIndex(out int index)
    {
        if (plugin.TryGetLocalPlayerIndex(out index))
            return true;

        status = "Local player is not available. Log in before using character/mod actions.";
        return false;
    }
}
