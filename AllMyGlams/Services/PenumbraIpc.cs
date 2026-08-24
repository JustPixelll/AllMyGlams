using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace AllMyGlams.Services;

public sealed class PenumbraIpc
{
    private readonly ICallGateSubscriber<Dictionary<string, string>> getModList;
    private readonly ICallGateSubscriber<string, string, Dictionary<string, object?>> getChangedItems;
    private readonly ICallGateSubscriber<int, (bool ObjectValid, bool IndividualSet, (Guid Id, string Name) EffectiveCollection)> getCollectionForObject;
    private readonly ICallGateSubscriber<Guid, bool, bool, int, (int, Dictionary<string, (bool Enabled, int Priority, Dictionary<string, List<string>> Settings, bool Inherited, bool Temporary)>?)> getAllModSettings;
    private readonly ICallGateSubscriber<Guid, string, string, bool, int> trySetMod;
    private readonly ICallGateSubscriber<Guid, string, string, int, int> trySetModPriority;
    private readonly ICallGateSubscriber<Guid, string, string, string, IReadOnlyList<string>, int> trySetModSettings;

    public Guid EffectiveCollectionId { get; private set; }
    public string EffectiveCollectionName { get; private set; } = "Unknown";
    public List<PenumbraModEntry> Mods { get; } = [];

    public PenumbraIpc(IDalamudPluginInterface pi)
    {
        getModList = pi.GetIpcSubscriber<Dictionary<string, string>>("Penumbra.GetModList");
        getChangedItems = pi.GetIpcSubscriber<string, string, Dictionary<string, object?>>("Penumbra.GetChangedItems.V5");
        getCollectionForObject = pi.GetIpcSubscriber<int, (bool ObjectValid, bool IndividualSet, (Guid Id, string Name) EffectiveCollection)>("Penumbra.GetCollectionForObject.V5");
        getAllModSettings = pi.GetIpcSubscriber<Guid, bool, bool, int, (int, Dictionary<string, (bool, int, Dictionary<string, List<string>>, bool, bool)>?)>("Penumbra.GetAllModSettings");
        trySetMod = pi.GetIpcSubscriber<Guid, string, string, bool, int>("Penumbra.TrySetMod.V5");
        trySetModPriority = pi.GetIpcSubscriber<Guid, string, string, int, int>("Penumbra.TrySetModPriority.V5");
        trySetModSettings = pi.GetIpcSubscriber<Guid, string, string, string, IReadOnlyList<string>, int>("Penumbra.TrySetModSettings.V5");
    }

    public bool Refresh(int objectIndex, GameDataService gameData, out string message)
    {
        try
        {
            var collection = getCollectionForObject.InvokeFunc(objectIndex);
            if (!collection.ObjectValid)
            {
                message = "Penumbra could not resolve a collection for the local player.";
                return false;
            }

            EffectiveCollectionId = collection.EffectiveCollection.Id;
            EffectiveCollectionName = collection.EffectiveCollection.Name;

            var modList = getModList.InvokeFunc();
            var (settingsEc, settings) = getAllModSettings.InvokeFunc(EffectiveCollectionId, false, false, 0);
            if (settingsEc is not (0 or 1))
                settings = null;

            Mods.Clear();
            foreach (var (directory, name) in modList)
            {
                Dictionary<string, object?> changed;
                try
                {
                    changed = getChangedItems.InvokeFunc(directory, name) ?? [];
                }
                catch
                {
                    changed = [];
                }

                var changedNames = changed.Keys
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                var entry = new PenumbraModEntry
                {
                    Directory = directory,
                    Name = string.IsNullOrWhiteSpace(name) ? directory : name,
                    ChangedItems = changedNames,
                    AffectsEquipment = changedNames.Any(gameData.IsWearableItemName),
                };

                if (settings is not null && settings.TryGetValue(directory, out var current))
                {
                    entry.Enabled = current.Enabled;
                    entry.Priority = current.Priority;
                    entry.Settings = current.Settings.ToDictionary(x => x.Key, x => x.Value.ToList(), StringComparer.Ordinal);
                    entry.Inherited = current.Inherited;
                    entry.Temporary = current.Temporary;
                }

                Mods.Add(entry);
            }

            Mods.Sort((a, b) =>
            {
                var equipment = b.AffectsEquipment.CompareTo(a.AffectsEquipment);
                return equipment != 0 ? equipment : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });

            message = $"Loaded {Mods.Count} Penumbra mod(s) for '{EffectiveCollectionName}'.";
            return true;
        }
        catch (Exception ex)
        {
            Mods.Clear();
            EffectiveCollectionId = Guid.Empty;
            EffectiveCollectionName = "Unavailable";
            message = $"Could not talk to Penumbra. Is it installed and enabled? {ex.Message}";
            return false;
        }
    }

    public bool SetEnabled(PenumbraModEntry mod, bool enabled, out string message)
    {
        if (EffectiveCollectionId == Guid.Empty)
        {
            message = "Refresh Penumbra first so AllMyGlams knows the active collection.";
            return false;
        }

        try
        {
            var ec = trySetMod.InvokeFunc(EffectiveCollectionId, mod.Directory, mod.Name, enabled);
            if (ec is not (0 or 1))
            {
                message = $"Penumbra rejected the enabled-state change (code {ec}).";
                return false;
            }

            mod.Enabled = enabled;
            mod.Inherited = false;
            message = $"{(enabled ? "Enabled" : "Disabled")} {mod.Name}.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Could not change Penumbra mod state: {ex.Message}";
            return false;
        }
    }

    public bool SetPriority(PenumbraModEntry mod, int priority, out string message)
    {
        if (EffectiveCollectionId == Guid.Empty)
        {
            message = "Refresh Penumbra first so AllMyGlams knows the active collection.";
            return false;
        }

        try
        {
            var ec = trySetModPriority.InvokeFunc(EffectiveCollectionId, mod.Directory, mod.Name, priority);
            if (ec is not (0 or 1))
            {
                message = $"Penumbra rejected the priority change (code {ec}).";
                return false;
            }

            mod.Priority = priority;
            mod.Inherited = false;
            message = $"Set {mod.Name} priority to {priority}.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Could not change Penumbra priority: {ex.Message}";
            return false;
        }
    }

    public bool SetOptionGroup(PenumbraModEntry mod, string group, IReadOnlyList<string> options, out string message)
    {
        if (EffectiveCollectionId == Guid.Empty)
        {
            message = "Refresh Penumbra first so AllMyGlams knows the active collection.";
            return false;
        }

        try
        {
            var ec = trySetModSettings.InvokeFunc(EffectiveCollectionId, mod.Directory, mod.Name, group, options);
            if (ec is not (0 or 1))
            {
                message = $"Penumbra rejected the option change (code {ec}).";
                return false;
            }

            mod.Settings[group] = options.ToList();
            mod.Inherited = false;
            message = $"Updated {mod.Name}: {group}.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Could not change Penumbra options: {ex.Message}";
            return false;
        }
    }
}
