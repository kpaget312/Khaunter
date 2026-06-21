using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using JumpKhaunter67.Windows;
using System;
using System.Numerics;

namespace JumpKhaunter67;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static ICondition Condition { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
    public readonly WindowSystem WindowSystem = new("JumpKhaunter67");
    internal readonly MotivationWindow motivationWindow;
    private readonly ConfigWindow configWindow;
    public Configuration Configuration { get; private set; }

    private static IObjectTable? ObjectTable;
    private bool wasAirborneLastFrame = false;
    private DateTime lastJumpTime = DateTime.MinValue;
    private float lastPlayerY = 0f;
    private bool wasAscending = false;

    public Plugin()
    {
        this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.Configuration.Initialize(PluginInterface);

        try { ObjectTable = (IObjectTable?)PluginInterface.GetService(typeof(IObjectTable)); } catch { ObjectTable = null; }

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

        // Rising edge of the Jumping condition flag (normal detection path)
        bool flagRisingEdge = currentlyAirborne && !this.wasAirborneLastFrame;
        this.wasAirborneLastFrame = currentlyAirborne;

        var elapsedMs = (DateTime.Now - this.lastJumpTime).TotalMilliseconds;
        const double debounceMs = 75.0;

        bool jumpDetected = flagRisingEdge && elapsedMs > debounceMs;

        // Y-velocity tracking: works with ObjectTable when available, skipped when null
        if (ObjectTable != null)
        {
            var player = ObjectTable[0];
            float currentY = player?.Position.Y ?? this.lastPlayerY;
            float yDiff = currentY - this.lastPlayerY;
            this.lastPlayerY = currentY;

            bool isAscending = yDiff > 0.03f;
            bool newAscension = isAscending && !this.wasAscending;
            if (isAscending)
                this.wasAscending = true;
            else if (yDiff < -0.03f || !currentlyAirborne)
                this.wasAscending = false;

            if (!jumpDetected && newAscension && currentlyAirborne && elapsedMs > debounceMs)
                jumpDetected = true;
        }
        else
        {
            // Fallback: rising-edge only, reset Y state on landing
            if (!currentlyAirborne)
                this.wasAscending = false;
        }

        if (jumpDetected)
        {
            lastJumpTime = DateTime.Now;
            this.Configuration.LifetimeJumps++;
            this.Configuration.Save();

            CheckMilestones(this.Configuration.LifetimeJumps);
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