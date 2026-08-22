using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Which skin family a player's model draws in, which the client derives rather than receives.
/// </summary>
/// <remarks>
/// **Extracted for the same reason as <c>BlendStates</c> and <c>DecalState</c>: a
/// conformance test has to be able to reach it.** The rule was one expression inside an object
/// initialiser in <c>MainForm</c>, so <c>SkinOverrideConformanceTests</c> could quote Valve's source
/// and had nothing of ours to compare it against.
///
/// **The rule is the client's, not the server's**, which is why it is computed here at all.
/// <c>c_tf_player.cpp:712-719</c> assigns <c>m_nSkin</c> from <c>m_iTeam</c> while setting the
/// model, and the field is marked <c>FTYPEDESC_PRIVATE</c> in the prediction data — so for a PLAYER
/// it is derived client state rather than a value the demo carries. Props are the opposite case: a
/// capture point's skin comes from ownership on the server and must be read.
///
/// That distinction was checked on exactly the suspicion that retaining <c>m_nSkin</c> had made this
/// redundant. It has not.
/// </remarks>
public static class PlayerSkin
{
    /// <summary>RED draws in family 0, BLU in family 1.</summary>
    /// <param name="team">The player's team, or null when the demo has not said yet.</param>
    /// <returns>The skin family index.</returns>
    /// <remarks>
    /// <c>m_nSkin = ( m_iTeam == TF_TEAM_RED ) ? 0 : 1</c>, and the polarity here is deliberately
    /// the other way round: this asks "is it BLU", defaulting to 0.
    ///
    /// **The two therefore disagree on everything that is neither RED nor BLU**, and that is the
    /// right call rather than an oversight. Valve's form sends spectator, unassigned and an unknown
    /// team to skin 1 — BLU — because the engine only ever reaches this line for a player who has a
    /// team. Here a team can genuinely be **null**: a player entity may exist before the demo has
    /// said which side they are on. Defaulting those to BLU would make every joining player flash
    /// blue for a tick or two.
    ///
    /// Written down because it is exactly the kind of difference that reads as a transcription
    /// error later — it is a transcription of the INTENT with a case the engine does not have.
    /// </remarks>
    public static int ForTeam(int? team) => team == SceneTeams.Blu ? 1 : 0;
}
