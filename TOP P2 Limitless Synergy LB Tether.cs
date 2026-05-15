using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Bindings.ImGui;
using ECommons;
using ECommons.Configuration;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.Hooks;
using ECommons.ImGuiMethods;
using ECommons.MathHelpers;
using Splatoon.SplatoonScripting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using ECommons.DalamudServices.Legacy;

namespace SplatoonScriptsOfficial.Duties.Endwalker.The_Omega_Protocol;

public class TOP_P2_Limitless_Synergy_LB_Tether : SplatoonScript
{
    private const uint LimitlessSynergyCast = 31544;
    private const uint OmegaMBladeDanceCast = 31540;
    private const uint OmegaFBladeDanceCast = 31541;
    private const uint OmegaMNameId = 7633;
    private const uint OmegaFNameId = 7634;
    private const float CenterX = 100f;
    private const float CenterZ = 100f;
    private const float TetherSourceDistance = 22f;
    private const float TetherSourceDistanceTolerance = 5f;
    private const float DropAdditionalRotation = 5.2359877f;
    private const uint TakenTetherColor = 3372220160;
    private const uint UntakenTetherColor = 3355508735;
    private const uint DropColor = 3355508509;

    public override Metadata? Metadata => new(1, "kudry + Codex");
    public override HashSet<uint> ValidTerritories => [1122];

    private readonly Dictionary<uint, uint> _tethers = [];
    private bool _allowed = false;
    private Config Conf => Controller.GetConfig<Config>();

    public override void OnSetup()
    {
        Controller.RegisterElement("TetherLine", new(2) { Enabled = false, radius = 0f, thicc = 8f, color = UntakenTetherColor });
        Controller.RegisterElementFromCode("DropGuide", """
        {
            "Name":"",
            "type":1,
            "Enabled":false,
            "offY":10.0,
            "radius":0.5,
            "color":3355508509,
            "Filled":false,
            "fillIntensity":0.5,
            "thicc":8.0,
            "refActorObjectID":0,
            "refActorComparisonType":2,
            "includeRotation":true,
            "AdditionalRotation":5.2359877,
            "tether":true
        }
        """);
    }

    public override void OnMessage(string Message)
    {
        if (Message.Contains($"(7635>{LimitlessSynergyCast})"))
        {
            ResetState();
            _allowed = true;
        }
    }

    public override void OnTetherCreate(uint source, uint target, uint data2, uint data3, uint data5)
    {
        if (!_allowed || Conf.Assignment == TetherAssignment.None)
            return;

        var normalizedTether = NormalizeTether(source, target);
        if (normalizedTether == null)
            return;

        var (sourceObjId, targetObjId) = normalizedTether.Value;
        if (!IsAssignedTetherSource(sourceObjId))
            return;

        _tethers[sourceObjId] = targetObjId;
    }

    public override void OnTetherRemoval(uint source, uint data2, uint data3, uint data5)
    {
        _tethers.Remove(source);
        var pair = _tethers.FirstOrDefault(x => x.Value == source);
        if (pair.Key != 0)
            _tethers.Remove(pair.Key);
    }

    public override void OnUpdate()
    {
        OffAll();

        if (!_allowed || Conf.Assignment == TetherAssignment.None)
            return;

        var tether = GetAssignedTether();
        if (tether == null)
            return;

        var (source, target) = tether.Value;
        if (source.GetObject() is not IBattleChara)
            return;

        var basePlayer = GetBasePlayer();
        var hasTether = basePlayer != null && IsPlayerObject(target, basePlayer);

        UpdateTetherLine(source, target, hasTether);
        UpdateDropGuide(source);
    }

    public override void OnDirectorUpdate(DirectorUpdateCategory category)
    {
        if (category.EqualsAny(DirectorUpdateCategory.Wipe, DirectorUpdateCategory.Commence, DirectorUpdateCategory.Recommence))
        {
            ResetState();
            OffAll();
        }
    }

    public override void OnReset()
    {
        ResetState();
        OffAll();
    }

    public override void OnSettingsDraw()
    {
        ImGuiEx.Text("TOP P2 - Limitless Synergy LB Tether");
        ImGui.SetNextItemWidth(160f);
        ImGuiEx.EnumCombo("Assignment", ref Conf.Assignment);

        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo("Script Override", string.IsNullOrEmpty(Conf.BasePlayerOverride) ? "No Override" : Conf.BasePlayerOverride))
        {
            if (ImGui.Selectable("No Override", string.IsNullOrEmpty(Conf.BasePlayerOverride)))
                Conf.BasePlayerOverride = "";

            foreach (var player in Svc.Objects.OfType<IPlayerCharacter>())
            {
                var name = player.Name.ToString();
                if (ImGui.Selectable(name, Conf.BasePlayerOverride == name))
                    Conf.BasePlayerOverride = name;
            }

            ImGui.EndCombo();
        }

        ImGui.Checkbox("Debug", ref Conf.Debug);

