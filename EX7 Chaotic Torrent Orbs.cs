using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Bindings.ImGui;
using ECommons;
using ECommons.Configuration;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using Splatoon.SplatoonScripting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SplatoonScriptsOfficial.Duties.Dawntrail;

internal class EX7_Chaotic_Torrent_Orbs : SplatoonScript
{
    private const uint Territory = 1362;
    private const uint TankOrbDataId = 0x4DC6;
    private const uint AssignmentOrbDataId = 0x4DC5;
    private const uint OrbStatusId = 2234;
    private const uint FirstOrbSoakStatusId = 2941;
    private const ushort Param75 = 75;
    private const ushort Param88 = 88;
    private const float CenterX = 100.0f;
    private const float CenterY = 100.0f;
    private const float Param75GuideDistance = 12.5f;
    private const float Param88GuideDistance = 7.0f;

    public override HashSet<uint> ValidTerritories => [Territory];
    public override Metadata? Metadata => new(1, "kudry + Codex");

    private Config Conf => Controller.GetConfig<Config>();
    private bool _gotFirstOrb = false;
    private Vector3? _param75GuidePosition = null;
    private Vector3? _param88GuidePosition = null;
    private bool _param75TargetVisible = false;
    private bool _param88TargetVisible = false;
    private nint _param75TargetAddress = nint.Zero;
    private nint _param88TargetAddress = nint.Zero;
    private bool _hadFirstOrbSoakStatus = false;

    public override void OnSetup()
    {
        RegisterGuide("Guide");
    }

    public override void OnUpdate()
    {
        OffAll();

        var role = GetLocalRole();
        if (role == Role.Unknown)
            return;

        var tankOrbs = GetOrbs(TankOrbDataId).ToList();
        var assignmentOrbs = GetOrbs(AssignmentOrbDataId).ToList();
        UpdateTargetCache(role, tankOrbs, assignmentOrbs);

        var hasFirstOrbSoakStatus = HasBasePlayerStatus(FirstOrbSoakStatusId);

        if (!_gotFirstOrb && _param75TargetVisible && !_hadFirstOrbSoakStatus && hasFirstOrbSoakStatus)
            _gotFirstOrb = true;

        if (!_gotFirstOrb && _param75TargetAddress != nint.Zero && !_param75TargetVisible && _param88TargetVisible)
            _gotFirstOrb = true;

        _hadFirstOrbSoakStatus = hasFirstOrbSoakStatus;

        var activeParam = _gotFirstOrb ? Param88 : Param75;
        var guidePosition = activeParam == Param88 ? _param88GuidePosition : _param75GuidePosition;

        if (guidePosition == null || (!_param75TargetVisible && !_param88TargetVisible))
        {
            if (_param75TargetAddress != nint.Zero && _param88TargetAddress != nint.Zero)
                ClearTargetCache();

            return;
        }

        MoveGuide(guidePosition.Value);
        DebugLog($"Role {role}, Param {activeParam}, Visible75 {_param75TargetVisible}, Visible88 {_param88TargetVisible}");
    }

    public override void OnReset()
    {
        OffAll();
        _gotFirstOrb = false;
        ClearTargetCache();
    }

    public override void OnSettingsDraw()
    {
        DrawOverrideCombo();
        DrawRoleOverrideCombo();
        ImGui.Checkbox("DebugPrint", ref Conf.DebugPrint);

        if (ImGui.CollapsingHeader("Debug"))
        {
            var tankOrbs = GetOrbs(TankOrbDataId).ToList();
            var assignmentOrbs = GetOrbs(AssignmentOrbDataId).ToList();

            ImGuiEx.Text($"Tank orbs 0x4DC6: {tankOrbs.Count}");
            ImGuiEx.Text($"Assignment orbs 0x4DC5: {assignmentOrbs.Count}");
            ImGuiEx.Text($"Param 75 orbs: {assignmentOrbs.Count(x => HasOrbStatusParam(x, Param75))}");
            ImGuiEx.Text($"Param 88 orbs: {assignmentOrbs.Count(x => HasOrbStatusParam(x, Param88))}");
            ImGuiEx.Text($"Got first orb: {_gotFirstOrb}");
            ImGuiEx.Text($"Param75 target visible: {_param75TargetVisible}");
            ImGuiEx.Text($"Param88 target visible: {_param88TargetVisible}");
            ImGuiEx.Text($"Base player: {GetBasePlayer()?.Name.ToString() ?? "None"}");
            ImGuiEx.Text($"Base player status 2941: {HasBasePlayerStatus(FirstOrbSoakStatusId)}");
            ImGuiEx.Text($"Assignment role: {GetLocalRole()}");
        }
    }

