using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Bindings.ImGui;
using ECommons.Configuration;
using ECommons.DalamudServices;
using ECommons.DalamudServices.Legacy;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using Splatoon.SplatoonScripting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SplatoonScriptsOfficial.Duties.Endwalker.The_Omega_Protocol;

internal unsafe class TOP_P6_Magic_Number_LB : SplatoonScript
{
    private const uint TopTerritory = 1122;
    private const uint MagicNumberCast = 31670;
    private const uint MagicNumberStatus = 0xDCC;
    private const uint DefaultTankLb3ActionId = 199;
    private const uint DefaultHealerLb3ActionId = 208;
    private static readonly LbRole[] RoleOptions = { LbRole.None, LbRole.MT, LbRole.ST, LbRole.H1, LbRole.H2 };

    public override HashSet<uint>? ValidTerritories => null;
    public override Metadata? Metadata => new(1, "kudry + Codex");

    private Config Conf => Controller.GetConfig<Config>();
    private int _magicNumberCastCount = 0;
    private bool _waitingForFirstHealerLb = false;
    private bool _waitingForSecondHealerLb = false;
    private bool _usedFirstLb = false;
    private bool _usedSecondLb = false;
    private long _lastCastStartedAt = 0;
    private long _lastStatusCheckAt = 0;
    private string _lastEvent = "Ready";
    private string _lastAttempt = "None";
    private string _lastPlannedLb = "None";
    private int _plannedLbCount = 0;
    private long _lastPlannedLbAt = 0;
    private bool? _lastUseResult = null;

    public override void OnStartingCast(uint source, uint castId)
    {
        if(castId != MagicNumberCast || Conf.SelectedRole == LbRole.None)
            return;

        if(Conf.AutomationOnlyInTop && Svc.ClientState.TerritoryType != TopTerritory)
        {
            DebugLog($"Ignoring cast {castId}; current territory is {Svc.ClientState.TerritoryType}");
            return;
        }

        _magicNumberCastCount++;
        _lastCastStartedAt = Environment.TickCount64;
        _lastEvent = $"Magic Number cast #{_magicNumberCastCount} started by {source.GetObject()?.Name ?? "Unknown"}";
        DebugLog(_lastEvent);

        if(_magicNumberCastCount == 1)
        {
            if(Conf.SelectedRole == LbRole.MT)
                TryUseLimitBreak("MT: first Magic Number cast");
            else if(Conf.SelectedRole == LbRole.H1)
                StartHealerWait(first: true);
        }
        else if(_magicNumberCastCount == 2)
        {
            if(Conf.SelectedRole == LbRole.ST)
                TryUseLimitBreak("ST: second Magic Number cast");
            else if(Conf.SelectedRole == LbRole.H2)
                StartHealerWait(first: false);
        }
    }

    public override void OnUpdate()
    {
        if(Conf.SelectedRole == LbRole.None)
            return;

        if(Conf.AutomationOnlyInTop && Svc.ClientState.TerritoryType != TopTerritory)
            return;

        if(!_waitingForFirstHealerLb && !_waitingForSecondHealerLb)
            return;

        if(Environment.TickCount64 - _lastStatusCheckAt < 100)
            return;

        _lastStatusCheckAt = Environment.TickCount64;

        var statusCount = CountPartyMembersWithMagicNumber();
        var partyCount = GetParty().Count;
        _lastEvent = $"Waiting status 0xDCC: {statusCount}/{partyCount}";

        if(!AllPartyMembersHaveMagicNumber())
            return;

        if(_waitingForFirstHealerLb && !_usedFirstLb)
            TryUseLimitBreak("H1: all party members have 0xDCC after first cast");
        else if(_waitingForSecondHealerLb && !_usedSecondLb)
            TryUseLimitBreak("H2: all party members have 0xDCC after second cast");

        _waitingForFirstHealerLb = false;
        _waitingForSecondHealerLb = false;
    }

    public override void OnReset()
    {
        ClearState();
    }

