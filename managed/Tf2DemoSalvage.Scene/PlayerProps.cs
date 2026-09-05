using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>What the installed game says a player looks like.</summary>
/// <remarks>
/// **An abstraction rather than the two concrete readers, because none of this is on the wire.** A
/// player's model, the suffix their weapon drives and whether their class air-walks all come from
/// the game's own scripts — <c>PlayerClassModels</c> and <c>WeaponRoles</c> — so a test that had to
/// supply them would need a TF2 install to assert how a player is placed.
/// </remarks>
public interface IPlayerAppearance
{
    /// <summary>The model a class is drawn with, or null when the install cannot say.</summary>
    public string? ModelOf(int playerClass);

    /// <summary>The activity suffix a held weapon drives, or null.</summary>
    public string? WeaponSuffix(string? weaponClass, int? playerClass);

    /// <summary>Whether a class air-walks at all. Only the medic opts out.</summary>
    public bool Airwalks(int playerClass);

    /// <summary>Whether landing plays a gesture for this class.</summary>
    /// <param name="playerClass">The class being drawn.</param>
    /// <returns>True unless the class script sets <c>DontDoNewJump</c>.</returns>
    /// <remarks>
    /// **`bNewJump`, which gates the landing gesture and nothing else**
    /// (`tf_playeranimstate.cpp:1482`). A class that sets `DontDoNewJump` still jumps; it just
    /// never plays `ACT_MP_JUMP_LAND` on the way down. Asked here for the same reason
    /// <see cref="Airwalks"/> is: the timeline knows the player landed and only the installed game
    /// knows whether that class shows it.
    /// </remarks>
    public bool Lands(int playerClass);

    /// <summary>The arms a class shows in first person, or null when the install cannot say.</summary>
    /// <param name="playerClass">The class being drawn.</param>
    /// <returns>The <c>c_&lt;class&gt;_arms</c> model, or null.</returns>
    /// <remarks>
    /// **The same question as the other three, and it was being asked from the window.** A player's
    /// arms come from the class script exactly as their body does, so `MainForm` reaching for
    /// <c>PlayerClassModels.Hands</c> to fill in a viewmodel argument was the view answering a
    /// domain question (B188, D90). Asked here, the scene resolves it from what it already holds.
    /// </remarks>
    public string? Hands(int playerClass);

    /// <summary>What an equipped item does to the body parts of whoever wears it.</summary>
    /// <param name="itemDefinitionIndex">The item, as <c>m_iItemDefinitionIndex</c> gives it.</param>
    /// <returns><see cref="ItemBodygroups.None"/> when the schema says nothing about it.</returns>
    /// <remarks>
    /// **Asked here for the same reason the model is: it is in the game's scripts, not on the
    /// wire** (B352). A demo carries the item's definition index and nothing about what the item
    /// hides, so a player prop built without an install keeps every default part — which is what a
    /// machine with no TF2 should draw, and is why this degrades to <c>None</c> rather than
    /// throwing.
    /// </remarks>
    public ItemBodygroups BodygroupsOf(int itemDefinitionIndex);
}

/// <summary>What one equipped item does to its wearer's body parts.</summary>
/// <param name="Named">Each body part NAME and the state the item puts it in.</param>
/// <param name="DeployedOnly">Whether it does so only while it is the active weapon.</param>
/// <param name="OverrideGroup">A part addressed by NUMBER, or -1 — <c>wm_bodygroup_override</c>.</param>
/// <param name="OverrideState">Which alternative that part takes, or -1 (B353).</param>
/// <remarks>
/// **One value rather than two members on <see cref="IPlayerAppearance"/>**, because the pair is
/// one question — what this item does to the body — and the flag is meaningless without the names.
///
/// **Named rather than indexed, because the engine resolves by name.**
/// `pOwner-&gt;FindBodygroupByName( pszBodyGroup )` (<c>econ_entity.cpp:2052</c>) runs against the
/// WEARER, so the same hat resolves to a different index on every class model and an item cannot
/// carry the answer.
///
/// **The override is the exception, and it is Valve's exception rather than ours.** The last arm of
/// `UpdateBodygroups` (<c>econ_entity.cpp:2083</c>) takes a part NUMBER straight from the schema
/// and applies it without a lookup, which is why the two forms sit side by side here.
///
/// **Use <see cref="None"/> rather than <c>default</c> for "nothing".** A default struct has null
/// names and zeroed override fields, and zero is a real part index — the guard that keeps this from
/// setting part 0 is `> -1` on both, exactly as the engine writes it.
/// </remarks>
public readonly record struct ItemBodygroups(
    IReadOnlyDictionary<string, int> Named,
    bool DeployedOnly,
    int OverrideGroup = -1,
    int OverrideState = -1)
{
    /// <summary>An item that changes nothing — the answer for anything the schema does not name.</summary>
    public static ItemBodygroups None { get; } =
        new(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), false);
}