    private void DrawOverrideCombo()
    {
        ImGui.SetNextItemWidth(220);
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
    }

    private void DrawRoleOverrideCombo()
    {
        ImGui.SetNextItemWidth(220);
        if (ImGui.BeginCombo("Assignment Role", GetRoleOverrideLabel(Conf.RoleOverride)))
        {
            foreach (var role in new[] { RoleOverride.Auto, RoleOverride.Tank, RoleOverride.Melee, RoleOverride.Ranged, RoleOverride.Healer })
            {
                if (ImGui.Selectable(GetRoleOverrideLabel(role), Conf.RoleOverride == role))
                    Conf.RoleOverride = role;
            }

            ImGui.EndCombo();
        }
    }

    private void RegisterGuide(string name)
    {
        Controller.RegisterElementFromCode(name, $$"""
        {
            "Name":"",
            "Enabled":false,
            "refX":100.0,
            "refY":100.0,
            "refZ":0.0,
            "radius":1.0,
            "color":3355508480,
            "Filled":false,
            "fillIntensity":0.2,
            "overlayBGColor":3355443200,
            "overlayTextColor":3372220415,
            "overlayVOffset":0.0,
            "overlayFScale":2.2,
            "thicc":8.0,
            "overlayText":"",
            "tether":true
        }
        """);
    }

    private void MoveGuide(Vector3 position)
    {
        var element = Controller.GetElementByName("Guide");
        element.refX = position.X;
        element.refY = position.Z;
        element.refZ = position.Y;
        element.Enabled = true;
    }

    private IEnumerable<IBattleChara> GetOrbs(uint dataId)
    {
        return Svc.Objects
            .OfType<IBattleChara>()
            .Where(x => x.DataId == dataId);
    }

    private static bool HasOrbStatusParam(IBattleChara orb, ushort param)
    {
        return orb.StatusList.Any(x => x.StatusId == OrbStatusId && x.Param == param);
    }

    private bool HasBasePlayerStatus(uint statusId)
    {
        return GetBasePlayer()?.StatusList.Any(x => x.StatusId == statusId) == true;
    }

    private void UpdateTargetCache(Role role, List<IBattleChara> tankOrbs, List<IBattleChara> assignmentOrbs)
    {
        var allOrbs = tankOrbs.Concat(assignmentOrbs).ToList();
        _param75TargetVisible = _param75TargetAddress != nint.Zero && allOrbs.Any(x => x.Address == _param75TargetAddress);
        _param88TargetVisible = _param88TargetAddress != nint.Zero && allOrbs.Any(x => x.Address == _param88TargetAddress);

        if (tankOrbs.Count < 2)
            return;

        var center = new Vector3(CenterX, 0.0f, CenterY);
        var northAngle = GetNorthAngle(tankOrbs, center);

        var param75Target = GetTargetOrb(role, Param75, tankOrbs, assignmentOrbs, center, northAngle);
        if (param75Target != null)
        {
            if (_param75TargetAddress == nint.Zero)
            {
                _hadFirstOrbSoakStatus = HasBasePlayerStatus(FirstOrbSoakStatusId);
            }
            else if (_param75TargetAddress != param75Target.Address)
            {
                _gotFirstOrb = false;
                _hadFirstOrbSoakStatus = HasBasePlayerStatus(FirstOrbSoakStatusId);
                _param88GuidePosition = null;
                _param88TargetAddress = nint.Zero;
                _param88TargetVisible = false;
            }

            _param75TargetVisible = true;
            _param75TargetAddress = param75Target.Address;
            _param75GuidePosition = GetGuidePosition(param75Target.Position, Param75GuideDistance);
        }

        var param88Target = GetTargetOrb(role, Param88, tankOrbs, assignmentOrbs, center, northAngle);
        if (param88Target != null)
        {
            _param88TargetVisible = true;
            _param88TargetAddress = param88Target.Address;
            _param88GuidePosition = GetGuidePosition(param88Target.Position, Param88GuideDistance);
        }
    }