    public override void OnSettingsDraw()
    {
        DrawRoleCombo();

        ImGui.Checkbox("Automation only in TOP", ref Conf.AutomationOnlyInTop);
        ImGui.Checkbox("DebugPrint", ref Conf.DebugPrint);

        ImGui.Separator();
        ImGuiEx.Text("Debug LB Button");
        ImGui.SameLine();
        if(ImGui.Button("Use selected LB action"))
            TryUseLimitBreak("Manual debug button", markAutomatedUse: false);

        ImGui.Checkbox("Use job-specific LB3 action ID", ref Conf.UseJobSpecificActionId);

        ImGui.SetNextItemWidth(120);
        var tankLb = (int)Conf.FallbackTankLb3ActionId;
        if(ImGui.InputInt("Fallback Tank LB3 Action ID", ref tankLb))
            Conf.FallbackTankLb3ActionId = Math.Max(0, tankLb);

        ImGui.SetNextItemWidth(120);
        var healerLb = (int)Conf.FallbackHealerLb3ActionId;
        if(ImGui.InputInt("Fallback Healer LB3 Action ID", ref healerLb))
            Conf.FallbackHealerLb3ActionId = Math.Max(0, healerLb);

        if(ImGui.Button("Reset internal state"))
            ClearState();

        if(ImGui.CollapsingHeader("Debug"))
        {
            var party = GetParty();
            ImGuiEx.Text($"Selected role: {Conf.SelectedRole}");
            ImGuiEx.Text($"Current territory: {Svc.ClientState.TerritoryType}");
            ImGuiEx.Text($"Magic Number cast count: {_magicNumberCastCount}");
            ImGuiEx.Text($"Waiting H1/H2: {_waitingForFirstHealerLb}/{_waitingForSecondHealerLb}");
            ImGuiEx.Text($"Party status 0xDCC: {CountPartyMembersWithMagicNumber()}/{party.Count}");
            ImGuiEx.Text($"Last cast age ms: {GetAgeMs(_lastCastStartedAt)}");
            ImGuiEx.Text($"Last event: {_lastEvent}");
            ImGuiEx.Text($"Planned LB count: {_plannedLbCount}");
            ImGuiEx.Text($"Last planned LB: {_lastPlannedLb}");
            ImGuiEx.Text($"Last planned LB age ms: {GetAgeMs(_lastPlannedLbAt)}");
            ImGuiEx.Text($"Last attempt: {_lastAttempt}");
            ImGuiEx.Text($"Last UseAction result: {(_lastUseResult?.ToString() ?? "None")}");
            ImGuiEx.Text($"Selected action id: {GetSelectedActionId()}");
            ImGuiEx.Text($"Local job row id: {Player.Object?.ClassJob.RowId.ToString() ?? "None"}");
        }
    }

    private void DrawRoleCombo()
    {
        ImGui.SetNextItemWidth(160);
        if(ImGui.BeginCombo("LB Role", Conf.SelectedRole.ToString()))
        {
            foreach(var role in RoleOptions)
            {
                if(ImGui.Selectable(role.ToString(), Conf.SelectedRole == role))
                {
                    Conf.SelectedRole = role;
                    DebugLog($"Role changed to {role}");
                }
            }

            ImGui.EndCombo();
        }
    }

    private void StartHealerWait(bool first)
    {
        if(first)
        {
            _waitingForFirstHealerLb = true;
            _lastEvent = "H1 waiting for all party members to receive 0xDCC";
        }
        else
        {
            _waitingForSecondHealerLb = true;
            _lastEvent = "H2 waiting for all party members to receive 0xDCC";
        }

        DebugLog(_lastEvent);
    }

    private bool TryUseLimitBreak(string reason, bool markAutomatedUse = true)
    {
        var actionId = GetSelectedActionId();
        _lastAttempt = $"{reason}, action={actionId}, role={Conf.SelectedRole}";
        RecordPlannedLimitBreak(reason, actionId);

        if(actionId == 0)
        {
            _lastUseResult = false;
            DebugLog($"{_lastAttempt}: skipped because action id is 0");
            return false;
        }

        var result = ActionManager.Instance()->UseAction(ActionType.Action, actionId);
        _lastUseResult = result;

        if(markAutomatedUse && result)
        {
            if(_magicNumberCastCount <= 1)
                _usedFirstLb = true;
            else
                _usedSecondLb = true;
        }

        DebugLog($"{_lastAttempt}: UseAction result={result}");
        return result;
    }

