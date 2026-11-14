using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One control point in `C_BaseTeamObjectiveResource` (c_team_objectiveresource.h), as `CControlPointIcon` reads it.</summary>
/// <param name="Visible">`m_bCPIsVisible`.</param>
/// <param name="Owner">`m_iOwner`.</param>
/// <param name="CappingTeam">`m_iCappingTeam`.</param>
/// <param name="Group">`m_iCPGroup`.</param>
/// <param name="Locked">`m_bCPLocked`.</param>
/// <param name="Blocked">`m_bBlocked`.</param>
/// <param name="LazyCapPercent">`m_flLazyCapPerc`.</param>
/// <param name="UnlockTime">`m_flUnlockTimes`.</param>
/// <param name="Icons">`m_iTeamIcons` by team, 0 through 3.</param>
/// <param name="Overlays">`m_iTeamOverlays` by team.</param>
/// <param name="TeamMembers">`m_iNumTeamMembers` by team.</param>
/// <param name="CanCap">`m_bTeamCanCap` by team.</param>
/// <param name="CapTime">`m_flTeamCapTime` by team.</param>
/// <param name="InMiniRound">`m_bInMiniRound`.</param>
/// <param name="PreviousRed">`m_iPreviousPoints` for red, three slots packed by <see cref="SceneObjectives.PackPrevious"/>.</param>
/// <param name="PreviousBlue">`m_iPreviousPoints` for blue, the same.</param>
public readonly record struct SceneControlPoint(
    bool Visible,
    int Owner,
    int CappingTeam,
    int Group,
    bool Locked,
    bool Blocked,
    float LazyCapPercent,
    float UnlockTime,
    TeamQuad<int> Icons,
    TeamQuad<int> Overlays,
    TeamQuad<int> TeamMembers,
    TeamQuad<bool> CanCap,
    TeamQuad<float> CapTime,
    bool InMiniRound = false,
    int PreviousRed = 0,
    int PreviousBlue = 0)
{
    /// <summary>`m_iPrevCappers`, `m_flStartCapAnimStart`, `m_flPulseTime` and `m_bAccelerateOverCapture` of the point's icon.</summary>
    public CapPulse Pulse { get; init; }

    /// <summary>`m_flCPTimerTimes`: when a `CCPTimerLogic` countdown on the point ends, or -1 with none.</summary>
    public float TimerTime { get; init; } = -1f;
}

/// <summary>The pulse `CControlPointIcon` starts, kept with the point so a seek lands on what playing accumulated.</summary>
/// <param name="PreviousCappers">`m_iPrevCappers`.</param>
/// <param name="Start">`m_flStartCapAnimStart`; 0 when not pulsing.</param>
/// <param name="Length">`m_flPulseTime`; 0 runs until stopped.</param>
/// <param name="Accelerate">`m_bAccelerateOverCapture`.</param>
public readonly record struct CapPulse(int PreviousCappers, float Start, float Length, bool Accelerate)
{
    /// <summary>`FINISHCAPANIM_SWOOP_LENGTH` (hud_controlpointicons.h:37).</summary>
    public const float FinishDelay = 0.2f;

    /// <summary>The length of a successful capture's pulse (hud_controlpointicons.cpp:336).</summary>
    public const float FinishLength = 0.5f;

    /// <summary>`UpdateCapImage` when the capping team changes (:355, :393), then `UpdateImage`'s successful capture (:327).</summary>
    /// <param name="capping">The capping team now.</param>
    /// <param name="owner">The owner now.</param>
    /// <param name="cappingChanged">Whether `SetCappingTeam` ran.</param>
    /// <param name="now">`gpGlobals->curtime`.</param>
    /// <returns>The pulse after this update.</returns>
    public CapPulse Next(int capping, int owner, bool cappingChanged, float now)
    {
        CapPulse next = this;

        if (cappingChanged)
        {
            next = capping != 0 && capping != owner
                ? new CapPulse(capping, now, 0f, true)
                : next with { Start = 0f };
        }

        return next.PreviousCappers != 0 && owner == next.PreviousCappers
            ? new CapPulse(0, now + FinishDelay, FinishLength, false)
            : next;
    }
}

