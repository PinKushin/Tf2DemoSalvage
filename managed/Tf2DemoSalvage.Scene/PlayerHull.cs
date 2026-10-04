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
}
