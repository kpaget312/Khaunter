using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using JumpKhaunter67.Windows;
using System;

namespace JumpKhaunter67;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static ICondition Condition { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; private set; } = null!; // Fixed Chat Error
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!; // Injected TextureProvider as static

    public readonly WindowSystem WindowSystem = new("JumpKhaunter67");
    internal readonly MotivationWindow motivationWindow;
    private readonly ConfigWindow configWindow;
    public Configuration Configuration { get; private set; }

    private bool isAirborne = false;
    private DateTime lastJumpTime = DateTime.MinValue;

    public Plugin()
    {
        this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.Configuration.Initialize(PluginInterface);

        this.motivationWindow = new MotivationWindow(this);
        this.configWindow = new ConfigWindow(this);
        
        // Start with both windows closed so they don't pop up unexpectedly
        this.motivationWindow.IsOpen = false;
        this.configWindow.IsOpen = false;

        WindowSystem.AddWindow(this.motivationWindow);
        WindowSystem.AddWindow(this.configWindow);

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUI;
        Framework.Update += OnFrameworkUpdate;

        CommandManager.AddHandler("/jk67", new Dalamud.Game.Command.CommandInfo(OnCommand) { HelpMessage = "JumpKhaunter67" });
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!ClientState.IsLoggedIn) return;

        bool currentlyAirborne = Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Jumping];
        // Debounce rapid inputs: require at least 250ms between counted jumps
        var elapsedMs = (DateTime.Now - this.lastJumpTime).TotalMilliseconds;
        const double debounceMs = 75.0;

        if (currentlyAirborne && !isAirborne && elapsedMs > debounceMs)
        {
            isAirborne = true;
            lastJumpTime = DateTime.Now;
            this.Configuration.LifetimeJumps++;
            this.Configuration.Save();

            CheckMilestones(this.Configuration.LifetimeJumps);
        }
        else if (!currentlyAirborne && isAirborne)
        {
            isAirborne = false;
        }
    }

    public void ResetCounter() // Added to fix CS1061
    {
        this.Configuration.LifetimeJumps = 0;
        this.Configuration.Save();
    }

    private void CheckMilestones(int count)
    {
        if (count == 67 || (count > 67 && (count - 67) % 300 == 0))
        {
            this.configWindow.IsOpen = false;
            this.motivationWindow.TriggerMilestone(count);
        }
    }

    private void OnCommand(string command, string args) => ToggleConfigUI();
    private void ToggleConfigUI() => this.configWindow.IsOpen = !this.configWindow.IsOpen;
    private void DrawUI() => WindowSystem.Draw();

    public void Dispose()
    {
        CommandManager.RemoveHandler("/jk67");
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUI;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUI;
        try { this.motivationWindow?.Dispose(); } catch { }
        try { this.configWindow?.Dispose(); } catch { }
        WindowSystem.RemoveAllWindows();
    }
}