/// <summary>A value per team, `TEAM_ARRAY`'s team axis: unassigned, spectator, red, blue.</summary>
/// <typeparam name="T">The value.</typeparam>
/// <param name="Unassigned">Team 0.</param>
/// <param name="Spectator">Team 1.</param>
/// <param name="Red">Team 2.</param>
/// <param name="Blue">Team 3.</param>
public readonly record struct TeamQuad<T>(T Unassigned, T Spectator, T Red, T Blue)
    where T : struct
{
    /// <summary>The value for a team.</summary>
    /// <param name="team">The team number.</param>
    /// <returns>The value, or the default past team 3.</returns>
    public T this[int team] => team switch
    {
        0 => Unassigned,
        1 => Spectator,
        2 => Red,
        3 => Blue,
        _ => default,
    };
}

/// <summary>What the control-point HUD reads of the objective resource at one tick.</summary>
/// <param name="Points">`m_iNumControlPoints` points.</param>
/// <param name="CapLayout">`m_pszCapLayoutInHUD`.</param>
/// <param name="IconNames">`GetMaterialNameFromIndex` for each icon or overlay a point names, from the `Materials` string table.</param>
/// <param name="PlayingMiniRounds">`m_bPlayingMiniRounds`.</param>
/// <param name="BaseRed">`m_iBaseControlPoints` for red.</param>
/// <param name="BaseBlue">`m_iBaseControlPoints` for blue.</param>
public sealed record SceneObjectives(
    IReadOnlyList<SceneControlPoint> Points,
    string CapLayout,
    IReadOnlyDictionary<int, string> IconNames,
    bool PlayingMiniRounds = false,
    int BaseRed = -1,
    int BaseBlue = -1)
{
    /// <summary>`TEAM_ARRAY`'s stride: `MAX_CONTROL_POINTS`.</summary>
    public const int MaxControlPoints = 8;

    /// <summary>`MAX_PREVIOUS_POINTS`.</summary>
    public const int MaxPreviousPoints = 3;

    /// <summary>The string table `g_pStringTableMaterials` finds (cdll_client_int.cpp).</summary>
    public const string MaterialsTable = "Materials";

    private const string Table = "DT_BaseTeamObjectiveResource.";

    /// <summary>`m_flCustomPositionX`: the icons' left as a share of the screen, or -1 for centred.</summary>
    public float CustomPositionX { get; init; } = -1f;

    /// <summary>`m_flCustomPositionY`: the icons' top as a share of the screen, or -1 for the bottom.</summary>
    public float CustomPositionY { get; init; } = -1f;

    /// <summary>The payload track, read with the points.</summary>
    public SceneTrack? Track { get; init; }

    /// <summary>How many names the `Materials` table held when <see cref="IconNames"/> was read.</summary>
    public int MaterialCount { get; init; }

    /// <inheritdoc/>
    public bool Equals(SceneObjectives? other) =>
        other is not null
        && PlayingMiniRounds == other.PlayingMiniRounds
        && BaseRed == other.BaseRed
        && BaseBlue == other.BaseBlue
        && Equals(Track, other.Track)
        && SameBits(CustomPositionX, other.CustomPositionX)
        && SameBits(CustomPositionY, other.CustomPositionY)
        && string.Equals(CapLayout, other.CapLayout, StringComparison.Ordinal)
        && Points.SequenceEqual(other.Points)
        && IconNames.Count == other.IconNames.Count
        && IconNames.All(name => other.IconNames.TryGetValue(name.Key, out string? same) && string.Equals(name.Value, same, StringComparison.Ordinal));

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(CapLayout, Points.Count, PlayingMiniRounds);

    /// <summary>Packs one team's three previous points, each -1 or a point index, into an int.</summary>
    /// <param name="first">Slot 0.</param>
    /// <param name="second">Slot 1.</param>
    /// <param name="third">Slot 2.</param>
    /// <returns>The packed value.</returns>
    public static int PackPrevious(int first, int second, int third) =>
        (first & 0xFF) | ((second & 0xFF) << 8) | ((third & 0xFF) << 16);

    /// <summary>`GetPreviousPointForPoint`'s answer for one slot of a packed value.</summary>
    /// <param name="packed">The packed value.</param>
    /// <param name="slot">The slot, 0 through 2.</param>
    /// <returns>The point index, or -1.</returns>
    public static int UnpackPrevious(int packed, int slot) => (sbyte)((packed >> (slot * 8)) & 0xFF);

    /// <summary>Reads the entity, reusing the last answer when nothing changed, as most frames change nothing and a frame lives as long as the demo.</summary>
    /// <param name="resource">The objective resource, or null.</param>
    /// <param name="last">The previous frame's answer, or null.</param>
    /// <param name="materials">The `Materials` string table.</param>
    /// <param name="now">`gpGlobals->curtime`.</param>
    /// <returns>The objectives, or null with no resource.</returns>
    internal static SceneObjectives? Read(EntityState? resource, SceneObjectives? last, NameTable materials, float now)
    {
        if (resource is null)
        {
            return null;
        }

        int count = Math.Clamp(resource.Integer(Table + "m_iNumControlPoints") ?? 0, 0, MaxControlPoints);
        SceneControlPoint[] points = new SceneControlPoint[count];

        for (int index = 0; index < count; index++)
        {
            SceneControlPoint read = new(
                resource.Integer(Key("m_bCPIsVisible", index)) is > 0,
                resource.Integer(Key("m_iOwner", index)) ?? 0,
                resource.Integer(Key("m_iCappingTeam", index)) ?? 0,
                resource.Integer(Key("m_iCPGroup", index)) ?? 0,
                resource.Integer(Key("m_bCPLocked", index)) is > 0,
                resource.Integer(Key("m_bBlocked", index)) is > 0,
                resource.Number(Key("m_flLazyCapPerc", index)) ?? 0f,
                resource.Number(Key("m_flUnlockTimes", index)) ?? 0f,
                Ints(resource, "m_iTeamIcons", index),
                Ints(resource, "m_iTeamOverlays", index),
                Ints(resource, "m_iNumTeamMembers", index),
                new TeamQuad<bool>(
                    resource.Integer(Key("m_bTeamCanCap", index)) is > 0,
                    resource.Integer(Key("m_bTeamCanCap", index + MaxControlPoints)) is > 0,
                    resource.Integer(Key("m_bTeamCanCap", index + (2 * MaxControlPoints))) is > 0,
                    resource.Integer(Key("m_bTeamCanCap", index + (3 * MaxControlPoints))) is > 0),
                new TeamQuad<float>(
                    resource.Number(Key("m_flTeamCapTime", index)) ?? 0f,
                    resource.Number(Key("m_flTeamCapTime", index + MaxControlPoints)) ?? 0f,
                    resource.Number(Key("m_flTeamCapTime", index + (2 * MaxControlPoints))) ?? 0f,
                    resource.Number(Key("m_flTeamCapTime", index + (3 * MaxControlPoints))) ?? 0f),
                resource.Integer(Key("m_bInMiniRound", index)) is > 0,
                Previous(resource, index, 2),
                Previous(resource, index, 3));

            SceneControlPoint? before = last is not null && index < last.Points.Count ? last.Points[index] : null;
            CapPulse pulse = before?.Pulse ?? default;

            points[index] = read with
            {
                TimerTime = resource.Number(Key("m_flCPTimerTimes", index)) ?? -1f,
                Pulse = pulse.Next(read.CappingTeam, read.Owner, read.CappingTeam != (before?.CappingTeam ?? 0), now),
            };
        }

        string layout = resource.Text(Table + "m_pszCapLayoutInHUD") ?? string.Empty;
        bool miniRounds = resource.Integer(Table + "m_bPlayingMiniRounds") is > 0;

        int baseRed = resource.Integer(Key("m_iBaseControlPoints", 2)) ?? -1;
        int baseBlue = resource.Integer(Key("m_iBaseControlPoints", 3)) ?? -1;
        SceneTrack track = new(
            [.. Enumerable.Range(0, MaxControlPoints).Select(index => resource.Number(Key("m_flPathDistance", index)) ?? 0f)],
            [.. Enumerable.Range(0, SceneTrack.MaxTeams).Select(team => resource.Integer(Key("m_nNumNodeHillData", team)) ?? 0)],
            [.. Enumerable.Range(0, SceneTrack.MaxTeams * SceneTrack.MaxHills * 2).Select(index => resource.Number(Key("m_flNodeHillData", index)) ?? 0f)],
            [.. Enumerable.Range(0, SceneTrack.MaxTeams).Select(team => resource.Integer(Key("m_bTrackAlarm", team)) is > 0)],
            [.. Enumerable.Range(0, SceneTrack.MaxTeams * SceneTrack.MaxHills).Select(index => resource.Integer(Key("m_bHillIsDownhill", index)) is > 0)]);

        if (last?.Track is { } lastTrack && lastTrack.Equals(track))
        {
            track = lastTrack;
        }

        float customX = resource.Number(Table + "m_flCustomPositionX") ?? -1f;
        float customY = resource.Number(Table + "m_flCustomPositionY") ?? -1f;

        // The table's size too: a name that arrives after its point would otherwise never be read.
        if (last is not null
            && last.PlayingMiniRounds == miniRounds
            && last.BaseRed == baseRed
            && last.BaseBlue == baseBlue
            && last.MaterialCount == materials.Count
            && ReferenceEquals(last.Track, track)
            && SameBits(last.CustomPositionX, customX)
            && SameBits(last.CustomPositionY, customY)
            && string.Equals(last.CapLayout, layout, StringComparison.Ordinal)
            && last.Points.SequenceEqual(points))
        {
            return last;
        }

        Dictionary<int, string> names = [];

        foreach (SceneControlPoint point in points)
        {
            for (int team = 0; team < 4; team++)
            {
                AddName(names, materials, point.Icons[team]);
                AddName(names, materials, point.Overlays[team]);
            }
        }

        return new SceneObjectives(points, layout, names, miniRounds, baseRed, baseBlue) { MaterialCount = materials.Count, CustomPositionX = customX, CustomPositionY = customY, Track = track };
    }

    // A decoded value is the same value only when its bits are: the comparison is of what arrived, not of nearness.
    private static bool SameBits(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

    private static void AddName(Dictionary<int, string> names, NameTable materials, int icon)
    {
        if (icon != 0 && materials.Name(icon) is { } name)
        {
            names[icon] = name;
        }
    }

    // `GetPreviousPointForPoint`: `iPrevIndex + (index * MAX_PREVIOUS_POINTS) + (team * MAX_CONTROL_POINTS * MAX_PREVIOUS_POINTS)`.
    private static int Previous(EntityState resource, int index, int team)
    {
        int start = (index * MaxPreviousPoints) + (team * MaxControlPoints * MaxPreviousPoints);

        return PackPrevious(
            resource.Integer(Key("m_iPreviousPoints", start)) ?? -1,
            resource.Integer(Key("m_iPreviousPoints", start + 1)) ?? -1,
            resource.Integer(Key("m_iPreviousPoints", start + 2)) ?? -1);
    }

    private static TeamQuad<int> Ints(EntityState resource, string name, int index) => new(
        resource.Integer(Key(name, index)) ?? 0,
        resource.Integer(Key(name, index + MaxControlPoints)) ?? 0,
        resource.Integer(Key(name, index + (2 * MaxControlPoints))) ?? 0,
        resource.Integer(Key(name, index + (3 * MaxControlPoints))) ?? 0);

    // A `SendPropArray3` arrives flat, as `m_iOwner.000` upward, with no table name. Built once per array (B613,
    // ElementKeys): this runs every frame, and formatting each key afresh was about a gigabyte of the f12 decode. Shared
    // across builders, which may run in parallel, hence the concurrent map.
    private static string Key(string name, int index) =>
        ArrayKeys.GetOrAdd(name, static array => new ElementKeys(array, ArrayKeysBuilt))[index];

    // Past every index read above (`m_iPreviousPoints` reaches 95); a larger one is still formatted, just not kept.
    private const int ArrayKeysBuilt = 128;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ElementKeys> ArrayKeys =
        new(StringComparer.Ordinal);
}
