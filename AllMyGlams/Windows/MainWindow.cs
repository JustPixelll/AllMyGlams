using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace AllMyGlams.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private OutfitRecord working = OutfitRecord.CreateBlank("My Outfit");
    private string status = "Ready.";
    private GlamSlot? pickerSlot;
    private string itemSearch = string.Empty;
    private string librarySearch = string.Empty;
    private string modSearch = string.Empty;
    private bool requestOpenItemPicker;

    public MainWindow(Plugin plugin)
        : base("All My Glams##AllMyGlams")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(780, 560),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        DrawHeader();
        ImGui.Separator();

        if (ImGui.BeginTabBar("##AllMyGlamsTabs"))
        {
            if (ImGui.BeginTabItem("Dresser"))
            {
                DrawDresserTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Wardrobe"))
            {
                DrawLibraryTab(false);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Favorites"))
            {
                DrawLibraryTab(true);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Mods"))
            {
                DrawModsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Sources"))
            {
                DrawSourcesTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        DrawItemPicker();
    }

    private void DrawHeader()
    {
        ImGui.TextWrapped("Build vanilla gear looks, capture your current Glamourer equipment, save them to your local wardrobe or Glamourer, and manage outfit-related Penumbra mods from one place.");
        ImGui.Spacing();
        ImGui.TextDisabled(status);
    }

    private void DrawDresserTab()
    {
        working.EnsureSlots();

        ImGui.SetNextItemWidth(360 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##outfitName", "Outfit name", ref working.Name, 100);

        if (ImGui.Button("Detect from Glamourer"))
            CaptureFromGlamourer();

        ImGui.SameLine();
        if (ImGui.Button("Apply"))
            ApplyWorking();

        ImGui.SameLine();
        if (ImGui.Button("Save to Glamourer"))
            SaveWorkingToGlamourer();

        ImGui.SameLine();
        if (ImGui.Button("Save to Wardrobe"))
            SaveWorkingToLibrary(false);

        ImGui.SameLine();
        if (ImGui.Button("Favorite"))
            SaveWorkingToLibrary(true);

        ImGui.SameLine();
        if (ImGui.Button("New / Clear"))
        {
            working = OutfitRecord.CreateBlank("My Outfit");
            status = "Started a blank outfit.";
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Apply controls whether that slot is included. A cleared slot with Apply enabled tells Glamourer to use its slot-specific Nothing item; disabling Apply leaves the current appearance untouched.");
        ImGui.Spacing();

        if (ImGui.BeginTable("##dresserTable", 7,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY,
                new Vector2(0, -1)))
        {
            ImGui.TableSetupColumn("Use", ImGuiTableColumnFlags.WidthFixed, 45 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 90 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, 50 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Dye 1", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Dye 2", ImGuiTableColumnFlags.WidthFixed, 150 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 55 * ImGuiHelpers.GlobalScale);
            ImGui.TableHeadersRow();

            foreach (var slot in GlamSlots.Ordered)
            {
                if (!plugin.Configuration.ShowAccessories && slot is GlamSlot.Ears or GlamSlot.Neck or GlamSlot.Wrists or GlamSlot.RFinger or GlamSlot.LFinger)
                    continue;

                var value = working.Slots[slot];
                ImGui.PushID($"dresser-{slot}");
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                var apply = value.Apply;
                if (ImGui.Checkbox("##apply", ref apply))
                    value.Apply = apply;

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(slot.DisplayName());

                ImGui.TableNextColumn();
                DrawItemIcon(value.ItemId, 36);

                ImGui.TableNextColumn();
                var item = plugin.GameData.GetItem(value.ItemId);
                var label = item?.Name ?? (value.ItemId == 0 ? "Select item..." : $"Unknown item #{value.ItemId}");
                if (ImGui.Button($"{label}##pick", new Vector2(-1, 0)))
                {
                    pickerSlot = slot;
                    itemSearch = item?.Name ?? string.Empty;
                    requestOpenItemPicker = true;
                }

                ImGui.TableNextColumn();
                DrawStainCombo("##stain1", ref value.Stain1);

                ImGui.TableNextColumn();
                DrawStainCombo("##stain2", ref value.Stain2);

                ImGui.TableNextColumn();
                if (ImGui.SmallButton("Clear"))
                {
                    value.ItemId = 0;
                    value.Stain1 = 0;
                    value.Stain2 = 0;
                    value.Apply = false;
                }

                ImGui.PopID();
            }

            ImGui.EndTable();
        }
    }

    private void DrawLibraryTab(bool favoritesOnly)
    {
        ImGui.SetNextItemWidth(330 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##librarySearch", "Search saved outfits...", ref librarySearch, 100);
        ImGui.SameLine();
        ImGui.TextDisabled(favoritesOnly ? "Favorites are saved locally." : $"{plugin.Configuration.Library.Count} saved outfit(s)");
        ImGui.Spacing();

        var delete = (Guid?)null;
        var outfits = plugin.Configuration.Library
            .Where(x => !favoritesOnly || x.Favorite)
            .Where(x => string.IsNullOrWhiteSpace(librarySearch)
                        || x.Name.Contains(librarySearch, StringComparison.CurrentCultureIgnoreCase)
                        || x.SourceName.Contains(librarySearch, StringComparison.CurrentCultureIgnoreCase)
                        || (x.SourceAuthor?.Contains(librarySearch, StringComparison.CurrentCultureIgnoreCase) ?? false))
            .OrderByDescending(x => x.Favorite)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var outfit in outfits)
        {
            outfit.EnsureSlots();
            ImGui.PushID(outfit.Id.ToString());

            var favorite = outfit.Favorite;
            if (ImGui.Checkbox("★##favorite", ref favorite))
            {
                outfit.Favorite = favorite;
                plugin.Configuration.Save();
            }

            ImGui.SameLine();
            var appliedSlots = outfit.Slots.Count(x => x.Value.Apply);
            ImGui.TextUnformatted(outfit.Name);
            ImGui.SameLine();
            ImGui.TextDisabled($"{appliedSlots} slot(s) · {outfit.SourceName}{(outfit.Mods.Count > 0 ? $" · {outfit.Mods.Count} mod(s)" : string.Empty)}");

            if (!string.IsNullOrWhiteSpace(outfit.SourceAuthor))
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"by {outfit.SourceAuthor}");
            }

            if (ImGui.SmallButton("Load into Dresser"))
            {
                working = outfit.Clone(false);
                status = $"Loaded '{outfit.Name}' into the Dresser.";
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Apply Gear"))
                ApplyOutfit(outfit);

            ImGui.SameLine();
            if (ImGui.SmallButton("Save to Glamourer"))
                SaveOutfitToGlamourer(outfit);

            ImGui.SameLine();
            if (ImGui.SmallButton("Duplicate"))
            {
                var clone = outfit.Clone();
                clone.Name += " Copy";
                plugin.Configuration.Library.Add(clone);
                plugin.Configuration.Save();
                status = $"Duplicated '{outfit.Name}'.";
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Delete"))
                delete = outfit.Id;

            if (!string.IsNullOrWhiteSpace(outfit.SourceUrl))
            {
                ImGui.TextDisabled($"Source: {outfit.SourceUrl}");
                if (outfit.SourceRating is not null)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($"Rating: {outfit.SourceRating}");
                }
            }

            ImGui.Separator();
            ImGui.PopID();
        }

        if (delete is { } id)
        {
            plugin.Configuration.Library.RemoveAll(x => x.Id == id);
            plugin.Configuration.Save();
            status = "Removed saved outfit.";
        }
    }

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

        ImGui.SameLine();
        if (ImGui.Button("Attach enabled equipment mods to outfit"))
        {
            working.Mods = plugin.Penumbra.Mods
                .Where(x => x.Enabled && x.AffectsEquipment)
                .Select(x => new PenumbraModRecipe
                {
                    Directory = x.Directory,
                    Name = x.Name,
                    Enabled = true,
                    Priority = x.Priority,
                    Settings = x.Settings.ToDictionary(y => y.Key, y => y.Value.ToList(), StringComparer.Ordinal),
                })
                .ToList();
            status = $"Attached {working.Mods.Count} enabled equipment-related Penumbra mod(s) to the working outfit.";
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Equipment detection uses Penumbra's Changed Items data and matches named changed items against wearable FFXIV item names. Expand a mod to inspect exactly what Penumbra reports.");
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
            if (mod.AffectsEquipment)
                ImGui.TextUnformatted("[GEAR]");
            else
                ImGui.TextDisabled("[other]");

            ImGui.SameLine();
            var treeOpen = ImGui.TreeNode($"{mod.Name}##tree");

            ImGui.SameLine();
            ImGui.TextDisabled(mod.Inherited ? "inherited" : mod.Temporary ? "temporary" : "direct");

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90 * ImGuiHelpers.GlobalScale);
            var priority = mod.Priority;
            if (ImGui.InputInt("##priority", ref priority, 1, 10))
                mod.Priority = priority;
            ImGui.SameLine();
            if (ImGui.SmallButton("Set priority"))
                plugin.Penumbra.SetPriority(mod, mod.Priority, out status);

            if (treeOpen)
            {
                ImGui.TextDisabled($"Directory: {mod.Directory}");
                ImGui.TextDisabled($"Changed items: {mod.ChangedItems.Count}");

                if (mod.Settings.Count > 0 && ImGui.TreeNode("Current option settings"))
                {
                    foreach (var (group, options) in mod.Settings)
                        ImGui.BulletText($"{group}: {(options.Count == 0 ? "(none)" : string.Join(", ", options))}");
                    ImGui.TreePop();
                }

                if (ImGui.TreeNode("Changed items / objects"))
                {
                    if (mod.ChangedItems.Count == 0)
                        ImGui.TextDisabled("Penumbra reports no named changed items for this mod.");
                    else
                        foreach (var changed in mod.ChangedItems)
                            ImGui.BulletText(changed);
                    ImGui.TreePop();
                }

                ImGui.TreePop();
            }

            ImGui.Separator();
            ImGui.PopID();
        }
    }

    private void DrawSourcesTab()
    {
        ImGui.TextWrapped("Sourced Wardrobe is the home for imported public glamour recipes such as Eorzea Collection looks. Imported entries keep attribution and their source URL, but the resolved item/dye recipe lives in AllMyGlams so applying it later does not require another network request.");
        ImGui.Spacing();
        ImGui.TextWrapped("The provider layer is intentionally separate from the dresser. This lets us add an explicitly disclosed, cached Eorzea Collection importer without making the core wardrobe depend on scraping or image hotlinking.");
        ImGui.Spacing();

        var sourced = plugin.Configuration.Library.Where(x => !string.Equals(x.SourceName, "Local", StringComparison.OrdinalIgnoreCase)).ToList();
        ImGui.TextDisabled($"Cached sourced outfits: {sourced.Count}");

        foreach (var outfit in sourced)
        {
            ImGui.PushID($"source-{outfit.Id}");
            ImGui.TextUnformatted(outfit.Name);
            ImGui.SameLine();
            ImGui.TextDisabled($"{outfit.SourceName}{(string.IsNullOrWhiteSpace(outfit.SourceAuthor) ? string.Empty : $" · {outfit.SourceAuthor}")}");
            if (!string.IsNullOrWhiteSpace(outfit.SourceUrl))
                ImGui.TextDisabled(outfit.SourceUrl);
            if (outfit.SourceLastRefreshed is not null)
                ImGui.TextDisabled($"Metadata last refreshed: {outfit.SourceLastRefreshed:yyyy-MM-dd HH:mm}");
            if (ImGui.SmallButton("Load"))
            {
                working = outfit.Clone(false);
                status = $"Loaded sourced outfit '{outfit.Name}'.";
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

        if (ImGui.Button("None / clear slot", new Vector2(-1, 0)))
        {
            var target = working.Slots[slot];
            target.ItemId = 0;
            target.Stain1 = 0;
            target.Stain2 = 0;
            target.Apply = false;
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

    private void DrawStainCombo(string id, ref byte stainId)
    {
        var current = plugin.GameData.GetStain(stainId);
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo(id, current.Name))
            return;

        foreach (var stain in plugin.GameData.Stains)
        {
            if (ImGui.Selectable($"{stain.Name}##{stain.Id}", stain.Id == stainId))
                stainId = stain.Id;
        }

        ImGui.EndCombo();
    }

    private void CaptureFromGlamourer()
    {
        if (!TryPlayerIndex(out var index))
            return;

        plugin.Glamourer.CaptureCurrent(working, index, out status);
        if (string.IsNullOrWhiteSpace(working.Name) || working.Name == "My Outfit")
            working.Name = "Captured Outfit";
    }

    private void ApplyWorking() => ApplyOutfit(working);

    private void ApplyOutfit(OutfitRecord outfit)
    {
        if (!TryPlayerIndex(out var index))
            return;

        plugin.Glamourer.ApplyOutfit(outfit, index, out status);
    }

    private void SaveWorkingToGlamourer() => SaveOutfitToGlamourer(working);

    private void SaveOutfitToGlamourer(OutfitRecord outfit)
        => plugin.Glamourer.SaveDesign(outfit, out _, out status);

    private void SaveWorkingToLibrary(bool favorite)
    {
        var clone = working.Clone();
        clone.Favorite = favorite;
        if (string.IsNullOrWhiteSpace(clone.Name))
            clone.Name = favorite ? "Favorite Outfit" : "Saved Outfit";

        plugin.Configuration.Library.Add(clone);
        plugin.Configuration.Save();
        status = $"Saved '{clone.Name}' to {(favorite ? "Favorites" : "Wardrobe")}.";
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

        status = "Local player is not available. Log in before using actor/mod actions.";
        return false;
    }
}
