using Dalamud.Configuration;
using Dalamud.Plugin;

namespace AllMyGlams;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public List<OutfitRecord> Library { get; set; } = [];
    public bool ModsEquipmentOnly { get; set; }
    public bool ShowAccessories { get; set; } = true;

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi)
    {
        pluginInterface = pi;
        Library ??= [];
        foreach (var outfit in Library)
            outfit.EnsureSlots();
    }

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
