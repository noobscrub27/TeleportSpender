using Dalamud.Game.Agent;
using Dalamud.Game.Agent.AgentArgTypes;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Text;
using Lumina.Text.ReadOnly;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TeleportCounter.Windows;

namespace TeleportCounter;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IAgentLifecycle AgentLifeCycle { get; private set; } = null!;

    private const string CommandName = "/tptracker";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("TeleportCounter");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private DateTime lastTeleportCast;
    private DateTime lastTeleportAction;
    private DateTime lastGilSpent;
    private List<ReadOnlySeString> allTeleportTexts;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        MainWindow = new MainWindow(this);
        ConfigWindow = new ConfigWindow(this);

        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggles the Teleport Tracker main window."
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Chat.ChatMessage += onChatMessage;
        // PostRecieveEvent:            after starting cast (regardless of teleport success)
        // PostUpdate:                  many times a second
        // PostGameEvent:               twice per teleport, any zone change counts as a teleport
        // PreGameEvent:                seemingly the same as above
        // PostReceiveEventWithResult:  never i think?
        // PostLevelChange:             maybe on level up? not what im looking for
        AgentLifeCycle.RegisterListener(AgentEvent.PostReceiveEvent, AgentId.Teleport, onCastTeleport);
        AgentLifeCycle.RegisterListener(AgentEvent.PostGameEvent, AgentId.Teleport, onTeleport);

        allTeleportTexts = new List<ReadOnlySeString>();

        // Add a simple message to the log with level set to information
        // Use /xllog to open the log window in-game
        // Example Output: 00:57:54.959 | INF | [TeleportCounter] ===A cool log message from Sample Plugin===
        Log.Information($"==={PluginInterface.Manifest.Name} initiated!===");
    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        Chat.ChatMessage -= onChatMessage;
        
        WindowSystem.RemoveAllWindows();
        AgentLifeCycle.UnregisterListener(AgentEvent.PreGameEvent, AgentId.TelepotTown, onTeleport);
        ConfigWindow.Dispose();
        MainWindow.Dispose();

    }
    private void OnCommand(string command, string args)
    {
        // In response to the slash command, toggle the display status of our main ui
        MainWindow.Toggle();
    }
    // unique character identifier: WorldnameFirstnameLastName
    public string getCharacterIdentifer()
    {
        if (PlayerState.IsLoaded)
        {
            var characterName = PlayerState.CharacterName;
            var homeWorldName = PlayerState.HomeWorld.Value.Name.ToString();
            if (homeWorldName == null | characterName == null)
            {
                if (Configuration.DebugLogging)
                {
                    Log.Information($"==={PluginInterface.Manifest.Name}: could not get character name.===");
                }
                return "none";
            }
            return $"{characterName}@{homeWorldName}";
        }
        return "none";
    }
    private static bool validTimeDifference(DateTime now, DateTime before, float maxTimeSpan)
    {
        var timeDifference = now.Subtract(before).TotalSeconds;
        return timeDifference <= maxTimeSpan;
    }
    private void onCastTeleport(AgentEvent type, AgentArgs args)
    {
        lastTeleportCast = DateTime.Now;
        if (Configuration.DebugLogging)
        {
            Log.Information($"==={PluginInterface.Manifest.Name}: {getCharacterIdentifer()} is teleporting... (teleport check 1/3)===");
        }
    }
    public void onTeleport(AgentEvent type, AgentArgs args)
    {
        var now = DateTime.Now;
        if (validTimeDifference(now, lastTeleportCast, Configuration.teleportTime)) {
            lastTeleportAction = now;
        }
        if (Configuration.DebugLogging)
        {
            if (lastTeleportAction == now)
            {
                Log.Information($"==={PluginInterface.Manifest.Name}: {getCharacterIdentifer()} teleported! (teleport check 2/3)===");
            }
            else
            {
                Log.Information($"==={PluginInterface.Manifest.Name}: {getCharacterIdentifer()} teleported! (no teleport check)===");
            }
            
        }
    }
    private void onChatMessage(IHandleableChatMessage message)
    {
        Regex gilSpentPattern = new Regex("You spent \\d{0,3},?\\d{1,3} gil.");
        var emptySender = message.OriginalSender.IsEmpty;
        var content = message.OriginalMessage.ToString();
        if (emptySender & gilSpentPattern.IsMatch(content))
        {
            var gilSpentString = content.Replace("You spent ", "");
            gilSpentString = gilSpentString.Replace(" gil.", "");
            gilSpentString = gilSpentString.Replace(",", "");
            int gilSpent = 0;
            if (int.TryParse(gilSpentString, out gilSpent)) {
                onGilSpent(gilSpent);
                if (Configuration.DebugLogging)
                {
                    Log.Information($"==={PluginInterface.Manifest.Name}: {getCharacterIdentifer()} spent {gilSpent} gil.===");
                }
            }
            else if (Configuration.DebugLogging)
            {
                Log.Information($"==={PluginInterface.Manifest.Name}: could not determine how much gil was spent.===");
            }        
        }
    }
    private void onGilSpent(int gil)
    {
        // if the last teleport action is later than the last gil spent
        if (lastTeleportAction.CompareTo(lastGilSpent) > 0)
        {
            var now = DateTime.Now;
            if (validTimeDifference(now, lastTeleportAction, Configuration.gilSpendTime))
            {
                lastGilSpent = now;
                var character = getCharacterIdentifer();
                if (character != "none") {
                    if (Configuration.characterNames.Contains(character) == false)
                    {
                        Configuration.characterNames.Add(character);
                        Configuration.lifetimeTeleportFees.Add(0);
                    }
                    var index = Configuration.characterNames.IndexOf(character);
                    // milestones
                    int nextMilestone;
                    if (Configuration.lifetimeTeleportFees[index] < 50000)
                    {
                        nextMilestone = 50000;
                    }
                    else if (Configuration.lifetimeTeleportFees[index] < 100000)
                    {
                        nextMilestone = 100000;
                    }
                    else if (Configuration.lifetimeTeleportFees[index] < 250000)
                    {
                        nextMilestone = 250000;
                    }
                    else if (Configuration.lifetimeTeleportFees[index] < 500000)
                    {
                        nextMilestone = 500000;
                    }
                    else
                    {
                        nextMilestone = (500000 * ((Configuration.lifetimeTeleportFees[index] / 500000) + 1));
                    }
                    Configuration.lifetimeTeleportFees[index] += gil;
                    if (Configuration.lifetimeTeleportFees[index] >= nextMilestone)
                    {
                        sendEchoChat($"You've now spent {nextMilestone:n0} gil on teleports for this character.");
                    }
                    Configuration.Save();
                    var nowString = now.ToString("G");
                    var resultStringBuilder = new SeStringBuilder();
                    resultStringBuilder.Append($"{nowString}: ");
                    resultStringBuilder.AppendSetItalic(true);
                    resultStringBuilder.Append(character);
                    resultStringBuilder.AppendSetItalic(false);
                    resultStringBuilder.Append($" spent {gil:n0} gil on a teleport.");
                    var resultSeString = resultStringBuilder.ToReadOnlySeString();
                    allTeleportTexts.Insert(0, resultSeString);
                    if (Configuration.DebugLogging)
                    {
                        Log.Information($"==={PluginInterface.Manifest.Name}: Spent gil saved. (teleport check 3/3)===");
                    }
                }
            }
        }
    }

    public void sendEchoChat(string message)
    {
        Chat.Print(message, "Teleport Tracker");
    }
    public bool teleportTextExists(int index)
    {
        if (allTeleportTexts.Count > index & index >= 0)
        {
            return true;
        }
        return false;
    }
    public ReadOnlySeString getTeleportText(int index)
    {
        return allTeleportTexts[index];
    }
    public int getTeleportHistoryCount()
    {
        return Math.Max(allTeleportTexts.Count, Configuration.teleportHistoryDisplayMaximum);
    }
    public int getCharacterIndex(string character)
    {
        return Configuration.characterNames.IndexOf(character);
    }
    public int getLifetimeTeleportFeesFromIndex(int index)
    {
        return Configuration.lifetimeTeleportFees[index];
    }
    public int getTotalLifetimeTeleportFees()
    {
        int result = 0;
        foreach (int i in Configuration.lifetimeTeleportFees)
        {
            result += i;
        }
        return result;
    }
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
