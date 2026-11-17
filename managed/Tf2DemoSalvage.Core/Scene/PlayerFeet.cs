using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// Every drawn player's anim-state feet, advanced once per rendered frame from the eyes the frame draws (B450, B750).
/// </summary>
/// <remarks>
/// <c>C_BaseAnimating::UpdateClientSideAnimations</c> runs once a frame in <c>OnRenderStart</c>, after the interpolation
/// (<c>cdll_client_int.cpp:2156</c>, <c>:2189</c>), and <c>C_TFPlayer::UpdateClientSideAnimation</c> hands each anim state
/// the eyes: the local player's <c>EyeAngles()</c>, <c>InterpolateViewpoint</c>'s local angles (B56), and anyone else's
/// <c>m_angEyeAngles</c>, which the interpolator wrote (<c>c_tf_player.cpp:4279-4288</c>, B749).
/// <c>ComputePoseParam_AimYaw</c> converges the feet by <c>gpGlobals-&gt;frametime</c> (<c>multiplayer_animstate.cpp:1759</c>).
///
/// **The frame time is the FRAME's, whoever was sampled last** (B750 review). A player not updated meanwhile — dead,
/// dormant or <c>EF_NODRAW</c>, where <c>ShouldUpdateAnimState</c> fails (<c>:1381-1395</c>) — keeps his feet, and
/// <c>ClearAnimationState</c> does not touch them (<c>:136-146</c>), so on his return they converge by one frame's time,
/// not by the whole absence. **And once a frame**: every consumer that asks for the players this frame — the moment, the
/// sounds, the bullets — reads the feet that frame turned, where advancing again would step the 45-degree goal again.
///
/// **One per playing viewer, never on the timeline**, for <see cref="DemoPlayer"/>'s reason: it is per-client state.
/// **Departures:** the engine's state runs from spawn; a player's here starts from the timeline's feet the first frame he
/// is asked for, and every player's restarts after a backward move, as a backward move restarts
/// <see cref="DemoPlayer"/>'s reader. A start plants goal and feet together, where the timeline's goal may differ.
/// Keyed on the entity index, so a reused slot carries the previous occupant's feet into the next one's first frame.
/// </remarks>
public sealed class PlayerFeet
{
    /// <summary>Each player's feet, the frame they were last turned, and `m_angRender[YAW]` as last drawn.</summary>
    private readonly Dictionary<int, (FeetYaw Feet, double Tick, float Rendered)> _players = [];

    /// <summary>The tick the current frame shows.</summary>
    private double _frame = double.NaN;

    /// <summary>The seconds the current frame advanced: <c>gpGlobals-&gt;frametime</c>.</summary>
    private float _frameSeconds;

    /// <summary>Advances one player's feet to <paramref name="tick"/>'s frame and says where they point.</summary>
    /// <param name="entityIndex">The player.</param>
    /// <param name="tick">The fractional tick this frame shows.</param>
    /// <param name="eyeYaw">The eye yaw the frame draws him with.</param>
    /// <param name="speed">His velocity's three-dimensional length (<c>:1709</c>).</param>
    /// <param name="timelineFeet">The timeline's feet at this tick, started from when there is no earlier frame.</param>
    /// <param name="intervalPerTick">Seconds a tick.</param>
    /// <returns><c>m_flCurrentFeetYaw</c>, which the class's path draws him at (<c>:1765</c>).</returns>
    internal float Advance(int entityIndex, double tick, float eyeYaw, float speed, float timelineFeet, float intervalPerTick)
    {
        BeginFrame(tick, intervalPerTick);

        if (!_players.TryGetValue(entityIndex, out (FeetYaw Feet, double Tick, float Rendered) known))
        {
            FeetYaw planted = FeetYaw.Planted(timelineFeet);
            _players[entityIndex] = (planted, tick, planted.Current);

            return planted.Current;
        }

        // Already turned this frame: read, not advanced again.
        if (Same(known.Tick, tick))
        {
            return known.Feet.Current;
        }

        FeetYaw feet = known.Feet;
        feet.Advance(eyeYaw, speed, _frameSeconds);
        _players[entityIndex] = (feet, tick, feet.Current);

        return feet.Current;
    }

    /// <summary>
    /// The drawn yaw of a custom model without the class's animations, which `CTFPlayerAnimState::Update` writes to
    /// `m_angRender` and returns before the feet (`tf_playeranimstate.cpp:340-366`, B765). The feet are left as they stood.
    /// </summary>
    /// <param name="entityIndex">The player.</param>
    /// <param name="tick">The fractional tick this frame shows.</param>
    /// <param name="written">The yaw this frame's branch writes, or null when the model does not rotate and it holds.</param>
    /// <param name="timelineYaw">The timeline's drawn yaw at this tick, started from when there is no earlier frame.</param>
    /// <param name="intervalPerTick">Seconds a tick.</param>
    /// <returns><c>m_angRender[YAW]</c>.</returns>
    internal float Render(int entityIndex, double tick, float? written, float timelineYaw, float intervalPerTick)
    {
        BeginFrame(tick, intervalPerTick);

        (FeetYaw Feet, double Tick, float Rendered) known = _players.TryGetValue(entityIndex, out (FeetYaw Feet, double Tick, float Rendered) was)
            ? was
            : (FeetYaw.Planted(timelineYaw), tick, timelineYaw);
        float rendered = written ?? known.Rendered;
        _players[entityIndex] = (known.Feet, tick, rendered);

        return rendered;
    }

    /// <summary>Starts <paramref name="tick"/>'s frame, once: every consumer of one frame asks at the same tick.</summary>
    private void BeginFrame(double tick, float intervalPerTick)
    {
        if (Same(tick, _frame))
        {
            return;
        }

        // NaN (no frame yet) and a backward move both fail this; a backward move restarts everyone.
        if (!(tick >= _frame))
        {
            _players.Clear();
            _frameSeconds = 0f;
        }
        else
        {
            _frameSeconds = (float)(tick - _frame) * intervalPerTick;
        }

        _frame = tick;
    }

    /// <summary>The same drawn moment, bit for bit: an identity, not a range.</summary>
    private static bool Same(double left, double right) =>
        System.BitConverter.DoubleToInt64Bits(left) == System.BitConverter.DoubleToInt64Bits(right);
}
