using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace JumpKhaunter67;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    public int LifetimeJumps { get; set; } = 0;
    public int Volume { get; set; } = 100;

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    public void Save()
    {
        this.pluginInterface?.SavePluginConfig(this);
    }
}