        if (ImGui.CollapsingHeader("Debug"))
        {
            ImGuiEx.Text($"Allowed: {_allowed}");
            ImGuiEx.Text($"Assignment: {Conf.Assignment}");
            ImGuiEx.Text($"Base player: {(GetBasePlayer() == null ? "None" : GetBasePlayer().Name.ToString())}");
            ImGuiEx.Text($"Recorded tethers: {_tethers.Count}");

            foreach (var tether in _tethers)
            {
                var source = tether.Key.GetObject();
                var target = tether.Value.GetObject();
                ImGuiEx.Text($"Source: {source} angle {GetSourceAngleText(tether.Key)} -> Target: {target}");
            }

            foreach (var source in Svc.Objects.OfType<IBattleChara>().Where(IsPotentialTetherSource))
            {
                ImGuiEx.Text($"Potential: {source} angle {GetSourceAngle(source.Position):0.0} assignment {GetSourceAssignment(source.Position)}");
            }
        }
    }

    private (uint Source, uint Target)? GetAssignedTether()
    {
        foreach (var tether in _tethers)
        {
            if (IsAssignedTetherSource(tether.Key))
                return (tether.Key, tether.Value);
        }

        return null;
    }

    private (uint Source, uint Target)? NormalizeTether(uint first, uint second)
    {
        if (IsAssignedTetherSource(first))
            return (first, second);

        if (IsAssignedTetherSource(second))
            return (second, first);

        if (IsPotentialTetherSource(first))
            return (first, second);

        if (IsPotentialTetherSource(second))
            return (second, first);

        return null;
    }

    private bool IsAssignedTetherSource(uint source)
    {
        if (source.GetObject() is not IBattleChara sourceObj)
            return false;

        if (!IsPotentialTetherSource(sourceObj))
            return false;

        return GetSourceAssignment(sourceObj.Position) == Conf.Assignment;
    }

    private static bool IsPotentialTetherSource(uint source)
    {
        return source.GetObject() is IBattleChara sourceObj && IsPotentialTetherSource(sourceObj);
    }

    private static bool IsPotentialTetherSource(IBattleChara source)
    {
        var distance = Vector2.Distance(new Vector2(CenterX, CenterZ), source.Position.ToVector2());
        var isKnownOmega = source.NameId == OmegaMNameId || source.NameId == OmegaFNameId;
        var isBladeDanceCaster = source.CastActionId == OmegaMBladeDanceCast || source.CastActionId == OmegaFBladeDanceCast;
        return (isKnownOmega || isBladeDanceCaster) && Math.Abs(distance - TetherSourceDistance) <= TetherSourceDistanceTolerance;
    }

    private IPlayerCharacter? GetBasePlayer()
    {
        if (!string.IsNullOrEmpty(Conf.BasePlayerOverride))
        {
            var overridePlayer = Svc.Objects
                .OfType<IPlayerCharacter>()
                .FirstOrDefault(x => string.Equals(x.Name.ToString(), Conf.BasePlayerOverride, StringComparison.OrdinalIgnoreCase));

            if (overridePlayer != null)
                return overridePlayer;
        }

        return Svc.ClientState.LocalPlayer;
    }

    private static bool IsPlayerObject(uint objectId, IPlayerCharacter player)
    {
        if (objectId == player.EntityId)
            return true;

        return objectId.GetObject() is IPlayerCharacter targetPlayer
            && string.Equals(targetPlayer.Name.ToString(), player.Name.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static TetherAssignment GetSourceAssignment(Vector3 position)
    {
        var roundedAngle = RoundToNearest45(GetSourceAngle(position));
        return roundedAngle switch
        {
            90 or 135 or 180 or -135 => TetherAssignment.MT,
            45 or 0 or -45 or -90 => TetherAssignment.ST,
            _ => TetherAssignment.None,
        };
    }

    private static float GetSourceAngle(Vector3 position)
    {
        var degrees = MathF.Atan2(CenterZ - position.Z, position.X - CenterX) * 180f / MathF.PI;
        return NormalizeAngle(degrees);
    }

    private static int RoundToNearest45(float degrees)
    {
        var rounded = (int)MathF.Round(degrees / 45f) * 45;
        return (int)NormalizeAngle(rounded);
    }

    private static float NormalizeAngle(float degrees)
    {
        while (degrees > 180f) degrees -= 360f;
        while (degrees <= -180f) degrees += 360f;
        return degrees;
    }

    private static string GetSourceAngleText(uint source)
    {
        if (source.GetObject() is not IBattleChara sourceObj)
            return "Unknown";

        return $"{GetSourceAngle(sourceObj.Position):0.0} / {RoundToNearest45(GetSourceAngle(sourceObj.Position))}";
    }

    private void UpdateTetherLine(uint source, uint target, bool hasTether)
    {
        if (!Controller.TryGetElementByName("TetherLine", out var element))
            return;

        var sourceObj = source.GetObject();
        var targetObj = target.GetObject();
        if (sourceObj == null || targetObj == null)
            return;

        element.Enabled = true;
        element.SetRefPosition(sourceObj.Position);
        element.SetOffPosition(targetObj.Position);
        element.color = hasTether ? TakenTetherColor : UntakenTetherColor;
        element.thicc = 8f;
        element.overlayText = "";
    }

    private void UpdateDropGuide(uint source)
    {
        if (!Controller.TryGetElementByName("DropGuide", out var element))
            return;

        element.Enabled = true;
        element.refActorObjectID = source;
        element.color = DropColor;
        element.thicc = 8f;
        element.radius = 0.5f;
        element.tether = true;
        element.includeRotation = true;
        element.AdditionalRotation = DropAdditionalRotation;
    }

    private void OffAll()
    {
        Controller.GetRegisteredElements().Each(x => x.Value.Enabled = false);
    }

    private void ResetState()
    {
        _tethers.Clear();
        _allowed = false;
    }

    public class Config : IEzConfig
    {
        public TetherAssignment Assignment = TetherAssignment.None;
        public string BasePlayerOverride = "";
        public bool Debug = false;
    }

    public enum TetherAssignment
    {
        None,
        MT,
        ST,
    }
}