/// <summary>A model's body parts, addressed the two ways the engine addresses them.</summary>
/// <remarks>
/// **Valve's own pair, kept apart because the engine keeps them apart** — `FindBodygroupByName` and
/// `SetBodygroup`, `shared/animation.cpp:927` and `:863`. An item's `player_bodygroups` names a
/// part and needs both; `wm_bodygroup_override` gives an INDEX and needs only the second (B353).
/// A single by-name operation cannot express the override at all, which is what this replaced.
///
/// **An interface rather than two delegates**, because they are one capability — what this model's
/// parts are — and a caller that could supply one without the other would be able to wire half of
/// it. The scene supplies <c>EntityModelSet</c>; anything with no models supplies
/// <see cref="NoBodygroups"/>.
/// </remarks>
public interface IModelBodygroups
{
    /// <summary>The index of a named part, or -1 when this model has none.</summary>
    /// <param name="modelPath">The model whose parts are being searched.</param>
    /// <param name="group">The part's name, as the item schema spells it.</param>
    /// <returns>An index for <see cref="SetBodygroup"/>, or -1.</returns>
    public int FindBodygroup(string modelPath, string group);

    /// <summary>A body number with one part set to one of its alternatives.</summary>
    /// <param name="modelPath">The model the number describes.</param>
    /// <param name="group">Which part, as <see cref="FindBodygroup"/> gives it.</param>
    /// <param name="value">Which alternative.</param>
    /// <param name="body">The body number to start from.</param>
    /// <returns>The new body number, or <paramref name="body"/> when the request cannot be honoured.</returns>
    /// <remarks>
    /// **It takes the body to start from, because parts share one integer.** They are digits of a
    /// mixed-radix number, so setting one has to subtract the digit it currently holds rather than
    /// OR a bit in — and returning contributions to be added carries into the NEXT part's digit
    /// whenever two items name the same part (B352).
    /// </remarks>
    public int SetBodygroup(string modelPath, int group, int value, int body);
}

/// <summary>A model set with nothing loaded, which answers every question honestly.</summary>
/// <remarks>
/// **Not a stand-in: this is a real state the production path passes through.** A model is packed
/// on first sight, so the frame a player first appears on has no <c>.mdl</c> to resolve against and
/// the engine's own lookup would fail too. Answering "no such part" leaves every default piece
/// drawn, which is one frame of a hat over hair rather than a piece removed on a guess.
/// </remarks>
public sealed class NoBodygroups : IModelBodygroups
{
    /// <summary>The only instance, since it holds nothing.</summary>
    public static NoBodygroups Instance { get; } = new();

    /// <inheritdoc/>
    public int FindBodygroup(string modelPath, string group) => -1;

    /// <inheritdoc/>
    public int SetBodygroup(string modelPath, int group, int value, int body) => body;
}

