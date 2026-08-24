using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AllMyGlams.Services;

public sealed class GlamourerIpc
{
    private const ulong ApplyOnce = 0x01;

    private readonly ICallGateSubscriber<int, byte, ulong, IReadOnlyList<byte>, uint, ulong, int> setItem;
    private readonly ICallGateSubscriber<string, string, (int, Guid)> addDesign;
    private readonly ICallGateSubscriber<int, uint, (int, JObject?)> getState;

    public GlamourerIpc(IDalamudPluginInterface pi)
    {
        setItem = pi.GetIpcSubscriber<int, byte, ulong, IReadOnlyList<byte>, uint, ulong, int>("Glamourer.SetItem.V3");
        addDesign = pi.GetIpcSubscriber<string, string, (int, Guid)>("Glamourer.AddDesign");
        getState = pi.GetIpcSubscriber<int, uint, (int, JObject?)>("Glamourer.GetState");
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
                if (!value.Apply)
                    continue;

                var stains = new byte[] { value.Stain1, value.Stain2 };
                var ec = setItem.InvokeFunc(objectIndex, (byte)slot, value.ItemId, stains, 0, ApplyOnce);
                if (ec != 0)
                {
                    message = $"Glamourer rejected {slot.DisplayName()} (code {ec}). {applied} slot(s) were already applied.";
                    return false;
                }

                applied++;
            }

            message = applied == 0
                ? "Nothing is marked Apply in this outfit."
                : $"Applied {applied} outfit slot(s) through Glamourer.";
            return applied > 0;
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

            if (state["Equipment"] is not JObject equipment)
            {
                message = "Glamourer returned a state without equipment data.";
                return false;
            }

            var captured = 0;
            foreach (var slot in GlamSlots.Ordered)
            {
                if (equipment[slot.ToString()] is not JObject item)
                    continue;

                var value = target.Slots[slot];
                value.ItemId = item["ItemId"]?.ToObject<ulong>() ?? 0;
                value.Stain1 = ToByte(item["Stain"]?.ToObject<int>() ?? 0);
                value.Stain2 = ToByte(item["Stain2"]?.ToObject<int>() ?? 0);
                value.Apply = true;
                captured++;
            }

            target.SourceName = "Local";
            target.SourceExternalId = null;
            target.SourceUrl = null;
            target.SourceAuthor = null;
            target.SourceRating = null;
            target.SourceLastRefreshed = null;

            message = $"Captured {captured} equipment slot(s) from your current Glamourer state.";
            return captured > 0;
        }
        catch (Exception ex)
        {
            message = $"Could not capture from Glamourer. Is it installed and enabled? {ex.Message}";
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
            message = $"Saved to Glamourer as '{outfit.Name}' ({guid}).";
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
                ["Apply"] = value.Apply,
                ["ApplyStain"] = value.Apply,
                ["ApplyCrest"] = false,
                ["Stain"] = value.Stain1,
                ["Stain2"] = value.Stain2,
            };
        }

        // Keep meta toggles explicitly non-applying as well.
        equipment["Hat"] = new JObject { ["Show"] = true, ["Apply"] = false };
        equipment["VieraEars"] = new JObject { ["Show"] = true, ["Apply"] = false };
        equipment["Visor"] = new JObject { ["IsToggled"] = false, ["Apply"] = false };
        equipment["Weapon"] = new JObject { ["Show"] = true, ["Apply"] = false };

        // Glamourer's V1 loader defaults BodyType to 1 if omitted. Supplying 0 here is
        // deliberate: every customization application flag remains false, so this design
        // changes equipment/dyes only and leaves the wearer's base avatar alone.
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

    private static JObject CustomizeValue(byte value)
        => new() { ["Value"] = value, ["Apply"] = false };

    private static byte ToByte(int value)
        => (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);
}
