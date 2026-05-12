using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons;
using ECommons.Configuration;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.GameHelpers.LegacyPlayer;
using ECommons.Hooks.ActionEffectTypes;
using ECommons.ImGuiMethods;
using ECommons.Schedulers;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Dalamud.Bindings.ImGui;
using Splatoon.SplatoonScripting;
using Splatoon.SplatoonScripting.Priority;
using System.Collections.Generic;
using System.Linq;

using ECommons.DalamudServices.Legacy;

namespace SplatoonScriptsOfficial.Duties.Endwalker.The_Omega_Protocol;
internal unsafe class Cosmo_Meteor_Adjuster_Priority : SplatoonScript
{
    #region PublicDef
    public override HashSet<uint> ValidTerritories => [1122];
    public override Metadata? Metadata => new(3, "Redmoon, kudry + Codex");
    #endregion

    #region PrivateDef
    private class FlareContainer
    {
        public IPlayerCharacter character;
        public bool mine = false;
        public float NorthDistance = 0;
        public float EastDistance = 0;
        public float WestDistance = 0;
        public float SouthDistance = 0;

        public FlareContainer(IPlayerCharacter character, bool mine)
        {
            this.character = character;
            this.mine = mine;
        }
    }

    private class CastID
    {
        public const uint CosmoMeteor = 31664;
        public const uint CosmoMeteorFlare = 31668;
    }

    private class VfxPath
    {
        public const string Flare = "vfx/lockon/eff/all_at8s_0v.avfx";
    }

    private enum StackPos
    {
        Undefined = 0,
        North = 1,
        South = 2
    }
    private const uint GuideColor = 4278255360;
    private readonly string[] GuideElements = { "FlareNorth", "FlareEast", "FlareWest", "FlareSouth", "StackNorth", "StackSouth" };
    private readonly string[] PrioritySlots = { "D3", "D4", "H2", "D2", "ST", "D1", "H1", "MT" };
    private readonly Job[] RangedDps = { Job.BRD, Job.MCH, Job.DNC };
    private StackPos _stackPos = StackPos.Undefined;
    private List<FlareContainer> _flarePos = [];
    private bool _gimmickActive = false;
    private bool _isFlareMine = false;
    private bool _isFindRange = false;
    private bool _scheduledFlareHide = false;
    private GameObjectManager* _gom = GameObjectManager.Instance();
    private (string, string)[] _flareData = new (string, string)[3];
    private Config C => Controller.GetConfig<Config>();
    private PriorityData PriorityData => C.PriorityData ??= new();
    #endregion

