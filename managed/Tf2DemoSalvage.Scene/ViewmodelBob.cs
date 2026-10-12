using System;
using System.Globalization;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// A viewmodel's head bob — `BobState_t` with `CalcViewModelBobHelper` and `AddViewModelBobHelper`
/// (`tf_weaponbase.h:70-85`, `tf_weaponbase.cpp:4713-4822`, B525).
/// </summary>
/// <remarks>
/// **State the engine steps once a frame**, kept on the viewmodel (`CTFViewModel::m_BobState`) and fed by the weapon
/// (`CTFWeaponBase::AddViewmodelBob`, `:4845`): the speed of the weapon's OWNER — the spectated player, in-eye — slewed
/// at 320 units a second per second, integrated into a bob time, and two sines read off it.
///
/// **It was "deliberately not copied"** (`MomentScene`'s viewmodel remarks, before D89) as "a function of movement and
/// elapsed time rather than of anything the recording holds". The speed is in the recording and the rest is this
/// arithmetic.
///
/// **What cannot be reproduced is the PHASE, and TF2 does not reproduce it either.** The bob time is an integral
/// from whenever the viewmodel was made over whoever's weapon it has shown since, so two people watching one demo
/// see the same sway out of step. This steps it from the viewer's own playback, as the client does from its own. A
/// paused frame changes nothing (`frametime` 0), so a `--shot`, which seeks and draws paused, has no bob at all —
/// as a TF2 client paused straight after `demo_gototick` would have whatever the skip left it.
///
/// **`cl_bobcycle` 0.8 and `cl_bobup` 0.5 are constants here**: both are `FCVAR_CHEAT` (`:4707-4708`), which a demo
/// resets to its default.
///
/// *Interpolated:* the speed handed in. The engine reads `GetLocalVelocity().Length2D()`, which for another player
/// is the network origin's change over the simulation time's (`C_BasePlayer::PostDataUpdate`); this is given the
/// scene player's speed, the animation state's estimate from position history. *Not built:* the second helping an
/// off-hand viewmodel gives itself (`CTFViewModel::AddViewModelBob`, viewmodel index 1), and the view shake after it
/// (`vieweffects->ApplyShake`, `baseviewmodel_shared.cpp:414`).
/// </remarks>
public sealed class ViewmodelBob
{
    /// <summary>`cl_bobcycle`.</summary>
    private const float BobCycle = 0.8f;

    /// <summary>`cl_bobup`.</summary>
    private const float BobUp = 0.5f;

    /// <summary>The speed the bob is full at, and its slew rate per second.</summary>
    private const float FullSpeed = 320f;

    private float _bobTime;
    private float _lastBobTime;
    private float _lastSpeed;

    /// <summary>`m_flVerticalBob`.</summary>
    public float Vertical { get; private set; }

    /// <summary>`m_flLateralBob`.</summary>
    public float Lateral { get; private set; }

    /// <summary>`CalcViewModelBobHelper`, once a frame.</summary>
    /// <param name="speed">The owner's horizontal speed, or null when there is no player.</param>
    /// <param name="seconds">`gpGlobals->curtime`.</param>
    /// <param name="frameAdvanced">Whether `gpGlobals->frametime` is not zero: false while paused.</param>
    public void Calc(float? speed, float seconds, bool frameAdvanced)
    {
        if (!frameAdvanced || speed is not { } moving)
        {
            return;
        }

        float most = MathF.Max(0f, (seconds - _lastBobTime) * FullSpeed);

        // `clamp( val, min, max )` is `val < min ? min : ( val > max ? max : val )`, written out because the two
        // bounds cross when time runs backwards and `Math.Clamp` would throw.
        moving = ClampAsTheEngine(moving, _lastSpeed - most, _lastSpeed + most);
        moving = ClampAsTheEngine(moving, -FullSpeed, FullSpeed);

        _lastSpeed = moving;

        // `RemapVal( speed, 0, 320, 0, 1 )`.
        float offset = moving / FullSpeed;

        _bobTime += (seconds - _lastBobTime) * offset;
        _lastBobTime = seconds;

        float cycle = _bobTime - ((int)(_bobTime / BobCycle) * BobCycle);

        Vertical = Swing(moving, cycle / BobCycle);

        // Valve's precedence, kept: twice the quotient, not the quotient by twice the cycle.
        cycle = _bobTime - ((int)(_bobTime / BobCycle * 2f) * BobCycle * 2f);

        Lateral = Swing(moving, cycle / (BobCycle * 2f));
    }

