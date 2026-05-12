using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons;
using ECommons.DalamudServices;
using ECommons.DalamudServices.Legacy;
using ECommons.GameHelpers;
using Splatoon.SplatoonScripting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SplatoonScriptsOfficial.Duties.Endwalker.The_Omega_Protocol;

internal class TOP_P5_Hello_World_Near_Far : SplatoonScript
{
    private const uint NearStatusId = 3442;
    private const uint FarStatusId = 3443;
    private const float StatusThresholdSeconds = 4.0f;
    private const uint GreenColor = 4278255360;
    private const uint BlueColor = 3372154880;

    public override HashSet<uint>? ValidTerritories => [1122];
    public override Metadata? Metadata => new(1, "kudry + Codex");

    public override void OnSetup()
    {
        RegisterElement("NearOwner", 7.0f, GreenColor);
        RegisterElement("NearClosest1", 3.0f, GreenColor);
        RegisterElement("NearClosest2", 3.0f, GreenColor);
        RegisterElement("FarOwner", 7.0f, BlueColor);
        RegisterElement("FarFarthest1", 3.0f, BlueColor);
        RegisterElement("FarFarthest2", 3.0f, BlueColor);
    }

    public override void OnUpdate()
    {
        OffAll();

        var party = GetPlayers();

        var nearOwner = GetStatusOwner(party, NearStatusId);
        var farOwner = GetStatusOwner(party, FarStatusId);

        if(nearOwner != null)
        {
            Show("NearOwner", nearOwner);

            var closest1 = party
                .Where(x => !SamePlayer(x, nearOwner))
                .OrderBy(x => Distance2D(x, nearOwner))
                .FirstOrDefault();

            Show("NearClosest1", closest1);

            if(closest1 != null)
            {
                var closest2 = party
                    .Where(x => !SamePlayer(x, closest1) && !SamePlayer(x, nearOwner))
                    .OrderBy(x => Distance2D(x, closest1))
                    .FirstOrDefault();

                Show("NearClosest2", closest2);
            }
        }

        if(farOwner != null)
            Show("FarOwner", farOwner);

        if(farOwner == null)
            return;

        var farthest1 = party
            .Where(x => !SamePlayer(x, farOwner))
            .OrderByDescending(x => Distance2D(x, farOwner))
            .FirstOrDefault();

        Show("FarFarthest1", farthest1);

        if(farthest1 == null)
            return;

        var farthest2 = party
            .Where(x => !SamePlayer(x, farthest1) && !SamePlayer(x, farOwner))
            .OrderByDescending(x => Distance2D(x, farthest1))
            .FirstOrDefault();

        Show("FarFarthest2", farthest2);
    }

    public override void OnReset()
    {
        OffAll();
    }

    private void RegisterElement(string name, float radius, uint color)
    {
        Controller.RegisterElementFromCode(name, $$"""
        {
            "Name":"",
            "type":1,
            "Enabled":false,
            "radius":{{radius}},
            "Donut":1.0,
            "color":{{color}},
            "Filled":true,
            "fillIntensity":0.5,
            "overlayBGColor":1879048192,
            "overlayTextColor":3372220415,
            "thicc":4.0,
            "refActorObjectID":0,
            "refActorComparisonType":2
        }
        """);
    }

    private void Show(string elementName, IPlayerCharacter player)
    {
        var element = Controller.GetElementByName(elementName);
        element.refActorObjectID = player.EntityId;
        element.Enabled = true;
    }

    private static IPlayerCharacter GetStatusOwner(IEnumerable<IPlayerCharacter> party, uint statusId)
    {
        foreach(var player in party)
        {
            var remaining = GetRemainingTime(player, statusId);
            if(remaining > 0.0f && remaining <= StatusThresholdSeconds)
                return player;
        }

        return null;
    }

    private static float GetRemainingTime(IPlayerCharacter player, uint statusId)
    {
        foreach(var status in player.StatusList)
        {
            if(status.StatusId == statusId)
                return status.RemainingTime;
        }

        return 0.0f;
    }

    private static float Distance2D(IPlayerCharacter a, IPlayerCharacter b)
    {
        var dx = a.Position.X - b.Position.X;
        var dz = a.Position.Z - b.Position.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private static bool SamePlayer(IPlayerCharacter a, IPlayerCharacter b)
    {
        return a.Address == b.Address || a.EntityId == b.EntityId || a.Name.ToString() == b.Name.ToString();
    }

    private List<IPlayerCharacter> GetPlayers()
    {
        return Svc.Objects
            .OfType<IPlayerCharacter>()
            .ToList();
    }

    private void OffAll()
    {
        Controller.GetRegisteredElements().Each(x => x.Value.Enabled = false);
    }
}
