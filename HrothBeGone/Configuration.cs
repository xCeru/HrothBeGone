using System;
using Dalamud.Configuration;

namespace HrothBeGone;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public bool RandomRace { get; set; } = false;
    public byte TargetRace { get; set; } = 1; // Hyur
    public bool IncludeLocalPlayer { get; set; } = false;
    public int ScanIntervalMs { get; set; } = 1000;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
