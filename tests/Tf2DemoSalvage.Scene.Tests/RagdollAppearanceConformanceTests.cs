using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A corpse's appearance is DERIVED from its class and team, exactly as
/// <c>C_TFRagdoll::CreateTFRagdoll</c> derives it (B315).
/// </summary>
/// <remarks>
/// **`DT_TFRagdoll` is `IMPLEMENT_CLIENTCLASS_DT_NOBASE`, so a corpse carries no model index, no
/// skin and no body.** That is the whole reason nothing drew: the fields a prop is built from are
/// not on the wire and never will be. The client does not miss them because it never reads them —
/// it computes the appearance from two integers that ARE sent, `m_iClass` and `m_iTeam`:
///
/// <code>
/// TFPlayerClassData_t *pData = GetPlayerClassData( m_iClass );
/// if ( pData ) nModelIndex = modelinfo->GetModelIndex( pData->GetModelName() );
/// …
/// if ( nModelIndex != -1 )
/// {
///     SetModelIndex( nModelIndex );
///     if ( m_iTeam == TF_TEAM_RED ) m_nSkin = 0; else m_nSkin = 1;
/// }
/// </code>
///
/// `c_tf_player.cpp:681-720`. Read-from-source.
///
/// **The test that matters is the one that distinguishes derived from networked.** A corpse whose
/// class and team are the only inputs must still come back with a model and a skin — so every case
/// here supplies nothing else, and a would-be implementation that reached for a model index would
/// return null for all of them.
/// </remarks>
public sealed class RagdollAppearanceConformanceTests
{
    /// <remarks>
    /// **The two teams must differ, or the assertion is insensitive to the manipulation.** A skin
    /// rule returning a constant passes any single-team test, and `_white`-style constants are how
    /// this project has shipped a both-teams-red bug before (`StudioSkins`' own remarks).
    /// </remarks>
    [Test]
    public void Skin_ForTheTwoTeams_IsZeroForRedAndOneForBlu()
    {
        RagdollAppearance.Of(Corpse(playerClass: 5, team: SceneTeams.Red), Classes())
            .Skin.ShouldBe(0);

        RagdollAppearance.Of(Corpse(playerClass: 5, team: SceneTeams.Blu), Classes())
            .Skin.ShouldBe(1);
    }

    /// <remarks>
    /// **The engine's `else` is unconditional**, so anything that is not RED paints BLU — there is
    /// no third branch for spectator or unassigned. A corpse can only be made by a player who was
    /// on a team, so this is a statement about the code rather than a case seen on the wire.
    /// </remarks>
    [Test]
    public void Skin_ForNoTeamAtAll_IsBlu()
    {
        RagdollAppearance.Of(Corpse(playerClass: 5, team: null), Classes()).Skin.ShouldBe(1);
    }

    /// <remarks>
    /// **Two classes, because one cannot tell a lookup from a constant.** Medic is 5 and Pyro is 7
    /// in `ETFClass` (`tf_shareddefs.h:203`), and their models are different files.
    /// </remarks>
    [Test]
    public void Model_ForAClassIndex_IsThatClassesModel()
    {
        Func<int, string?> classes = Classes();

        RagdollAppearance.Of(Corpse(playerClass: 5, team: SceneTeams.Red), classes)
            .Model.ShouldBe("models/player/medic.mdl");

        RagdollAppearance.Of(Corpse(playerClass: 7, team: SceneTeams.Red), classes)
            .Model.ShouldBe("models/player/pyro.mdl");
    }

    /// <remarks>
    /// **`GetPlayerClassData` can return a class with no model, and the engine checks.** Its guard
    /// is `if ( nModelIndex != -1 ) { SetModelIndex(…); … }` — so a corpse whose class resolves to
    /// nothing keeps no model AND no skin, because both live inside that one block. The skin coming
    /// back unset is the part a careless reading loses, since the two lines look independent.
    /// </remarks>
    [Test]
    public void Model_ForAClassWithNoModel_LeavesBothModelAndSkinUnset()
    {
        RagdollAppearance corpse =
            RagdollAppearance.Of(Corpse(playerClass: 42, team: SceneTeams.Blu), Classes());

        corpse.Model.ShouldBeNull();
        corpse.Skin.ShouldBeNull();
    }

    /// <summary>A corpse carrying only the two integers the appearance is derived from.</summary>
    private static SceneRagdoll Corpse(int playerClass, int? team) =>
        new(EntityIndex: 40,
            Serial: 1,
            PlayerClass: playerClass,
            Team: team,
            X: 0f,
            Y: 0f,
            Z: 0f,
            Gib: false,
            Burning: false,
            FeignDeath: false,
            WasDisguised: false,
            FirstTick: 100,
            LastTick: 200);

    /// <summary>
    /// A class table, in the shape the engine reads one: an index in, a model name out.
    /// </summary>
    /// <remarks>
    /// **Synthetic, and that makes it stronger rather than weaker** (D38). The real table is parsed
    /// from `scripts/playerclasses/*.txt` inside the game's VPKs, so a test built on it needs TF2
    /// installed and cannot run on CI or a measurement box — and it would be comparing two readings
    /// of Valve's data rather than checking our derivation. This one HAS ground truth, because the
    /// test put the answer in. `PlayerClassModels.Model` is the production binding and
    /// `ClassAirwalkTests` is what proves it reads the real files.
    /// </remarks>
    private static Func<int, string?> Classes() =>
        playerClass => playerClass switch
        {
            5 => "models/player/medic.mdl",
            7 => "models/player/pyro.mdl",
            _ => null,
        };
}
