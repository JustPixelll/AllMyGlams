namespace AllMyGlams.Windows;

public sealed partial class MainWindow
{
    private DateTime nextLiveEquipmentSyncUtc = DateTime.MinValue;

    public void TickLiveDresser()
    {
        if (!IsOpen || workingDirty || DateTime.UtcNow < nextLiveEquipmentSyncUtc)
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

        // The character changed outside the Dresser (gearset swap, another Glamourer
        // action, etc.). Follow the live actor unless the user has unsaved editor changes.
        live.Name = "Game Look";
        live.Mods = CaptureActiveModRecipes();
        working = live;
        workingNameIsSource = true;
        workingDirty = false;
        status = "Detected an external equipment change; Dresser synchronized to the current character look.";
    }
}
