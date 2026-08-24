using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace AllMyGlams.Services;

public sealed class GameDataService
{
    private readonly Dictionary<ulong, ItemRecord> itemsById = [];
    private readonly List<ItemRecord> items = [];
    private readonly List<StainRecord> stains = [];
    private readonly HashSet<string> wearableNames = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<StainRecord> Stains => stains;

    public GameDataService(IDataManager data)
    {
        foreach (var item in data.GetExcelSheet<Item>())
        {
            if (item.RowId == 0)
                continue;

            var name = item.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var category = item.EquipSlotCategory.ValueNullable;
            if (category is null)
                continue;

            var record = new ItemRecord(item.RowId, name, item.Icon, item.EquipSlotCategory.RowId);
            items.Add(record);
            itemsById[item.RowId] = record;
            wearableNames.Add(name);
        }

        items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        stains.Add(new StainRecord(0, "None"));
        foreach (var stain in data.GetExcelSheet<Stain>())
        {
            if (stain.RowId == 0 || stain.RowId > byte.MaxValue)
                continue;

            var name = stain.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            stains.Add(new StainRecord((byte)stain.RowId, name));
        }

        stains.Sort((a, b) => a.Id == 0 ? -1 : b.Id == 0 ? 1 : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
    }

    public ItemRecord? GetItem(ulong itemId)
        => itemsById.GetValueOrDefault(itemId);

    public StainRecord GetStain(byte id)
        => stains.FirstOrDefault(x => x.Id == id) ?? new StainRecord(id, id == 0 ? "None" : $"Dye #{id}");

    public bool IsWearableItemName(string changedItemName)
        => wearableNames.Contains(changedItemName.Trim());

    public List<ItemRecord> SearchItems(GlamSlot slot, string query, int limit = 150)
    {
        query = query.Trim();
        var result = new List<ItemRecord>(Math.Min(limit, 150));

        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(query) && !item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                continue;

            // Fetching the row again would be wasteful; EquipSlotCategoryId is retained so callers
            // can inspect it later, while this fast slot predicate is populated from the sheet below.
            // We use the pre-built compatibility cache instead.
            if (!slotCompatibility.TryGetValue(slot, out var ids) || !ids.Contains(item.Id))
                continue;

            result.Add(item);
            if (result.Count >= limit)
                break;
        }

        return result;
    }

    private readonly Dictionary<GlamSlot, HashSet<ulong>> slotCompatibility = GlamSlots.Ordered.ToDictionary(x => x, _ => new HashSet<ulong>());

    public void BuildSlotCompatibility(IDataManager data)
    {
        foreach (var item in data.GetExcelSheet<Item>())
        {
            if (item.RowId == 0 || item.EquipSlotCategory.ValueNullable is not { } category)
                continue;

            foreach (var slot in GlamSlots.Ordered)
                if (Fits(category, slot))
                    slotCompatibility[slot].Add(item.RowId);
        }
    }

    private static bool Fits(EquipSlotCategory category, GlamSlot slot) => slot switch
    {
        GlamSlot.MainHand => category.MainHand != 0,
        GlamSlot.OffHand => category.OffHand != 0,
        GlamSlot.Head => category.Head != 0,
        GlamSlot.Body => category.Body != 0,
        GlamSlot.Hands => category.Gloves != 0,
        GlamSlot.Legs => category.Legs != 0,
        GlamSlot.Feet => category.Feet != 0,
        GlamSlot.Ears => category.Ears != 0,
        GlamSlot.Neck => category.Neck != 0,
        GlamSlot.Wrists => category.Wrists != 0,
        GlamSlot.RFinger => category.FingerR != 0 || category.FingerL != 0,
        GlamSlot.LFinger => category.FingerL != 0 || category.FingerR != 0,
        _ => false,
    };
}
