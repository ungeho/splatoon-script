using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Bindings.ImGui;
using ECommons;
using ECommons.Configuration;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using Splatoon.SplatoonScripting;
using Splatoon.SplatoonScripting.Priority;
using System.Collections.Generic;
using System.Linq;

namespace SplatoonScriptsOfficial.Duties.Endwalker.The_Omega_Protocol
{

public unsafe class TOP_Cross_Oversampled_Wave_Cannon : SplatoonScript
{
    private static readonly string[] Slots = new string[] { "MT", "ST", "H1", "H2", "D1", "D2", "D3", "D4" };
    private static readonly string[] THSlots = new string[] { "MT", "ST", "H1", "H2" };
    private static readonly string[] DPSSlots = new string[] { "D1", "D2", "D3", "D4" };

    public override HashSet<uint>? ValidTerritories { get; } = new HashSet<uint>() { 1122 };
    public override Metadata? Metadata => new Metadata(2, "kudry + Codex");

    private Config Conf => Controller.GetConfig<Config>();

    public override void OnSetup()
    {
        Controller.RegisterElementFromCode("RoleMonitorCount", "{\"Name\":\"RoleMonitorCount\",\"Enabled\":false,\"refX\":100.0,\"refY\":100.0,\"refZ\":0.0,\"radius\":0.0,\"overlayBGColor\":3355443200,\"overlayTextColor\":3355508480,\"overlayVOffset\":3.0,\"overlayFScale\":5.0,\"thicc\":5.0,\"overlayText\":\"TH 0/4\",\"tether\":false}");

        RegisterGuide("検知なし Tank 北 31595 Right", 98.26352f, 90.15192f);
        RegisterGuide("検知あり Tank  北 31595 Right", 94.26424f, 91.80848f);
        RegisterGuide("検知なし Tank 北 31596 Left", 101.73648f, 90.15192f);
        RegisterGuide("検知あり Tank 北 31596 Left", 105.73576f, 91.80848f);
        RegisterGuide("検知なし Tank 東", 110.0f, 100.0f);
        RegisterGuide("検知あり Tank 東 + 南", 108.19152f, 105.73576f);
        RegisterGuide("検知あり Tank 東 + 北", 108.19152f, 94.26424f);

        RegisterGuide("検知なし Healer 北 31595 Right", 98.38762f, 81.5704f);
        RegisterGuide("検知あり Healer  北 31595 Right", 94.59112f, 82.30836f);
        RegisterGuide("検知なし Healer 北 31596 Left", 101.61238f, 81.5704f);
        RegisterGuide("検知あり Healer 北 31596 Left", 105.40888f, 82.30836f);
        RegisterGuide("検知なし Healer 東", 118.5f, 100.0f);
        RegisterGuide("検知あり Healer 東 + 南", 117.69164f, 105.40888f);

        RegisterGuide("検知なし Melee 南 31595 Right", 98.26352f, 109.84808f);
        RegisterGuide("検知あり Melee  南 31595 Right", 94.26424f, 108.19152f);
        RegisterGuide("検知なし Melee 南 31596 Left", 101.73648f, 109.84808f);
        RegisterGuide("検知あり Melee 南 31596 Left", 105.73576f, 108.19152f);
        RegisterGuide("検知なし Melee 西", 90.0f, 100.0f);
        RegisterGuide("検知あり Melee 西 + 北", 91.80848f, 94.26424f);
        RegisterGuide("検知あり Melee 西 + 南", 91.80848f, 105.73576f);

        RegisterGuide("検知なし Range 南 31595 Right", 98.38762f, 118.4296f);
        RegisterGuide("検知あり Range 南 31595 Right", 94.59112f, 117.69164f);
        RegisterGuide("検知なし Range 南 31596 Left", 101.61238f, 118.4296f);
        RegisterGuide("検知あり Range 南 31596 Left", 105.40888f, 117.69164f);
        RegisterGuide("検知なし Range 西", 81.5f, 100.0f);
        RegisterGuide("検知あり Range 西 + 北", 82.30836f, 94.59112f);
    }

