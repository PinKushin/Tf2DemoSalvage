using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
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
    /// <param name="items">
    /// The econ schema, or null when the install is unread. Two of the engine's five wearable skips
    /// need it — see <see cref="Skipped"/> — and both of its defaults are to KEEP, so a null schema
    /// draws too much rather than too little.
    /// </param>
    /// <param name="gibsOf">
    /// Resolves a class model's <c>break</c> list, or null when models are not open yet (B371). A
    /// gibbed corpse with no list draws NOTHING rather than falling back to its body: the engine has
    /// already removed that body, so drawing it would be a divergence rather than a graceful
    /// degradation.
    /// </param>
    /// <param name="intervalPerTick">
    /// Seconds per tick, so a gib's own <c>fadetime</c> can be measured from its corpse's death.
    /// Zero disables the fade, which is what a caller with no clock should get.
    /// </param>
    /// <param name="appearance">
    /// What each worn item hides, for the corpse's own <c>m_nBody</c> (B395). Null draws every
    /// part's first alternative, which is what a viewer with no install can say.
    /// </param>
    /// <param name="bodygroups">
    /// The class model's part table, which turns a bodygroup NAME into its index. Null for the
    /// same reason, and the two travel together — one without the other answers nothing.
    /// </param>
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
        IReadOnlySet<int>? visible = null,
        ItemSchema? items = null,
        Func<string, IReadOnlyList<PhysicsBreakPiece>>? gibsOf = null,
        float intervalPerTick = 0f,
        IPlayerAppearance? appearance = null,
        IModelBodygroups? bodygroups = null)
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

            // **A gibbed death draws PIECES and no body** (B371). `CreateTFGibs` spawns the gibs
            // and then removes the ragdoll outright — `EndFadeOut()`, or
            // `SetRenderMode( kRenderNone )` (`c_tf_player.cpp:1124-1133`) — so a corpse drawn here
            // as well would be a whole body standing inside its own remains. `m_bGib` was decoded
            // and read by NOTHING until this line, which is why it was.
            if (corpse.Gib)
            {
                drawn += Gibs(corpse, model, tick, intervalPerTick, gibsOf, into);

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

                    // **The player's bodygroups, copied at death** (B395,
                    // `c_tf_player.cpp:790-793`). Zero until now, which showed every corpse with
                    // the stock geometry its cosmetics are modelled to replace — a helmet under a
                    // hat.
                    //
                    // **Computed from the corpse's OWN worn list, before the wearable skips**, so
                    // the order matches the engine's: it copies the player's body at `:790` and
                    // only refuses HEAD and MISC items at `:10206`. Taking the body from the
                    // emitted props instead would apply the skip first and lose exactly the case
                    // that matters — a decapitated corpse keeps the hidden-head bodygroup its hat
                    // imposed, which is what makes a headless corpse a real TF2 look.
                    Body = BodyAtDeath(corpse, model, appearance, bodygroups),

                    // **What the player was DOING when they died, so the corpse can be posed the
                    // way the engine poses it** (B316). `CreateTFRagdoll` copies the player's own
                    // animation across for anyone but the local player —
                    //
                    //     m_flAnimTime = pPlayer->m_flAnimTime;
                    //     SetSequence( pPlayer->GetSequence() );
                    //     m_flPlaybackRate = pPlayer->GetPlaybackRate();
                    //
                    // (`c_tf_player.cpp:757-766`), and a SourceTV recording has no local player at
                    // all, so every corpse in one takes that branch.
                    //
                    // **The wire already carries what that sequence was chosen FROM, on the corpse
                    // itself.** `m_vecRagdollVelocity` is the player's velocity at the moment of
                    // death and `m_bOnGround` is their ground state, which are the two inputs
                    // `PlayerAnimation` needs — so this needs no capture from the player's own
                    // entity, which is just as well: a player's speed is never on the wire and is
                    // derived from position deltas at draw time.
                    //
                    // **Horizontal only**, because the activity a player is in is chosen by ground
                    // speed; the vertical component is what the jump and fall activities read from
                    // the ground flag instead.
                    Speed = corpse.Velocity is { } moving
                        ? MathF.Sqrt((moving.X * moving.X) + (moving.Y * moving.Y))
                        : null,
                    Flags = corpse.OnGround ? PlayerActivityState.OnGround : 0,
                },
                ClassName: RagdollClassName,

                // **The death animation, decided here and resolved where models are open** (B323).
                // Null for the great majority: only a headshot, a decapitation or a backstab is
                // eligible at all, and three quarters of those discard it on the draw.
                DeathSequence: RagdollDeath.SequenceFor(corpse),

                // **The gold or ice VMT, replacing every material the model has** (B325).
                //
                //     m_MaterialOverride.Init( materialOverrideFilename, TEXTURE_GROUP_CLIENT_EFFECTS );
                //
                // `c_tf_player.cpp:980`. Null for every corpse in this corpus — 0 of 566 measured —
                // which is why the decode was authored a specimen rather than tested on a demo.
                MaterialOverride: look.Material,

                // **When it died, which is when its physics starts** (B58). See `SceneProp`.
                FirstTick: corpse.FirstTick,

                // **And how it died, which is what makes it fly** (B58). Decoded since the corpse
                // reader was written and read by nothing until the simulation existed to want it.
                Force: corpse.Force,
                ForceBone: corpse.ForceBone,
                RagdollVelocity: corpse.Velocity));

            drawn++;

            drawn += Worn(corpse, drawnAs, at, into, items, look.Material);
        }

        return drawn;
    }

    /// <summary>Hangs a corpse's cosmetics on it.</summary>
    /// <param name="corpse">The corpse.</param>
    /// <param name="drawnAs">The index the corpse itself is drawn under.</param>
    /// <param name="at">Its position in the list, which its wearables' indices are derived from.</param>
    /// <param name="into">The buffer to append to.</param>
    /// <param name="items">The econ schema, for the two skips that need it; null keeps everything.</param>
    /// <param name="material">
    /// The gold or ice VMT the corpse itself took, or null. **Passed in rather than re-derived**, so
    /// an item cannot disagree with the body it hangs on (B243).
    /// </param>
    /// <returns>How many were added.</returns>
    /// <remarks>
    /// **Bone-merged onto the corpse, which is how they were worn in life.** The engine builds a
    /// `C_EconWearableGib` per item and calls `MoveBoneAttachments` to bring them across
    /// (`c_tf_player.cpp:10169-10251`); here the model is simply re-attached to the corpse's own
    /// index, and the existing merge path matches the bones by name exactly as it does for a living
    /// player (`docs/memory/bone-merge-sends-no-position.md`).
    ///
    /// **Their indices are derived from the corpse's position and their own**, never from the
    /// wearable entity they came from — the same reasoning as B318. A hat borrowed its owner's
    /// index while its owner is alive and wearing another one, and `EntityModelSet` would be keying
    /// both to the same cache.
    ///
    /// **A gold or iced corpse repaints its cosmetics too, and the engine does it as a SECOND pass
    /// rather than by inheritance** (B325):
    ///
    /// <code>
    /// // override all of our wearables, too
    /// for ( C_BaseEntity *pEntity = ClientEntityList().FirstBaseEntity(); … )
    ///     if ( pEntity->GetFollowedEntity() == this )
    ///         if ( CEconEntity *pItem = dynamic_cast&lt; CEconEntity * &gt;( pEntity ) )
    ///             pItem->SetMaterialOverride( m_iTeam, materialOverrideFilename );
    /// </code>
    ///
    /// `c_tf_player.cpp:982-993`. Read-from-source. **The override is per renderable, not per
    /// entity** — that loop exists precisely because a hat is drawn by its own model call and would
    /// otherwise stay its own colour on a golden body. Its predicate is `GetFollowedEntity() == this`,
    /// which is the follow relation this method's props already stand in.
    /// </remarks>
    private static int Worn(
        SceneRagdoll corpse,
        int drawnAs,
        int at,
        ICollection<SceneProp> into,
        ItemSchema? items,
        string? material)
    {
        if (corpse.Worn is not { Count: > 0 } worn)
        {
            return 0;
        }

        // **Decapitation drops the head and misc slots**, which is the engine's third skip:
        //
        //   if ( IsDecapitationCustomDamageType( pRagdoll->GetDamageCustom() ) )
        //   {
        //       int iLoadoutSlot = pEconItemView ? …GetDefaultLoadoutSlot() : LOADOUT_POSITION_INVALID;
        //       if ( iLoadoutSlot == LOADOUT_POSITION_HEAD || iLoadoutSlot == LOADOUT_POSITION_MISC )
        //           continue;
        //   }
        //
        // `c_tf_player.cpp:10208-10217`. A head that came off should not still be wearing the hat.
        // **Exactly the four `IsDecapitationCustomDamageType` names, read rather than inferred**
        // (`c_tf_player.cpp:10161-10167`). `TF_DMG_CUSTOM_HEADSHOT_DECAPITATION` is NOT among them
        // despite its name, and was in the first version of this line because the name reads like
        // it should be. A sniper's decapitating headshot keeps the hat.
        bool beheaded = corpse.DamageCustom is Decapitation
            or BarbarianSwing or DecapitationBoss or MerasmusDecapitation;

        int added = 0;

        for (int item = 0; item < worn.Count && item < MostWornPerCorpse; item++)
        {
            if (Skipped(worn[item], items, beheaded))
            {
                continue;
            }

            into.Add(new SceneProp(
                FirstWornEntityIndex + (at * MostWornPerCorpse) + item,
                worn[item].Model,
                ScenePropTrack.Classify(worn[item].Model),

                // **No transform of its own, and that is not an omission.** `FollowEntity` sets
                // `EF_BONEMERGE` and then zeroes the local origin and angles
                // (`baseentity_shared.cpp:2360-2371`), so a worn item never carries one — the
                // corpse's bones place it.
                new ScenePose { Skin = corpse.Team == SceneTeams.Red ? 0 : 1 },
                AttachedTo: drawnAs,
                BoneMerged: true,
                OwnedBy: drawnAs,
                ClassName: WearableClassName,

                // The corpse's own gold or ice, applied again here — `SetMaterialOverride` on each
                // followed econ entity, quoted above. Null for every ordinary corpse.
                MaterialOverride: material));

            added++;
        }

        return added;
    }

    /// <summary>The bodygroups the player had when this corpse was made — <c>m_nBody</c> (B395).</summary>
    /// <param name="corpse">The corpse, for its worn list and the feign-death guard.</param>
    /// <param name="model">The class model the corpse draws with.</param>
    /// <param name="appearance">The item schema's view of what each item hides, or null.</param>
    /// <param name="bodygroups">The model's own part table, or null.</param>
    /// <returns>The body, or 0 when the install cannot say.</returns>
    /// <remarks>
    /// **The engine copies the whole value off the living player** (`c_tf_player.cpp:790-793`):
    ///
    /// <code>
    /// if ( !m_bFeignDeath || m_bWasDisguised )
    /// {
    ///     pPlayer-&gt;RecalcBodygroupsIfDirty();
    ///     m_nBody = pPlayer-&gt;GetBody();
    /// }
    /// </code>
    ///
    /// **Recomputed from the corpse's worn list rather than carried on the record, because the
    /// computation needs `items_game.txt` and the decode layer has none** — the same split
    /// <see cref="SceneWornItem"/> exists for. The inputs are fixed at death, so the answer is the
    /// player's.
    ///
    /// **Before <see cref="Skipped"/>, deliberately.** The engine takes the body at `:790` and only
    /// refuses HEAD and MISC wearables at `:10206-10214`, so a decapitated corpse keeps the
    /// hidden-head bodygroup its hat imposed and loses the hat. Computing this from the props that
    /// survive the skip would invert that order and lose the one case the whole mechanism is about.
    ///
    /// **The feign-death guard is Valve's and is reproduced rather than simplified.** A feign death
    /// leaves the player alive, so there is no new body to copy — unless the spy was disguised,
    /// where the engine takes it anyway.
    ///
    /// **What this does NOT include, stated rather than left to be found:** bodygroups driven by
    /// the player's WEAPONS. `GetBody()` on the living player includes them, and a corpse's worn
    /// list is cosmetics only — `WornAtDeath` filters weapons out because
    /// `CreateBoneAttachmentsFromWearables` walks the econ wearable list (`c_tf_player.cpp:10178`).
    /// Eight shipped items set `DeployedOnly` and all eight are weapons, so the gap is exactly those
    /// and it needs the held weapon at the death tick, which no corpse record carries yet.
    /// </remarks>
    private static int BodyAtDeath(
        SceneRagdoll corpse,
        string model,
        IPlayerAppearance? appearance,
        IModelBodygroups? bodygroups)
    {
        // **Null is the no-install answer and it is 0, which is also the engine's starting value** —
        // `m_pOuter->m_nBody = 0` opens `RecalculatePlayerBodygroups`. A viewer with no
        // `items_game.txt` draws every part's first alternative, which is what it drew before this
        // existed.
        if (appearance is null || bodygroups is null ||
            corpse.Carried is not { Count: > 0 } carried ||
            (corpse.FeignDeath && !corpse.WasDisguised))
        {
            return 0;
        }

        int body = 0;

        // **Three passes in Valve's order, not one loop** (`tf_player_shared.cpp:13693-13709`):
        //
        //     m_pOuter->m_nBody = 0;
        //     CTFWeaponBase::UpdateWeaponBodyGroups( m_pOuter, false );
        //     CEconWearable::UpdateWearableBodyGroups( m_pOuter );
        //     CTFWeaponBase::UpdateWeaponBodyGroups( m_pOuter, true );
        //
        // The order is only visible when two items touch the SAME part, and then it decides which
        // wins — so a single pass over one list agrees with the engine right up until it does not,
        // silently. `SetBodygroup` is last-writer-wins, which is what makes that possible.
        foreach (SceneCarriedItem item in carried)
        {
            if (item.Weapon)
            {
                body = PlayerProps.Bodygroup(
                    item.ItemDefinitionIndex, item.Deployed, appearance, bodygroups, model, body);
            }
        }

        foreach (SceneCarriedItem item in carried)
        {
            if (!item.Weapon)
            {
                // A wearable is never the active weapon, so it can never satisfy the deployed-only
                // guard — and none of the eight items that set the flag is a wearable.
                body = PlayerProps.Bodygroup(
                    item.ItemDefinitionIndex, deployed: false, appearance, bodygroups, model, body);
            }
        }

        // **The third pass is the deployed-only weapons**, and `PlayerProps.Bodygroup` already
        // refuses an item whose flag is set unless it is the one being held — so the first pass
        // above dropped exactly those, and this one applies them.
        foreach (SceneCarriedItem item in carried)
        {
            if (item is { Weapon: true, Deployed: true })
            {
                body = PlayerProps.Bodygroup(
                    item.ItemDefinitionIndex, deployed: true, appearance, bodygroups, model, body);
            }
        }

        return body;
    }

    /// <summary>Whether the engine would leave this item off the corpse.</summary>
    /// <param name="item">The worn item.</param>
    /// <param name="items">The econ schema, or null when the install is unread.</param>
    /// <param name="beheaded">Whether the death took the head off.</param>
    /// <returns>True to skip it.</returns>
    /// <remarks>
    /// **Two of `CreateBoneAttachmentsFromWearables`'s five skips, and both need `items_game.txt`**
    /// (B324) — which is why they were left out when the cosmetics first landed and why they are
    /// here rather than in the decode.
    ///
    /// **The drop-type skip** is `if ( pItem->GetDropType() >= ITEM_DROP_TYPE_DROP ) continue;`
    /// (`c_tf_player.cpp:10206`). Such an item becomes a falling gib through `DropWearable` instead
    /// of riding the corpse, so leaving it on is one item drawn twice. Measured: 463 of 11,497 item
    /// definitions declare it, 4%.
    ///
    /// **The decapitation skip** drops the HEAD and MISC slots — and HEAD is unreachable, which is
    /// Valve's own quirk rather than ours: `tf_item_schema.cpp:941-944` rewrites the exact
    /// lower-case string `"head"` to `"misc"` before the table lookup, with a case-SENSITIVE
    /// compare, so no item whose `item_slot` is spelled `head` can resolve to `LOADOUT_POSITION_HEAD`.
    /// Measured on the shipped schema: **0 of 11,497 items are HEAD and 9,536 are MISC.** So in
    /// practice a decapitation drops five sixths of a player's cosmetics, and the HEAD half of the
    /// engine's condition never fires.
    ///
    /// **An item the schema does not know is KEPT.** `pEconItemView` being null gives
    /// `LOADOUT_POSITION_INVALID`, which matches neither case, and a missing `drop_type` defaults to
    /// "stay attached". Both of the engine's defaults are to keep, so an unreadable install draws
    /// too much rather than too little.
    /// </remarks>
    private static bool Skipped(SceneWornItem item, ItemSchema? items, bool beheaded)
    {
        if (items is null || item.ItemDefinitionIndex is not { } definition)
        {
            return false;
        }

        if (items.DropType(definition) >= ItemSchema.DropTypeDrop)
        {
            return true;
        }

        return beheaded && items.DefaultLoadoutSlot(definition) is
            ItemSchema.LoadoutSlotHead or ItemSchema.LoadoutSlotMisc;
    }

    /// <summary><c>TF_DMG_CUSTOM_DECAPITATION</c>.</summary>
    private const int Decapitation = 20;

    /// <summary><c>TF_DMG_CUSTOM_TAUNTATK_BARBARIAN_SWING</c>, which also takes a head off.</summary>
    private const int BarbarianSwing = 24;

    /// <summary><c>TF_DMG_CUSTOM_DECAPITATION_BOSS</c>.</summary>
    private const int DecapitationBoss = 41;

    /// <summary><c>TF_DMG_CUSTOM_MERASMUS_DECAPITATION</c>.</summary>
    private const int MerasmusDecapitation = 60;

    /// <summary>Most cosmetics one corpse's block of indices has room for.</summary>
    /// <remarks>
    /// **Eight, which is what the engine reserves.** `m_hRagWearables` is declared
    /// `RecvPropUtlVector( RECVINFO_UTLVECTOR( m_hRagWearables ), 8, … )` (`c_tf_player.cpp:535`) —
    /// the field itself is dead (nothing draws from it), but its width is Valve's own statement of
    /// how many a corpse can carry. Measured: 531 wearables across 150 corpses, 3.5 each.
    /// </remarks>
    private const int MostWornPerCorpse = 8;

    /// <summary>Where a corpse's cosmetics take their entity indices from.</summary>
    /// <remarks>
    /// **Its own range again, for B318's reason.** These are not the wearable entities the demo
    /// sent — those belong to a player who is alive again by now, wearing them — so borrowing their
    /// indices would key two different models to one per-entity cache. Above the corpses' own
    /// 2048..4095 and the viewmodel's 4096..4098, with room for eight per corpse.
    /// </remarks>
    private const int FirstWornEntityIndex = 8192;

    /// <summary>The server class a cosmetic arrives as.</summary>
    private const string WearableClassName = "CTFWearable";

    /// <summary>The server class a corpse arrives as.</summary>
    /// <remarks>
    /// **Public because the sequence a corpse rests in is decided where models are open**, which is
    /// `EntityModels.UpdateClientSideAnimations` and not here — and it has to recognise a corpse to
    /// do it (B316). A second literal there would be a second place to be wrong about the name.
    /// </remarks>
    public const string RagdollClassName = "CTFRagdoll";

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

    /// <summary>The class every gib prop is filed under, as corpses have their own.</summary>
    public const string GibClassName = "CTFPlayerGib";

    /// <summary>Where gib indices start, clear of the corpses at 2048 and the viewmodel at 4096.</summary>
    /// <remarks>
    /// **The same reasoning as <see cref="FirstCorpseEntityIndex"/> and for the same crash.** A gib
    /// is a client-side object with per-entity caches keyed by index, and nine of them come from one
    /// corpse — so each needs a slot that nothing else can take. `NUM_ENT_ENTRIES` is 8192, so
    /// 4608 upward is free of the networked range, the corpses and the viewmodel alike.
    /// </remarks>
    public const int FirstGibEntityIndex = 4608;

    /// <summary>How many pieces one corpse may spawn, so the index space cannot be overrun.</summary>
    /// <remarks>
    /// TF2's classes declare nine; the cap is generous and exists so a stranger's `.phy` (D32)
    /// cannot walk a corpse's gibs into the next corpse's indices.
    /// </remarks>
    private const int MaximumGibsPerCorpse = 32;

    /// <summary>The pieces one gibbed corpse is drawn as, in place of its body (B371).</summary>
    /// <param name="corpse">The corpse that gibbed.</param>
    /// <param name="model">Its class model, whose <c>.phy</c> declares the pieces.</param>
    /// <param name="tick">The tick being drawn.</param>
    /// <param name="intervalPerTick">Seconds per tick, for the fade.</param>
    /// <param name="gibsOf">Resolves a model's break list, or null when models are not open.</param>
    /// <param name="into">Where the props are appended.</param>
    /// <returns>How many were added.</returns>
    /// <remarks>
    /// **Each piece is thrown by <see cref="PlayerGibs"/> and placed by physics afterwards.** The
    /// prop is emitted at the corpse's own origin because that is where `CreatePlayerGibs` spawns
    /// them — `breakablepropparams_t breakParams( vecOrigin, … )` — and the simulation moves it from
    /// there.
    ///
    /// **The fade is the piece's own, not the corpse's.** `fadetime` is 10 on every TF2 gib against
    /// `cl_ragdoll_fade_time`'s 15 for a body, so gibs leave first; a gib past its fade is simply
    /// not emitted, which is what `EndFadeOut` does to it.
    /// </remarks>
    private static int Gibs(
        SceneRagdoll corpse,
        string model,
        double tick,
        float intervalPerTick,
        Func<string, IReadOnlyList<PhysicsBreakPiece>>? gibsOf,
        ICollection<SceneProp> into)
    {
        if (gibsOf?.Invoke(model) is not { Count: > 0 } pieces)
        {
            // No list means the model is not open yet, or declares none. Drawing the body instead
            // would be worse than drawing nothing: the engine has already removed it.
            return 0;
        }

        double since = intervalPerTick > 0f ? (tick - corpse.FirstTick) * intervalPerTick : 0d;

        int drawn = 0;
        int count = Math.Min(pieces.Count, MaximumGibsPerCorpse);

        for (int piece = 0; piece < count; piece++)
        {
            if (since > pieces[piece].FadeTime)
            {
                continue;
            }

            into.Add(new SceneProp(
                FirstGibEntityIndex + (corpse.EntityIndex * MaximumGibsPerCorpse) + piece,
                PhysicsModel.GibPath(pieces[piece].Model),
                SceneModelKind.Studio,
                new ScenePose
                {
                    X = corpse.X,
                    Y = corpse.Y,
                    Z = corpse.Z,
                    Yaw = corpse.Yaw,
                },
                ClassName: GibClassName,
                FirstTick: corpse.FirstTick,

                // The piece's own throw and the corpse's shared spin, which the simulation stages
                // onto the body exactly as a corpse's killing blow is staged.
                Force: PlayerGibs.Velocity(corpse, piece),
                RagdollVelocity: PlayerGibs.Spin(corpse)));

            drawn++;
        }

        return drawn;
    }
}
