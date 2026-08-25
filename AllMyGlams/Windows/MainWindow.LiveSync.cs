namespace AllMyGlams.Windows;

public sealed partial class MainWindow
{
    private DateTime nextLiveEquipmentSyncUtc = DateTime.MinValue;

    public void TickLiveDresser()
    {
        if (!IsOpen || DateTime.UtcNow < nextLiveEquipmentSyncUtc)
            return;

        nextLiveEquipmentSyncUtc = DateTime.UtcNow.AddSeconds(1);
        if (!TryPlayerIndex(out var index))
            return;

        var live = OutfitRecord.CreateBlank("Game Look");
        if (!plugin.Glamourer.CaptureCurrent(live, index, out _))
            return;

        working.EnsureSlots();
        live.EnsureSlots();
        var changed = GlamSlots.Ordered.Any(slot =>
        {
            var current = working.Slots[slot];
            var actual = live.Slots[slot];
            return current.ItemId != actual.ItemId
                   || current.Stain1 != actual.Stain1
                   || current.Stain2 != actual.Stain2;
        });

        if (!changed)
            return;

        // The Dresser is a view of the actor, not a detached staging buffer. If anything
        // changes outside AMG, copy the live slots into the existing working record so its
        // name/source metadata can survive while the equipment display stays truthful.
        foreach (var slot in GlamSlots.Ordered)
            CopySlot(live.Slots[slot], working.Slots[slot]);

        workingDirty = true;
        if (workingNameIsSource && !working.Name.Equals("Game Look", StringComparison.OrdinalIgnoreCase))
        {
            working.Name = "Custom";
            workingNameIsSource = false;
        }

        status = "Dresser synchronized to the character's current equipment appearance.";
    }
}