    public override void OnUpdate()
    {
        OffAll();

        if (!IsMechanicRunning(out var castId))
            return;

        var localPlayer = GetBasePlayer();
        if (localPlayer == null)
            return;

        var mySlot = GetPrioritySlot(localPlayer);
        if (mySlot == "Unknown")
            return;

        var isSupport = IsTHSlot(mySlot);
        var monitorSlots = GetMonitorSlots(isSupport);
        var monitorCount = monitorSlots.Count;

        var countElement = Controller.GetElementByName("RoleMonitorCount");
        countElement.overlayText = $"{(isSupport ? "TH" : "DPS")} {monitorCount}/4";
        countElement.Enabled = true;

        var guideName = ResolveGuideName(isSupport, castId, mySlot, GetMonitorKey(monitorSlots));
        if (guideName == null)
            return;

        Controller.GetElementByName(guideName).Enabled = true;
    }

    private void RegisterGuide(string name, float x, float y)
    {
        Controller.RegisterElementFromCode(name, $"{{\"Name\":\"{name}\",\"Enabled\":false,\"refX\":{x},\"refY\":{y},\"refZ\":0.0,\"radius\":1.0,\"color\":3358064384,\"Filled\":false,\"fillIntensity\":0.5,\"overlayBGColor\":1879048192,\"overlayTextColor\":3372220415,\"thicc\":4.0,\"overlayText\":\"\",\"tether\":true}}");
    }

    private void OffAll()
    {
        Controller.GetRegisteredElements().Each(x => x.Value.Enabled = false);
    }

    public override void OnSettingsDraw()
    {
        ImGuiEx.Text("Priority list: MT, ST, H1, H2, D1, D2, D3, D4");
        ImGuiEx.Text("Set players in this exact order.");
        Conf.PriorityData.Draw();

        ImGui.Separator();
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

        ImGui.Checkbox("PrintDebug", ref Conf.IsDebug);
        if (ImGui.CollapsingHeader("Debug"))
        {
            var localPlayer = GetBasePlayer();
            var mySlot = localPlayer == null ? "Unknown" : GetPrioritySlot(localPlayer);
            var isSupport = mySlot == "Unknown" ? false : IsTHSlot(mySlot);
            var monitorSlots = mySlot == "Unknown" ? new List<string>() : GetMonitorSlots(isSupport);
            var castText = IsMechanicRunning(out var castId) ? castId.ToString() : "None";

            ImGuiEx.Text($"My priority slot: {mySlot}");
            ImGuiEx.Text($"My role group: {(isSupport ? "TH" : "DPS")}");
            ImGuiEx.Text($"Script Override: {(string.IsNullOrEmpty(Conf.BasePlayerOverride) ? "No Override" : Conf.BasePlayerOverride)}");
            ImGuiEx.Text($"Base player: {(localPlayer == null ? "None" : localPlayer.Name.ToString())}");
            ImGuiEx.Text($"Boss cast: {castText}");
            ImGuiEx.Text($"Role group monitor count: {monitorSlots.Count}/4");
            ImGuiEx.Text($"Role group monitor slots: {GetMonitorKey(monitorSlots)}");
            ImGuiEx.Text($"Guide: {ResolveGuideName(isSupport, castId, mySlot, GetMonitorKey(monitorSlots)) ?? "None"}");
        }
    }

    private List<string> GetMonitorSlots(bool isSupport)
    {
        var groupSlots = isSupport ? THSlots : DPSSlots;
        var players = Conf.PriorityData.GetPlayers(x => x.IGameObject is IPlayerCharacter).ToList();
        var result = new List<string>();

        for (var i = 0; i < players.Count && i < Slots.Length; i++)
        {
            if (!groupSlots.Contains(Slots[i]))
                continue;

            if (players[i].IGameObject is IPlayerCharacter player && player.HasOversampledMonitor())
                result.Add(Slots[i]);
        }

        return result;
    }

