using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons;
using ECommons.Configuration;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using Splatoon.SplatoonScripting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SplatoonScriptsOfficial.Duties.Endwalker.The_Omega_Protocol;

internal class TOP_P6_Limiter_Cut_Wave_Cannon : SplatoonScript
{
    private const uint LimiterCutCast = 31660;
    private const uint WaveCannonSourceCast = 31661;
    private const uint LimiterCutWaveCannonCast = 31663;
    private const uint WaveCannonNpcDataId = 0x2FE0;
    private const float CenterX = 100f;
    private const float CenterZ = 100f;
    private const float OuterRadius = 24f;
    private const float FirstGuideRadius = 8f;
    private const float GuideRadius = 16f;
    private const float HalfOctant = 0.5f;
    private const int FirstGuideDelayMs = 9700;
    private const int FirstDistance16DelayMs = 2500;
    private const int Distance16StepDelayMs = 1700;
    private const int FirstPreviewLeadMs = 3000;
    private const int PreviewLeadMs = 1000;
    private const uint GuideColor = 4278255360;
    private const uint PreviewGuideColor = 4278255615;

    private readonly List<int> _waveCannonIndices = [];
    private List<GuideStep> _guideSteps = [];
    private bool _limiterCutActive = false;
    private bool _waveCannonCastStarted = false;
    private bool _sequenceStarted = false;
    private int _limiterCutWaveCannonCount = 0;
    private int _sequenceId = 0;
    private long _limiterCutStartedAt = 0;
    private int? _firstIndex = null;
    private int? _rotationDirection = null;
    private Config C => Controller.GetConfig<Config>();

    public override HashSet<uint>? ValidTerritories => [1122];
    public override Metadata? Metadata => new(1, "kudry + Codex");

    public override void OnSetup()
    {
        Controller.RegisterElementFromCode("Guide", """
        {
            "Name":"",
            "type":0,
            "Enabled":false,
            "refX":100.0,
            "refY":100.0,
            "refZ":0.0,
            "radius":1.0,
            "color":4278255360,
            "Filled":false,
            "fillIntensity":0.2,
            "overlayBGColor":1879048192,
            "overlayTextColor":3372220415,
            "thicc":8.0,
            "overlayText":"",
            "tether":true
        }
        """);
        Controller.RegisterElementFromCode("PreviewGuide", """
        {
            "Name":"",
            "type":0,
            "Enabled":false,
            "refX":100.0,
            "refY":100.0,
            "refZ":0.0,
            "radius":1.0,
            "color":4278255615,
            "Filled":false,
            "fillIntensity":0.2,
            "overlayBGColor":1879048192,
            "overlayTextColor":3372220415,
            "thicc":8.0,
            "overlayText":"",
            "tether":true
        }
        """);
    }

    public override void OnStartingCast(uint source, uint castId)
    {
        if(castId == LimiterCutCast)
        {
            ResetState();
            OffPreviewGuide();
            _limiterCutActive = true;
            _limiterCutStartedAt = Environment.TickCount64;
            MoveGuide(new Vector3(CenterX, 0f, CenterZ));
            return;
        }

        if(!_limiterCutActive) return;

        if(castId == WaveCannonSourceCast)
        {
            RecordWaveCannonSource(source);
            ScanWaveCannonSources();
            TryStartGuideSequence();
            return;
        }

        if(castId == LimiterCutWaveCannonCast)
        {
            _limiterCutWaveCannonCount++;
            _waveCannonCastStarted = true;
            TryStartGuideSequence();
        }
    }

    public override void OnUpdate()
    {
        if(!_limiterCutActive) return;

        ScanWaveCannonSources();
        TryStartGuideSequence();
        UpdateGuideByTimeline();
    }

    private void ScanWaveCannonSources()
    {
        foreach(var npc in Svc.Objects.OfType<IBattleChara>())
        {
            if(IsWaveCannonNpc(npc) && npc.CastActionId == WaveCannonSourceCast)
            {
                RecordWaveCannonSource(npc);
            }
        }
    }

    public override void OnReset()
    {
        ResetState();
        OffGuide();
        OffPreviewGuide();
    }