    #region public
    public override void OnSetup()
    {
        Controller.RegisterElementFromCode("FlareNorth", "{\"Name\":\"\",\"type\":0,\"Enabled\":false,\"refX\":100.0,\"refY\":85.5,\"refZ\":-5.4569678E-12,\"offX\":0.0,\"offY\":0.0,\"offZ\":0.0,\"radius\":1.0,\"color\":3355508546,\"Filled\":false,\"fillIntensity\":0.5,\"thicc\":8.0,\"tether\":false,\"includeRotation\":false}");
        Controller.RegisterElementFromCode("FlareEast", "{\"Name\":\"\",\"type\":0,\"Enabled\":false,\"refX\":114.5,\"refY\":100.0,\"refZ\":-5.4569678E-12,\"offX\":0.0,\"offY\":0.0,\"offZ\":0.0,\"radius\":1.0,\"color\":3355508546,\"Filled\":false,\"fillIntensity\":0.5,\"thicc\":8.0,\"tether\":false,\"includeRotation\":false}");
        Controller.RegisterElementFromCode("StackNorth", "{\"Name\":\"\",\"type\":0,\"Enabled\":false,\"refX\":100.0,\"refY\":85.5,\"refZ\":-5.4569678E-12,\"offX\":0.0,\"offY\":0.0,\"offZ\":0.0,\"radius\":1.0,\"color\":4278245160,\"Filled\":false,\"fillIntensity\":0.5,\"thicc\":8.0,\"tether\":true,\"includeRotation\":false}");
        Controller.RegisterElementFromCode("StackSouth", "{\"Name\":\"\",\"type\":0,\"Enabled\":false,\"refX\":100.0,\"refY\":114.5,\"refZ\":-5.4569678E-12,\"offX\":0.0,\"offY\":0.0,\"offZ\":0.0,\"radius\":1.0,\"color\":3355508546,\"Filled\":false,\"fillIntensity\":0.5,\"thicc\":8.0,\"tether\":true,\"includeRotation\":false}");
        Controller.RegisterElementFromCode("FlareWest", "{\"Name\":\"\",\"type\":0,\"Enabled\":false,\"refX\":85.5,\"refY\":100.0,\"refZ\":-5.4569678E-12,\"offX\":0.0,\"offY\":0.0,\"offZ\":0.0,\"radius\":1.0,\"color\":3355508546,\"Filled\":false,\"fillIntensity\":0.5,\"thicc\":8.0,\"tether\":true,\"includeRotation\":false}");
        Controller.RegisterElementFromCode("FlareSouth", "{\"Name\":\"\",\"type\":0,\"Enabled\":false,\"refX\":100.0,\"refY\":114.5,\"refZ\":-5.4569678E-12,\"offX\":0.0,\"offY\":0.0,\"offZ\":0.0,\"radius\":1.0,\"color\":3355508546,\"Filled\":false,\"fillIntensity\":0.5,\"thicc\":8.0,\"tether\":true,\"includeRotation\":false}");
    }

    public override void OnVFXSpawn(uint target, string vfxPath)
    {
        if(target.GetObject() is IPlayerCharacter character && _gimmickActive)
        {
            if(vfxPath == VfxPath.Flare)
            {
                _flarePos.Add(new FlareContainer(character, false));
                ScheduleFlareHide();

                if(character.Address == Svc.ClientState.LocalPlayer.Address)
                {
                    _flarePos.Last().mine = true;
                    _isFlareMine = true;
                }

                if(RangedDps.Contains(character.GetJob()))
                {
                    _isFindRange = true;
                    _stackPos = StackPos.South;
                }
                else if(_flarePos.Count >= 3 && _isFindRange == false)
                {
                    _stackPos = StackPos.North;
                }

                if(_flarePos.Count >= 3)
                {
                    RefreshGuides();
                }
            }
        }
    }

    public override void OnStartingCast(uint source, uint castId)
    {
        if(castId == CastID.CosmoMeteor)
        {
            _gimmickActive = true;
        }
    }

    public override void OnActionEffectEvent(ActionEffectSet set)
    {
        if(set.Action == null)
            return;

        if(set.Action.Value.RowId == 40165)
        {
            OnReset();
        }
    }

    public override void OnUpdate()
    {
        foreach(var elementName in GuideElements)
        {
            var element = Controller.GetElementByName(elementName);
            if(element.Enabled)
            {
                element.color = GuideColor;
            }
        }
    }

    public override void OnReset()
    {
        _flarePos.Clear();
        _stackPos = StackPos.Undefined;
        _isFlareMine = false;
        _gimmickActive = false;
        _flareData = new (string, string)[3];
        _isFindRange = false;
        _scheduledFlareHide = false;
        Controller.GetRegisteredElements().Each(x => x.Value.Enabled = false);
    }

    public class Config : IEzConfig
    {
        public bool Debug = false;
        public string BasePlayerOverride = "";
        public PriorityData PriorityData = new();
        public bool SwapH1MtWhenD3H1MtStackSouth = false;
    }

