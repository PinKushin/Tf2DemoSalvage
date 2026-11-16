using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// Every drawn player's anim-state feet, advanced once per rendered frame from the eyes the frame draws (B450, B750).
/// </summary>
/// <remarks>
/// <c>C_TFPlayer::UpdateClientSideAnimation</c> runs each frame and hands the anim state the eyes: the local player's
/// <c>EyeAngles()</c>, <c>InterpolateViewpoint</c>'s local angles (B56), and anyone else's <c>m_angEyeAngles</c>, which
/// the interpolator wrote (<c>c_tf_player.cpp:4279-4288</c>, B749). <c>ComputePoseParam_AimYaw</c> converges the feet by
/// <c>gpGlobals-&gt;frametime</c> (<c>multiplayer_animstate.cpp:1759</c>).
///
/// **One per playing viewer, never on the timeline**, for <see cref="DemoPlayer"/>'s reason: it is per-client state.
/// The frame time is the playback moved since that player's last frame. **Departure:** the engine's state runs from
/// spawn; each player's here starts, and restarts after a backward move, from the timeline's feet at that tick — the
/// nearest carried state, as a backward move restarts <see cref="DemoPlayer"/>'s reader. Keyed on the entity index, so a
/// reused slot carries the previous occupant's feet into the next one's first frame.
/// </remarks>
public sealed class PlayerFeet
{
    private readonly Dictionary<int, (FeetYaw Feet, double LastTick)> _players = [];

    /// <summary>Advances one player's feet to <paramref name="tick"/> and says where they point.</summary>
    /// <param name="entityIndex">The player.</param>
    /// <param name="tick">The fractional tick this frame shows.</param>
    /// <param name="eyeYaw">The eye yaw the frame draws him with.</param>
    /// <param name="speed">His velocity's three-dimensional length (<c>:1709</c>).</param>
    /// <param name="timelineFeet">The timeline's feet at this tick, started from when there is no earlier frame.</param>
    /// <param name="intervalPerTick">Seconds a tick.</param>
    /// <returns><c>m_flCurrentFeetYaw</c>.</returns>
    internal float Advance(int entityIndex, double tick, float eyeYaw, float speed, float timelineFeet, float intervalPerTick)
    {
        (FeetYaw feet, double last) = _players.TryGetValue(entityIndex, out (FeetYaw, double) known) ? known : (default, double.NaN);

        // NaN (no frame yet) and a backward move both fail this.
        if (!(tick >= last))
        {
            feet = FeetYaw.Planted(timelineFeet);
            last = tick;
        }

        feet.Advance(eyeYaw, speed, (float)(tick - last) * intervalPerTick);
        _players[entityIndex] = (feet, tick);

        return feet.Current;
    }
}
