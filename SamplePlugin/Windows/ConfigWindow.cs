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
        this.Size = new Vector2(300, 230);
        this.SizeCondition = ImGuiCond.Always;
        this.PositionCondition = ImGuiCond.Once;
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

        ImGui.Spacing();
        ImGui.Separator();
        int vol = this.plugin.Configuration.Volume;
        if (ImGui.SliderInt("Volume", ref vol, 0, 100, "%d%%"))
        {
            this.plugin.Configuration.Volume = vol;
            this.plugin.Configuration.Save();
            this.plugin.motivationWindow.SetVolume(vol);
        }
        ImGui.Spacing();
        if (ImGui.Button("Test Audio"))
        {
            this.plugin.motivationWindow.PlayTestAudio();
        }
    }
}