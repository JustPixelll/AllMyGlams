namespace AllMyGlams.Windows;

public sealed partial class MainWindow
{
    public void RefreshLiveDresserOnly()
    {
        if (!TryPlayerIndex(out var index))
            return;

        CaptureLiveLook("Game Look", index);
        nextLiveEquipmentSyncUtc = DateTime.UtcNow.AddMilliseconds(350);
    }
}
