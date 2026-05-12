using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons;
using ECommons.Configuration;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using Splatoon.SplatoonScripting;
using Splatoon.SplatoonScripting.Priority;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SplatoonScriptsOfficial.Duties.Endwalker.The_Omega_Protocol;

internal class TOP_P1_Pantokrator_Initial_Position_Priority : SplatoonScript
{
    private const uint PantokratorCast = 31501;
    private const uint FlameThrowerCast = 32368;
    private const uint Status1 = 0xBBC;
    private const uint Status2 = 0xBBD;
    private const uint Status3 = 0xBBE;
    private const uint Status4 = 0xD7B;
    private const float CenterX = 100f;
    private const float CenterZ = 100f;
    private const float InitialDistance = 10f;
    private const float FlameGuideDistance = 10f;
    private const uint GuideColor = 4278255360;
    private const int InitialGuideDelayMs = 2200;
    private const int InitialGuideDurationMs = 4500;
    private const int FlameGuideDelayMs = 1800;
    private const int FlameGuideDurationMs = 3500;
    private const float SameAxisTolerance = 5f;

    private readonly List<CastRecord> _flameCasts = [];
    private float? _pantokratorAngle = null;
    private float? _firstFlameAngle = null;
    private int _flameDirection = 0;
    private long _pantokratorStartedAt = 0;
    private long _firstFlameStartedAt = 0;
    private long _secondFlameStartedAt = 0;
    private string _lastEvent = "";
    private string _lastSkipReason = "";
    private string _lastGuide = "";

    private Config C => Controller.GetConfig<Config>();

    public override HashSet<uint>? ValidTerritories => [1122];
    public override Metadata? Metadata => new(1, "kudry + Codex");

    public override void OnSetup()
    {
        Controller.RegisterElementFromCode("Guide", $$"""
        {
            "Name":"",
            "type":0,
            "Enabled":false,
            "refX":100.0,
            "refY":100.0,
            "refZ":0.0,
            "radius":1.0,
            "color":{{GuideColor}},
            "Filled":false,
            "fillIntensity":0.2,
            "overlayBGColor":1879048192,
            "overlayTextColor":3372220415,
            "thicc":8.0,
            "overlayText":"",
            "tether":true
        }
        """);
        Controller.RegisterElementFromCode("PreGuide", $$"""
        {
            "Name":"",
            "type":0,
            "Enabled":false,
            "refX":100.0,
            "refY":100.0,
            "refZ":0.0,
            "radius":2.0,
            "color":{{GuideColor}},
            "Filled":false,
            "fillIntensity":0.2,
            "overlayBGColor":1879048192,
            "overlayTextColor":3372220415,
            "thicc":8.0,
            "overlayText":"",
            "tether":true
        }
        """);
        Controller.RegisterElementFromCode("RotationText", """
        {
            "Name":"",
            "type":0,
            "Enabled":false,
            "refX":100.0,
            "refY":100.0,
            "refZ":0.0,
            "radius":0.0,
            "overlayBGColor":3355443200,
            "overlayTextColor":3355508527,
            "overlayVOffset":2.5,
            "overlayFScale":3.0,
            "thicc":1.0,
            "overlayText":"",
            "tether":false
        }
        """);
    }

    public override void OnStartingCast(uint source, uint castId)
    {
        if (castId == PantokratorCast)
        {
            ResetState();
            _pantokratorAngle = GetCastAngle(source);
            _pantokratorStartedAt = Environment.TickCount64;
            _lastEvent = $"31501 angle={FormatAngle(_pantokratorAngle)}";
            return;
        }

        if (castId != FlameThrowerCast)
        {
            return;
        }

        RecordFlameCast(source);
    }

