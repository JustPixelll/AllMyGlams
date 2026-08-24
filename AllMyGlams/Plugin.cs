using AllMyGlams.Services;
using AllMyGlams.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AllMyGlams;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/allmyglams";
    private const string ShortCommandName = "/amg";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; }
    public GameDataService GameData { get; }
    public GlamourerIpc Glamourer { get; }
    public PenumbraIpc Penumbra { get; }

    public readonly WindowSystem WindowSystem = new("AllMyGlams");
    private readonly MainWindow mainWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        GameData = new GameDataService(DataManager);
        Glamourer = new GlamourerIpc(PluginInterface);
        Penumbra = new PenumbraIpc(PluginInterface);

        mainWindow = new MainWindow(this);
        WindowSystem.AddWindow(mainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) { HelpMessage = "Open All My Glams." });
        CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand) { HelpMessage = "Open All My Glams." });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenMainUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenMainUi;
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(ShortCommandName);
        WindowSystem.RemoveAllWindows();
        mainWindow.Dispose();
    }

    public bool TryGetLocalPlayerIndex(out int objectIndex)
    {
        var player = ObjectTable.LocalPlayer;
        if (player is null)
        {
            objectIndex = -1;
            return false;
        }

        objectIndex = player.ObjectIndex;
        return objectIndex >= 0;
    }

    private void OnCommand(string command, string args) => mainWindow.Toggle();
    private void OpenMainUi() => mainWindow.IsOpen = true;
}
