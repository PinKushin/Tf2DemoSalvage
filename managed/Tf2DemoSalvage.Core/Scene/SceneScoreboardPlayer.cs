namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// One player resource slot, as `CTFClientScoreBoardDialog::UpdatePlayerList` reads it off
/// `CTFPlayerResource`/`DT_PlayerResource` (`tf_clientscoreboard.cpp:1284-1621`).
/// </summary>
/// <remarks>
/// **Every slot that is connected or valid, not only the ones with a position this tick.** The
/// scoreboard's own loop is `for ( playerIndex = 1; playerIndex &lt;= MAX_PLAYERS; playerIndex++ )
/// if ( g_PR-&gt;IsConnected( playerIndex ) || g_PR-&gt;IsValid( playerIndex ) )` (:1284-1286) — a
/// player who has not spawned, or a corpse whose entity carries no origin, still belongs in the
/// list. <see cref="ScenePlayer"/> is built from the opposite rule (a known origin), so this is a
/// separate collection rather than an addition to it, the same reasoning that keeps
/// <c>SceneBuilding</c> and <c>SceneRoundTimer</c> apart from it.
///
/// **Not modelled:** `IsFakePlayer` (`c_playerresource.cpp:256`) reads the engine's own
/// `player_info_t::fakeplayer`, not a resource array — the demo carries the same bit on the
/// `userinfo` string table instead, as `Core.Net.PlayerInfo.IsBot`, so a caller wanting "is this a
/// bot" reads the roster rather than this record. `GetActiveDominations` clamps its result into
/// `SCOREBOARD_DOMINATION_ICONS` (`:1338-1353`) purely to pick an icon; this carries the raw count
/// and leaves clamping to whoever draws it.
/// </remarks>
/// <param name="EntityIndex">The player slot, 1 to `MAX_PLAYERS` (101 on `TF_DLL`, `shareddefs.h:254`).</param>
public readonly record struct SceneScoreboardPlayer(int EntityIndex)
{
    /// <summary>`IsConnected` — `m_bConnected` (`c_playerresource.cpp:24`).</summary>
    public bool Connected { get; init; }

    /// <summary>`IsValid` — `m_bValid` (`:30`).</summary>
    public bool Valid { get; init; }

    /// <summary>`GetTeam` — `m_iTeam` (`:26`), the base resource's copy the scoreboard actually reads (`tf_clientscoreboard.cpp:1289`).</summary>
    public int? Team { get; init; }

    /// <summary>`IsAlive` — `m_bAlive` (`:27`).</summary>
    public bool Alive { get; init; }

    /// <summary>`GetPlayerScore` — `m_iScore` (`:23`); the "kills" dialog variable in `UpdatePlayerDetails` (`tf_clientscoreboard.cpp:1883`), not the scoreboard's own "score" column.</summary>
    public int Score { get; init; }

    /// <summary>`CTFPlayerResource::GetTotalScore` — `m_iTotalScore` (`c_tf_playerresource.cpp:25`): the scoreboard's "score" column (`:1377`).</summary>
    public int TotalScore { get; init; }

    /// <summary>`GetDeaths` — `m_iDeaths` (`c_playerresource.cpp:25`).</summary>
    public int Deaths { get; init; }

    /// <summary>`GetPing` — `m_iPing` (`:22`).</summary>
    public int Ping { get; init; }

    /// <summary>`CTFPlayerResource::GetPlayerClass` — `m_iPlayerClass` (`c_tf_playerresource.cpp:28`).</summary>
    public int? PlayerClass { get; init; }

    /// <summary>`GetActiveDominations` — `m_iActiveDominations` (`:30`), raw (see remarks).</summary>
    public int ActiveDominations { get; init; }
}