    public override void OnSettingsDraw()
    {
        ImGuiEx.Text("# How to determine left/right priority:");
        ImGuiEx.Text("Set players in clockwise priority order from north ranged DPS to northwest.");
        PriorityData.Draw();
        ImGui.Checkbox("StackSouth D3/H1/MT: swap H1 and MT", ref C.SwapH1MtWhenD3H1MtStackSouth);

        DrawOverrideCombo();

        if(ImGui.SmallButton("Test priority"))
        {
            if(TryGetPriorityList(out var list))
            {
                ImGui.SetClipboardText(string.Join(", ", list.Select(x => x.Name.ToString())));
            }
        }

        if(ImGui.CollapsingHeader("Debug"))
        {
            ImGui.Text("Stack Position: " + _stackPos.ToString());
            ImGui.Text("Gimmick Active: " + _gimmickActive.ToString());
            ImGui.Text("Flare Mine: " + _isFlareMine.ToString());
            ImGui.Text("Script Override: " + (string.IsNullOrEmpty(C.BasePlayerOverride) ? "No Override" : C.BasePlayerOverride));
            ImGui.Text("Base Player: " + (GetBasePlayer()?.Name.ToString() ?? "None"));

            foreach(var data in _flareData)
            {
                if(data.Item1 == null || data.Item2 == null) continue;
                ImGui.Text(data.Item1 + ": " + data.Item2);
            }

            List<ImGuiEx.EzTableEntry> entries = [];
            foreach(var character in _flarePos)
            {
                entries.Add(new ImGuiEx.EzTableEntry("Name", () => ImGui.Text(character.character.Name.ToString())));
                entries.Add(new ImGuiEx.EzTableEntry("Mine", () => ImGui.Text(character.mine.ToString())));
                entries.Add(new ImGuiEx.EzTableEntry("East", () => ImGui.Text(character.EastDistance.ToString())));
                entries.Add(new ImGuiEx.EzTableEntry("West", () => ImGui.Text(character.WestDistance.ToString())));
                entries.Add(new ImGuiEx.EzTableEntry("North", () => ImGui.Text(character.NorthDistance.ToString())));
                entries.Add(new ImGuiEx.EzTableEntry("South", () => ImGui.Text(character.SouthDistance.ToString())));
            }
            ImGuiEx.EzTable(entries);
        }
    }
    #endregion

    #region private
    private bool TryGetPriorityList(out List<IPlayerCharacter> values)
    {
        var priorityPlayers = PriorityData.GetPlayers(x => x.IGameObject is IPlayerCharacter)?.ToList() ?? [];
        values = [];

        foreach(var priorityPlayer in priorityPlayers)
        {
            if(priorityPlayer.IGameObject is IPlayerCharacter player)
            {
                values.Add(player);
            }
        }

        return values.Count > 0;
    }

    private void ArbitPosition()
    {
        string[] northElementsArray = { "FlareEast", "FlareSouth", "FlareWest" };
        string[] southElementsArray = { "FlareNorth", "FlareEast", "FlareWest" };
        var basePlayer = GetBasePlayer();

        List<IPlayerCharacter> priorityList = [];

        if(!TryGetPriorityList(out priorityList)) return;

        if(_stackPos == StackPos.North)
        {
            var i = 0;
            foreach(var priorityMember in priorityList)
            {
                if(i >= northElementsArray.Length) break;

                if(_flarePos.Any(x => x.character.Address == priorityMember.Address))
                {
                    _flareData[i] = (northElementsArray[i], priorityMember.Name.ToString());
                    if(basePlayer != null && basePlayer.Address == priorityMember.Address)
                    {
                        EnableGuide(northElementsArray[i]);
                    }
                    i++;
                }
            }
        }
        else if(_stackPos == StackPos.South)
        {
            ApplyStackSouthSpecialPriority(priorityList);

            var i = 0;
            foreach(var priorityMember in priorityList)
            {
                if(i >= southElementsArray.Length) break;

                if(_flarePos.Any(x => x.character.Address == priorityMember.Address))
                {
                    _flareData[i] = (southElementsArray[i], priorityMember.Name.ToString());
                    if(basePlayer != null && basePlayer.Address == priorityMember.Address)
                    {
                        EnableGuide(southElementsArray[i]);
                    }
                    i++;
                }
            }
        }
    }