    private void ClearTargetCache()
    {
        _gotFirstOrb = false;
        _param75GuidePosition = null;
        _param88GuidePosition = null;
        _param75TargetVisible = false;
        _param88TargetVisible = false;
        _param75TargetAddress = nint.Zero;
        _param88TargetAddress = nint.Zero;
        _hadFirstOrbSoakStatus = HasBasePlayerStatus(FirstOrbSoakStatusId);
    }

    private static IBattleChara? GetTargetOrb(Role role, ushort activeParam, List<IBattleChara> tankOrbs, List<IBattleChara> assignmentOrbs, Vector3 center, double northAngle)
    {
        if (role == Role.Tank)
        {
            return tankOrbs.FirstOrDefault(x => HasOrbStatusParam(x, activeParam));
        }

        var roleIndex = role switch
        {
            Role.Melee => 0,
            Role.Ranged => 1,
            Role.Healer => 2,
            _ => -1
        };

        if (roleIndex == -1)
            return null;

        var sorted = assignmentOrbs
            .Where(x => HasOrbStatusParam(x, activeParam))
            .OrderBy(x => ClockwiseAngleFromNorth(x.Position, center, northAngle))
            .ToList();

        return sorted.Count > roleIndex ? sorted[roleIndex] : null;
    }

    private Role GetLocalRole()
    {
        if (Conf.RoleOverride != RoleOverride.Auto)
        {
            return Conf.RoleOverride switch
            {
                RoleOverride.Tank => Role.Tank,
                RoleOverride.Melee => Role.Melee,
                RoleOverride.Ranged => Role.Ranged,
                RoleOverride.Healer => Role.Healer,
                _ => Role.Unknown
            };
        }

        return GetBasePlayer()?.ClassJob.RowId switch
        {
            1 or 3 or 19 or 21 or 32 or 37 => Role.Tank,
            2 or 4 or 20 or 22 or 29 or 30 or 34 or 39 or 41 => Role.Melee,
            5 or 7 or 23 or 25 or 26 or 27 or 31 or 35 or 36 or 38 or 42 => Role.Ranged,
            6 or 24 or 28 or 33 or 40 => Role.Healer,
            _ => Role.Unknown
        };
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

        return Player.Object;
    }

    private static Vector3 GetGuidePosition(Vector3 orbPosition, float distance)
    {
        var angle = Math.Atan2(orbPosition.Z - CenterY, orbPosition.X - CenterX);
        return new Vector3(
            CenterX + MathF.Cos((float)angle) * distance,
            orbPosition.Y,
            CenterY + MathF.Sin((float)angle) * distance);
    }

    private static Vector3 GetCenter(IEnumerable<IBattleChara> orbs)
    {
        var positions = orbs.Select(x => x.Position).ToList();
        return new Vector3(
            positions.Average(x => x.X),
            positions.Average(x => x.Y),
            positions.Average(x => x.Z));
    }

    private static double GetNorthAngle(List<IBattleChara> tankOrbs, Vector3 center)
    {
        var tankCenter = GetCenter(tankOrbs);
        return Math.Atan2(tankCenter.Z - center.Z, tankCenter.X - center.X);
    }

    private static double ClockwiseAngleFromNorth(Vector3 position, Vector3 center, double northAngle)
    {
        var angle = Math.Atan2(position.Z - center.Z, position.X - center.X);
        var clockwise = angle - northAngle;

        while (clockwise < 0)
            clockwise += Math.Tau;

        while (clockwise >= Math.Tau)
            clockwise -= Math.Tau;

        return clockwise;
    }

    private void OffAll()
    {
        Controller.GetRegisteredElements().Each(x => x.Value.Enabled = false);
    }

    private void DebugLog(string message)
    {
        if (Conf.DebugPrint)
            DuoLog.Information($"[混沌の激流] {message}");
    }

    private static string GetRoleOverrideLabel(RoleOverride role)
    {
        return role switch
        {
            RoleOverride.Auto => "Auto",
            RoleOverride.Tank => "Tank",
            RoleOverride.Melee => "Melee (D1D2)",
            RoleOverride.Ranged => "Ranged (D3D4)",
            RoleOverride.Healer => "Healer",
            _ => "Auto"
        };
    }

    public class Config : IEzConfig
    {
        public bool DebugPrint = false;
        public string BasePlayerOverride = "";
        public RoleOverride RoleOverride = RoleOverride.Auto;
    }

    public enum RoleOverride
    {
        Auto,
        Tank,
        Melee,
        Ranged,
        Healer
    }

    private enum Role
    {
        Unknown,
        Tank,
        Melee,
        Ranged,
        Healer
    }
}
