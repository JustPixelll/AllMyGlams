using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AllMyGlams.Services;

public sealed class GlamourerIpc
{
    private const ulong ApplyOnce = 0x01;
    private const ulong ApplyEquipment = 0x02;

    private readonly ICallGateSubscriber<int, byte, ulong, IReadOnlyList<byte>, uint, ulong, int> setItem;
    private readonly ICallGateSubscriber<string, string, (int, Guid)> addDesign;
    private readonly ICallGateSubscriber<int, uint, (int, JObject?)> getState;
    private readonly ICallGateSubscriber<int, uint, ulong, int> revertState;
    private readonly ICallGateSubscriber<Dictionary<Guid, (string DisplayName, string FullPath, uint DisplayColor, bool ShownInQdb)>> getDesignListExtended;
    private readonly ICallGateSubscriber<Guid, JObject?> getDesignJObject;
    private readonly ICallGateSubscriber<Guid, int, uint, ulong, int> applyDesign;

    public List<GlamourerDesignEntry> Designs { get; } = [];

    public GlamourerIpc(IDalamudPluginInterface pi)
    {
        setItem = pi.GetIpcSubscriber<int, byte, ulong, IReadOnlyList<byte>, uint, ulong, int>("Glamourer.SetItem.V3");
        addDesign = pi.GetIpcSubscriber<string, string, (int, Guid)>("Glamourer.AddDesign");
        getState = pi.GetIpcSubscriber<int, uint, (int, JObject?)>("Glamourer.GetState");
        revertState = pi.GetIpcSubscriber<int, uint, ulong, int>("Glamourer.RevertState");
        getDesignListExtended = pi.GetIpcSubscriber<Dictionary<Guid, (string, string, uint, bool)>>("Glamourer.GetDesignListExtended");
        getDesignJObject = pi.GetIpcSubscriber<Guid, JObject?>("Glamourer.GetDesignJObject");
        applyDesign = pi.GetIpcSubscriber<Guid, int, uint, ulong, int>("Glamourer.ApplyDesign");
    }

    public bool ApplyOutfit(OutfitRecord outfit, int objectIndex, out string message)
    {
        outfit.EnsureSlots();
        var applied = 0;

        try
        {
            foreach (var slot in GlamSlots.Ordered)
            {
                var value = outfit.Slots[slot];
                var stains = new byte[] { value.Stain1, value.Stain2 };
                // ItemId 0 is intentionally meaningful: Glamourer resolves it to the
                // slot-specific Nothing item, so an empty dresser slot really becomes None.
                var ec = setItem.InvokeFunc(objectIndex, (byte)slot, value.ItemId, stains, 0, ApplyOnce);
                if (ec != 0)
                {
                    message = $"Glamourer rejected {slot.DisplayName()} (code {ec}). {applied} slot(s) were already applied.";
                    return false;
                }

                applied++;
            }

            message = $"Applied the complete {applied}-slot Dresser look through Glamourer.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Could not talk to Glamourer. Is it installed and enabled? {ex.Message}";
            return false;
        }
    }

    public bool CaptureCurrent(OutfitRecord target, int objectIndex, out string message)
    {
        target.EnsureSlots();

        try
        {
            var (ec, state) = getState.InvokeFunc(objectIndex, 0);
            if (ec != 0 || state is null)
            {
                message = $"Glamourer could not provide the current actor state (code {ec}).";
                return false;
            }

            if (!ReadCurrentEquipment(state, target, out var captured))
            {
                message = "Glamourer returned a state without equipment data.";
                return false;
            }

            ResetSourceMetadata(target);
            message = $"Captured the current {captured}-slot look from your character, including explicit None slots.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Could not capture from Glamourer. Is it installed and enabled? {ex.Message}";
            return false;
        }
    }

    public bool RevertEquipmentToGame(int objectIndex, out string message)
    {
        try
        {
            var ec = revertState.InvokeFunc(objectIndex, 0, ApplyOnce | ApplyEquipment);
            if (ec is not (0 or 1))
            {
                message = $"Glamourer could not revert equipment to the game state (code {ec}).";
                return false;
            }

            message = "Reverted Glamourer equipment overrides to the game look.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Could not revert through Glamourer. Is it installed and enabled? {ex.Message}";
            return false;
        }
    }