    private string GetMonitorKey(List<string> monitorSlots)
    {
        var order = monitorSlots.Any(x => IsTHSlot(x)) ? THSlots : DPSSlots;
        return string.Join(",", order.Where(monitorSlots.Contains));
    }

    private static bool IsMechanicRunning(out uint castId)
    {
        var caster = Svc.Objects.FirstOrDefault(x => x is IBattleChara b && (b.CastActionId == 31595 || b.CastActionId == 31596)) as IBattleChara;
        if (caster != null)
        {
            castId = caster.CastActionId;
            return true;
        }

        castId = 0;
        return false;
    }

    private string GetPrioritySlot(IPlayerCharacter player)
    {
        var players = Conf.PriorityData.GetPlayers(x => x.IGameObject is IPlayerCharacter).ToList();

        for (var i = 0; i < players.Count && i < Slots.Length; i++)
        {
            if (players[i].Name.ToString() == player.Name.ToString())
                return Slots[i];
        }

        return "Unknown";
    }

    private IPlayerCharacter? GetBasePlayer()
    {
        if (!string.IsNullOrEmpty(Conf.BasePlayerOverride))
        {
            var overridePlayer = Svc.Objects
                .OfType<IPlayerCharacter>()
                .FirstOrDefault(x => string.Equals(x.Name.ToString(), Conf.BasePlayerOverride, System.StringComparison.OrdinalIgnoreCase));

            if (overridePlayer != null)
                return overridePlayer;
        }

        return Player.Object;
    }

    private static bool IsTHSlot(string slot)
    {
        return slot is "MT" or "ST" or "H1" or "H2";
    }

    private static string? ResolveGuideName(bool isSupport, uint castId, string slot, string monitorKey)
    {
        if (isSupport)
            return ResolveTHGuideName(castId, slot, monitorKey);

        return ResolveDPSGuideName(castId, slot, monitorKey);
    }

    private static string? ResolveTHGuideName(uint castId, string slot, string monitorKey)
    {
        return monitorKey switch
        {
            "" => slot switch
            {
                "MT" => TankNorth(castId, false),
                "ST" => "検知なし Tank 東",
                "H1" => HealerNorth(castId, false),
                "H2" => "検知なし Healer 東",
                _ => null
            },
            "MT" or "ST" => slot switch
            {
                "MT" => monitorKey == "MT" ? "検知あり Tank 東 + 南" : TankNorth(castId, false),
                "ST" => monitorKey == "ST" ? "検知あり Tank 東 + 南" : TankNorth(castId, false),
                "H1" => HealerNorth(castId, false),
                "H2" => "検知なし Healer 東",
                _ => null
            },
            "H1" or "H2" => slot switch
            {
                "MT" => TankNorth(castId, false),
                "ST" => "検知なし Tank 東",
                "H1" => monitorKey == "H1" ? "検知あり Healer 東 + 南" : HealerNorth(castId, false),
                "H2" => monitorKey == "H2" ? "検知あり Healer 東 + 南" : HealerNorth(castId, false),
                _ => null
            },
            "MT,ST" => slot switch
            {
                "MT" => TankNorth(castId, true),
                "ST" => "検知あり Tank 東 + 南",
                "H1" => HealerNorth(castId, false),
                "H2" => "検知なし Healer 東",
                _ => null
            },
            "MT,H1" => slot switch
            {
                "MT" => "検知あり Tank 東 + 南",
                "ST" => TankNorth(castId, false),
                "H1" => HealerNorth(castId, true),
                "H2" => "検知なし Healer 東",
                _ => null
            },
            "MT,H2" => slot switch
            {
                "MT" => TankNorth(castId, true),
                "ST" => "検知なし Tank 東",
                "H1" => HealerNorth(castId, false),
                "H2" => "検知あり Healer 東 + 南",
                _ => null
            },
            "ST,H1" => slot switch
            {
                "MT" => TankNorth(castId, false),
                "ST" => "検知あり Tank 東 + 南",
                "H1" => HealerNorth(castId, true),
                "H2" => "検知なし Healer 東",
                _ => null
            },
            "ST,H2" => slot switch
            {
                "MT" => "検知なし Tank 東",
                "ST" => TankNorth(castId, true),
                "H1" => HealerNorth(castId, false),
                "H2" => "検知あり Healer 東 + 南",
                _ => null
            },
            "H1,H2" => slot switch
            {
                "MT" => TankNorth(castId, false),
                "ST" => "検知なし Tank 東",
                "H1" => HealerNorth(castId, true),
                "H2" => "検知あり Healer 東 + 南",
                _ => null
            },
            "MT,ST,H1" => slot switch
            {
                "MT" => TankNorth(castId, true),
                "ST" => "検知あり Tank 東 + 北",
                "H1" => "検知あり Healer 東 + 南",
                "H2" => HealerNorth(castId, false),
                _ => null
            },
            "MT,ST,H2" => slot switch
            {
                "MT" => TankNorth(castId, true),
                "ST" => "検知あり Tank 東 + 北",
                "H1" => HealerNorth(castId, false),
                "H2" => "検知あり Healer 東 + 南",
                _ => null
            },
            "MT,H1,H2" => slot switch
            {
                "MT" => "検知あり Tank 東 + 北",
                "ST" => TankNorth(castId, false),
                "H1" => HealerNorth(castId, true),
                "H2" => "検知あり Healer 東 + 南",
                _ => null
            },
            "ST,H1,H2" => slot switch
            {
                "MT" => TankNorth(castId, false),
                "ST" => "検知あり Tank 東 + 北",
                "H1" => HealerNorth(castId, true),
                "H2" => "検知あり Healer 東 + 南",
                _ => null
            },
            _ => null
        };
    }