    /// <summary>`AddViewModelBobHelper`.</summary>
    /// <param name="at">The viewmodel's origin and angles so far.</param>
    /// <returns>Where the bob leaves it.</returns>
    public ViewmodelPlacement Add(ViewmodelPlacement at)
    {
        (float fx, float fy, float fz) = AngleVectors.Forward(at.Pitch, at.Yaw);
        (float rx, float ry, float rz) = AngleVectors.Right(at.Pitch, at.Yaw, at.Roll);

        float forward = Vertical * 0.4f;
        float sideways = Lateral * 0.2f;

        return new ViewmodelPlacement(
            at.X + (fx * forward) + (rx * sideways),
            at.Y + (fy * forward) + (ry * sideways),
            at.Z + (fz * forward) + (Vertical * 0.1f) + (rz * sideways),
            at.Pitch - (Vertical * 0.4f),
            at.Yaw - (Lateral * 0.3f),
            at.Roll + (Vertical * 0.5f));
    }

    /// <summary>The state in one line, for a log.</summary>
    /// <returns>The bob time and the two bobs.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"bob time {_bobTime:0.###}, vertical {Vertical:0.###}, lateral {Lateral:0.###}");

    /// <summary>One bob from a cycle fraction: the up half and the down half, then the sine and the clamp.</summary>
    private static float Swing(float speed, float cycle)
    {
        double angle = cycle < BobUp
            ? Math.PI * cycle / BobUp
            : Math.PI + (Math.PI * (cycle - BobUp) / (1.0 - BobUp));

        float bob = speed * 0.005f;

        bob = (float)((bob * 0.3) + (bob * 0.7 * Math.Sin((float)angle)));

        return ClampAsTheEngine(bob, -7f, 4f);
    }

    private static float ClampAsTheEngine(float value, float least, float most)
    {
        if (value < least)
        {
            return least;
        }

        return value > most ? most : value;
    }
}

/// <summary>
/// The min-mode offset — `tf_use_min_viewmodels` and the weapon's `min_viewmodel_offset`
/// (`CTFViewModel::CalcViewModelView`, `tf_viewmodel.cpp:196-221`; `CTFWeaponBase::GetViewmodelOffset`,
/// `tf_weaponbase.cpp:6937-6953`; B525).
/// </summary>
/// <remarks>
/// <code>
/// bool bMinMode = tf_use_min_viewmodels.GetBool();
/// if ( bMinMode ) {
///     AngleVectors( eyeAngles, &amp;forward, &amp;right, &amp;up );
///     viewmodelOffset = pWeapon->GetViewmodelOffset();     // UTIL_StringToVector of the string attribute, else zero
///     vecNewOrigin += viewmodelOffset.x * forward + viewmodelOffset.y * right + viewmodelOffset.z * up;
/// }
/// </code>
/// Applied to the origin BEFORE the base adds the bob. *Not built:* `tf_viewmodels_offset_override` (a cheat) and
/// the inspect animation's blend of the offset (`Gain( 1 − s_inspectInterp, 0.5 )`), which is one while nobody is
/// inspecting.
/// </remarks>
public static class MinViewmodel
{
    /// <summary>Moves the eye by a weapon's min-mode offset.</summary>
    /// <param name="eye">The eye.</param>
    /// <param name="on">`tf_use_min_viewmodels`.</param>
    /// <param name="attribute">The weapon item's `min_viewmodel_offset`, such as <c>10 -3 -10</c>; null for none.</param>
    /// <returns>The eye, moved.</returns>
    public static ViewmodelPlacement Offset(ViewmodelPlacement eye, bool on, string? attribute)
    {
        if (!on || string.IsNullOrEmpty(attribute))
        {
            return eye;
        }

        // `UTIL_StringToVector`: up to three numbers, each `atof` of the text up to the next space.
        string[] parts = attribute.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Span<float> offset = stackalloc float[3];

        for (int axis = 0; axis < 3 && axis < parts.Length; axis++)
        {
            offset[axis] = (float)Core.Primitives.CStdlib.Atof(parts[axis]);
        }

        (float fx, float fy, float fz) = AngleVectors.Forward(eye.Pitch, eye.Yaw);
        (float rx, float ry, float rz) = AngleVectors.Right(eye.Pitch, eye.Yaw, eye.Roll);
        (float ux, float uy, float uz) = AngleVectors.Up(eye.Pitch, eye.Yaw, eye.Roll);

        return eye with
        {
            X = eye.X + (offset[0] * fx) + (offset[1] * rx) + (offset[2] * ux),
            Y = eye.Y + (offset[0] * fy) + (offset[1] * ry) + (offset[2] * uy),
            Z = eye.Z + (offset[0] * fz) + (offset[1] * rz) + (offset[2] * uz),
        };
    }
}