    public bool RefreshDesigns(out string message)
    {
        try
        {
            Designs.Clear();
            foreach (var (id, data) in getDesignListExtended.InvokeFunc())
                Designs.Add(new GlamourerDesignEntry(id, data.DisplayName, data.FullPath, data.DisplayColor, data.ShownInQdb));

            Designs.Sort((a, b) => string.Compare(a.FullPath, b.FullPath, StringComparison.CurrentCultureIgnoreCase));
            message = $"Loaded {Designs.Count} Glamourer design(s).";
            return true;
        }
        catch (Exception ex)
        {
            Designs.Clear();
            message = $"Could not read Glamourer designs. Is Glamourer installed and enabled? {ex.Message}";
            return false;
        }
    }

    public bool LoadDesignIntoOutfit(GlamourerDesignEntry design, OutfitRecord target, int objectIndex, out string message)
    {
        target.EnsureSlots();
        try
        {
            // A Glamourer design can be partial. The AllMyGlams dresser can not be partial,
            // so start with what the character is actually wearing and overlay the design's
            // item/stain application flags to obtain the complete resulting look.
            var (stateEc, state) = getState.InvokeFunc(objectIndex, 0);
            if (stateEc != 0 || state is null || !ReadCurrentEquipment(state, target, out _))
            {
                message = $"Could not read the current character state before loading '{design.DisplayName}'.";
                return false;
            }

            var json = getDesignJObject.InvokeFunc(design.Id);
            if (json is null || !OverlayDesignEquipment(json, target, out var changed))
            {
                message = $"Could not read equipment from '{design.DisplayName}'.";
                return false;
            }

            target.Name = design.DisplayName;
            target.Mods.Clear();
            ResetSourceMetadata(target);
            message = $"Loaded '{design.DisplayName}' into the Dresser as a complete look ({changed} design slot change(s)).";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Could not load Glamourer design: {ex.Message}";
            return false;
        }
    }

    public bool ApplyExistingDesign(GlamourerDesignEntry design, int objectIndex, out string message)
    {
        try
        {
            var ec = applyDesign.InvokeFunc(design.Id, objectIndex, 0, ApplyOnce | ApplyEquipment);
            if (ec != 0)
            {
                message = $"Glamourer rejected '{design.DisplayName}' (code {ec}).";
                return false;
            }

            message = $"Applied equipment from '{design.DisplayName}'.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Could not apply Glamourer design: {ex.Message}";
            return false;
        }
    }

    public bool SaveDesign(OutfitRecord outfit, out Guid designId, out string message)
    {
        designId = Guid.Empty;
        outfit.EnsureSlots();

        try
        {
            var json = BuildEquipmentOnlyDesign(outfit).ToString(Formatting.None);
            var (ec, guid) = addDesign.InvokeFunc(json, string.IsNullOrWhiteSpace(outfit.Name) ? "AllMyGlams Outfit" : outfit.Name.Trim());
            if (ec != 0 || guid == Guid.Empty)
            {
                message = $"Glamourer could not save the design (code {ec}).";
                return false;
            }

            designId = guid;
            message = $"Saved the complete equipment look to Glamourer as '{outfit.Name}' ({guid}).";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Could not save to Glamourer. Is it installed and enabled? {ex.Message}";
            return false;
        }
    }