    public override void OnSettingsDraw()
    {
        ImGui.Checkbox("Debug", ref C.Debug);

        if(ImGui.CollapsingHeader("Debug"))
        {
            ImGuiEx.Text($"Limiter cut active: {_limiterCutActive}");
            ImGuiEx.Text($"31663 started: {_waveCannonCastStarted}");
            ImGuiEx.Text($"31663 count: {_limiterCutWaveCannonCount}");
            ImGuiEx.Text($"Sequence started: {_sequenceStarted}");
            ImGuiEx.Text($"Elapsed ms: {GetElapsedMs()}");
            ImGuiEx.Text($"Current step: {GetCurrentStepIndex()}");
            ImGuiEx.Text($"First index: {(_firstIndex?.ToString() ?? "None")}");
            ImGuiEx.Text($"Direction: {GetDirectionText()}");
            ImGuiEx.Text($"31661 indices: {string.Join(", ", _waveCannonIndices)}");
        }
    }

    private void RecordWaveCannonSource(uint source)
    {
        if(source.GetObject() is not IBattleChara npc) return;

        RecordWaveCannonSource(npc);
    }

    private void RecordWaveCannonSource(IBattleChara npc)
    {
        var index = GetOctantIndex(npc.Position);
        if(_waveCannonIndices.Contains(index)) return;

        _waveCannonIndices.Add(index);
        _firstIndex ??= index;

        if(_rotationDirection == null && _waveCannonIndices.Count >= 2)
        {
            _rotationDirection = GetRotationDirection(_waveCannonIndices[0], _waveCannonIndices[1]);
        }
    }

    private static bool IsWaveCannonNpc(IBattleChara npc)
    {
        var distanceFromCenter = Math.Sqrt(Math.Pow(npc.Position.X - CenterX, 2) + Math.Pow(npc.Position.Z - CenterZ, 2));
        return npc.DataId == WaveCannonNpcDataId || Math.Abs(distanceFromCenter - OuterRadius) <= 3f;
    }

    private void TryStartGuideSequence()
    {
        if(_sequenceStarted || _firstIndex == null || _rotationDirection == null) return;
        if(!_waveCannonCastStarted && _waveCannonIndices.Count < 2) return;

        _sequenceStarted = true;
        _sequenceId++;
        _guideSteps = BuildGuideSteps(_firstIndex.Value, _rotationDirection.Value);
        UpdateGuideByTimeline();
    }

    private void UpdateGuideByTimeline()
    {
        if(!_limiterCutActive) return;

        if(!_sequenceStarted)
        {
            MoveGuide(new Vector3(CenterX, 0f, CenterZ));
            OffPreviewGuide();
            return;
        }

        UpdatePreviewGuideByTimeline();

        var stepIndex = GetCurrentStepIndex();
        if(stepIndex < 0)
        {
            MoveGuide(new Vector3(CenterX, 0f, CenterZ));
            return;
        }

        if(stepIndex >= _guideSteps.Count)
        {
            OffGuide();
            OffPreviewGuide();
            return;
        }

        var step = _guideSteps[stepIndex];
        MoveGuide(PositionFromIndex(step.Index, step.Distance));
    }

    private static List<GuideStep> BuildGuideSteps(int firstIndex, int direction)
    {
        var oppositeFirstIndex = NormalizeIndex(firstIndex - direction);
        return
        [
            new(oppositeFirstIndex, FirstGuideRadius),
            new(oppositeFirstIndex, GuideRadius),
            new(oppositeFirstIndex + direction * HalfOctant, GuideRadius),
            new(oppositeFirstIndex + direction, GuideRadius),
            new(oppositeFirstIndex + direction * 1.5f, GuideRadius),
            new(oppositeFirstIndex + direction * 2f, GuideRadius),
        ];
    }

    private static int GetRotationDirection(int firstIndex, int secondIndex)
    {
        var diff = NormalizeIndex(secondIndex - firstIndex);
        return diff <= 4 ? 1 : -1;
    }

