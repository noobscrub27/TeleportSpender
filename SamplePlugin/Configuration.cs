using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace TeleportCounter;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool DebugLogging { get; set; } = false;

    // max time between teleport cast and teleport action
    // the cast usually takes a bit over 5 seconds
    public float teleportTime = 9.0f;
    // max time between teleport action and gil spent
    // this usually takes under a second
    public float gilSpendTime = 5.0f;
    public List<string> characterNames = new List<string>();
    public List<int> lifetimeTeleportFees = new List<int>();
    public int teleportHistoryDisplayMaximum = 100;

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
