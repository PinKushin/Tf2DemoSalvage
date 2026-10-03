namespace Tf2DemoSalvage.Core.Scene;

/// <summary>Which of the player's own activity tables `CTFPlayerAnimState::ActivityOverride` walks (B437).</summary>
/// <remarks>
/// **Named for the tables** — `s_acttableKartState`, `s_acttableCompetitiveLoserState`, `s_acttableLoserState`,
/// `s_acttableBuildingDeployed` (`tf_playeranimstate.cpp:157-221`). The rows are the installed game's concern and
/// live beside the weapon tables in Content; Core decides which applies, because it holds the state that decides.
/// </remarks>
public enum PlayerActivityOverride
{
    /// <summary>No table: the weapon's alone.</summary>
    None,

    /// <summary>In a Halloween kart.</summary>
    KartState,

    /// <summary>On the losing side of a competitive match.</summary>
    CompetitiveLoserState,

    /// <summary>Humiliated: `m_Shared.IsLoser()`.</summary>
    LoserState,

    /// <summary>Carrying a building: `m_Shared.IsCarryingObject()`.</summary>
    BuildingDeployed,
}

/// <summary>`CTFPlayerAnimState::ActivityOverride`'s choice of table (`tf_playeranimstate.cpp:223-258`).</summary>
public static class PlayerActivityOverrides
{
    /// <summary>The table for a player's state, in the engine's order.</summary>
    /// <param name="conditions">`m_nPlayerCond` and its extensions.</param>
    /// <param name="isLoser">`m_Shared.IsLoser()`, the rule <see cref="LoserState.IsLoser"/>.</param>
    /// <param name="carrying">`m_Shared.IsCarryingObject()`.</param>
    /// <returns>The table, or <see cref="PlayerActivityOverride.None"/>.</returns>
    /// <remarks>
    /// <code>
    /// if ( InCond( TF_COND_HALLOWEEN_KART ) )          kart
    /// else if ( InCond( TF_COND_COMPETITIVE_LOSER ) )  competitive loser
    /// else if ( IsLoser() )                            loser
    /// else if ( IsCarryingObject() )                   building deployed
    /// </code>
    /// </remarks>
    public static PlayerActivityOverride For(PlayerConditions conditions, bool isLoser, bool carrying)
    {
        if (conditions.Has(PlayerConditions.HalloweenKart))
        {
            return PlayerActivityOverride.KartState;
        }

        if (conditions.Has(PlayerConditions.CompetitiveLoser))
        {
            return PlayerActivityOverride.CompetitiveLoserState;
        }

        if (isLoser)
        {
            return PlayerActivityOverride.LoserState;
        }

        return carrying ? PlayerActivityOverride.BuildingDeployed : PlayerActivityOverride.None;
    }
}
