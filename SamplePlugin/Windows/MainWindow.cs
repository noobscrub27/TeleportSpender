using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using Lumina.Text;
using Lumina.Text.ReadOnly;
using System;
using System.Numerics;

namespace TeleportCounter.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly ReadOnlySeString noTeleportText;
    private readonly Plugin plugin;

    // We give this window a hidden ID using ##.
    // The user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public MainWindow(Plugin plugin)
        : base("Teleport Tracker##noobscrubTTMainWindowID", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(450, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        SeStringBuilder noTeleportTextBuilder = new SeStringBuilder();
        noTeleportTextBuilder
            .AppendSetItalic(true)
            .Append("You have not teleported this session.");
        noTeleportText = noTeleportTextBuilder.ToReadOnlySeString();
        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var character = plugin.getCharacterIdentifer();
        if (character != "none")
        {
            int characterIndex = plugin.getCharacterIndex(character);
            if (characterIndex >= 0)
            {
                ImGui.Text($"While logged in as {character}, you have spent {plugin.getLifetimeTeleportFeesFromIndex(characterIndex):n0} gil on teleports.");
            }
        }
        ImGui.Text($"On this account, you've spent {plugin.getTotalLifetimeTeleportFees():n0} gil on teleports.");
        ImGui.Spacing();

        // Normally a BeginChild() would have to be followed by an unconditional EndChild(),
        // ImRaii takes care of this after the scope ends.
        // This works for all ImGui functions that require specific handling, examples are BeginTable() or Indent().
        using (var child = ImRaii.Child("SomeChildWithAScrollbar", Vector2.Zero, true))
        {
            // Check if this child is drawing
            if (child.Success)
            {
                int displayedMessages = 0;
                int historyCount = plugin.getTeleportHistoryCount();
                if (historyCount > 0)
                {
                    for (int i = 0; i < historyCount; i++)
                    {
                        if (plugin.teleportTextExists(i))
                        {
                            ImGuiHelpers.SeStringWrapped(plugin.getTeleportText(i));
                            displayedMessages++;
                        }
                    }
                }
                if (displayedMessages == 0)
                {
                    ImGuiHelpers.SeStringWrapped(noTeleportText);
                }
            }
        }
    }
}
