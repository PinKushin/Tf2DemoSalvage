using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Turns the corpses a demo describes into props the scene can draw (B315).
/// </summary>
/// <remarks>
/// **A corpse cannot become a <c>ScenePropTrack</c> where every other prop does, and the reason is
/// the layering rather than the format.** A track is built inside `DemoTimeline`, which takes
/// nothing but the demo's bytes — deliberately, so decoding runs on a machine with no TF2 installed
/// (`docs/memory/ci-is-the-machine-without-tf2.md`). A corpse's model is not in the demo at all; it
/// is derived from `m_iClass` through `scripts/playerclasses/*.txt` inside the game's VPKs. So the
/// decode carries the class and this layer, which may open the install, turns it into a prop.
///
/// **What the engine draws and this does not, stated rather than left to be discovered:**
///
/// - **The physics.** 75% of corpses are `InitAsClientRagdoll` and fall where the solver puts them;
///   this holds the networked `m_vecRagdollOrigin`. See D136 for why the 25/75 split itself is not
///   an open question, and B58 for the physics.
/// - **`m_nBody`**, copied off the player under `if ( !m_bFeignDeath || m_bWasDisguised )`
///   (`c_tf_player.cpp:790-793`), and the `RagdollSpawn` sequence, which needs the model opened and
///   so sits further down the render path than this.
///
/// **The fade is NOT among them, and the measurement is why.** It looked like a detail worth
/// deferring — until ending a corpse at its entity's lifetime was measured at 57 bodies on the map
/// at once against a twelve-player roster. `RagdollFade` carries `C_TFRagdoll::ClientThink`'s rule
/// and is what makes this a feature rather than a defect.
/// </remarks>
public static class RagdollProps
{
    /// <summary>Fills a buffer with the corpses present at a tick.</summary>
    /// <param name="corpses">Every corpse the demo described.</param>
    /// <param name="tick">The moment being shown.</param>
    /// <param name="modelForClass">The class table — an index in, a model name out.</param>
    /// <param name="fade">When each corpse expires, or null to keep every one the demo describes.</param>
    /// <param name="visible">
    /// Which entities were visible on the previous frame — the engine's own arrangement, since
    /// <c>IsVisible()</c> reports the last render. Null treats every corpse as unseen, which is the
    /// safe direction: it expires them on the long timer rather than keeping them for ever.
    /// </param>
    /// <param name="into">The buffer to append to; NOT cleared.</param>
    /// <returns>How many were appended.</returns>
    /// <remarks>
    /// **Appended rather than cleared, because this runs after the props.** The scene's buffer is
    /// filled by `DemoTimeline.PropsAt`, which clears it as its first act; clearing again here would
    /// throw away every prop in the scene and leave a match containing nothing but its dead.
    ///
    /// **Two bounds, and only the second is what a viewer sees.** The entity's window
    /// (`FirstTick`..`LastTick`) says when the demo described the corpse at all; `RagdollFade` says
    /// when the client would still have been drawing it. The first alone admits far more bodies
    /// than TF2 ever shows, because the server keeps one ragdoll per player until that player next
    /// dies — 57 at once, measured. The second alone would draw corpses the demo never mentioned.
    /// </remarks>
    public static int Fill(
        IReadOnlyList<SceneRagdoll> corpses,
        double tick,
        Func<int, string?> modelForClass,
        ICollection<SceneProp> into,
        RagdollFade? fade = null,
        IReadOnlySet<int>? visible = null)
    {
        ArgumentNullException.ThrowIfNull(corpses);
        ArgumentNullException.ThrowIfNull(modelForClass);
        ArgumentNullException.ThrowIfNull(into);

        int drawn = 0;

        for (int at = 0; at < corpses.Count; at++)
        {
            SceneRagdoll corpse = corpses[at];

            if (tick < corpse.FirstTick || tick > corpse.LastTick)
            {
                continue;
            }

            // **One index per CORPSE, not per slot** (B318). Keying on the corpse's own entity index
            // would give the second occupant of a reused slot the first one's per-entity caches, and
            // two class models do not have the same bone count — which is the crash this fixes,
            // narrowed rather than removed. The position in the list is unique for the life of the
            // timeline, which is what a per-entity cache needs.
            int drawnAs = FirstCorpseEntityIndex + at;

            if (drawnAs >= ViewmodelScene.ArmsEntityIndex)
            {
                // Past the range reserved for corpses. A match reaching this has about 2,000 dead,
                // some hours long; drawing one under a viewmodel's index would be worse than not
                // drawing it, and silently is the only option left at this point.
                continue;
            }

            // **The entity's lifetime is the OUTER bound and the fade is the real one.** The server
            // keeps one ragdoll per player until that player next dies, so the window above admits
            // far more bodies than TF2 ever draws — 57 at once against a twelve-player roster,
            // measured. `RagdollFade` is `ClientThink`'s rule, which is what actually removes them.
            //
            // **Visibility is asked under the DRAWN index**, since that is what the renderer put in
            // the set — asking under the corpse's own slot would report every corpse unseen and
            // expire each one on the long timer, a fade that looks plausible and is never right.
            if (fade is not null &&
                fade.Gone(corpse, tick * fade.IntervalPerTick,
                    visible?.Contains(drawnAs) ?? false))
            {
                continue;
            }

            RagdollAppearance look = RagdollAppearance.Of(corpse, modelForClass);

            // The engine's own guard: no model means the whole block is skipped, skin included.
            if (look.Model is not { } model || look.Skin is not { } skin)
            {
                continue;
            }

            into.Add(new SceneProp(
                drawnAs,
                model,
                ScenePropTrack.Classify(model),
                // **Sequence 0 is a KNOWN gap, not the engine's answer** (B316). An earlier version
                // of this comment cited `LookupSequence( "RagdollSpawn" )` as the rule and that is
                // the wrong branch: `CreateTFRagdoll` reaches for RagdollSpawn only under
                // `else` — the LOCAL player — and takes
                //
                //     SetSequence( pPlayer->GetSequence() );
                //
                // for everyone else (`c_tf_player.cpp:757-766`). A SourceTV recording has no local
                // player at all, so in this project's own reference demo EVERY corpse takes the
                // copy branch. Zero is neither rule; it is what a `ScenePose` holds when nothing
                // has set it, and it is why a corpse stands up straight.
                //
                // Copying needs the player's sequence and cycle, which for a player are NOT on the
                // wire — the client rebuilds them
                // (`docs/memory/the-player-send-table-excludes-the-animation.md`) — so the value
                // lives in this project's own client-side animation and not in the timeline. That
                // is the shape of the fix, and it is why it is not one line here.
                new ScenePose
                {
                    X = corpse.X,
                    Y = corpse.Y,
                    Z = corpse.Z,

                    // **Yaw only, because that is all `GetRenderAngles` gives a standing player.**
                    // A player's pitch lives in the head's pose parameters rather than in the body
                    // transform, so carrying it here would tip the whole corpse over backwards for
                    // anyone who died looking up.
                    Yaw = corpse.Yaw,
                    Skin = skin,
                },
                ClassName: RagdollClassName));

            drawn++;
        }

        return drawn;
    }

