using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Colors;
using ECommons;
using ECommons.Configuration;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.Hooks;
using ECommons.ImGuiMethods;
using ECommons.MathHelpers;
using ECommons.Schedulers;
using Dalamud.Bindings.ImGui;
using Splatoon.SplatoonScripting;
using Splatoon.SplatoonScripting.Priority;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;

using ECommons.DalamudServices.Legacy;

namespace SplatoonScriptsOfficial.Duties.Endwalker.The_Omega_Protocol;

public unsafe class TOP_P1_Program_Loop_Priority_With_Self_Priority : SplatoonScript
{
    private const uint ProgramLoopCast = 31491;
    private const uint PantokratorCast = 31501;
    private const uint PantokratorFlameThrowerCast = 32368;
    private const uint PantokratorStatus1 = 0xBBC;
    private const uint PantokratorStatus2 = 0xBBD;
    private const uint PantokratorStatus3 = 0xBBE;
    private const uint PantokratorStatus4 = 0xD7B;

    public override HashSet<uint> ValidTerritories => [1122];
    public override Metadata Metadata => new(19, "NightmareXIV, damolitionn, kudry");
    private Config Conf => Controller.GetConfig<Config>();
    private HashSet<uint> TetheredPlayers = [];
    private List<uint> Towers = [];
    private List<uint> TowerOrder = [];
    private List<uint> TetherOrder = [];
    private string NewPlayer = "";
    private uint myTether = 0;
    private DateTime programLoopPriorityDisplayUntil = DateTime.MinValue;

    public override void OnSetup()
    {
        SetupElements();
    }

    private void SetupElements()
    {
        Controller.Clear();
        Controller.RegisterElement("dbg1", new(1) { Enabled = false, refActorComparisonType = 2, overlayVOffset = 1, radius = 3f, color = Conf.TowerColor1.ToUint() });
        Controller.RegisterElement("dbg2", new(1) { Enabled = false, refActorComparisonType = 2, overlayVOffset = 1, radius = 3f, color = Conf.TowerColor1.ToUint() });
        Controller.RegisterElementFromCode("TetherAOE1", "{\"Name\":\"\",\"type\":1,\"Enabled\":false,\"radius\":14.0,\"Donut\":1.0,\"color\":3372155125,\"fillIntensity\":0.6857143,\"thicc\":4.0,\"refActorObjectID\":268635045,\"refActorComparisonType\":2,\"onlyTargetable\":true}");
        Controller.RegisterElementFromCode("TetherAOE2", "{\"Name\":\"\",\"type\":1,\"Enabled\":false,\"radius\":14.0,\"Donut\":1.0,\"color\":3372155125,\"fillIntensity\":0.6857143,\"thicc\":4.0,\"refActorObjectID\":268635045,\"refActorComparisonType\":2,\"onlyTargetable\":true}");
        Controller.RegisterElement("Tether1", new(2) { thicc = 5f, radius = 0f });
        Controller.RegisterElement("Tether2", new(2) { thicc = 5f, radius = 0f });
        Controller.RegisterElement("SelfTetherReminder", new(1) { Enabled = false, refActorType = 1, radius = 0, overlayVOffset = 2f, overlayTextColor = ImGuiColors.DalamudWhite.ToUint() });
        Controller.RegisterElement("SelfTower", new(1) { Enabled = false, refActorComparisonType = 2, radius = 3f, thicc = 7f, Filled = false, fillIntensity = 0f, overlayText = "Take tower", overlayTextColor = 0xFF000000, tether = true, overlayBGColor = ImGuiColors.ParsedPink.ToUint() });
        Controller.RegisterElementFromCode("SelfPairPriority", "{\"Enabled\":false,\"Name\":\"\",\"refX\":100.0,\"refY\":100.0,\"radius\":0.0,\"overlayBGColor\":3355443200,\"overlayTextColor\":3355508527,\"overlayVOffset\":2.0,\"overlayFScale\":3.0,\"thicc\":1.0,\"overlayText\":\"Priority 2\"}");
        Controller.TryRegisterLayoutFromCode("Proximity", "~Lv2~{\"Enabled\":false,\"Name\":\"Proximity\",\"Group\":\"\",\"ZoneLockH\":[1122],\"ElementsL\":[{\"Name\":\"\",\"type\":1,\"radius\":0.0,\"color\":4278129920,\"thicc\":4.0,\"refActorPlaceholder\":[\"<2>\",\"<3>\",\"<4>\",\"<5>\",\"<6>\",\"<7>\",\"<8>\"],\"refActorComparisonType\":5,\"tether\":true}],\"MaxDistance\":15.2,\"UseDistanceLimit\":true,\"DistanceLimitType\":1}", out _);
        Controller.RegisterElementFromCode("SafeNorth", "{\"Enabled\":false,\"Name\":\"\",\"refX\":100.0,\"refY\":84.0,\"radius\":4.0,\"color\":4278190080,\"Filled\":false,\"fillIntensity\":0.0,\"thicc\":5.0}");
        Controller.RegisterElementFromCode("SafeSouth", "{\"Enabled\":false,\"Name\":\"\",\"refX\":100.0,\"refY\":116.0,\"radius\":4.0,\"color\":4278190080,\"Filled\":false,\"fillIntensity\":0.0,\"thicc\":5.0}");
        Controller.RegisterElementFromCode("SafeWest", "{\"Enabled\":false,\"Name\":\"\",\"refX\":84.0,\"refY\":100.0,\"radius\":4.0,\"color\":4278190080,\"Filled\":false,\"fillIntensity\":0.0,\"thicc\":5.0}");
        Controller.RegisterElementFromCode("SafeEast", "{\"Enabled\":false,\"Name\":\"\",\"refX\":116.0,\"refY\":100.0,\"radius\":4.0,\"color\":4278190080,\"Filled\":false,\"fillIntensity\":0.0,\"thicc\":5.0}");
        Controller.RegisterElementFromCode("NonRoleSafeSpot", "{\"Name\":\"\",\"type\":0,\"Enabled\":false,\"refX\":100.0,\"refY\":100.0,\"radius\":0.8,\"color\":4278255360,\"Filled\":false,\"fillIntensity\":0.0,\"thicc\":8.0,\"tether\":true}");
    }