    private void RecordPlannedLimitBreak(string reason, uint actionId)
    {
        _plannedLbCount++;
        _lastPlannedLbAt = Environment.TickCount64;
        _lastPlannedLb = $"{reason}, action={actionId}, castAgeMs={GetAgeMs(_lastCastStartedAt)}, party0xDCC={CountPartyMembersWithMagicNumber()}/{GetParty().Count}";
        DebugLog($"Would use LB now: {_lastPlannedLb}");
    }

    private uint GetSelectedActionId()
    {
        if(Conf.UseJobSpecificActionId)
        {
            var jobActionId = GetCurrentJobLimitBreak3ActionId();
            if(jobActionId != 0)
                return jobActionId;
        }

        return Conf.SelectedRole switch
        {
            LbRole.MT or LbRole.ST => (uint)Conf.FallbackTankLb3ActionId,
            LbRole.H1 or LbRole.H2 => (uint)Conf.FallbackHealerLb3ActionId,
            _ => 0
        };
    }

    private static uint GetCurrentJobLimitBreak3ActionId()
    {
        return Player.Object?.ClassJob.RowId switch
        {
            19 => 199,   // PLD: Last Bastion
            21 => 4240,  // WAR: Land Waker
            32 => 4241,  // DRK: Dark Force
            37 => 17105, // GNB: Gunmetal Soul
            24 => 208,   // WHM: Pulse of Life
            28 => 4247,  // SCH: Angel Feathers
            33 => 4248,  // AST: Astral Stasis
            40 => 24859, // SGE: Techne Makre
            _ => 0
        };
    }

    private List<IPlayerCharacter> GetParty()
    {
        return Svc.Objects
            .OfType<IPlayerCharacter>()
            .ToList();
    }

    private bool AllPartyMembersHaveMagicNumber()
    {
        var party = GetParty();
        return party.Count >= Conf.RequiredPartyCount && party.All(HasMagicNumberStatus);
    }

    private int CountPartyMembersWithMagicNumber()
    {
        return GetParty().Count(HasMagicNumberStatus);
    }

    private static bool HasMagicNumberStatus(IPlayerCharacter player)
    {
        return player.StatusList.Any(x => x.StatusId == MagicNumberStatus);
    }

    private static long GetAgeMs(long startedAt)
    {
        return startedAt == 0 ? 0 : Environment.TickCount64 - startedAt;
    }

    private void ClearState()
    {
        _magicNumberCastCount = 0;
        _waitingForFirstHealerLb = false;
        _waitingForSecondHealerLb = false;
        _usedFirstLb = false;
        _usedSecondLb = false;
        _lastCastStartedAt = 0;
        _lastStatusCheckAt = 0;
        _lastEvent = "Ready";
        _lastAttempt = "None";
        _lastPlannedLb = "None";
        _plannedLbCount = 0;
        _lastPlannedLbAt = 0;
        _lastUseResult = null;
    }

    private void DebugLog(string message)
    {
        if(Conf.DebugPrint)
            DuoLog.Information($"[TOP P6 Magic Number LB] {message}");
    }

    public class Config : IEzConfig
    {
        public LbRole SelectedRole = LbRole.None;
        public bool AutomationOnlyInTop = true;
        public bool DebugPrint = true;
        public bool UseJobSpecificActionId = true;
        public int FallbackTankLb3ActionId = (int)DefaultTankLb3ActionId;
        public int FallbackHealerLb3ActionId = (int)DefaultHealerLb3ActionId;
        public int RequiredPartyCount = 8;
    }

    public enum LbRole
    {
        None,
        MT,
        ST,
        H1,
        H2
    }
}