    public JObject BuildEquipmentOnlyDesign(OutfitRecord outfit)
    {
        outfit.EnsureSlots();
        var equipment = new JObject();

        foreach (var slot in GlamSlots.Ordered)
        {
            var value = outfit.Slots[slot];
            equipment[slot.ToString()] = new JObject
            {
                ["ItemId"] = value.ItemId,
                ["Crest"] = false,
                ["Apply"] = true,
                ["ApplyStain"] = true,
                ["ApplyCrest"] = false,
                ["Stain"] = value.Stain1,
                ["Stain2"] = value.Stain2,
            };
        }

        equipment["Hat"] = new JObject { ["Show"] = true, ["Apply"] = false };
        equipment["VieraEars"] = new JObject { ["Show"] = true, ["Apply"] = false };
        equipment["Visor"] = new JObject { ["IsToggled"] = false, ["Apply"] = false };
        equipment["Weapon"] = new JObject { ["Show"] = true, ["Apply"] = false };

        var customize = new JObject
        {
            ["ModelId"] = 0,
            ["Race"] = CustomizeValue(0),
            ["Clan"] = CustomizeValue(0),
            ["Gender"] = CustomizeValue(0),
            ["BodyType"] = CustomizeValue(0),
            ["Wetness"] = new JObject { ["Value"] = false, ["Apply"] = false },
        };

        return new JObject
        {
            ["FileVersion"] = 1,
            ["Equipment"] = equipment,
            ["Bonus"] = new JObject(),
            ["Customize"] = customize,
            ["Parameters"] = new JObject(),
            ["Materials"] = new JObject(),
        };
    }

    private static bool ReadCurrentEquipment(JObject json, OutfitRecord target, out int captured)
    {
        captured = 0;
        if (json["Equipment"] is not JObject equipment)
            return false;

        foreach (var slot in GlamSlots.Ordered)
        {
            var value = target.Slots[slot];
            if (equipment[slot.ToString()] is not JObject item)
            {
                value.ItemId = 0;
                value.Stain1 = 0;
                value.Stain2 = 0;
                value.Apply = true;
                captured++;
                continue;
            }

            value.ItemId = NormalizeNothing(slot, item["ItemId"]?.ToObject<ulong>() ?? 0);
            value.Stain1 = ToByte(item["Stain"]?.ToObject<int>() ?? 0);
            value.Stain2 = ToByte(item["Stain2"]?.ToObject<int>() ?? 0);
            value.Apply = true;
            captured++;
        }

        return true;
    }

    private static bool OverlayDesignEquipment(JObject json, OutfitRecord target, out int changed)
    {
        changed = 0;
        if (json["Equipment"] is not JObject equipment)
            return false;

        foreach (var slot in GlamSlots.Ordered)
        {
            if (equipment[slot.ToString()] is not JObject item)
                continue;

            var value = target.Slots[slot];
            var applyItem = item["Apply"]?.ToObject<bool>() ?? false;
            var applyStain = item["ApplyStain"]?.ToObject<bool>() ?? false;
            if (applyItem)
            {
                value.ItemId = NormalizeNothing(slot, item["ItemId"]?.ToObject<ulong>() ?? 0);
                changed++;
            }

            if (applyStain)
            {
                value.Stain1 = ToByte(item["Stain"]?.ToObject<int>() ?? 0);
                value.Stain2 = ToByte(item["Stain2"]?.ToObject<int>() ?? 0);
                if (!applyItem)
                    changed++;
            }

            value.Apply = true;
        }

        return true;
    }

    private static ulong NormalizeNothing(GlamSlot slot, ulong itemId)
    {
        if (itemId == 0)
            return 0;

        // Armor/accessory Nothing is normally serialized with a slot-specific synthetic
        // uint ID. Weapon/off-hand Nothing can instead use the FullEquipType-specific
        // synthetic range (uint.MaxValue - 384 - type). Custom model IDs live above 2^48,
        // so recognizing this small uint-reserved range does not collapse custom items.
        var physicalSlot = slot == GlamSlot.LFinger ? GlamSlot.RFinger : slot;
        var slotNothingId = (ulong)(uint.MaxValue - 128u - (uint)physicalSlot);
        if (itemId == slotNothingId)
            return 0;

        if (itemId <= uint.MaxValue)
        {
            var offset = (ulong)uint.MaxValue - itemId;
            if (offset is >= 384 and <= 512)
                return 0;
        }

        return itemId;
    }

    private static void ResetSourceMetadata(OutfitRecord target)
    {
        target.SourceName = "Local";
        target.SourceExternalId = null;
        target.SourceUrl = null;
        target.SourceAuthor = null;
        target.SourceRating = null;
        target.SourceLastRefreshed = null;
    }

    private static JObject CustomizeValue(byte value)
        => new() { ["Value"] = value, ["Apply"] = false };

    private static byte ToByte(int value)
        => (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);
}