    private void ApplyStackSouthSpecialPriority(List<IPlayerCharacter> priorityList)
    {
        if(!C.SwapH1MtWhenD3H1MtStackSouth)
            return;

        if(!IsD3H1MtFlareSet(priorityList))
            return;

        var h1Index = GetPrioritySlotIndex(priorityList, "H1");
        var mtIndex = GetPrioritySlotIndex(priorityList, "MT");

        if(h1Index < 0 || mtIndex < 0)
            return;

        var temporary = priorityList[h1Index];
        priorityList[h1Index] = priorityList[mtIndex];
        priorityList[mtIndex] = temporary;
    }

    private bool IsD3H1MtFlareSet(List<IPlayerCharacter> priorityList)
    {
        List<string> flareSlots = [];

        for(var i = 0; i < priorityList.Count && i < PrioritySlots.Length; i++)
        {
            if(_flarePos.Any(x => x.character.Address == priorityList[i].Address))
            {
                flareSlots.Add(PrioritySlots[i]);
            }
        }

        return flareSlots.Count == 3
            && flareSlots.Contains("D3")
            && flareSlots.Contains("H1")
            && flareSlots.Contains("MT");
    }

    private int GetPrioritySlotIndex(List<IPlayerCharacter> priorityList, string slot)
    {
        for(var i = 0; i < priorityList.Count && i < PrioritySlots.Length; i++)
        {
            if(PrioritySlots[i] == slot)
                return i;
        }

        return -1;
    }

    private void EnableGuide(string elementName)
    {
        var element = Controller.GetElementByName(elementName);
        element.Enabled = true;
        element.tether = true;
        element.color = GuideColor;
    }

    private void DrawOverrideCombo()
    {
        ImGui.Separator();
        ImGui.SetNextItemWidth(220);
        if(ImGui.BeginCombo("Script Override", string.IsNullOrEmpty(C.BasePlayerOverride) ? "No Override" : C.BasePlayerOverride))
        {
            if(ImGui.Selectable("No Override", string.IsNullOrEmpty(C.BasePlayerOverride)))
            {
                C.BasePlayerOverride = "";
                RefreshGuidesIfReady();
            }

            foreach(var player in Svc.Objects.OfType<IPlayerCharacter>())
            {
                var name = player.Name.ToString();
                if(ImGui.Selectable(name, C.BasePlayerOverride == name))
                {
                    C.BasePlayerOverride = name;
                    RefreshGuidesIfReady();
                }
            }

            ImGui.EndCombo();
        }
    }

    private IPlayerCharacter? GetBasePlayer()
    {
        if(!string.IsNullOrEmpty(C.BasePlayerOverride))
        {
            var overridePlayer = Svc.Objects
                .OfType<IPlayerCharacter>()
                .FirstOrDefault(x => string.Equals(x.Name.ToString(), C.BasePlayerOverride, System.StringComparison.OrdinalIgnoreCase));

            if(overridePlayer != null)
            {
                return overridePlayer;
            }
        }

        return Svc.ClientState.LocalPlayer as IPlayerCharacter;
    }

    private void RefreshGuidesIfReady()
    {
        if(_flarePos.Count >= 3)
        {
            RefreshGuides();
        }
    }

    private void RefreshGuides()
    {
        Controller.GetRegisteredElements().Each(x => x.Value.Enabled = false);
        ArbitPosition();

        var basePlayer = GetBasePlayer();
        var isBasePlayerFlare = basePlayer != null && _flarePos.Any(x => x.character.Address == basePlayer.Address);

        if(isBasePlayerFlare) return;

        if(_stackPos == StackPos.North)
        {
            EnableGuide("StackNorth");
        }
        else if(_stackPos == StackPos.South)
        {
            EnableGuide("StackSouth");
        }
    }

    private void ScheduleFlareHide()
    {
        if(_scheduledFlareHide) return;

        _scheduledFlareHide = true;
        _ = new TickScheduler(() =>
        {
            Controller.GetRegisteredElements().Each(x => x.Value.Enabled = false);
        }, 8000);
    }
    #endregion
}