    private static string? ResolveDPSGuideName(uint castId, string slot, string monitorKey)
    {
        return monitorKey switch
        {
            "" => slot switch
            {
                "D1" => "検知なし Melee 西",
                "D2" => MeleeSouth(castId, false),
                "D3" => "検知なし Range 西",
                "D4" => RangeSouth(castId, false),
                _ => null
            },
            "D1" => slot switch
            {
                "D1" => "検知あり Melee 西 + 北",
                "D2" => MeleeSouth(castId, false),
                "D3" => "検知なし Range 西",
                "D4" => RangeSouth(castId, false),
                _ => null
            },
            "D2" => slot switch
            {
                "D1" => MeleeSouth(castId, false),
                "D2" => "検知あり Melee 西 + 北",
                "D3" => "検知なし Range 西",
                "D4" => RangeSouth(castId, false),
                _ => null
            },
            "D3" => slot switch
            {
                "D1" => "検知なし Melee 西",
                "D2" => MeleeSouth(castId, false),
                "D3" => "検知あり Range 西 + 北",
                "D4" => RangeSouth(castId, false),
                _ => null
            },
            "D4" => slot switch
            {
                "D1" => "検知なし Melee 西",
                "D2" => MeleeSouth(castId, false),
                "D3" => RangeSouth(castId, false),
                "D4" => "検知あり Range 西 + 北",
                _ => null
            },
            "D1,D2" => slot switch
            {
                "D1" => "検知あり Melee 西 + 北",
                "D2" => MeleeSouth(castId, true),
                "D3" => "検知なし Range 西",
                "D4" => RangeSouth(castId, false),
                _ => null
            },
            "D1,D3" => slot switch
            {
                "D1" => MeleeSouth(castId, true),
                "D2" => "検知なし Melee 西",
                "D3" => "検知あり Range 西 + 北",
                "D4" => RangeSouth(castId, false),
                _ => null
            },
            "D1,D4" => slot switch
            {
                "D1" => "検知あり Melee 西 + 北",
                "D2" => MeleeSouth(castId, false),
                "D3" => "検知なし Range 西",
                "D4" => RangeSouth(castId, true),
                _ => null
            },
            "D2,D3" => slot switch
            {
                "D1" => "検知なし Melee 西",
                "D2" => MeleeSouth(castId, true),
                "D3" => "検知あり Range 西 + 北",
                "D4" => RangeSouth(castId, false),
                _ => null
            },
            "D2,D4" => slot switch
            {
                "D1" => MeleeSouth(castId, false),
                "D2" => "検知あり Melee 西 + 北",
                "D3" => "検知なし Range 西",
                "D4" => RangeSouth(castId, true),
                _ => null
            },
            "D3,D4" => slot switch
            {
                "D1" => "検知なし Melee 西",
                "D2" => MeleeSouth(castId, false),
                "D3" => "検知あり Range 西 + 北",
                "D4" => RangeSouth(castId, true),
                _ => null
            },
            "D1,D2,D3" => slot switch
            {
                "D1" => "検知あり Melee 西 + 南",
                "D2" => MeleeSouth(castId, true),
                "D3" => "検知あり Range 西 + 北",
                "D4" => RangeSouth(castId, false),
                _ => null
            },
            "D1,D2,D4" => slot switch
            {
                "D1" => "検知あり Melee 西 + 南",
                "D2" => MeleeSouth(castId, true),
                "D3" => RangeSouth(castId, false),
                "D4" => "検知あり Range 西 + 北",
                _ => null
            },
            "D1,D3,D4" => slot switch
            {
                "D1" => "検知あり Melee 西 + 南",
                "D2" => MeleeSouth(castId, false),
                "D3" => "検知あり Range 西 + 北",
                "D4" => RangeSouth(castId, true),
                _ => null
            },
            "D2,D3,D4" => slot switch
            {
                "D1" => MeleeSouth(castId, false),
                "D2" => "検知あり Melee 西 + 南",
                "D3" => "検知あり Range 西 + 北",
                "D4" => RangeSouth(castId, true),
                _ => null
            },
            _ => null
        };
    }

