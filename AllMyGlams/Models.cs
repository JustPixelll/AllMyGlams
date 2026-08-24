namespace AllMyGlams;

public enum GlamSlot : byte
{
    MainHand = 1,
    OffHand = 2,
    Head = 3,
    Body = 4,
    Hands = 5,
    Legs = 7,
    Feet = 8,
    Ears = 9,
    Neck = 10,
    Wrists = 11,
    RFinger = 12,
    LFinger = 14,
}

public static class GlamSlots
{
    public static readonly GlamSlot[] Ordered =
    [
        GlamSlot.MainHand,
        GlamSlot.OffHand,
        GlamSlot.Head,
        GlamSlot.Body,
        GlamSlot.Hands,
        GlamSlot.Legs,
        GlamSlot.Feet,
        GlamSlot.Ears,
        GlamSlot.Neck,
        GlamSlot.Wrists,
        GlamSlot.RFinger,
        GlamSlot.LFinger,
    ];

    public static string DisplayName(this GlamSlot slot) => slot switch
    {
        GlamSlot.MainHand => "Main Hand",
        GlamSlot.OffHand => "Off Hand",
        GlamSlot.Head => "Head",
        GlamSlot.Body => "Body",
        GlamSlot.Hands => "Hands",
        GlamSlot.Legs => "Legs",
        GlamSlot.Feet => "Feet",
        GlamSlot.Ears => "Earrings",
        GlamSlot.Neck => "Necklace",
        GlamSlot.Wrists => "Bracelets",
        GlamSlot.RFinger => "Right Ring",
        GlamSlot.LFinger => "Left Ring",
        _ => slot.ToString(),
    };
}

public sealed class OutfitSlot
{
    public ulong ItemId { get; set; }
    public byte Stain1 { get; set; }
    public byte Stain2 { get; set; }
    public bool Apply { get; set; }

    public OutfitSlot Clone() => new()
    {
        ItemId = ItemId,
        Stain1 = Stain1,
        Stain2 = Stain2,
        Apply = Apply,
    };
}

public sealed class PenumbraModRecipe
{
    public string Directory { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int Priority { get; set; }
    public Dictionary<string, List<string>> Settings { get; set; } = new(StringComparer.Ordinal);

    public PenumbraModRecipe Clone() => new()
    {
        Directory = Directory,
        Name = Name,
        Enabled = Enabled,
        Priority = Priority,
        Settings = Settings.ToDictionary(x => x.Key, x => x.Value.ToList(), StringComparer.Ordinal),
    };
}

public sealed class OutfitRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Outfit";
    public bool Favorite { get; set; }
    public Dictionary<GlamSlot, OutfitSlot> Slots { get; set; } = CreateBlankSlots();
    public List<PenumbraModRecipe> Mods { get; set; } = [];

    // Sourced wardrobe metadata. Local outfits leave these empty.
    public string SourceName { get; set; } = "Local";
    public string? SourceExternalId { get; set; }
    public string? SourceUrl { get; set; }
    public string? SourceAuthor { get; set; }
    public int? SourceRating { get; set; }
    public DateTimeOffset? SourceLastRefreshed { get; set; }

    public static OutfitRecord CreateBlank(string name = "New Outfit") => new() { Name = name };

    public static Dictionary<GlamSlot, OutfitSlot> CreateBlankSlots()
        => GlamSlots.Ordered.ToDictionary(x => x, _ => new OutfitSlot());

    public void EnsureSlots()
    {
        Slots ??= CreateBlankSlots();
        foreach (var slot in GlamSlots.Ordered)
            Slots.TryAdd(slot, new OutfitSlot());
    }

    public OutfitRecord Clone(bool newId = true)
    {
        EnsureSlots();
        return new OutfitRecord
        {
            Id = newId ? Guid.NewGuid() : Id,
            Name = Name,
            Favorite = Favorite,
            Slots = Slots.ToDictionary(x => x.Key, x => x.Value.Clone()),
            Mods = Mods.Select(x => x.Clone()).ToList(),
            SourceName = SourceName,
            SourceExternalId = SourceExternalId,
            SourceUrl = SourceUrl,
            SourceAuthor = SourceAuthor,
            SourceRating = SourceRating,
            SourceLastRefreshed = SourceLastRefreshed,
        };
    }
}

public sealed record ItemRecord(
    ulong Id,
    string Name,
    uint IconId,
    uint EquipSlotCategoryId);

public sealed record StainRecord(byte Id, string Name);

public sealed class PenumbraModEntry
{
    public string Directory { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool Enabled { get; set; }
    public int Priority { get; set; }
    public bool Inherited { get; set; }
    public bool Temporary { get; set; }
    public bool AffectsEquipment { get; init; }
    public List<string> ChangedItems { get; init; } = [];
    public Dictionary<string, List<string>> Settings { get; set; } = new(StringComparer.Ordinal);
}
