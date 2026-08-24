using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace AllMyGlams.Services;

public sealed class GameDataService
{
    private readonly Dictionary<ulong, ItemRecord> itemsById = [];
    private readonly Dictionary<string, List<ItemRecord>> itemsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ulong>> englishItemIdsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte> englishStainIdsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ItemRecord> items = [];
    private readonly List<StainRecord> stains = [];
    private readonly HashSet<string> wearableNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<GlamSlot, HashSet<ulong>> slotCompatibility = GlamSlots.Ordered.ToDictionary(x => x, _ => new HashSet<ulong>());

    public IReadOnlyList<StainRecord> Stains => stains;

    public GameDataService(IDataManager data)
    {
        var localItems = data.GetExcelSheet<Item>();
        foreach (var item in localItems)
        {
            if (item.RowId == 0)
                continue;

            var name = item.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (item.EquipSlotCategory.ValueNullable is not { } category)
                continue;

            var record = new ItemRecord(item.RowId, name, item.Icon, item.EquipSlotCategory.RowId);
            items.Add(record);
            itemsById[item.RowId] = record;
            wearableNames.Add(name);
            if (!itemsByName.TryGetValue(name, out var named))
                itemsByName[name] = named = [];
            named.Add(record);

            foreach (var slot in GlamSlots.Ordered)
                if (Fits(category, slot))
                    slotCompatibility[slot].Add(item.RowId);
        }

        // Eorzea Collection uses English canonical FFXIV names regardless of the user's
        // client language. Keep a second lookup by RowId so imports also work on DE/FR/JP clients.
        foreach (var item in data.GetExcelSheet<Item>(ClientLanguage.English))
        {
            if (item.RowId == 0)
                continue;

            var englishName = item.Name.ToString().Trim();
            if (englishName.Length == 0)
                continue;

            if (!englishItemIdsByName.TryGetValue(englishName, out var ids))
                englishItemIdsByName[englishName] = ids = [];
            ids.Add(item.RowId);
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

        foreach (var stain in data.GetExcelSheet<Stain>(ClientLanguage.English))
        {
            if (stain.RowId == 0 || stain.RowId > byte.MaxValue)
                continue;

            var name = stain.Name.ToString().Trim();
            if (name.Length > 0)
                englishStainIdsByName[name] = (byte)stain.RowId;
        }

        stains.Sort((a, b) => a.Id == 0 ? -1 : b.Id == 0 ? 1 : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
    }

    public ItemRecord? GetItem(ulong itemId)
        => itemId == 0 ? null : itemsById.GetValueOrDefault(itemId);

    public IReadOnlyList<ItemRecord> GetItemsByName(string itemName)
        => itemsByName.TryGetValue(itemName.Trim(), out var found) ? found : [];

    public ItemRecord? ResolveEnglishItem(string itemName, GlamSlot slot)
    {
        if (!englishItemIdsByName.TryGetValue(itemName.Trim(), out var ids))
            return null;

        foreach (var id in ids)
            if (ItemFitsSlot(id, slot) && itemsById.TryGetValue(id, out var record))
                return record;

        return null;
    }

    public bool ItemFitsSlot(ulong itemId, GlamSlot slot)
        => slotCompatibility.TryGetValue(slot, out var ids) && ids.Contains(itemId);

    public StainRecord GetStain(byte id)
        => stains.FirstOrDefault(x => x.Id == id) ?? new StainRecord(id, id == 0 ? "None" : $"Dye #{id}");

    public bool TryResolveEnglishStain(string stainName, out byte id)
    {
        stainName = stainName.Trim();
        if (stainName.Length == 0
            || stainName.Equals("Undyed", StringComparison.OrdinalIgnoreCase)
            || stainName.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            id = 0;
            return true;
        }

        return englishStainIdsByName.TryGetValue(stainName, out id);
    }

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

            if (!ItemFitsSlot(item.Id, slot))
                continue;

            result.Add(item);
            if (result.Count >= limit)
                break;
        }

        return result;
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
