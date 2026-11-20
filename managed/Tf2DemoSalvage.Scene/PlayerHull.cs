using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>A TF2 player's collision hull, for the questions that ask for its middle.</summary>
/// <remarks>
/// <c>VEC_HULL_MAX</c> is 82 units tall and <c>VEC_DUCK_HULL_MAX</c> 62, both from a floor at the origin
/// (`tf_gamerules.cpp`'s <c>g_TFViewVectors</c>), so <c>WorldSpaceCenter()</c> — the hull's middle — is 41 or 31 units above
/// a player's feet depending on <c>FL_DUCKING</c>.
/// </remarks>
public static class PlayerHull
{
    /// <summary><c>FL_DUCKING</c> (`const.h:149`).</summary>
    public const int DuckingFlag = 1 << 1;

    /// <summary>The standing hull's height.</summary>
    public const float StandingHeight = 82f;

    /// <summary>The ducked hull's height.</summary>
    public const float DuckedHeight = 62f;

    /// <summary>How far above a player's origin <c>WorldSpaceCenter()</c> is.</summary>
    /// <param name="flags">The player's <c>m_fFlags</c>, or null when the recording did not say.</param>
    /// <returns>Half the standing or ducked hull's height.</returns>
    public static float CenterHeight(int? flags) =>
        (((flags ?? 0) & DuckingFlag) != 0 ? DuckedHeight : StandingHeight) * 0.5f;

    /// <summary>The player's collision property as the client sets it (B69).</summary>
    /// <param name="flags">The player's <c>m_fFlags</c>, or null when the recording did not say.</param>
    /// <returns><c>SOLID_BBOX</c> over the standing or ducked hull.</returns>
    /// <remarks>
    /// <c>C_BasePlayer::PostThink</c> sets <c>VEC_DUCK_HULL_MIN/MAX</c> under <c>FL_DUCKING</c> and <c>VEC_HULL_MIN/MAX</c>
    /// otherwise (<c>c_baseplayer.cpp:2084-2092</c>); TF's are (−24, −24, 0) to (24, 24, 82 or 62)
    /// (<c>tf_gamerules.cpp:1313-1317</c>). <c>SOLID_BBOX</c> is 2 (<c>const.h:240</c>), so the box is world-aligned.
    /// </remarks>
    public static SceneCollision Collision(int? flags) =>
        new(SolidBox, SolidFlags: 0, CollisionGroup: 0)
        {
            Mins = (-HalfWidth, -HalfWidth, 0f),
            Maxs = (HalfWidth, HalfWidth, CenterHeight(flags) * 2f),
        };

    /// <summary><c>SOLID_BBOX</c> (<c>const.h:240</c>).</summary>
    private const int SolidBox = 2;

    /// <summary>The hull's half width, both hulls (<c>tf_gamerules.cpp:1313-1317</c>).</summary>
    private const float HalfWidth = 24f;
}