    /// <summary>The server class a corpse arrives as.</summary>
    private const string RagdollClassName = "CTFRagdoll";

    /// <summary>Where a drawn corpse's entity index starts, above every networked one.</summary>
    /// <remarks>
    /// **A corpse must not draw under the slot the demo gave it, and this is a crash rather than a
    /// nicety** (B318). `EntityModelSet` keys its per-entity caches — the pose, the skinning
    /// buffers — by entity index, and an index is reused: slot 752 is a corpse for a few seconds
    /// and something else before and after. The stale pose is sized for whichever model had more
    /// bones, and `Skinning` then indexes the new model's shorter bone list with it:
    /// `ArgumentOutOfRangeException`, on the first frame with a corpse in view.
    ///
    /// **The engine has the same problem and answers it by moving the corpse out of the networked
    /// index space.** `CreateTFRagdoll` ends with `m_nRenderFX = kRenderFxRagdoll` and
    /// `InitAsClientRagdoll` (`c_tf_player.cpp:883-921`), making the ragdoll a CLIENT-side entity;
    /// Source gives those indices at or above `MAX_EDICTS`, which is 2048 (`const.h:65-67`), while
    /// the client entity list holds `NUM_ENT_ENTRIES` = 8192. A client ragdoll therefore cannot
    /// share a slot with anything the server sends, which is exactly the guarantee needed here.
    ///
    /// 2048 is Valve's own boundary and leaves 2048..4095 for corpses, clear of the viewmodel's
    /// 4096..4098 — this project's own numbering, and the precedent for doing it at all.
    ///
    /// **The offset is the corpse's position in the list, not its entity index.** Moving the whole
    /// slot range up would leave two corpses that reused one slot still sharing a cache, and two
    /// class models do not have the same bone count — the same crash, reached less often, which is
    /// the worst kind of fix.
    /// </remarks>
    public const int FirstCorpseEntityIndex = 2048;
}
