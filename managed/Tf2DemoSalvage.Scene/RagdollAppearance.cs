using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// What a corpse looks like, derived from the two integers <c>DT_TFRagdoll</c> sends.
/// </summary>
/// <param name="Model">Its model path, or null when the class names no model.</param>
/// <param name="Skin">
/// Its skin family, or null when there is no model. **Null together with the model, deliberately**
/// — the engine sets both inside one <c>if ( nModelIndex != -1 )</c> block, so a corpse with no
/// model never reaches the skin lines at all.
/// </param>
/// <param name="Material">
/// One VMT replacing every material the model has — gold or ice — or null for the ordinary case.
/// See <see cref="MaterialFor"/> for which wins and why the legacy wrench path does not paint.
/// </param>
public readonly record struct RagdollAppearance(string? Model, int? Skin, string? Material = null)
{
    /// <summary>What a Saxxy or Golden Wrench kill paints a corpse with.</summary>
    /// <remarks>`c_tf_player.cpp:958`.</remarks>
    public const string GoldMaterial = "models/player/shared/gold_player.vmt";

    /// <summary>What a Spy-cicle backstab paints a corpse with.</summary>
    /// <remarks>`c_tf_player.cpp:966`.</remarks>
    public const string IceMaterial = "models/player/shared/ice_player.vmt";

    /// <summary>The single material that replaces every one of a corpse's own, if any.</summary>
    /// <param name="corpse">The corpse.</param>
    /// <returns>A VMT path, or null when the corpse keeps its own materials.</returns>
    /// <remarks>
    /// **Two subtleties, both easy to lose by reading the tail of `CreateTFRagdoll` alone.**
    ///
    /// <code>
    /// const char *materialOverrideFilename = NULL;
    ///
    /// if ( m_bFixedConstraints )
    /// {
    ///     if ( m_bGoldRagdoll )
    ///         materialOverrideFilename = "models/player/shared/gold_player.vmt";
    /// }
    ///
    /// if ( m_bIceRagdoll )
    ///     materialOverrideFilename = "models/player/shared/ice_player.vmt";
    /// </code>
    ///
    /// `c_tf_player.cpp:952-971`. Read-from-source.
    ///
    /// **Ice beats gold**, because its assignment is second and unconditional — a corpse that is
    /// somehow both comes out frozen, not golden.
    ///
    /// **And the legacy wrench path does NOT paint.** Earlier in the function,
    /// `if ( m_bGoldRagdoll || m_iDamageCustom == TF_DMG_CUSTOM_GOLD_WRENCH )` plays the sound and
    /// sets `m_bFixedConstraints`, which reads as "either makes it gold". It does not: the material
    /// block tests `m_bGoldRagdoll` again inside the constraints guard, so a Golden Wrench kill with
    /// the flag clear gets stiff constraints and a noise and keeps its own skin. Reproduced as
    /// written.
    ///
    /// **`m_bFixedConstraints` is not tested here, and it is not a dropped guard.** It is client-side
    /// state rather than a networked field, and the only assignment that can precede the material
    /// block is the one two lines under the gold test itself (`c_tf_player.cpp:733`) — so
    /// `m_bGoldRagdoll` implies it. Checked rather than assumed: `CreateTFRagdoll` runs straight
    /// through from 700 to 971 with no `return` between the two, and nothing anywhere clears the
    /// flag. The other assignment (`:859`, ice caught in midair) can only add a corpse that is
    /// already taking the ice path. The conjunction therefore reduces to `m_bGoldRagdoll` exactly.
    /// Arithmetic on read-from-source.
    /// </remarks>
    public static string? MaterialFor(SceneRagdoll corpse)
    {
        // Ice first, because it is assigned second in the engine and so overwrites gold.
        if (corpse.Ice)
        {
            return IceMaterial;
        }

        return corpse.Gold ? GoldMaterial : null;
    }

    /// <summary>Derives a corpse's appearance.</summary>
    /// <param name="corpse">The corpse, as <c>DT_TFRagdoll</c> described it.</param>
    /// <param name="modelForClass">
    /// The class table — an index in, a model name out, which is the shape of
    /// <c>GetPlayerClassData( m_iClass )->GetModelName()</c>. Production passes
    /// <c>PlayerClassModels.Model</c>.
    /// </param>
    /// <returns>The model and skin to draw it with.</returns>
    /// <remarks>
    /// **This is the whole reason corpses were invisible, and it is not a decode fault.**
    /// <c>DT_TFRagdoll</c> is `IMPLEMENT_CLIENTCLASS_DT_NOBASE` (`c_tf_player.cpp:518`), so it
    /// inherits nothing from `DT_BaseAnimating` and carries no `m_nModelIndex`, no `m_nSkin` and no
    /// `m_nBody`. Every corpse in a match decoded correctly and none could be built into a prop,
    /// because the fields a prop is made from were never on the wire. The client has the same
    /// problem and solves it by DERIVING, not by reading:
    ///
    /// <code>
    /// TFPlayerClassData_t *pData = GetPlayerClassData( m_iClass );
    /// if ( pData ) nModelIndex = modelinfo->GetModelIndex( pData->GetModelName() );
    ///
    /// if ( nModelIndex != -1 )
    /// {
    ///     SetModelIndex( nModelIndex );
    ///     if ( m_iTeam == TF_TEAM_RED ) m_nSkin = 0; else m_nSkin = 1;
    /// }
    /// </code>
    ///
    /// `C_TFRagdoll::CreateTFRagdoll`, `c_tf_player.cpp:681-720`. Read-from-source.
    ///
    /// **What is NOT done here, and why each is separate:**
    ///
    /// - **The live-player branch.** The engine prefers `pPlayer->GetPlayerClass()->GetModelName()`
    ///   over `m_iClass` when the player is still around and is not a spy being drawn disguised.
    ///   Both routes end at the same class table, and they differ only for a custom player model
    ///   (`m_iszCustomModel`, `tf_playerclass_shared.cpp:139`) — which nothing in TF2 sets outside
    ///   Mann-vs-Machine bots. Filed rather than guessed at.
    /// - **`m_nBody`**, which the engine copies WHOLE from the player: `m_nBody = pPlayer->GetBody()`
    ///   under `if ( !m_bFeignDeath || m_bWasDisguised )` (`c_tf_player.cpp:790-793`). It needs the
    ///   player's bodygroup state at the moment of death, which is a different question from this
    ///   one and has its own guard.
    /// - **The zombie skin swap.** `if ( pPlayer &amp;&amp; pPlayer-&gt;BRenderAsZombie() )` runs
    ///   `AdjustSkinIndexForZombie` over the skin this method chose (`c_tf_player.cpp:722-726`) —
    ///   a Halloween mode, its own flag, and it needs the living player rather than the corpse.
    ///
    /// **Gold and ice ARE done**, in <see cref="MaterialFor"/>, and are carried as a material rather
    /// than a skin because that is what the engine does with them (B325).
    ///
    /// **The skin is written out here rather than calling <c>PlayerSkin.ForTeam</c>, and that is not
    /// a DRY failure — the two rules genuinely differ.** They come from two different engine
    /// functions, which agree on RED and BLU and disagree on everything else:
    ///
    /// <code>
    /// // C_TFPlayer::GetSkin, c_tf_player.cpp:7807-7817 — what PlayerSkin.ForTeam implements
    /// case TF_TEAM_RED:  nSkin = 0; break;
    /// case TF_TEAM_BLUE: nSkin = 1; break;
    /// default:           nSkin = 0; break;
    ///
    /// // C_TFRagdoll::CreateTFRagdoll, c_tf_player.cpp:712-719 — what this implements
    /// if ( m_iTeam == TF_TEAM_RED ) m_nSkin = 0; else m_nSkin = 1;
    /// </code>
    ///
    /// A player with no team falls to RED; a corpse with no team falls to BLU. Sharing the helper
    /// would import the player's `default` into the corpse and silently repaint one of them. Found
    /// by `Skin_ForNoTeamAtAll_IsBlu`, which failed against exactly that reuse.
    /// </remarks>
    public static RagdollAppearance Of(SceneRagdoll corpse, Func<int, string?> modelForClass)
    {
        ArgumentNullException.ThrowIfNull(modelForClass);

        // `GetPlayerClassData( m_iClass )` — and a corpse whose class never arrived has none.
        if (corpse.PlayerClass is not { } playerClass ||
            modelForClass(playerClass) is not { } model)
        {
            return new RagdollAppearance(null, null);
        }

        return new RagdollAppearance(
            model,
            Skin: corpse.Team == SceneTeams.Red ? 0 : 1,
            Material: MaterialFor(corpse));
    }
}
