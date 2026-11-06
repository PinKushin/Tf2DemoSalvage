using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CTFArrowPanel` (game/client/tf/tf_hud_flagstatus.cpp:58): a compass turned toward its entity from the local view.</summary>
public sealed class TfArrowPanel(VguiPanel? parent, string? name) : VguiPanel(parent, name)
{
    private const int TeamRed = 2;
    private const int TeamBlue = 3;
    private const string Neutral = "hud/objectives_flagpanel_compass_grey";

    private string _material = Neutral;
    private float _rotation;

    /// <inheritdoc/>
    public override string ClassName => "CTFArrowPanel";

    /// <summary>`m_hEntity`'s team and `WorldSpaceCenter()`, or null with none.</summary>
    public (int Team, (float X, float Y, float Z) Center)? Entity { get; set; }

    /// <summary>The material `OnTick` (:78) picks: the team's compass, or its no-arrow face for the flag the in-eye target carries.</summary>
    /// <param name="team">The entity's team.</param>
    /// <param name="carriedByTarget">Whether the in-eye observer target carries this flag.</param>
    /// <returns>The material.</returns>
    /// <remarks>**Not ported:** Mann vs. Machine's alarm flashing (:88-117).</remarks>
    public static string Material(int team, bool carriedByTarget) => team switch
    {
        TeamRed => carriedByTarget ? "hud/objectives_flagpanel_compass_red_noArrow" : "hud/objectives_flagpanel_compass_red",
        TeamBlue => carriedByTarget ? "hud/objectives_flagpanel_compass_blue_noArrow" : "hud/objectives_flagpanel_compass_blue",
        _ => Neutral,
    };

    /// <summary>`GetAngleRotation` (:167): degrees from the view's flattened forward to the entity, positive toward the right.</summary>
    /// <param name="eye">`CalcView`'s eye origin.</param>
    /// <param name="angles">`CalcView`'s angles.</param>
    /// <param name="target">The entity's `WorldSpaceCenter()`.</param>
    /// <returns>The rotation.</returns>
    public static float AngleRotation((float X, float Y, float Z) eye, (float Pitch, float Yaw, float Roll) angles, (float X, float Y, float Z) target)
    {
        (float X, float Y) toFlag = Normalise(target.X - eye.X, target.Y - eye.Y);

        // `AngleVectors` (mathlib_base.cpp): forward and right, each with z dropped and renormalised.
        float sp = MathF.Sin(angles.Pitch * MathF.PI / 180f);
        float cp = MathF.Cos(angles.Pitch * MathF.PI / 180f);
        float sy = MathF.Sin(angles.Yaw * MathF.PI / 180f);
        float cy = MathF.Cos(angles.Yaw * MathF.PI / 180f);
        float sr = MathF.Sin(angles.Roll * MathF.PI / 180f);
        float cr = MathF.Cos(angles.Roll * MathF.PI / 180f);

        (float X, float Y) forward = Normalise(cp * cy, cp * sy);
        (float X, float Y) right = Normalise((-1f * sr * sp * cy) + (-1f * cr * -sy), (-1f * sr * sp * sy) + (-1f * cr * cy));

        // `acos` of a dot rounded past 1 is NaN, in C as here: Valve's, kept.
        float angle = MathF.Acos((toFlag.X * forward.X) + (toFlag.Y * forward.Y));

        if ((toFlag.X * right.X) + (toFlag.Y * right.Y) < 0f)
        {
            angle *= -1f;
        }

        return angle * 180f / MathF.PI;
    }

    /// <summary>`OnTick` and the rotation `Paint` builds, from this frame's state.</summary>
    /// <param name="state">The game state.</param>
    /// <param name="carriedByTarget">Whether the in-eye observer target carries this entity.</param>
    public void Update(HudState state, bool carriedByTarget)
    {
        if (Entity is not { } entity)
        {
            return;
        }

        _material = Material(entity.Team, carriedByTarget);
        _rotation = state.View is { } view ? AngleRotation(view.Origin, view.Angles, entity.Center) : 0f;
    }

    /// <inheritdoc/>
    /// <remarks>`Paint` (:212): one quad about the panel's centre, turned about Z by the rotation.</remarks>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (Entity is null)
        {
            return;
        }

        float radians = _rotation * MathF.PI / 180f;
        float c = MathF.Cos(radians);
        float s = MathF.Sin(radians);
        // `nWidth/2`: integer halves, as Valve's.
        int halfWide = Wide / 2;
        int halfTall = Tall / 2;

        VguiVertex Corner(float x, float y, float u, float v) =>
            new(halfWide + (x * c) - (y * s), halfTall + (x * s) + (y * c), u, v);

        surface.DrawSetTexture(_material);
        surface.DrawSetColor((255, 255, 255, 255));

        VguiVertex topLeft = Corner(-halfWide, -halfTall, 0f, 0f);
        VguiVertex topRight = Corner(halfWide, -halfTall, 1f, 0f);
        VguiVertex bottomRight = Corner(halfWide, halfTall, 1f, 1f);
        VguiVertex bottomLeft = Corner(-halfWide, halfTall, 0f, 1f);

        Span<VguiVertex> triangle = stackalloc VguiVertex[3];

        (triangle[0], triangle[1], triangle[2]) = (topLeft, topRight, bottomRight);
        surface.DrawTexturedPolygon(triangle);
        (triangle[0], triangle[1], triangle[2]) = (topLeft, bottomRight, bottomLeft);
        surface.DrawTexturedPolygon(triangle);
    }

    private static (float X, float Y) Normalise(float x, float y)
    {
        float length = MathF.Sqrt((x * x) + (y * y));

        return length > 0f ? (x / length, y / length) : (0f, 0f);
    }
}

/// <summary>`CTFFlagStatus` (tf_hud_flagstatus.cpp:276): one flag's compass and its home, dropped or moving icon.</summary>
public sealed class TfFlagStatus : VguiEditablePanel
{
    private readonly TfImagePanel _statusIcon;
    private string? _image;

    /// <summary>`CTFFlagStatus( parent, name )`.</summary>
    /// <param name="parent">The flag objectives.</param>
    /// <param name="name">"RedFlag" or "BlueFlag".</param>
    public TfFlagStatus(VguiPanel parent, string name)
        : base(parent, name)
    {
        Arrow = new TfArrowPanel(this, "Arrow");
        _statusIcon = new TfImagePanel(this, "StatusIcon");
        _ = new TfImagePanel(this, "Briefcase");
    }

    /// <summary>`m_pArrow`.</summary>
    public TfArrowPanel Arrow { get; }

    /// <summary>`m_hEntity`: the flag's entity index, or 0.</summary>
    public int Entity { get; set; }

    /// <summary>The status icon `UpdateStatus` (:327) sets.</summary>
    /// <param name="flag">The flag.</param>
    /// <returns>The image.</returns>
    /// <remarks>**Not ported:** Mann vs. Machine's bomb images.</remarks>
    public static string StatusImage(SceneFlag flag)
    {
        if (flag.IsDropped)
        {
            return "../hud/objectives_flagpanel_ico_flag_dropped";
        }

        return flag.IsStolen ? "../hud/objectives_flagpanel_ico_flag_moving" : "../hud/objectives_flagpanel_ico_flag_home";
    }

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        LoadControlSettings("resource/UI/FlagStatus.res", context);
        _image = null;
    }

    /// <summary>`UpdateStatus` (:327) and the arrow's tick.</summary>
    /// <param name="state">The game state.</param>
    /// <param name="flag">The flag.</param>
    /// <param name="carriedByTarget">Whether the in-eye observer target carries it.</param>
    public void UpdateStatus(HudState state, SceneFlag flag, bool carriedByTarget)
    {
        string image = StatusImage(flag);

        if (image != _image)
        {
            _image = image;
            _statusIcon.SetImage(image);
        }

        Arrow.Entity = (flag.Team, flag.Center);
        Arrow.Update(state, carriedByTarget);
    }
}

/// <summary>`CTFHudFlagObjectives` (tf_hud_flagstatus.cpp:364): the CTF panel — each flag's state, the scores, and what the carrier sees.</summary>
/// <remarks>
/// **From state each think, where the engine ticks every 250 ms and listens for `flagstatus_update`**: the flags' state is the
/// entities', which the event only announces. **Not ported:** Mann vs. Machine, special delivery and power-up mode (the poison
/// timer), and the robot-destruction callouts.
/// </remarks>
public sealed class TfHudFlagObjectives : VguiEditablePanel
{
    private const int TeamUnassigned = 0;
    private const int TeamRed = 2;
    private const int TeamBlue = 3;

    // `ETFFlagType` (tf_shareddefs.h): attack/defend, territory control, invade and resource control carry the own team's icon.
    private const int FlagTypeAttackDefend = 1;
    private const int FlagTypeResourceControl = 4;

    private int _validFlags = -1;
    private bool _playingHybrid;
    private (int? Blue, int? Red, int CapsPerRound) _scores = (null, null, -1);
    private string? _specImageName;
    private bool _carryingFlag;
    private bool _flagAnimationPlayed;

    /// <summary>`m_bCarryingFlag`.</summary>
    public bool CarryingFlag => _carryingFlag;

    /// <summary>`Reset` (:508): the outline hidden, then `UpdateStatus` at once, shown or not.</summary>
    /// <param name="state">The game state.</param>
    public void Reset(HudState state)
    {
        HudViewport.Of(this)?.Animations.StartAnimationSequence("FlagOutlineHide");

        IReadOnlyList<SceneFlag> flags = state.CaptureFlags?.Flags ?? [];
        int target = state.ObserverMode == ObserverModes.InEye ? state.ObserverTarget : 0;

        UpdateStatus(state, flags, CarriedBy(flags, state.LocalIndex), target == 0 ? 0 : CarriedBy(flags, target)?.EntityIndex ?? 0);
    }

    /// <summary>`CTFHudFlagObjectives( parent, name )`.</summary>
    /// <param name="parent">The objective status.</param>
    public TfHudFlagObjectives(VguiPanel parent)
        : base(parent, "ObjectiveStatusFlagPanel")
    {
        RedFlag = new TfFlagStatus(this, "RedFlag");
        BlueFlag = new TfFlagStatus(this, "BlueFlag");
    }

    /// <summary>`m_pRedFlag`.</summary>
    public TfFlagStatus RedFlag { get; }

    /// <summary>`m_pBlueFlag`.</summary>
    public TfFlagStatus BlueFlag { get; }

    /// <summary>The `.res` conditions `ApplySchemeSettings` (:401) builds from how many flag panels point at a flag.</summary>
    /// <param name="flags">0, 1 or 2 — one when both point at the same flag.</param>
    /// <param name="hybrid">`IsPlayingHybrid_CTF_CP()`.</param>
    /// <returns>The conditions, or null for none.</returns>
    public static string[]? Conditions(int flags, bool hybrid)
    {
        if (flags == 0)
        {
            return ["if_no_flags"];
        }

        if (!hybrid && flags != 1)
        {
            return null;
        }

        List<string> conditions = [];

        if (hybrid)
        {
            conditions.Add("if_hybrid");
        }

        conditions.Add(flags == 1 ? "if_hybrid_single" : "if_hybrid_double");

        return [.. conditions];
    }

    /// <summary>`UpdateStatus` (:775)'s carried image team: the own team's for the objective flag types, else the enemy's.</summary>
    /// <param name="type">`GetType()`.</param>
    /// <param name="localTeam">`GetLocalPlayerTeam()`.</param>
    /// <returns>The team whose icon is shown.</returns>
    public static int CarriedImageTeam(int type, int localTeam)
    {
        if (type is >= FlagTypeAttackDefend and <= FlagTypeResourceControl)
        {
            return localTeam == TeamBlue ? TeamBlue : TeamRed;
        }

        // "normal CTF behavior (carrying the enemy flag)".
        return localTeam == TeamRed ? TeamBlue : TeamRed;
    }

    /// <summary>`CCaptureFlag::GetHudIcon` (entity_capture_flag.cpp:311): `m_szHudIcon`, else `TF_FLAG_ICON`, with "blue" for BLU and "red" for any other.</summary>
    /// <param name="icon">`m_szHudIcon`.</param>
    /// <param name="team">The team.</param>
    /// <returns>The image.</returns>
    public static string HudIcon(string icon, int team) =>
        $"{(string.IsNullOrEmpty(icon) ? "../hud/objectives_flagpanel_carried" : icon)}_{(team == TeamBlue ? "blue" : "red")}";

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:401): the layout by flag count, then the outline at alpha 0.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);

        int flags = (RedFlag.Entity != 0 ? 1 : 0) + (BlueFlag.Entity != 0 ? 1 : 0);

        if (flags == 2 && RedFlag.Entity == BlueFlag.Entity)
        {
            flags = 1;
        }

        LoadControlSettings("resource/UI/HudObjectiveFlagPanel.res", context, Conditions(flags, _playingHybrid));

        FindChildByName("OutlineImage")?.SetAlpha(0f);
        _scores = (null, null, -1);
        _specImageName = null;
    }

    /// <summary>`OnTick` (:537) and `UpdateStatus` (:775), from this frame's state.</summary>
    /// <param name="state">The game state.</param>
    public void Update(HudState state)
    {
        IReadOnlyList<SceneFlag> flags = state.CaptureFlags?.Flags ?? [];
        int valid = AssignFlags(flags);

        if (_validFlags != valid || _playingHybrid != state.Rules.PlayingHybridCtfCp)
        {
            _validFlags = valid;
            _playingHybrid = state.Rules.PlayingHybridCtfCp;
            InvalidateLayout(reloadScheme: true);
        }

        UpdateScores(state);

        int target = state.ObserverMode == ObserverModes.InEye ? state.ObserverTarget : 0;
        SceneFlag? targetFlag = target == 0 ? null : CarriedBy(flags, target);

        // :670-698: an in-eye target with the flag shows the image of the flag it carries.
        if (FindChildByName("SpecCarriedImage") is VguiImagePanel spec)
        {
            spec.Visible = targetFlag is not null;

            if (targetFlag is not null)
            {
                string image = state.Player(target)?.Team == TeamRed ? "../hud/objectives_flagpanel_carried_blue" : "../hud/objectives_flagpanel_carried_red";

                if (image != _specImageName)
                {
                    _specImageName = image;
                    spec.SetImage(image);
                }
            }
        }

        UpdateStatus(state, flags, CarriedBy(flags, state.LocalIndex), targetFlag?.EntityIndex ?? 0);
    }

    private static SceneFlag? CarriedBy(IReadOnlyList<SceneFlag> flags, int player)
    {
        for (int at = 0; at < flags.Count; at++)
        {
            if (player != 0 && flags[at].Carrier == player)
            {
                return flags[at];
            }
        }

        return null;
    }

    private static SceneFlag? Find(IReadOnlyList<SceneFlag> flags, int entity)
    {
        for (int at = 0; at < flags.Count; at++)
        {
            if (flags[at].EntityIndex == entity)
            {
                return flags[at];
            }
        }

        return null;
    }

    /// <summary>:539-628: drop a panel's disabled flag, then hand each shown flag to its team's panel.</summary>
    private int AssignFlags(IReadOnlyList<SceneFlag> flags)
    {
        if (Find(flags, BlueFlag.Entity) is not { Disabled: false })
        {
            BlueFlag.Entity = 0;
        }

        if (Find(flags, RedFlag.Entity) is not { Disabled: false })
        {
            RedFlag.Entity = 0;
        }

        int valid = 0;

        for (int at = 0; at < flags.Count; at++)
        {
            SceneFlag flag = flags[at];

            if (flag.Disabled && !flag.VisibleWhenDisabled)
            {
                continue;
            }

            switch (flag.Team)
            {
                case TeamRed:
                    RedFlag.Entity = flag.EntityIndex;
                    break;
                case TeamBlue:
                    BlueFlag.Entity = flag.EntityIndex;
                    break;
                case TeamUnassigned when BlueFlag.Entity == 0:
                    BlueFlag.Entity = flag.EntityIndex;
                    BlueFlag.Visible = true;
                    RedFlag.Visible = false;
                    break;
                case TeamUnassigned when RedFlag.Entity == 0 && flag.EntityIndex != BlueFlag.Entity:
                    RedFlag.Entity = flag.EntityIndex;
                    RedFlag.Visible = true;
                    break;
                default:
                    break;
            }

            valid++;
        }

        return valid;
    }

    /// <summary>:636-667: flag captures against `tf_flag_caps_per_round`, else the straight score.</summary>
    private void UpdateScores(HudState state)
    {
        if (state.Rules.PlayingHybridCtfCp)
        {
            return;
        }

        int capsPerRound = state.ConVars.GetInt("tf_flag_caps_per_round");
        int? blue = Shown(state.TeamStanding(TeamBlue), capsPerRound);
        int? red = Shown(state.TeamStanding(TeamRed), capsPerRound);

        SetPlayingToLabelVisible(capsPerRound > 0);

        // The same values Valve sets each 250 ms tick, set here only on a change: each set re-lays the labels.
        if ((blue, red, capsPerRound) == _scores)
        {
            return;
        }

        _scores = (blue, red, capsPerRound);

        if (blue is { } blueScore)
        {
            SetDialogVariable("bluescore", blueScore);
        }

        if (red is { } redScore)
        {
            SetDialogVariable("redscore", redScore);
        }

        if (capsPerRound > 0)
        {
            SetDialogVariable("rounds", capsPerRound);
        }
    }

    private static int? Shown(SceneTeam? team, int capsPerRound)
    {
        if (team is not { } standing)
        {
            return null;
        }

        return capsPerRound > 0 ? standing.FlagCaptures : standing.Score;
    }

    private void SetPlayingToLabelVisible(bool visible)
    {
        if (FindChildByName("PlayingTo") is { } playingTo && FindChildByName("PlayingToBG") is { } background)
        {
            playingTo.Visible = visible;
            background.Visible = visible;
        }
    }

    /// <summary>`UpdateStatus` (:775): the carrier's image and capture-zone compass, else each flag panel's status.</summary>
    private void UpdateStatus(HudState state, IReadOnlyList<SceneFlag> flags, SceneFlag? carried, int targetCarries)
    {
        VguiImagePanel? carriedImage = FindChildByName("CarriedImage") as VguiImagePanel;
        TfArrowPanel? capturePoint = FindChildByName("CaptureFlag") as TfArrowPanel;

        if (carried is { } own)
        {
            _carryingFlag = true;
            BlueFlag.Visible = false;
            RedFlag.Visible = false;

            // :809-834: the image is chosen once, as the panel shows; a team change mid-carry keeps it.
            if (carriedImage is { Visible: false })
            {
                carriedImage.SetImage(HudIcon(own.HudIcon, CarriedImageTeam(own.Type, state.Team)));
                carriedImage.Visible = true;
            }

            // :834-838: the outline sequence once per carry.
            if (!_flagAnimationPlayed)
            {
                _flagAnimationPlayed = true;
                HudViewport.Of(this)?.Animations.StartAnimationSequence("FlagOutline");
            }

            if (capturePoint is not null)
            {
                capturePoint.Visible = true;

                // :848-858: the last enabled capture zone of the local player's team, not dormant.
                IReadOnlyList<SceneCaptureZone> zones = state.CaptureFlags?.Zones ?? [];

                for (int at = 0; at < zones.Count; at++)
                {
                    if (!zones[at].Dormant && zones[at].Team == state.Team && !zones[at].Disabled)
                    {
                        capturePoint.Entity = (zones[at].Team, zones[at].Center);
                    }
                }

                capturePoint.Update(state, carriedByTarget: false);
            }

            return;
        }

        // :863-870: dropping it plays the sequence again.
        if (_carryingFlag)
        {
            _carryingFlag = false;
            HudViewport.Of(this)?.Animations.StartAnimationSequence("FlagOutline");
        }

        _flagAnimationPlayed = false;

        if (carriedImage is not null)
        {
            carriedImage.Visible = false;
        }

        if (capturePoint is not null)
        {
            capturePoint.Visible = false;
        }

        UpdateFlagPanel(state, BlueFlag, flags, targetCarries);
        UpdateFlagPanel(state, RedFlag, flags, targetCarries);
    }

    private static void UpdateFlagPanel(HudState state, TfFlagStatus panel, IReadOnlyList<SceneFlag> flags, int targetCarries)
    {
        if (Find(flags, panel.Entity) is { } flag)
        {
            panel.Visible = true;
            panel.UpdateStatus(state, flag, flag.EntityIndex == targetCarries);
        }
        else
        {
            panel.Visible = false;
        }
    }
}
