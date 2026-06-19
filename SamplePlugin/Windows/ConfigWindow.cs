using JumpKhaunter67;
using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace JumpKhaunter67.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin) : base(
        "JumpKhaunter67 Configuration",
        ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.Size = new Vector2(300, 150);
        this.SizeCondition = ImGuiCond.Always;
        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextColored(new Vector4(1, 0.84f, 0, 1), "JumpKhaunter67 Settings");
        ImGui.Separator();
        ImGui.Text($"Total Lifetime Jumps: {this.plugin.Configuration.LifetimeJumps}");
        ImGui.Spacing();
        if (ImGui.Button("Reset Jump Counter")) { this.plugin.ResetCounter(); }
    }
}