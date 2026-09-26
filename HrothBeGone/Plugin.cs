using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using CharacterBaseStruct = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CharacterBase;
using CharacterStruct = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using FFXIVObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;
using HrothBeGone.Windows;

namespace HrothBeGone;

public sealed unsafe class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;

    private const string CommandName = "/hrothbegone";
    private const int CustomizeByteCount = 26;
    private const byte HrothgarRace = 7;
    private const ulong CharaWindowActorId = 0xE0000000;
    private const double RedrawDelayMs = 150.0;

    // Race IDs: 1 Hyur, 2 Elezen, 3 Lalafell, 4 Miqo'te, 5 Roegadyn, 6 Au Ra, 7 Hrothgar, 8 Viera.
    private static readonly byte[] ReplaceableRaces = { 1, 2, 3, 4, 5, 6, 8 };

    // Creator hairstyle counts per race, used to clamp Hrothgar hair values
    // into a range every target race can render (per PotatoFamine2).
    private static readonly Dictionary<byte, int> RaceHairCounts = new()
    {
        [1] = 13, // Hyur
        [2] = 12, // Elezen
        [3] = 13, // Lalafell
        [4] = 12, // Miqo'te
        [5] = 13, // Roegadyn
        [6] = 12, // Au Ra
        [8] = 17, // Viera
    };

    // Racial starter gear model ids by race (index race-1), gender column
    // 0 = male, 1 = female. -1 = no known equivalent.
    private static readonly short[,] RaceStarterGear =
    {
        { 84, 85 },    // Hyur
        { 86, 87 },    // Elezen
        { 92, 93 },    // Lalafell
        { 88, 89 },    // Miqo'te
        { 90, 91 },    // Roegadyn
        { 257, 258 },  // Au Ra
        { 597, -1 },   // Hrothgar (female piece not mapped)
        { -1, 581 },   // Viera (male piece not mapped)
    };

    private static readonly HashSet<ushort> RacialStarterGearModels = new()
        { 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 257, 258, 597, 581 };

    public Configuration Configuration { get; }
    public WindowSystem WindowSystem { get; } = new("HrothBeGone");
    private readonly ConfigWindow configWindow;

    private sealed class OriginalAppearance
    {
        public byte[] Customize { get; } = new byte[CustomizeByteCount];
        public ulong[] Equipment { get; } = new ulong[10];
        public byte ReplacementRace { get; set; }
    }

    private readonly Dictionary<ulong, OriginalAppearance> originals = new();
    private readonly Dictionary<ulong, DateTime> pendingShows = new();
    private readonly Random random = new();
    private DateTime nextScan = DateTime.MinValue;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.ScanIntervalMs = Math.Clamp(Configuration.ScanIntervalMs, 250, 10000);

        configWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(configWindow);

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open HrothBeGone configuration."
        });

        Framework.Update += OnFrameworkUpdate;
        ClientState.TerritoryChanged += OnTerritoryChanged;

        Log.Information("[HrothBeGone] Loaded.");
    }

    public void Dispose()
    {
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;
        CommandManager.RemoveHandler(CommandName);

        RevertAll();
        FlushPendingShows();

        WindowSystem.RemoveAllWindows();
    }

    private void OnCommand(string command, string args) => ToggleConfigUi();

    public void ToggleConfigUi() => configWindow.Toggle();

    private void OnTerritoryChanged(uint territory)
    {
        // Actors from the previous zone are gone and their object ids get
        // reused; dropping the maps keeps restore data off unrelated actors.
        originals.Clear();
        pendingShows.Clear();
        nextScan = DateTime.MinValue;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        ProcessPendingShows();

        if (!Configuration.Enabled || DateTime.UtcNow < nextScan)
            return;

        nextScan = DateTime.UtcNow.AddMilliseconds(Configuration.ScanIntervalMs);
        Scan();
    }

    private void ProcessPendingShows()
    {
        if (pendingShows.Count == 0)
            return;

        var now = DateTime.UtcNow;
        List<ulong>? due = null;
        foreach (var pair in pendingShows)
        {
            if (now >= pair.Value)
                (due ??= new List<ulong>()).Add(pair.Key);
        }

        if (due == null)
            return;

        foreach (var id in due)
        {
            pendingShows.Remove(id);
            var obj = ObjectTable.SearchById(id);
            if (obj != null)
                SetHidden(obj, false);
        }
    }

    private void FlushPendingShows()
    {
        // Called from Dispose after the framework handler is detached: finish
        // any in-flight re-show cycle on a later framework tick so no actor is
        // left hidden when the plugin is unloaded. If the deferred work cannot
        // be scheduled, re-show immediately instead.
        if (pendingShows.Count == 0)
            return;

        var ids = pendingShows.Keys.ToArray();
        pendingShows.Clear();

        try
        {
            Framework.RunOnTick(
                () => ApplyVisibility(ids, false),
                TimeSpan.FromMilliseconds(RedrawDelayMs + 50));
        }
        catch
        {
            ApplyVisibility(ids, false);
        }
    }

    private static void ApplyVisibility(IReadOnlyList<ulong> ids, bool hidden)
    {
        foreach (var id in ids)
        {
            var obj = ObjectTable.SearchById(id);
            if (obj != null)
                SetHidden(obj, hidden);
        }
    }

    private void Scan()
    {
        // ObjectTable must be consumed on the framework/main thread.
        foreach (var obj in ObjectTable)
        {
            if (obj == null || obj.Address == nint.Zero)
                continue;

            var go = (GameObjectStruct*)obj.Address;
            var kind = go->ObjectKind;
            if (kind != FFXIVObjectKind.Pc &&
                kind != FFXIVObjectKind.BattleNpc &&
                kind != FFXIVObjectKind.EventNpc &&
                kind != FFXIVObjectKind.Retainer)
                continue;

            if (!Configuration.IncludeLocalPlayer &&
                (IsLocalPlayer(obj) || obj.GameObjectId == CharaWindowActorId))
                continue;

            try
            {
                var character = (CharacterStruct*)obj.Address;
                if (character == null || !SupportsHumanCustomize(character))
                    continue;

                ref var customize = ref character->DrawData.CustomizeData;
                bool tracked = originals.TryGetValue(obj.GameObjectId, out var prior);

                if (customize.Race == HrothgarRace)
                {
                    // New Hrothgar actor, or a tracked one the game reset
                    // (respawn / re-entry): apply the replacement.
                    if (tracked)
                        originals.Remove(obj.GameObjectId);

                    byte targetRace = Configuration.RandomRace
                        ? ReplaceableRaces[random.Next(ReplaceableRaces.Length)]
                        : Configuration.TargetRace;

                    ApplyReplacement(obj, character, targetRace);
                }
                else if (tracked && prior != null && customize.Race != prior.ReplacementRace)
                {
                    // The actor no longer shows our replacement; forget it so a
                    // later Hrothgar reusing this id is handled cleanly.
                    originals.Remove(obj.GameObjectId);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"[HrothBeGone] Failed to replace '{obj.Name}'.");
            }
        }
    }

    private void ApplyReplacement(IGameObject obj, CharacterStruct* character, byte targetRace)
    {
        ref var customize = ref character->DrawData.CustomizeData;

        var original = new OriginalAppearance();
        byte* raw = (byte*)&character->DrawData.CustomizeData;
        for (int i = 0; i < CustomizeByteCount; i++)
            original.Customize[i] = raw[i];
        for (int i = 0; i < 10; i++)
            original.Equipment[i] = character->DrawData.EquipmentModelIds[i].Value;
        original.ReplacementRace = targetRace;

        byte gender = customize.Sex;

        // Race + clan. Clan parity (odd = first clan, even = second clan of the
        // race) is preserved, keeping the race/clan pair valid for the gender.
        customize.Race = targetRace;
        customize.Tribe = (byte)(targetRace * 2 - customize.Tribe % 2);

        // Clamp Hrothgar-specific values into ranges valid for the target race:
        // an out-of-range face/hair value produces a decapitated or broken model.
        customize.BodyType = (byte)(customize.BodyType % 2);
        customize.Face = customize.Face == 0 ? (byte)1 : (byte)((customize.Face - 1) % 4 + 1);
        customize.Hairstyle = (byte)(customize.Hairstyle % RaceHairCounts[targetRace] + 1);
        customize.TailShape = targetRace is 4 or 6 or 8 ? (byte)1 : (byte)0;

        originals[obj.GameObjectId] = original;
        MapRacialStarterGear(character, targetRace, gender);

        // Hide now and re-show after a short delay. Toggling visibility across
        // frames makes the game rebuild the actor's draw object from
        // Character.DrawData, which applies the new race. Human.UpdateDrawData
        // refuses race changes, and a same-frame toggle is a no-op.
        SetHidden(obj, true);
        pendingShows[obj.GameObjectId] = DateTime.UtcNow.AddMilliseconds(RedrawDelayMs);

        Log.Information($"[HrothBeGone] Replaced '{obj.Name}' Hrothgar -> {RaceName(targetRace)}.");
    }

    private static void MapRacialStarterGear(CharacterStruct* character, byte targetRace, byte gender)
    {
        // Racial starter gear (e.g. Hempen pieces) is race-locked and renders
        // invisible on another race's model; remap it to the target race's piece.
        int col = gender == 1 ? 1 : 0;
        short replacement = RaceStarterGear[targetRace - 1, col];
        if (replacement < 0)
            return;

        var equipment = character->DrawData.EquipmentModelIds;
        for (int i = 0; i < equipment.Length; i++)
        {
            ref var slot = ref equipment[i];
            if (RacialStarterGearModels.Contains(slot.Id))
            {
                slot.Id = (ushort)replacement;
                slot.Variant = 1;
            }
        }
    }

    private void RevertAll()
    {
        foreach (var obj in ObjectTable)
        {
            if (obj == null || obj.Address == nint.Zero)
                continue;

            if (!originals.TryGetValue(obj.GameObjectId, out var original))
                continue;

            try
            {
                var character = (CharacterStruct*)obj.Address;
                byte* raw = (byte*)&character->DrawData.CustomizeData;
                for (int i = 0; i < CustomizeByteCount; i++)
                    raw[i] = original.Customize[i];
                for (int i = 0; i < 10; i++)
                    character->DrawData.EquipmentModelIds[i].Value = original.Equipment[i];

                SetHidden(obj, true);
                pendingShows[obj.GameObjectId] = DateTime.UtcNow.AddMilliseconds(RedrawDelayMs);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"[HrothBeGone] Failed to restore '{obj.Name}'.");
            }
        }

        originals.Clear();
    }

    private bool IsLocalPlayer(IGameObject obj)
        => ObjectTable.LocalPlayer != null &&
           ObjectTable.LocalPlayer.GameObjectId == obj.GameObjectId;

    private static void SetHidden(IGameObject obj, bool hidden)
        => SetHidden((GameObjectStruct*)obj.Address, hidden);

    private static void SetHidden(GameObjectStruct* go, bool hidden)
    {
        if (go == null)
            return;

        if (hidden)
            go->RenderFlags |= VisibilityFlags.Model;
        else
            go->RenderFlags &= ~VisibilityFlags.Model;

        // A ridden mount occupies the object slot after its rider; toggle it
        // too so it does not float while the rider is hidden.
        if (go->ObjectKind == FFXIVObjectKind.Pc)
        {
            int next = go->ObjectIndex + 1;
            if (next >= 0 && next < ObjectTable.Length)
            {
                var neighbor = ObjectTable[next];
                if (neighbor != null && neighbor.Address != nint.Zero)
                {
                    var n = (GameObjectStruct*)neighbor.Address;
                    if (n->ObjectKind == FFXIVObjectKind.Mount)
                    {
                        if (hidden)
                            n->RenderFlags |= VisibilityFlags.Model;
                        else
                            n->RenderFlags &= ~VisibilityFlags.Model;
                    }
                }
            }
        }
    }

    private static bool SupportsHumanCustomize(CharacterStruct* character)
    {
        var characterBase = GetCharacterBaseDrawObject(character);
        return characterBase != null &&
               characterBase->GetModelType() == CharacterBaseStruct.ModelType.Human;
    }

    private static CharacterBaseStruct* GetCharacterBaseDrawObject(CharacterStruct* character)
    {
        if (character == null || character->DrawObject == null)
            return null;

        return character->DrawObject->GetObjectType() == FFXIVClientStructs.FFXIV.Client.Graphics.Scene.ObjectType.CharacterBase
            ? (CharacterBaseStruct*)character->DrawObject
            : null;
    }

    public void SetEnabled(bool enabled)
    {
        if (Configuration.Enabled == enabled)
            return;

        if (!enabled)
            RevertAll();

        Configuration.Enabled = enabled;
        Configuration.Save();
        nextScan = DateTime.MinValue;
    }

    public void SetRandomRace(bool randomRace)
    {
        if (Configuration.RandomRace == randomRace)
            return;

        RevertAll();
        Configuration.RandomRace = randomRace;
        Configuration.Save();
        nextScan = DateTime.MinValue;
    }

    public void SetTargetRace(byte race)
    {
        if (!RaceHairCounts.ContainsKey(race) || Configuration.TargetRace == race)
            return;

        RevertAll();
        Configuration.TargetRace = race;
        Configuration.Save();
        nextScan = DateTime.MinValue;
    }

    public void SetIncludeLocalPlayer(bool include)
    {
        if (Configuration.IncludeLocalPlayer == include)
            return;

        if (!include)
            RevertAll();

        Configuration.IncludeLocalPlayer = include;
        Configuration.Save();
        nextScan = DateTime.MinValue;
    }

    public static string RaceName(byte race) => race switch
    {
        1 => "Hyur",
        2 => "Elezen",
        3 => "Lalafell",
        4 => "Miqo'te",
        5 => "Roegadyn",
        6 => "Au Ra",
        8 => "Viera",
        _ => "Unknown",
    };

    public static IReadOnlyList<byte> AvailableRaces => ReplaceableRaces;
    public bool IsEnabled => Configuration.Enabled;
}