    public override void OnUpdate()
    {
        if (TetherOrder.Count == 8)
        {
            UpdateTethers();
        }
        UpdateSelfPairPriority();
    }

    public override void OnTetherCreate(uint source, uint target, uint data2, uint data3, uint data5)
    {
        if (IsOmega(target, out _))
        {
            TetheredPlayers.Add(source);
            //UpdateTethers();
        }
    }

    public override void OnTetherRemoval(uint source, uint data2, uint data3, uint data5)
    {
        TetheredPlayers.Remove(source);
        //UpdateTethers();
    }

    private void UpdateTethers()
    {
        var tetheredPlayers = TetheredPlayers.ToArray();
        if (Controller.Scene == 2 && tetheredPlayers.Length >= 2)
        {
            var omega = GetOmega();
            if (Conf.Debug && Conf.Towers != TowerStartPoint.Disable_towers)
            {
                var cTowers = Towers.TakeLast(2).ToArray();
                if (cTowers.Length == 2)
                {
                    {
                        if (Controller.TryGetElementByName("dbg1", out var e))
                        {
                            e.Enabled = true;
                            e.refActorObjectID = cTowers[0];
                            e.overlayText = Conf.Debug ? $"{GetTowerAngle(cTowers[0].GetObject().Position.ToVector2())}" : "";
                        }
                    }
                    {
                        if (Controller.TryGetElementByName("dbg2", out var e))
                        {
                            e.Enabled = true;
                            e.refActorObjectID = cTowers[1];
                            e.overlayText = Conf.Debug ? $"{GetTowerAngle(cTowers[1].GetObject().Position.ToVector2())}" : "";
                        }
                    }
                }
            }

            {
                if (Controller.TryGetElementByName("SelfTetherReminder", out var e))
                {
                    if (IsTakingCurrentTether(Svc.ClientState.LocalPlayer.EntityId))
                    {
                        e.Enabled = true;
                        myTether = 0;

                        if (Conf.DisplayTetherSafeSpots)
                        {
                            SwitchTetherSafeSpots(true);
                            var currentTowers = GetCurrentTowers();
                            if (currentTowers.Length == 2)
                            {
                                { if (Controller.TryGetElementByName($"Safe{MathHelper.GetCardinalDirection(new(100, 100), currentTowers[0].GetObject().Position.ToVector2())}", out var s)) { s.Enabled = false; } }
                                { if (Controller.TryGetElementByName($"Safe{MathHelper.GetCardinalDirection(new(100, 100), currentTowers[1].GetObject().Position.ToVector2())}", out var s)) { s.Enabled = false; } }
                            }
                        }
                        else
                        {
                            SwitchTetherSafeSpots(false);
                        }

                        if (tetheredPlayers.Contains(Svc.ClientState.LocalPlayer.EntityId))
                        {
                            e.overlayBGColor = Conf.ValidTetherColor.ToUint();
                            e.overlayTextColor = Conf.OverlayTextColor.ToUint();
                            e.overlayFScale = 1;
                            e.overlayText = "Tether";
                            if (Conf.UseProximity && Controller.TryGetLayoutByName("Proximity", out var l))
                            {
                                l.ElementsL[0].color = Conf.ProximityColor.ToUint();
                                l.Enabled = true;
                            }

                            if (Conf.DisplayTetherSafeSpots && Conf.TetherSafeSpotEnableDetect)
                            {
                                var SafeSpots = Enum.GetValues<CardinalDirection>().Select(x => Controller.GetElementByName($"Safe{x}")).Where(x => x != null && x.Enabled).OrderBy(x => GetTowerAngle(new Vector2(x.refX, x.refY))).ToArray();

                                if (SafeSpots.Length == 2)
                                {
                                    var pair = GetPairNumber(TowerOrder, GetTetherMechanicStep());
                                    if (pair.Count() == 2)
                                    {
                                        var players = Conf.PriorityData.GetPlayers(player => pair.Any(pairMember => pairMember == player.IGameObject.EntityId));

                                        if (players == null) return;

                                        if (players[0].IGameObject.EntityId == Svc.ClientState.LocalPlayer.EntityId)
                                        {
                                            SafeSpots[0].tether = true;
                                        }
                                        else
                                        {
                                            SafeSpots[1].tether = true;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            e.overlayBGColor = GradientColor.Get(Conf.InvalidTetherColor1, Conf.InvalidTetherColor2, 500).ToUint();
                            e.overlayTextColor = Conf.OverlayTextColor.ToUint();
                            e.overlayFScale = Conf.InvalidOverlayScale;
                            e.overlayText = "!!! PICK UP TETHER !!!";
                            if (Conf.EnlargeMyTether)
                            {
                                myTether = GetMyTetherToPick(tetheredPlayers);
                            }
                        }
                    }
                    else
                    {
                        myTether = 0;
                        if (Controller.TryGetLayoutByName("Proximity", out var l))
                        {
                            l.Enabled = false;
                        }
                        e.Enabled = false;
                        SwitchTetherSafeSpots(false);
                    }
                }
            }
            {
                if (Controller.TryGetElementByName("TetherAOE1", out var e))
                {
                    e.Enabled = IsTakingCurrentTether(tetheredPlayers[0]) || tetheredPlayers[0] == myTether || Conf.ShowAOEAlways;
                    e.refActorObjectID = tetheredPlayers[0];
                    e.radius = 14f;
                    e.Donut = 1f;
                    e.color = Conf.TetherAOECol.ToUint();
                    e.fillIntensity = 0.6857143f;
                    e.thicc = 4f;
                }
            }
            {
                if (Controller.TryGetElementByName("Tether1", out var e))
                {
                    e.Enabled = true;
                    e.SetRefPosition(omega.Position);
                    e.SetOffPosition(tetheredPlayers[0].GetObject().Position);
                    e.thicc = tetheredPlayers[0] == myTether ? 12f : 5f;
                    e.overlayText = tetheredPlayers[0] == myTether ? "Pick this tether" : "";
                    e.color = (IsTakingCurrentTether(tetheredPlayers[0]) ? Conf.ValidTetherColor : GradientColor.Get(Conf.InvalidTetherColor1, Conf.InvalidTetherColor2, 500)).ToUint();
                }
            }
            {
                if (Controller.TryGetElementByName("TetherAOE2", out var e))
                {
                    e.Enabled = IsTakingCurrentTether(tetheredPlayers[1]) || tetheredPlayers[1] == myTether || Conf.ShowAOEAlways;
                    e.refActorObjectID = tetheredPlayers[1];
                    e.radius = 14f;
                    e.Donut = 1f;
                    e.color = Conf.TetherAOECol.ToUint();
                    e.fillIntensity = 0.6857143f;
                    e.thicc = 4f;
                }
            }
            {
                if (Controller.TryGetElementByName("Tether2", out var e))
                {
                    e.Enabled = true;
                    e.SetRefPosition(omega.Position);
                    e.SetOffPosition(tetheredPlayers[1].GetObject().Position);
                    e.thicc = tetheredPlayers[1] == myTether ? 12f : 5f;
                    e.overlayText = tetheredPlayers[1] == myTether ? "Pick this tether" : "";
                    e.color = (IsTakingCurrentTether(tetheredPlayers[1]) ? Conf.ValidTetherColor : GradientColor.Get(Conf.InvalidTetherColor1, Conf.InvalidTetherColor2, 500)).ToUint();
                }
            }
            {
                if (Conf.Towers != TowerStartPoint.Disable_towers && Controller.TryGetElementByName("SelfTower", out var e))
                {
                    if (IsTakingCurrentTower(Svc.ClientState.LocalPlayer.EntityId))
                    {
                        e.Enabled = true;
                        e.color = GradientColor.Get(Conf.TowerColor1, Conf.TowerColor2).ToUint();
                        e.Filled = false;
                        e.fillIntensity = 0f;
                        e.overlayBGColor = e.color;
                        e.overlayTextColor = Conf.OverlayTextColor.ToUint();
                        var currentTowers = GetCurrentTowers();
                        if (currentTowers.Length == 2)
                        {
                            var players = Conf.PriorityData.GetPlayers(player => IsTakingCurrentTower(player.IGameObject.EntityId));

                            if (players == null) return;

                            if (players[0].IGameObject.EntityId == Svc.ClientState.LocalPlayer.EntityId)
                            {
                                e.refActorObjectID = currentTowers[0];

                            }
                            else
                            {
                                e.refActorObjectID = currentTowers[1];

                            }
                        }
                    }
                    else
                    {
                        e.Enabled = false;
                    }
                }
            }
            UpdateNonRoleSafeSpot();
        }
        else
        {
            Controller.GetElementByName("TetherAOE1").Enabled = false;
            Controller.GetElementByName("TetherAOE2").Enabled = false;
            Controller.GetElementByName("Tether1").Enabled = false;
            Controller.GetElementByName("Tether2").Enabled = false;
            Controller.GetElementByName("SelfTetherReminder").Enabled = false;
            Controller.GetElementByName("SelfPairPriority").Enabled = false;
            Controller.GetElementByName("dbg1").Enabled = false;
            Controller.GetElementByName("dbg2").Enabled = false;
            Controller.GetElementByName("NonRoleSafeSpot").Enabled = false;
            if (Controller.TryGetLayoutByName("Proximity", out var l)) { l.Enabled = false; }
            SwitchTetherSafeSpots(false);
        }
    }

    private void UpdateNonRoleSafeSpot()
    {
        if (!Controller.TryGetElementByName("NonRoleSafeSpot", out var e))
        {
            return;
        }

        var localPlayer = Svc.ClientState.LocalPlayer;
        if (localPlayer == null
            || Conf.Towers == TowerStartPoint.Disable_towers
            || IsTakingCurrentTether(localPlayer.EntityId)
            || IsTakingCurrentTower(localPlayer.EntityId))
        {
            e.Enabled = false;
            return;
        }

        var currentTowers = GetCurrentTowers();
        if (currentTowers.Length != 2)
        {
            e.Enabled = false;
            return;
        }

        var candidates = GetNonRoleSafeSpotCandidates(currentTowers);
        if (candidates.Length == 0)
        {
            e.Enabled = false;
            return;
        }

        var currentPosition = localPlayer.Position.ToVector2();
        var safeSpot = candidates.OrderBy(candidate => Vector2.DistanceSquared(currentPosition, candidate)).First();
        e.Enabled = true;
        e.refX = safeSpot.X;
        e.refY = safeSpot.Y;
        e.radius = 0.8f;
        e.color = 0xFF00FF00;
        e.thicc = 8f;
        e.Filled = false;
        e.fillIntensity = 0f;
        e.tether = true;
    }

    private Vector2[] GetNonRoleSafeSpotCandidates(uint[] currentTowers)
    {
        var directions = currentTowers
            .Select(tower => MathHelper.GetCardinalDirection(new(100, 100), tower.GetObject().Position.ToVector2()))
            .Distinct()
            .ToArray();

        if (directions.Length != 2)
        {
            return [];
        }

        var first = directions[0];
        var second = directions[1];

        if (AreOpposite(first, second))
        {
            return directions.Select(GetTowerSideSafeSpot).ToArray();
        }

        return [GetBetweenTowersSafeSpot(first, second)];
    }

    private static bool AreOpposite(CardinalDirection first, CardinalDirection second)
    {
        return (first == CardinalDirection.North && second == CardinalDirection.South)
            || (first == CardinalDirection.South && second == CardinalDirection.North)
            || (first == CardinalDirection.East && second == CardinalDirection.West)
            || (first == CardinalDirection.West && second == CardinalDirection.East);
    }

    private static Vector2 GetTowerSideSafeSpot(CardinalDirection direction)
    {
        return direction switch
        {
            CardinalDirection.North => new(100f, 91f),
            CardinalDirection.East => new(109f, 100f),
            CardinalDirection.South => new(100f, 109f),
            CardinalDirection.West => new(91f, 100f),
            _ => new(100f, 100f),
        };
    }

    private static Vector2 GetBetweenTowersSafeSpot(CardinalDirection first, CardinalDirection second)
    {
        if (HasDirections(first, second, CardinalDirection.North, CardinalDirection.East)) return new(107.071f, 92.928f);
        if (HasDirections(first, second, CardinalDirection.East, CardinalDirection.South)) return new(107.071f, 107.071f);
        if (HasDirections(first, second, CardinalDirection.South, CardinalDirection.West)) return new(92.928f, 107.071f);
        if (HasDirections(first, second, CardinalDirection.West, CardinalDirection.North)) return new(92.928f, 92.928f);
        return new(100f, 100f);
    }

    private static bool HasDirections(CardinalDirection first, CardinalDirection second, CardinalDirection expectedFirst, CardinalDirection expectedSecond)
    {
        return (first == expectedFirst && second == expectedSecond)
            || (first == expectedSecond && second == expectedFirst);
    }

    private void SwitchTetherSafeSpots(bool enabled)
    {
        {
            if (Controller.TryGetElementByName("SafeNorth", out var e))
            {
                e.Enabled = enabled;
                e.tether = false;
                e.Filled = false;
                e.fillIntensity = 0f;
                if (enabled) e.color = Conf.TetherSafeSpotColor.ToUint();
            }
        }
        {
            if (Controller.TryGetElementByName("SafeSouth", out var e))
            {
                e.Enabled = enabled;
                e.tether = false;
                e.Filled = false;
                e.fillIntensity = 0f;
                if (enabled) e.color = Conf.TetherSafeSpotColor.ToUint();
            }
        }
        {
            if (Controller.TryGetElementByName("SafeWest", out var e))
            {
                e.Enabled = enabled;
                e.tether = false;
                e.Filled = false;
                e.fillIntensity = 0f;
                if (enabled) e.color = Conf.TetherSafeSpotColor.ToUint();
            }
        }
        {
            if (Controller.TryGetElementByName("SafeEast", out var e))
            {
                e.Enabled = enabled;
                e.tether = false;
                e.Filled = false;
                e.fillIntensity = 0f;
                if (enabled) e.color = Conf.TetherSafeSpotColor.ToUint();
            }
        }
    }

    private uint[] GetCurrentTowers()
    {
        return GetPairNumber(Towers, GetCurrentMechanicStep()).OrderBy(x => GetTowerAngle(x.GetObject().Position.ToVector2())).ToArray();
    }

    private uint GetMyTetherToPick(uint[] tetheredPlayers)
    {
        var localPlayer = Svc.ClientState.LocalPlayer;
        if (localPlayer == null || tetheredPlayers.Contains(localPlayer.EntityId))
        {
            return 0;
        }

        var pair = GetPairNumber(TowerOrder, GetTetherMechanicStep()).ToArray();
        if (pair.Length != 2 || !pair.Contains(localPlayer.EntityId))
        {
            return 0;
        }

        var incorrectTethers = tetheredPlayers.Where(player => !pair.Contains(player)).ToArray();
        if (incorrectTethers.Length == 1)
        {
            return incorrectTethers[0];
        }

        if (incorrectTethers.Length != 2)
        {
            return 0;
        }

        var players = Conf.PriorityData.GetPlayers(player => pair.Any(pairMember => pairMember == player.IGameObject.EntityId))?.ToArray();
        if (players == null || players.Length != 2)
        {
            return 0;
        }

        var orderedTethers = OrderTethersByConfiguredAngle(incorrectTethers);
        var priorityIndex = players[0].IGameObject.EntityId == localPlayer.EntityId ? 0 : 1;
        return orderedTethers.Length > priorityIndex ? orderedTethers[priorityIndex] : 0;
    }

    private uint[] OrderTethersByConfiguredAngle(uint[] tetheredPlayers)
    {
        return tetheredPlayers
            .OrderBy(player => GetTowerAngle(player.GetObject().Position.ToVector2()))
            .ToArray();
    }

    private float GetTowerAngle(Vector2 x)
    {
        var firstTower =
            Conf.Towers == TowerStartPoint.Start_NorthEast ? 45 :
            Conf.Towers == TowerStartPoint.Start_SouthEast ? 45 + 90 :
            Conf.Towers == TowerStartPoint.Start_SouthWest ? 45 + 90 * 2 :
            Conf.Towers == TowerStartPoint.Start_NorthWest ? 45 + 90 * 3 : throw new Exception("There is a problem in GetTowerAngle function");
        return (MathHelper.GetRelativeAngle(new(100f, 100f), x) + 360 - firstTower) % 360;
    }

    private bool IsTakingCurrentTether(uint p)
    {
        var step = GetCurrentMechanicStep();
        return GetPairNumber(TetherOrder, step).Contains(p);
    }

    private bool IsTakingCurrentTower(uint p)
    {
        var step = GetCurrentMechanicStep();
        return GetPairNumber(TowerOrder, step).Contains(p);
    }

    public override void OnObjectCreation(nint newObjectPtr)
    {
        new TickScheduler(delegate
        {
            var obj = Svc.Objects.FirstOrDefault(x => x.Address == newObjectPtr);
            if (obj != null)
            {
                if (obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj)
                {
                    //PluginLog.Information($"Event obj spawn: {obj} {obj.DataId}");
                }
                if (obj.DataId == 2013244 && GetOmega() != null)
                {
                    Towers.Add(obj.EntityId);
                    if (TowerOrder.Count == 0)
                    {
                        GetPlayersWithNumber(1).Each(x => TowerOrder.Add(x.EntityId));
                        GetPlayersWithNumber(2).Each(x => TowerOrder.Add(x.EntityId));
                        GetPlayersWithNumber(3).Each(x => TowerOrder.Add(x.EntityId));
                        GetPlayersWithNumber(4).Each(x => TowerOrder.Add(x.EntityId));
                        GetPlayersWithNumber(3).Each(x => TetherOrder.Add(x.EntityId));
                        GetPlayersWithNumber(4).Each(x => TetherOrder.Add(x.EntityId));
                        GetPlayersWithNumber(1).Each(x => TetherOrder.Add(x.EntityId));
                        GetPlayersWithNumber(2).Each(x => TetherOrder.Add(x.EntityId));
                    }
                }
            }
        });
    }

    public override void OnMessage(string Message)
    {
        if (Message.Contains($"{ProgramLoopCast} (7695>{ProgramLoopCast})")) //starts casting program loop
        {
            Reset();
            programLoopPriorityDisplayUntil = DateTime.Now.AddSeconds(40);
        }
    }

    public override void OnStartingCast(uint source, uint castId)
    {
        if (castId == PantokratorCast || castId == PantokratorFlameThrowerCast)
        {
            programLoopPriorityDisplayUntil = DateTime.MinValue;
            if (Controller.TryGetElementByName("SelfPairPriority", out var e))
            {
                e.Enabled = false;
            }
        }
    }

    public override void OnDirectorUpdate(DirectorUpdateCategory category)
    {
        if (category.EqualsAny(DirectorUpdateCategory.Commence, DirectorUpdateCategory.Recommence, DirectorUpdateCategory.Wipe))
        {
            Reset();
        }
    }

    private void Reset()
    {
        programLoopPriorityDisplayUntil = DateTime.MinValue;
        TetheredPlayers.Clear();
        UpdateTethers();
        Controller.GetElementByName("SelfPairPriority").Enabled = false;
        Towers.Clear();
        TowerOrder.Clear();
        TetherOrder.Clear();
    }

    private int GetCurrentMechanicStep()
    {
        if (GetPlayersWithNumber(1).Any()) return 1;
        if (GetPlayersWithNumber(2).Any()) return 2;
        if (GetPlayersWithNumber(3).Any()) return 3;
        if (GetPlayersWithNumber(4).Any()) return 4;
        return 0;
    }

    private int GetTetherMechanicStep()
    {
        if (GetPlayersWithNumber(1).Any()) return 3;
        if (GetPlayersWithNumber(2).Any()) return 4;
        if (GetPlayersWithNumber(3).Any()) return 1;
        if (GetPlayersWithNumber(4).Any()) return 2;
        return 0;
    }

    private IEnumerable<IPlayerCharacter> GetPlayersWithNumber(int n)
    {
        var debuff = GetDebuffByNumber(n);
        foreach (var x in Svc.Objects)
        {
            if (x is IPlayerCharacter p && p.StatusList.Any(z => z.StatusId == debuff))
            {
                yield return (IPlayerCharacter)x;
            }
        }
    }

    private int GetDebuffByNumber(int n)
    {
        if (n == 1) return 3004;
        if (n == 2) return 3005;
        if (n == 3) return 3006;
        if (n == 4) return 3451;
        throw new Exception($"Invalid GetDebuffByNumber query {n}");
    }

    private IBattleChara? GetOmega()
    {
        return Svc.Objects.FirstOrDefault(x => x is IBattleChara o && o.NameId == 7695 && o.IsTargetable()) as IBattleChara;
    }

    private bool IsOmega(uint oid, [NotNullWhen(true)] out IBattleChara? omega)
    {
        if (oid.TryGetObject(out var obj) && obj is IBattleChara o && o.NameId == 7695)
        {
            omega = o;
            return true;
        }
        omega = null;
        return false;
    }

    public override void OnSettingsDraw()
    {
        ImGuiEx.Text("TOP P1 - Program Loop Priority");
        ImGui.Separator();

        ImGuiEx.Text($"Tethers:");
        ImGui.ColorEdit4("Tether's AOE color", ref Conf.TetherAOECol, ImGuiColorEditFlags.NoInputs);
        ImGui.ColorEdit4("Valid tether color", ref Conf.ValidTetherColor, ImGuiColorEditFlags.NoInputs);
        ImGui.ColorEdit4("##Invalid1", ref Conf.InvalidTetherColor1, ImGuiColorEditFlags.NoInputs);
        ImGui.SameLine();
        ImGui.ColorEdit4("Invalid tether colors", ref Conf.InvalidTetherColor2, ImGuiColorEditFlags.NoInputs);
        ImGui.SetNextItemWidth(100f);
        ImGui.SliderFloat("Invalid tether reminder size", ref Conf.InvalidOverlayScale.ValidateRange(1, 5), 1, 5);
        ImGui.ColorEdit4("Invalid tether reminder color", ref Conf.OverlayTextColor, ImGuiColorEditFlags.NoInputs);
        ImGui.Checkbox($"Display AOE under incorrect tether", ref Conf.ShowAOEAlways);
        ImGui.Checkbox($"Tether AOE proximity detector", ref Conf.UseProximity);
        if (Conf.UseProximity)
        {
            ImGui.SameLine();
            ImGui.ColorEdit4("Proximity tether color", ref Conf.ProximityColor, ImGuiColorEditFlags.NoInputs);
        }
        ImGui.Checkbox($"Display tether drop spots when it's my order to take it", ref Conf.DisplayTetherSafeSpots);
        if (Conf.DisplayTetherSafeSpots)
        {
            ImGui.Checkbox($"Detect my designated spot based on same priority as towers", ref Conf.TetherSafeSpotEnableDetect);
            ImGui.ColorEdit4("Safe spot indicator color", ref Conf.TetherSafeSpotColor, ImGuiColorEditFlags.NoInputs);
        }
        ImGui.Checkbox($"Detect tether that I'm supposed to pick up from the same start direction as towers and make it larger", ref Conf.EnlargeMyTether);


        ImGui.Separator();

        ImGuiEx.Text($"Towers:");
        ImGui.SetNextItemWidth(200f);
        ImGuiEx.EnumCombo($"Tower handling", ref Conf.Towers);

        ImGuiEx.Text($"P1 Program Loop priority from North going Clockwise:");
        if (ImGui.Button("Configure for NAUR"))
        {
            Conf.Towers = TowerStartPoint.Start_NorthWest;
            //h2 r2 m2 t2 t1 m1 r1 h1
            Conf.PriorityData.PriorityLists =
            [
                new()
                {
                    IsRole = true,
                    List =
                    [
                        new() { Role = RolePosition.H2},
                        new() { Role = RolePosition.R2},
                        new() { Role = RolePosition.M2},
                        new() { Role = RolePosition.T2},
                        new() { Role = RolePosition.T1},
                        new() { Role = RolePosition.M1},
                        new() { Role = RolePosition.R1},
                        new() { Role = RolePosition.H1}
                    ]
                }
            ];
        }
        ImGui.SameLine();
        if (ImGui.Button("Configure for LPDU"))
        {
            Conf.Towers = TowerStartPoint.Start_NorthWest;
            //m1 m2 t1 t2 r1 r2 h1 h2
            Conf.PriorityData.PriorityLists =
            [
                new()
                {
                    IsRole = true,
                    List =
                    [
                        new() { Role = RolePosition.M1},
                        new() { Role = RolePosition.M2},
                        new() { Role = RolePosition.T1},
                        new() { Role = RolePosition.T2},
                        new() { Role = RolePosition.R1},
                        new() { Role = RolePosition.R2},
                        new() { Role = RolePosition.H1},
                        new() { Role = RolePosition.H2}
                    ]
                }
            ];
        }
        Conf.PriorityData.Draw();

        ImGui.ColorEdit4("Primary tower color", ref Conf.TowerColor1, ImGuiColorEditFlags.NoInputs);
        ImGui.SameLine();
        ImGui.ColorEdit4("Secondary tower color", ref Conf.TowerColor2, ImGuiColorEditFlags.NoInputs);

        ImGui.Separator();

        SetupElements();


        ImGui.Separator();

        if (ImGui.CollapsingHeader("Debug"))
        {
            ImGui.Checkbox($"Debug info", ref Conf.Debug);

            foreach (var x in TetheredPlayers)
            {
                ImGuiEx.Text($"Tether Player: {x} {x.GetObject()}");
            }
            ImGui.Separator();
            TetherOrder.Each(x => ImGuiEx.Text($"Tether order: {x.GetObject()}"));
            TowerOrder.Each(x => ImGuiEx.Text($"Tower order: {x.GetObject()}"));
            ImGuiEx.Text($"GetCurrentMechanicStep() {GetCurrentMechanicStep()}");
            ImGuiEx.Text($"GetTetherMechanicStep() {GetTetherMechanicStep()}");
            Towers.Each(x => ImGuiEx.Text($"Towers: {x.GetObject()?.Position.ToString() ?? "unk position"}"));
        }
    }

    private void UpdateSelfPairPriority()
    {
        if (!Controller.TryGetElementByName("SelfPairPriority", out var e))
        {
            return;
        }

        if (DateTime.Now > programLoopPriorityDisplayUntil || IsPantokratorActive())
        {
            e.Enabled = false;
            return;
        }

        var priority = GetSelfPairPriority();
        if (priority == 0)
        {
            e.Enabled = false;
            return;
        }

        e.Enabled = true;
        e.refX = 100f;
        e.refY = 100f;
        e.overlayText = $"Priority {priority}";
    }

    private int GetSelfPairPriority()
    {
        var localPlayer = Svc.ClientState.LocalPlayer;
        if (localPlayer == null)
        {
            return 0;
        }

        var number = GetPlayerNumber(localPlayer);
        if (number == 0)
        {
            return 0;
        }

        var pair = GetPlayersWithNumber(number).ToList();
        if (pair.Count != 2)
        {
            return 0;
        }

        var players = Conf.PriorityData.GetPlayers(player => pair.Any(pairMember => pairMember.EntityId == player.IGameObject.EntityId))?.ToList();
        if (players == null || players.Count != 2)
        {
            return 0;
        }

        var index = players.FindIndex(player => player.IGameObject.EntityId == localPlayer.EntityId);
        return index >= 0 ? index + 1 : 0;
    }

    private int GetPlayerNumber(IPlayerCharacter player)
    {
        for (var i = 1; i <= 4; i++)
        {
            var debuff = GetDebuffByNumber(i);
            if (player.StatusList.Any(status => status.StatusId == debuff))
            {
                return i;
            }
        }

        return 0;
    }

    private bool IsPantokratorActive()
    {
        if (Svc.Objects.OfType<IBattleChara>().Any(x => x.CastActionId == PantokratorCast || x.CastActionId == PantokratorFlameThrowerCast))
        {
            return true;
        }

        return Svc.Objects.OfType<IPlayerCharacter>().Any(HasPantokratorStatus);
    }

    private static bool HasPantokratorStatus(IPlayerCharacter player)
    {
        return player.StatusList.Any(status => status.StatusId == PantokratorStatus1
            || status.StatusId == PantokratorStatus2
            || status.StatusId == PantokratorStatus3
            || status.StatusId == PantokratorStatus4);
    }

    public class Config : IEzConfig
    {
        public Vector4 TetherAOECol = 0xFF00E4C8u.ToVector4();
        public Vector4 TowerColor1 = 0x23FF00FFu.ToVector4();
        public Vector4 TowerColor2 = 0x23FF00FFu.ToVector4();
        public Vector4 ValidTetherColor = 0x00FFFFFFu.ToVector4();
        public Vector4 InvalidTetherColor1 = 0xFFB500FFu.ToVector4();
        public Vector4 InvalidTetherColor2 = 0xFF0000FFu.ToVector4();
        public Vector4 OverlayTextColor = 0xFF000000u.ToVector4();
        public bool UseProximity = false;
        public Vector4 ProximityColor = ImGuiColors.ParsedBlue;
        public float InvalidOverlayScale = 2f;
        public bool ShowAOEAlways = false;
        public Direction MyDirection = Direction.Clockwise;
        public bool Debug = false;
        public TowerStartPoint Towers = TowerStartPoint.Start_NorthEast;
        public bool DisplayTetherSafeSpots = true;
        public bool TetherSafeSpotEnableDetect = true;
        public Vector4 TetherSafeSpotColor = 0xFF000000u.ToVector4();
        public bool EnlargeMyTether = true;
        public PriorityData PriorityData = new();
    }

    public enum Direction { Clockwise, Counter_clockwise }
    public enum TowerStartPoint { Disable_towers, Start_NorthEast, Start_SouthEast, Start_SouthWest, Start_NorthWest }

    internal static IEnumerable<T> GetPairNumber<T>(IEnumerable<T> e, int n)
    {
        var s = e.ToArray();
        if (n == 1 && s.Length >= 2)
        {
            yield return s[0];
            yield return s[1];
        }
        if (n == 2 && s.Length >= 4)
        {
            yield return s[2];
            yield return s[3];
        }
        if (n == 3 && s.Length >= 6)
        {
            yield return s[4];
            yield return s[5];
        }
        if (n == 4 && s.Length >= 8)
        {
            yield return s[6];
            yield return s[7];
        }
    }
}