/// <summary>What the installed game actually says, from its own scripts.</summary>
/// <param name="Classes">The class script, or null when no install was found.</param>
/// <param name="Roles">The weapon-to-activity map, or null.</param>
/// <param name="Items">The item schema, or null when no install was found (B352).</param>
/// <remarks>
/// **Null means "no install", and answering null is the honest response.** A viewer with no TF2
/// draws what it can rather than refusing, so every member here degrades to "cannot say" rather
/// than throwing — and a player whose model cannot be named is simply not drawn, which
/// <see cref="PlayerProps.Add"/> treats as a reason to skip.
/// </remarks>
public sealed record GameAppearance(
    PlayerClassModels? Classes, WeaponRoles? Roles, ItemSchema? Items = null)
    : IPlayerAppearance
{
    /// <inheritdoc/>
    public string? ModelOf(int playerClass) => Classes?.Model(playerClass);

    /// <inheritdoc/>
    public string? WeaponSuffix(string? weaponClass, int? playerClass) =>
        Roles?.Suffix(weaponClass, playerClass);

    /// <inheritdoc/>
    /// <remarks>
    /// **True when the install cannot say**, because air-walking is the general case and only the
    /// medic opts out. Defaulting to false would stop every class air-walking on a machine with no
    /// TF2, which is a silent behaviour change rather than a missing asset.
    /// </remarks>
    public bool Airwalks(int playerClass) => Classes?.Airwalks(playerClass) != false;

    /// <inheritdoc/>
    /// <remarks>
    /// **True when the install cannot say**, for the same reason as <see cref="Airwalks"/>: landing
    /// is the general case and `GetInt( "DontDoNewJump", 0 )` means an unmentioned key describes a
    /// class that lands.
    /// </remarks>
    public bool Lands(int playerClass) => Classes?.Lands(playerClass) != false;

    /// <inheritdoc/>
    public string? Hands(int playerClass) => Classes?.Hands(playerClass);

    /// <inheritdoc/>
    public ItemBodygroups BodygroupsOf(int itemDefinitionIndex)
    {
        if (Items is null)
        {
            return ItemBodygroups.None;
        }

        (int group, int state) = Items.WorldmodelBodygroupOverrideFor(itemDefinitionIndex);

        return new ItemBodygroups(
            Items.PlayerBodygroupsFor(itemDefinitionIndex),
            Items.HidesBodygroupsWhenDeployedOnly(itemDefinitionIndex),
            group,
            state);
    }
}

/// <summary>Turns the timeline's players into props the draw loop can pose.</summary>
/// <remarks>
/// **Players become props rather than getting a pipeline of their own.** A player is a model at a
/// pose, which is exactly what the prop path already draws, lights and interpolates — and a second
/// implementation would agree with the first only until one of them gained a feature.
///
/// **The conversion is OURS, not the engine's**, and that is why it carries this much comment.
/// Valve has no equivalent step: a player is already a <c>C_BaseAnimating</c> in the renderables
/// list (<c>clientleafsystem.h:48</c>). Ours exists because <c>DemoTimeline</c> keeps
/// <c>PlayerTracks</c> apart from <c>Props</c> — a player's model is never networked, so the
/// timeline cannot name it and resolves it from the installed game instead.
/// </remarks>
public static class PlayerProps
{
    /// <summary>The model a player is drawn with, or null when they are not drawn at all.</summary>
    /// <param name="player">The player.</param>
    /// <param name="appearance">What the installed game says they look like.</param>
    /// <returns>The model path, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="appearance"/> is null.</exception>
    /// <remarks>
    /// **One predicate, because two callers have to agree and nothing made them.** The draw loop
    /// adds a model for a player, and the marker pass draws a DOT for a player with no model — so
    /// the two answers are the same question asked from opposite sides, and any disagreement gives
    /// a player both a body and a dot on top of it, or neither.
    ///
    /// **Three separate reasons not to draw, and they are not interchangeable.**
    ///
    /// <list type="bullet">
    /// <item><b>Not playing</b> — spectators and the SourceTV camera are <c>CTFPlayer</c> entities
    /// with real positions that follow the action, so drawing everything puts convincing players
    /// where nobody is standing.</item>
    /// <item><b>Not drawn</b> — the dead keep a team and a position: the position of whoever they
    /// are spectating. Several corpses therefore stack inside the living player they are watching,
    /// which is what "two soldiers in a ball" was. **And the marker pass is where this is easiest
    /// to get wrong**: "no model means a dot" would turn every corpse into a marker gliding around
    /// the map behind whoever it was spectating — the same defect in a cheaper primitive.</item>
    /// <item><b>No class, or no model for it</b> — the install cannot say what they look like, and
    /// a prop with no model draws as a missing asset, which reads as a loading fault rather than as
    /// a player we could not name.</item>
    /// </list>
    /// </remarks>
    public static string? ModelFor(ScenePlayer player, IPlayerAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        // **The class we DRAW them as, which is not always the class they are.**
        // `C_TFPlayer::ValidateModelIndex` (`c_tf_player.cpp:8998`) takes the model from
        // `GetPlayerClassData( GetDisguiseClass() )` when a spy is disguised and we are on the other
        // team. `Disguise.VisibleClass` is that branch and its `else`.
        return player.IsPlaying && player.Drawn && Disguise.VisibleClass(player) is { } playerClass
            ? appearance.ModelOf(playerClass)
            : null;
    }