    public override void OnUpdate()
    {
        ScanActiveCasts();
        OffGuide();

        var player = GetBasePlayer();
        if (player == null)
        {
            _lastSkipReason = "No base player";
            return;
        }

        if (!TryGetMyStatusNumber(player, out var number))
        {
            _lastSkipReason = "Base player has no Pantokrator number status";
            return;
        }

        var pairPriorityIndex = GetPairPriorityIndex(player, number);
        if (pairPriorityIndex < 0)
        {
            _lastSkipReason = "Could not resolve pair priority";
            return;
        }

        var position = ResolveGuidePosition(number, pairPriorityIndex);
        if (position != null)
        {
            _lastSkipReason = "";
            MoveGuide("Guide", position.Value);
            return;
        }

        var prePosition = ResolvePreGuidePosition(number, pairPriorityIndex);
        if (prePosition != null)
        {
            _lastSkipReason = "";
            MoveGuide("PreGuide", prePosition.Value);
            UpdateRotationText();
            return;
        }

        UpdateRotationText();
        _lastSkipReason = "Could not resolve guide position";
    }

    public override void OnReset()
    {
        ResetState();
        OffGuide();
    }

    public override void OnSettingsDraw()
    {
        ImGuiEx.Text("TOP P1 Pantokrator initial position priority");
        ImGuiEx.Text("In each debuff pair, higher priority goes North/East and lower priority goes South/West.");
        C.PriorityData.Draw();

        ImGui.Separator();
        ImGui.SetNextItemWidth(220);
        if (ImGui.BeginCombo("Script Override", string.IsNullOrEmpty(C.BasePlayerOverride) ? "No Override" : C.BasePlayerOverride))
        {
            if (ImGui.Selectable("No Override", string.IsNullOrEmpty(C.BasePlayerOverride)))
            {
                C.BasePlayerOverride = "";
            }

            foreach (var player in Svc.Objects.OfType<IPlayerCharacter>().OrderBy(x => x.Name.ToString()))
            {
                var name = player.Name.ToString();
                if (ImGui.Selectable(name, C.BasePlayerOverride == name))
                {
                    C.BasePlayerOverride = name;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.Checkbox("Debug", ref C.Debug);
        if (C.Debug)
        {
            var player = GetBasePlayer();
            ImGuiEx.Text($"Base player: {player?.Name.ToString() ?? "None"}");
            if (player != null && TryGetMyStatusNumber(player, out var number))
            {
                var pairPriority = GetPairPriorityIndex(player, number);
                ImGuiEx.Text($"Base player debuff number: {number}");
                ImGuiEx.Text($"Same debuff players in priority list: {CountPriorityPlayersWithNumber(number)}");
                ImGuiEx.Text($"Base player pair priority: {(pairPriority < 0 ? "Unknown" : (pairPriority + 1).ToString())}");
            }
            ImGuiEx.Text($"31501 angle: {FormatAngle(_pantokratorAngle)}");
            ImGuiEx.Text($"First 32368 angle: {FormatAngle(_firstFlameAngle)}");
            ImGuiEx.Text($"32368 direction: {(_flameDirection > 0 ? "Clockwise" : _flameDirection < 0 ? "Counter-clockwise" : "Unknown")}");
            ImGuiEx.Text($"32368 axes: {string.Join(", ", _flameCasts.Select(x => FormatAngle(x.Angle)))}");
            ImGuiEx.Text($"31501 age ms: {GetPantokratorAgeMs()}");
            ImGuiEx.Text($"First 32368 age ms: {GetFirstFlameAgeMs()}");
            ImGuiEx.Text($"Second 32368 age ms: {GetSecondFlameAgeMs()}");
            ImGuiEx.Text($"Last guide: {(string.IsNullOrEmpty(_lastGuide) ? "None" : _lastGuide)}");
            ImGuiEx.Text($"Last event: {_lastEvent}");
            ImGuiEx.Text($"Last skip reason: {(string.IsNullOrEmpty(_lastSkipReason) ? "None" : _lastSkipReason)}");
        }
    }

    private void ScanActiveCasts()
    {
        foreach (var caster in Svc.Objects.OfType<IBattleChara>())
        {
            if (caster.CastActionId == PantokratorCast && _pantokratorAngle == null)
            {
                _pantokratorAngle = GetCastAngle(caster);
                _pantokratorStartedAt = Environment.TickCount64;
                _lastEvent = $"31501 scan angle={FormatAngle(_pantokratorAngle)}";
            }

            if (caster.CastActionId == FlameThrowerCast)
            {
                RecordFlameCast(caster);
            }
        }
    }

    private void RecordFlameCast(uint source)
    {
        if (source.GetObject() is not IBattleChara caster)
        {
            return;
        }

        RecordFlameCast(caster);
    }

    private void RecordFlameCast(IBattleChara caster)
    {
        if (_flameCasts.Any(x => x.Source == caster.EntityId))
        {
            return;
        }

        float? angle = GetCastAngle(caster);
        float? axisAngle = null;
        if (angle.HasValue)
        {
            axisAngle = NormalizeAxisAngle(angle.Value);
        }

        if (axisAngle != null && _flameCasts.Any(x => x.Angle != null && GetAxisDistance(x.Angle.Value, axisAngle.Value) <= SameAxisTolerance))
        {
            return;
        }

        _flameCasts.Add(new(caster.EntityId, axisAngle, Environment.TickCount64));
        _lastEvent = $"32368 #{_flameCasts.Count} angle={FormatAngle(angle)} axis={FormatAngle(axisAngle)}";

        if (_flameCasts.Count == 1)
        {
            _firstFlameAngle = axisAngle;
            _firstFlameStartedAt = Environment.TickCount64;
            _flameDirection = 0;
            return;
        }

        if (_flameCasts.Count == 2 && _firstFlameAngle != null && axisAngle != null)
        {
            _secondFlameStartedAt = Environment.TickCount64;
            _flameDirection = GetClockwiseDirection(_firstFlameAngle.Value, axisAngle.Value);
        }
    }

    private Vector3? ResolveGuidePosition(int number, int pairPriorityIndex)
    {
        if (_firstFlameAngle != null && IsFlameGuideWindow() && _flameDirection != 0)
        {
            return null;
        }

        if (_firstFlameAngle != null)
        {
            return null;
        }

        if (_pantokratorAngle == null)
        {
            return null;
        }

        if (!IsInitialGuideWindow())
        {
            return null;
        }

        var useNorthSouth = ShouldUseNorthSouth(_pantokratorAngle.Value, _firstFlameAngle);
        var highPriority = pairPriorityIndex == 0;

        if (useNorthSouth)
        {
            var angle = highPriority ? 0f : 180f;
            _lastGuide = $"Initial angle={angle:0.0}";
            return PositionFromAngle(angle, InitialDistance);
        }

        {
            var angle = highPriority ? 90f : 270f;
            _lastGuide = $"Initial angle={angle:0.0}";
            return PositionFromAngle(angle, InitialDistance);
        }
    }

    private Vector3? ResolvePreGuidePosition(int number, int pairPriorityIndex)
    {
        if (_firstFlameAngle != null && IsFlamePreGuideWindow())
        {
            return ResolveAxisPreGuidePosition(pairPriorityIndex);
        }

        return null;
    }

    private Vector3? ResolveAxisPreGuidePosition(int pairPriorityIndex)
    {
        if (_firstFlameAngle == null)
        {
            return null;
        }

        var highPriority = pairPriorityIndex == 0;
        var angle = GetPriorityNinetyDegreeAngle(highPriority);

        _lastGuide = $"Pre angle={angle:0.0}";
        return PositionFromAngle(angle, FlameGuideDistance);
    }

    private void UpdateRotationText()
    {
        if (!Controller.TryGetElementByName("RotationText", out var element))
        {
            return;
        }

        if (_flameDirection == 0 || !IsRotationTextWindow())
        {
            element.Enabled = false;
            return;
        }

        element.Enabled = true;
        element.overlayText = _flameDirection > 0 ? "CW" : "CCW";
    }

    private float GetPriorityNinetyDegreeAngle(bool highPriority)
    {
        var firstFlameAngle = _firstFlameAngle ?? 0f;
        var useNorthSouth = ShouldUseNorthSouth(_pantokratorAngle ?? firstFlameAngle, _firstFlameAngle);
        var targetAngle = useNorthSouth
            ? (highPriority ? 0f : 180f)
            : (highPriority ? 90f : 270f);

        var clockwiseCandidate = NormalizeAngle(firstFlameAngle + 90f);
        var counterClockwiseCandidate = NormalizeAngle(firstFlameAngle - 90f);
        return GetAngleDistance(clockwiseCandidate, targetAngle) <= GetAngleDistance(counterClockwiseCandidate, targetAngle)
            ? clockwiseCandidate
            : counterClockwiseCandidate;
    }

    private static bool ShouldUseNorthSouth(float pantokratorAngle, float? firstFlameAngle)
    {
        if (firstFlameAngle != null && IsNearCardinal(firstFlameAngle.Value, 0f, 20f))
        {
            return false;
        }

        if (firstFlameAngle != null && IsNearCardinal(firstFlameAngle.Value, 180f, 20f))
        {
            return false;
        }

        if (IsNearCardinal(pantokratorAngle, 315f, 25f) || IsNearCardinal(pantokratorAngle, 135f, 25f))
        {
            return true;
        }

        return true;
    }

    private int GetPairPriorityIndex(IPlayerCharacter player, int number)
    {
        var priorityPlayers = C.PriorityData.GetPlayers(priority => priority.IGameObject is IPlayerCharacter)?.ToList();
        if (priorityPlayers == null)
        {
            return -1;
        }

        var pair = priorityPlayers
            .Where(priority => priority.IGameObject is IPlayerCharacter candidate && HasNumberStatus(candidate, number))
            .ToList();

        if (pair.Count != 2)
        {
            return -1;
        }

        return pair.FindIndex(priority => priority.IGameObject.EntityId == player.EntityId);
    }

    private int CountPriorityPlayersWithNumber(int number)
    {
        var priorityPlayers = C.PriorityData.GetPlayers(priority => priority.IGameObject is IPlayerCharacter)?.ToList();
        if (priorityPlayers == null)
        {
            return 0;
        }

        return priorityPlayers.Count(priority => priority.IGameObject is IPlayerCharacter candidate && HasNumberStatus(candidate, number));
    }

    private IPlayerCharacter? GetBasePlayer()
    {
        if (!string.IsNullOrEmpty(C.BasePlayerOverride))
        {
            var overridePlayer = Svc.Objects
                .OfType<IPlayerCharacter>()
                .FirstOrDefault(x => string.Equals(x.Name.ToString(), C.BasePlayerOverride, StringComparison.OrdinalIgnoreCase));

            if (overridePlayer != null)
            {
                return overridePlayer;
            }
        }

        return Player.Object;
    }

    private static bool TryGetMyStatusNumber(IPlayerCharacter player, out int number)
    {
        if (HasStatus(player, Status1))
        {
            number = 1;
            return true;
        }

        if (HasStatus(player, Status2))
        {
            number = 2;
            return true;
        }

        if (HasStatus(player, Status3))
        {
            number = 3;
            return true;
        }

        if (HasStatus(player, Status4))
        {
            number = 4;
            return true;
        }

        number = 0;
        return false;
    }

    private static bool HasStatus(IPlayerCharacter player, uint statusId)
    {
        return player.StatusList.Any(x => x.StatusId == statusId);
    }

    private static bool HasNumberStatus(IPlayerCharacter player, int number)
    {
        return number switch
        {
            1 => HasStatus(player, Status1),
            2 => HasStatus(player, Status2),
            3 => HasStatus(player, Status3),
            4 => HasStatus(player, Status4),
            _ => false,
        };
    }

    private bool IsInitialGuideWindow()
    {
        var age = GetPantokratorAgeMs();
        return age >= InitialGuideDelayMs && age <= InitialGuideDelayMs + InitialGuideDurationMs;
    }

    private bool IsFlameGuideWindow()
    {
        var age = GetSecondFlameAgeMs();
        return age >= FlameGuideDelayMs && age <= FlameGuideDelayMs + FlameGuideDurationMs;
    }

    private bool IsFlamePreGuideWindow()
    {
        var age = _secondFlameStartedAt == 0 ? GetFirstFlameAgeMs() : GetSecondFlameAgeMs();
        if (age < 0)
        {
            return false;
        }

        return _secondFlameStartedAt == 0 || age <= FlameGuideDelayMs + FlameGuideDurationMs;
    }

    private bool IsRotationTextWindow()
    {
        var age = GetSecondFlameAgeMs();
        return age >= 0 && age <= FlameGuideDelayMs + FlameGuideDurationMs;
    }

    private long GetPantokratorAgeMs()
    {
        return _pantokratorStartedAt == 0 ? -1 : Environment.TickCount64 - _pantokratorStartedAt;
    }

    private long GetFirstFlameAgeMs()
    {
        return _firstFlameStartedAt == 0 ? -1 : Environment.TickCount64 - _firstFlameStartedAt;
    }

    private long GetSecondFlameAgeMs()
    {
        return _secondFlameStartedAt == 0 ? -1 : Environment.TickCount64 - _secondFlameStartedAt;
    }

    private static int GetClockwiseDirection(float firstAngle, float secondAngle)
    {
        var diff = NormalizeAxisDelta(secondAngle - firstAngle);
        return diff >= 0f ? 1 : -1;
    }

    private static float? GetCastAngle(uint source)
    {
        if (source.GetObject() is not IBattleChara caster)
        {
            return null;
        }

        return GetCastAngle(caster);
    }

    private static float? GetCastAngle(IBattleChara caster)
    {
        var distanceFromCenter = MathF.Sqrt(MathF.Pow(caster.Position.X - CenterX, 2f) + MathF.Pow(caster.Position.Z - CenterZ, 2f));
        return distanceFromCenter > 1.5f
            ? PositionToAngle(caster.Position)
            : RotationToAngle(caster.Rotation);
    }

    private static float RotationToAngle(float rotation)
    {
        return NormalizeAngle(rotation * 180f / MathF.PI);
    }

    private static float PositionToAngle(Vector3 position)
    {
        return NormalizeAngle(MathF.Atan2(position.X - CenterX, CenterZ - position.Z) * 180f / MathF.PI);
    }

    private static Vector3 PositionFromAngle(float angle, float distance)
    {
        var radians = NormalizeAngle(angle) * MathF.PI / 180f;
        return new Vector3(
            CenterX + MathF.Sin(radians) * distance,
            0f,
            CenterZ - MathF.Cos(radians) * distance);
    }

    private static bool IsNearCardinal(float angle, float target, float tolerance)
    {
        var diff = MathF.Abs(NormalizeAngle(angle - target));
        return MathF.Min(diff, 360f - diff) <= tolerance;
    }

    private static float GetAngleDistance(float a, float b)
    {
        var diff = MathF.Abs(NormalizeAngle(a - b));
        return MathF.Min(diff, 360f - diff);
    }

    private static float GetAxisDistance(float a, float b)
    {
        return MathF.Abs(NormalizeAxisDelta(a - b));
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        return angle < 0 ? angle + 360f : angle;
    }

    private static float NormalizeAxisAngle(float angle)
    {
        var normalized = NormalizeAngle(angle);
        return normalized >= 180f ? normalized - 180f : normalized;
    }

    private static float NormalizeAxisDelta(float angle)
    {
        angle %= 180f;
        if (angle > 90f)
        {
            angle -= 180f;
        }
        else if (angle < -90f)
        {
            angle += 180f;
        }

        return angle;
    }

    private static string FormatAngle(float? angle)
    {
        return angle == null ? "None" : $"{angle.Value:0.0}";
    }

    private void MoveGuide(string name, Vector3 position)
    {
        var element = Controller.GetElementByName(name);
        element.Enabled = true;
        element.refX = position.X;
        element.refY = position.Z;
        element.refZ = position.Y;
    }

    private void OffGuide()
    {
        if (Controller.TryGetElementByName("Guide", out var element))
        {
            element.Enabled = false;
        }

        if (Controller.TryGetElementByName("PreGuide", out var preElement))
        {
            preElement.Enabled = false;
        }

        if (Controller.TryGetElementByName("RotationText", out var rotationElement))
        {
            rotationElement.Enabled = false;
        }
    }

    private void ResetState()
    {
        _flameCasts.Clear();
        _pantokratorAngle = null;
        _firstFlameAngle = null;
        _flameDirection = 0;
        _pantokratorStartedAt = 0;
        _firstFlameStartedAt = 0;
        _secondFlameStartedAt = 0;
        _lastEvent = "";
        _lastSkipReason = "";
        _lastGuide = "";
    }

    private readonly record struct CastRecord(uint Source, float? Angle, long StartedAt);

    public class Config : IEzConfig
    {
        public bool Debug = false;
        public string BasePlayerOverride = "";
        public PriorityData PriorityData = new();
    }
}