    private static int GetOctantIndex(Vector3 position)
    {
        var degrees = Math.Atan2(position.X - CenterX, CenterZ - position.Z) * 180.0 / Math.PI;
        var index = (int)Math.Round(degrees / 45.0);
        return NormalizeIndex(index);
    }

    private static Vector3 PositionFromIndex(float index, float distance)
    {
        var radians = NormalizeIndex(index) * Math.PI / 4.0;
        return new Vector3(
            CenterX + (float)Math.Sin(radians) * distance,
            0f,
            CenterZ - (float)Math.Cos(radians) * distance);
    }

    private void MoveGuide(Vector3 position)
    {
        var element = Controller.GetElementByName("Guide");
        element.refX = position.X;
        element.refY = position.Z;
        element.refZ = position.Y;
        element.radius = 1f;
        element.thicc = 8f;
        element.tether = true;
        element.color = GuideColor;
        element.Enabled = true;
    }

    private void MovePreviewGuide(Vector3 position)
    {
        var element = Controller.GetElementByName("PreviewGuide");
        element.refX = position.X;
        element.refY = position.Z;
        element.refZ = position.Y;
        element.radius = 1f;
        element.thicc = 8f;
        element.tether = true;
        element.color = PreviewGuideColor;
        element.Enabled = true;
    }

    private void OffGuide()
    {
        Controller.GetElementByName("Guide").Enabled = false;
    }

    private void OffPreviewGuide()
    {
        Controller.GetElementByName("PreviewGuide").Enabled = false;
    }

    private void ResetState()
    {
        _sequenceId++;
        _waveCannonIndices.Clear();
        _guideSteps.Clear();
        _limiterCutActive = false;
        _waveCannonCastStarted = false;
        _sequenceStarted = false;
        _limiterCutWaveCannonCount = 0;
        _limiterCutStartedAt = 0;
        _firstIndex = null;
        _rotationDirection = null;
    }

    private string GetDirectionText()
    {
        return _rotationDirection switch
        {
            1 => "Clockwise",
            -1 => "Counterclockwise",
            _ => "Unknown",
        };
    }

    private int GetCurrentStepIndex()
    {
        var elapsed = GetElapsedMs();
        if(elapsed < FirstGuideDelayMs) return -1;
        if(_guideSteps.Count > 0 && elapsed >= GetStepStartMs(_guideSteps.Count)) return _guideSteps.Count;

        for(var i = _guideSteps.Count - 1; i >= 0; i--)
        {
            if(elapsed >= GetStepStartMs(i))
            {
                return i;
            }
        }

        return -1;
    }

    private void UpdatePreviewGuideByTimeline()
    {
        var elapsed = GetElapsedMs();
        var nextStepIndex = GetNextStepIndex(elapsed);

        if(nextStepIndex < 0)
        {
            OffPreviewGuide();
            return;
        }

        var leadMs = nextStepIndex == 0 ? FirstPreviewLeadMs : PreviewLeadMs;
        var stepStartMs = GetStepStartMs(nextStepIndex);

        if(elapsed < stepStartMs - leadMs || elapsed >= stepStartMs)
        {
            OffPreviewGuide();
            return;
        }

        var step = _guideSteps[nextStepIndex];
        MovePreviewGuide(PositionFromIndex(step.Index, step.Distance));
    }

    private int GetNextStepIndex(long elapsed)
    {
        for(var i = 0; i < _guideSteps.Count; i++)
        {
            if(elapsed < GetStepStartMs(i))
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetStepStartMs(int stepIndex)
    {
        if(stepIndex <= 0) return FirstGuideDelayMs;

        return FirstGuideDelayMs + FirstDistance16DelayMs + (stepIndex - 1) * Distance16StepDelayMs;
    }

    private long GetElapsedMs()
    {
        return _limiterCutStartedAt == 0 ? 0 : Environment.TickCount64 - _limiterCutStartedAt;
    }

    private static int NormalizeIndex(int index)
    {
        return (index % 8 + 8) % 8;
    }

    private static float NormalizeIndex(float index)
    {
        return (index % 8f + 8f) % 8f;
    }

    private readonly record struct GuideStep(float Index, float Distance);

    public class Config : IEzConfig
    {
        public bool Debug = false;
    }
}