    /// <summary>Adds a prop for every player who should be drawn.</summary>
    /// <param name="players">The players at this moment, from the timeline.</param>
    /// <param name="into">The draw list to add to.</param>
    /// <param name="appearance">What the installed game says they look like.</param>
    /// <param name="bodygroups">
    /// The model's body parts — <c>EntityModelSet</c> in production. Passed in rather than reached
    /// for, because only the model set has the <c>.mdl</c> and only it knows which index a part's
    /// name has on this model.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **<paramref name="into"/> is read as well as written, and the order is the reason it is a
    /// list.** The props already in it are this moment's wearables and weapons, so it stands in for
    /// the engine's <c>m_hMyWearables</c> and <c>m_hMyWeapons</c>; the player props appended below
    /// are not equipment and are deliberately not looked at, which is what the captured count
    /// enforces. Enumerating a list while adding to it would throw in any case.
    /// </remarks>
    public static void Add(
        IReadOnlyList<ScenePlayer> players,
        IList<SceneProp> into,
        IPlayerAppearance appearance,
        IModelBodygroups bodygroups)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(bodygroups);

        // Captured before a single player is appended, so the equipment scan below reads only what
        // the props pass produced — see the remarks.
        int equipment = into.Count;

        foreach (ScenePlayer player in players)
        {
            // **Three separate reasons not to draw, and they are not interchangeable.**
            //
            // `IsPlaying` keeps out spectators and the SourceTV camera, which are CTFPlayer
            // entities with real positions that follow the action — drawing them puts convincing
            // players where nobody is standing.
            //
            // `Drawn` keeps out the dead, who keep a team and a position: the position of whoever
            // they are spectating. Several corpses therefore stack inside the living player they
            // are watching, which is what "two soldiers in a ball" was.
            //
            // A missing class or model means the install cannot say what they look like, and a prop
            // with no model draws as a missing asset — which reads as a loading fault rather than
            // as a player we could not name.
            if (ModelFor(player, appearance) is not { } model ||
                player.PlayerClass is not { } playerClass)
            {
                continue;
            }

            // **What their equipment does to them, before anything else writes a body number**
            // (B352). This is `RecalculatePlayerBodygroups`' own starting point — the field is
            // cleared and rebuilt from the items — so the mask below composes onto it.
            int equipped = Equipped(player, into, equipment, appearance, bodygroups, model);

            into.Add(new SceneProp(
                player.EntityIndex,
                model,
                SceneModelKind.Studio,
                new ScenePose
                {
                    X = player.X,
                    Y = player.Y,
                    Z = player.Z,
                    Yaw = player.Yaw,
                    Scale = 1f,

                    // **Carried through, because this pose is built field by field** (B312). A
                    // value with no assignment here is one the renderer never sees whatever the
                    // timeline decoded — the failure `docs/memory/a-moves-regressions-are-wiring.md`
                    // records, where three fields shipped lost with the suite green.
                    HeadScale = player.HeadScale,
                    TorsoScale = player.TorsoScale,
                    HandScale = player.HandScale,
                    Speed = player.Speed,
                    Flags = player.Flags,

                    // **The one that made this comment's warning real** (B346). Carried on the
                    // prop track first, where it stamped zero across 570 tracks — because all 332
                    // sends that change it belong to `CTFPlayer`, and a player is not a prop track.
                    // Every unit test passed throughout, which is why
                    // `PlayerPoseWiringCompletenessTests` now guards this hop as a CLASS rather
                    // than one field at a time — it has lost four now (B259, B312 x3, B346).
                    DiscontinuitySeconds = player.DiscontinuitySeconds,
                    Slot = appearance.WeaponSuffix(player.WeaponClass, player.PlayerClass),
                    AirborneSeconds = player.AirborneSeconds,
                    EyePitch = player.EyePitch,
                    EyeYaw = player.EyeYaw,
                    AimYaw = player.AimYaw,
                    WaterLevel = player.WaterLevel,

                    // **Both halves of the air-walk meet here.** The timeline says the player rose
                    // fast enough to start one; the class script says whether their class does it
                    // at all, and only the medic opts out. Neither layer can answer both.
                    Airwalking = player.Airwalking && appearance.Airwalks(playerClass),

                    // **Which way the legs run.** A movement sequence is a blend grid and these are
                    // its coordinates; without them the grid's corner is taken, which is one fixed
                    // direction regardless of facing.
                    MoveX = player.MoveX,
                    MoveY = player.MoveY,

                    // **RED is skin 0 and BLU is skin 1**, which is the game's own convention:
                    // `m_nSkin = ( team == TF_TEAM_RED ) ? 0 : 1`. Without it every player draws in
                    // the model's first family, which is red — both teams in red.
                    //
                    // **Computed here rather than read from the entity, deliberately.** For a
                    // player the client computes it itself: `c_tf_player.cpp:712-719` assigns
                    // `m_nSkin` from `m_iTeam` while setting the model, and the field is marked
                    // FTYPEDESC_PRIVATE in the prediction data. It is client state derived from
                    // team, not a value the server sends for players. Props are the opposite case:
                    // a capture point's skin comes from ownership on the server and must be read.
                    //
                    // Not reproduced: the client's two skin OVERRIDES applied straight after —
                    // AdjustSkinIndexForZombie for Halloween, and the gold ragdoll from
                    // TF_DMG_CUSTOM_GOLD_WRENCH.
                    // **Via the disguise, which changes both the team and the family.**
                    // `C_TFPlayer::GetSkin` (`c_tf_player.cpp:7801`) substitutes the disguise team
                    // for an enemy and then adds a mask offset; `Disguise.VisibleSkin` carries
                    // those branches, with the ones it does not implement named at its declaration.
                    Skin = Disguise.VisibleSkin(player),

                    // **The other half of the mask, and the half that was missing.** `GetSkin`
                    // above decides WHICH mask is painted; the mask MESH is a body part, and at
                    // `m_nBody = 0` it is not drawn at all — measured on the shipped
                    // `models/player/spy.mdl`, part 1 named `spyMask`, two alternatives, mask at
                    // alternative 1. So a spy drew with a soldier's mask texture on a mesh nobody
                    // drew, which reached the owner as *"is not wearing the mask"*.
                    //
                    // `C_TFPlayer::ValidateModelIndex`'s tail (`c_tf_player.cpp:9024`) sets it in
                    // exactly the two cases `GetSkin` adds an offset for, which is what makes the
                    // two one mechanism.
                    //
                    // **Applied OVER the equipment, because that is the order in one frame**
                    // (B352). `C_TFPlayer::DrawModel` calls `RecalcBodygroupsIfDirty()`
                    // (`c_tf_player.cpp:6935`), which clears `m_nBody` and rebuilds it from the
                    // items, and then falls through to `C_BaseAnimating::DrawModel`, which calls
                    // `ValidateModelIndex()` (`c_baseanimating.cpp:3195`) under TF_CLIENT_DLL. The
                    // mask therefore survives the rebuild rather than being wiped by it, and a
                    // hat's part keeps its own digit.
                    Body = Disguise.WearsMask(player)
                        ? bodygroups.SetBodygroup(
                            model,
                            bodygroups.FindBodygroup(model, Disguise.MaskBodygroup),
                            1,
                            equipped)
                        : equipped,

                    // **A player's gestures, which are the only animation layers they have**
                    // (B282). `tf_player.cpp:774` excludes `overlay_vars` from the player's send
                    // table, so the reload and the flinch arrive as `CTEPlayerAnimEvent` temp
                    // entities and the timeline turns them into slots.
                    //
                    // **`bNewJump` is applied here**, because it is the other half of the same
                    // pattern as air-walk: the timeline knows the player landed and only the
                    // installed game knows whether that class shows it
                    // (`tf_playeranimstate.cpp:1482`). A class that sets `DontDoNewJump` loses the
                    // JUMP slot and keeps every other gesture.
                    Gestures = Landing(player.Gestures, appearance.Lands(playerClass)),
                },
                ClientSideAnimated: player.ClientSideAnimated));
        }
    }

    /// <summary>Drops the landing gesture for a class that does not play one.</summary>
    /// <param name="gestures">What the timeline collected, or null.</param>
    /// <param name="lands">Whether this class plays a landing gesture.</param>
    /// <returns>The gestures to draw, or null when there are none.</returns>
    /// <remarks>
    /// **The engine never creates it for such a class** — `if ( bNewJump ) RestartGesture( … )`
    /// (`tf_playeranimstate.cpp:1507`) — and the timeline cannot know, because `DontDoNewJump` is
    /// in the class script rather than on the wire. Filtering here reaches the same drawn result
    /// from the only layer that has the answer.
    ///
    /// **Only the JUMP slot, and only the landing.** The slot also carries the double jump, which
    /// the demo really does send and which `bNewJump` does not gate.
    /// </remarks>
    private static IReadOnlyList<SceneGesture>? Landing(
        IReadOnlyList<SceneGesture>? gestures, bool lands)
    {
        if (lands || gestures is not { Count: > 0 })
        {
            return gestures;
        }

        List<SceneGesture> kept = [];

        foreach (SceneGesture gesture in gestures)
        {
            if (gesture.Slot != GestureSlot.Jump ||
                !string.Equals(
                    gesture.ActivityName,
                    PlayerGestureFeed.LandActivity,
                    StringComparison.Ordinal))
            {
                kept.Add(gesture);
            }
        }

        return kept.Count > 0 ? kept : null;
    }

    /// <summary>The body number a player's equipment leaves them with.</summary>
    /// <param name="player">Whose equipment, and whose active weapon decides the deployed-only items.</param>
    /// <param name="props">This moment's props; only the first <paramref name="equipment"/> are read.</param>
    /// <param name="equipment">How many props existed before any player was appended.</param>
    /// <param name="appearance">What the installed game says each item hides.</param>
    /// <param name="bodygroups">The wearer's model's body parts.</param>
    /// <param name="model">The wearer's model, on which the names are resolved.</param>
    /// <returns>Zero for a player wearing nothing the schema names.</returns>
    /// <remarks>
    /// **`CTFPlayerShared::RecalculatePlayerBodygroups` (`tf_player_shared.cpp:13693`), whose three
    /// passes collapse into this one loop — for reasons of arithmetic, not convenience:**
    ///
    /// <code>
    ///   m_pOuter-&gt;m_nBody = 0;
    ///   CTFWeaponBase::UpdateWeaponBodyGroups( m_pOuter, false );
    ///   CEconWearable::UpdateWearableBodyGroups( m_pOuter );
    ///   CTFWeaponBase::UpdateWeaponBodyGroups( m_pOuter, true );
    /// </code>
    ///
    /// Both callers pass a state of 1 — `pWpn-&gt;UpdateBodygroups( pPlayer, 1 )`
    /// (`tf_weaponbase.cpp:6229`) and `nVisibleState = 1` (`econ_wearable.cpp:317`) — and
    /// `CEconEntity::UpdateBodygroups` applies an entry only when its value equals that state
    /// (`econ_entity.cpp:2046`). Every entry that CAN apply therefore sets its group to 1, no two
    /// applied entries can disagree, and the order between passes cannot change the result.
    ///
    /// **The one thing the split really decides is the deployed-only weapon**, which survives here
    /// as its own condition against <c>ActiveWeapon</c>.
    ///
    /// **What is deliberately not reproduced, and what it would cost:**
    /// <list type="bullet">
    /// <item><c>nVisibleState = 0</c> for a pyro-vision-filtered item, the only route by which an
    /// entry valued 0 applies. It is a client vision filter, not demo state.</item>
    /// <item><c>IsBeingRepurposedForTaunt</c> and <c>IsDynamicModelLoading</c>, which skip a weapon
    /// mid-taunt and one whose model has not arrived. Taunt props are B351; a model we have not
    /// loaded produces no prop here to walk.</item>
    /// <item>The style arm of <c>UpdateBodygroups</c>, and this one is PROVED dead rather than
    /// deferred: <c>GetStyleInfo</c> needs <c>GetSOCData</c>, which finds an inventory only for the
    /// subscribed account (<c>econ_item_view.cpp:839</c>), and a demo has none. The exception is
    /// the networked `item style override` attribute — B234.</item>
    /// <item><c>wm_bodygroup_override</c>, which sets a part by INDEX rather than by name. Two
    /// shipped items declare it and it needs a second resolver — B353.</item>
    /// </list>
    ///
    /// **The equipped set is read from the draw list rather than from a roster, and that IS the
    /// engine's own reconstruction.** `m_hMyWearables` is a server-side vector; the client rebuilds
    /// its copy from each wearable entity as it arrives, keyed by the owner the entity names. The
    /// owner-or-wearer chain here is the same one the paint and the burn level resolve through
    /// (`MomentScene.cs:305`).
    /// </remarks>
    private static int Equipped(
        ScenePlayer player,
        IList<SceneProp> props,
        int equipment,
        IPlayerAppearance appearance,
        IModelBodygroups bodygroups,
        string model)
    {
        int body = 0;

        for (int index = 0; index < equipment; index++)
        {
            SceneProp prop = props[index];

            if (prop.ItemDefinitionIndex is not { } item
                || (prop.OwnedBy ?? prop.AttachedTo) != player.EntityIndex)
            {
                continue;
            }

            ItemBodygroups groups = appearance.BodygroupsOf(item);

            // `if ( bHideBodygroupsDeployedOnly && pPlayer->GetActiveWeapon() != pWpn ) continue;`
            // (`tf_weaponbase.cpp:6226`). All eight shipped items that set the flag are weapons, so
            // asking whether this prop is the one being held is the whole of the third pass.
            if (groups.DeployedOnly && player.ActiveWeapon != prop.EntityIndex)
            {
                continue;
            }

            if (groups.Named is { Count: > 0 } named)
            {
                foreach ((string name, int state) in named)
                {
                    // `if ( iBody != iState ) continue;` with iState fixed at 1 — see the remarks.
                    // The name is then resolved and set separately because that is the engine's own
                    // pair: `FindBodygroupByName` answering -1 is a `continue`, not a body of -1.
                    if (state == AppliedState)
                    {
                        body = bodygroups.SetBodygroup(
                            model, bodygroups.FindBodygroup(model, name), AppliedState, body);
                    }
                }
            }

            // **The last arm, and the only one that takes a part NUMBER** (B353,
            // `econ_entity.cpp:2083`). It runs after the named entries because the engine runs it
            // there, and both guards are Valve's: the fields default to -1
            // (`econ_item_schema.h:1065`) and half a declaration does nothing.
            //
            // **An item can declare ONLY this**, which is why the empty-names check above is no
            // longer a `continue` — the Purity Fist has no `player_bodygroups` at all.
            if (groups.OverrideGroup > -1 && groups.OverrideState > -1)
            {
                body = bodygroups.SetBodygroup(
                    model, groups.OverrideGroup, groups.OverrideState, body);
            }
        }

        return body;
    }

    /// <summary>The state both equipment passes run at, and so the only value that applies.</summary>
    private const int AppliedState = 1;
}
