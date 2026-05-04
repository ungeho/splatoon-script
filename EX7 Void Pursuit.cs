using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Bindings.ImGui;
using ECommons;
using ECommons.Configuration;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using Splatoon.SplatoonScripting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SplatoonScriptsOfficial.Duties.Dawntrail;

internal class EX7_Void_Pursuit : SplatoonScript
{
    private const uint Territory = 1362;
    private const uint OrbDataId = 0x4EB5;
    private const uint TetherData2 = 0;
    private const uint OrbChaseTetherData3 = 404;
    private const uint PlayerFollowTetherData3 = 405;
    private const uint TetherData5 = 15;
    private const float CenterX = 100.0f;
    private const float CenterY = 100.0f;
    private const float GuideDistance = 8.4f;
    private const long TetherMemoryMs = 20000;
    private const long TetherPairWindowMs = 20000;

    public override HashSet<uint> ValidTerritories => [Territory];
    public override Metadata? Metadata => new(1, "kudry + Codex");

    private Config Conf => Controller.GetConfig<Config>();
    private readonly Dictionary<string, OrbRecord> _playerToOrb = [];
    private string _followSourcePlayerName = "";
    private long _followTetherMs = 0;
    private bool _guideSuppressed = false;
    private long _guideSuppressedMs = 0;

    public override void OnSetup()
    {
        Controller.RegisterElementFromCode("Guide", """
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
            "overlayText":"",
            "tether":true,
            "thicc":8.0
        }
        """);
    }

    public override void OnTetherCreate(uint source, uint target, uint data2, uint data3, uint data5)
    {
        DebugLog($"Tether source={source}, target={target}, param=({data2}, {data3}, {data5}), sourceObj={source.GetObject()?.Name}, targetObj={target.GetObject()?.Name}");

        if (data2 != TetherData2 || data5 != TetherData5)
            return;

        if (data3 == OrbChaseTetherData3)
        {
            if (IsSelfTether(source, target))
            {
                OffAll();
                ClearFollowState();
                _guideSuppressed = true;
                _guideSuppressedMs = Environment.TickCount64;
                DebugLog("Self tether switched to 404, suppressing guide");
                return;
            }

            if (IsGuideSuppressed())
            {
                DebugLog("Guide suppressed, ignoring 404 tether");
                return;
            }

            ClearExpiredGuideSuppression();
            RecordOrbChaseTether(source, target);
            return;
        }

        if (IsGuideSuppressed())
        {
            DebugLog("Guide suppressed, ignoring tether");
            return;
        }

        ClearExpiredGuideSuppression();

        if (data3 == PlayerFollowTetherData3)
            RecordPlayerFollowTether(source, target);
    }

    public override void OnUpdate()
    {
        OffAll();

        if (IsGuideSuppressed())
            return;

        ClearExpiredGuideSuppression();

        if (string.IsNullOrEmpty(_followSourcePlayerName))
            return;

        if (Environment.TickCount64 - _followTetherMs > TetherMemoryMs)
        {
            ClearState();
            return;
        }

        if (!_playerToOrb.TryGetValue(_followSourcePlayerName, out var orbRecord))
            return;

        if (Math.Abs(orbRecord.TimestampMs - _followTetherMs) > TetherPairWindowMs)
            return;

        var orb = Svc.Objects
            .OfType<IBattleChara>()
            .FirstOrDefault(x => x.Address == orbRecord.Address && x.DataId == OrbDataId);

        if (orb == null)
            return;

        MoveGuide(GetGuidePosition(orb.Position));
    }

    public override void OnReset()
    {
        OffAll();
        ClearState();
    }

    public override void OnSettingsDraw()
    {
        ImGui.Checkbox("DebugPrint", ref Conf.DebugPrint);

        if (ImGui.CollapsingHeader("Debug"))
        {
            ImGuiEx.Text($"Follow source player: {_followSourcePlayerName}");
            ImGuiEx.Text($"Tracked orb count: {_playerToOrb.Count}");
            ImGuiEx.Text($"Follow tether age ms: {(Environment.TickCount64 - _followTetherMs)}");
            ImGuiEx.Text($"Guide suppressed: {_guideSuppressed}");
        }
    }