    private static string? TankNorth(uint castId, bool monitor)
    {
        if (castId == 31595)
            return monitor ? "検知あり Tank  北 31595 Right" : "検知なし Tank 北 31595 Right";

        if (castId == 31596)
            return monitor ? "検知あり Tank 北 31596 Left" : "検知なし Tank 北 31596 Left";

        return null;
    }

    private static string? HealerNorth(uint castId, bool monitor)
    {
        if (castId == 31595)
            return monitor ? "検知あり Healer  北 31595 Right" : "検知なし Healer 北 31595 Right";

        if (castId == 31596)
            return monitor ? "検知あり Healer 北 31596 Left" : "検知なし Healer 北 31596 Left";

        return null;
    }

    private static string? MeleeSouth(uint castId, bool monitor)
    {
        if (castId == 31595)
            return monitor ? "検知あり Melee  南 31595 Right" : "検知なし Melee 南 31595 Right";

        if (castId == 31596)
            return monitor ? "検知あり Melee 南 31596 Left" : "検知なし Melee 南 31596 Left";

        return null;
    }

    private static string? RangeSouth(uint castId, bool monitor)
    {
        if (castId == 31595)
            return monitor ? "検知あり Range 南 31595 Right" : "検知なし Range 南 31595 Right";

        if (castId == 31596)
            return monitor ? "検知あり Range 南 31596 Left" : "検知なし Range 南 31596 Left";

        return null;
    }

    public class Config : IEzConfig
    {
        public bool IsDebug = false;
        public string BasePlayerOverride = "";
        public PriorityData PriorityData = new PriorityData();
    }
}

public static class CrossOWCExtensions
{
    public static bool HasOversampledMonitor(this IPlayerCharacter player)
    {
        return player.StatusList.Any(x => x.StatusId == 3452 || x.StatusId == 3453);
    }
}
}