    private void RecordOrbChaseTether(uint source, uint target)
    {
        var sourceObject = source.GetObject();
        var targetObject = target.GetObject();

        if (sourceObject is IBattleChara sourceOrb && sourceOrb.DataId == OrbDataId && targetObject is IPlayerCharacter targetPlayer)
        {
            _playerToOrb[targetPlayer.Name.ToString()] = new(sourceOrb.Address, Environment.TickCount64);
            DebugLog($"Orb {sourceOrb.EntityId} chasing {targetPlayer.Name}");
        }
        else if (targetObject is IBattleChara targetOrb && targetOrb.DataId == OrbDataId && sourceObject is IPlayerCharacter sourcePlayer)
        {
            _playerToOrb[sourcePlayer.Name.ToString()] = new(targetOrb.Address, Environment.TickCount64);
            DebugLog($"Orb {targetOrb.EntityId} chasing {sourcePlayer.Name}");
        }
    }

    private void RecordPlayerFollowTether(uint source, uint target)
    {
        if (Player.Object == null)
            return;

        var sourceObject = source.GetObject();
        var targetObject = target.GetObject();

        if (SamePlayer(targetObject, Player.Object) && sourceObject is IPlayerCharacter sourcePlayer)
        {
            PruneOldOrbRecords();
            _followSourcePlayerName = sourcePlayer.Name.ToString();
            _followTetherMs = Environment.TickCount64;
            DebugLog($"Follow source: {sourcePlayer.Name}");
        }
        else if (SamePlayer(sourceObject, Player.Object) && targetObject is IPlayerCharacter targetPlayer)
        {
            PruneOldOrbRecords();
            _followSourcePlayerName = targetPlayer.Name.ToString();
            _followTetherMs = Environment.TickCount64;
            DebugLog($"Follow target: {targetPlayer.Name}");
        }
    }

    private static bool IsSelfTether(uint source, uint target)
    {
        if (Player.Object == null)
            return false;

        return SamePlayer(source.GetObject(), Player.Object) || SamePlayer(target.GetObject(), Player.Object);
    }

    private static bool SamePlayer(IGameObject? a, IGameObject? b)
    {
        return a != null && b != null && (a.Address == b.Address || a.Name.ToString() == b.Name.ToString());
    }

    private static Vector3 GetGuidePosition(Vector3 orbPosition)
    {
        var angle = Math.Atan2(orbPosition.Z - CenterY, orbPosition.X - CenterX) - Math.PI / 2.0;
        return new Vector3(
            CenterX + MathF.Cos((float)angle) * GuideDistance,
            orbPosition.Y,
            CenterY + MathF.Sin((float)angle) * GuideDistance);
    }

    private void MoveGuide(Vector3 position)
    {
        var element = Controller.GetElementByName("Guide");
        element.refX = position.X;
        element.refY = position.Z;
        element.refZ = position.Y;
        element.Enabled = true;
    }

    private void OffAll()
    {
        Controller.GetRegisteredElements().Each(x => x.Value.Enabled = false);
    }

    private void ClearState()
    {
        ClearFollowState();
        ClearGuideSuppression();
    }

    private void ClearFollowState()
    {
        _playerToOrb.Clear();
        _followSourcePlayerName = "";
        _followTetherMs = 0;
    }

    private void ClearGuideSuppression()
    {
        _guideSuppressed = false;
        _guideSuppressedMs = 0;
    }

    private bool IsGuideSuppressed()
    {
        return _guideSuppressed && Environment.TickCount64 - _guideSuppressedMs <= TetherMemoryMs;
    }

    private void ClearExpiredGuideSuppression()
    {
        if (_guideSuppressed && Environment.TickCount64 - _guideSuppressedMs > TetherMemoryMs)
            ClearGuideSuppression();
    }

    private void PruneOldOrbRecords()
    {
        var now = Environment.TickCount64;
        foreach (var key in _playerToOrb.Where(x => now - x.Value.TimestampMs > TetherPairWindowMs).Select(x => x.Key).ToList())
            _playerToOrb.Remove(key);
    }

    private void DebugLog(string message)
    {
        if (Conf.DebugPrint)
            ECommons.Logging.DuoLog.Information($"[無の追跡] {message}");
    }

    public class Config : IEzConfig
    {
        public bool DebugPrint = false;
    }

    private readonly record struct OrbRecord(nint Address, long TimestampMs);
}
