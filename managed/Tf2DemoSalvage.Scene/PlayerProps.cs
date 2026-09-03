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
}

/// <summary>What the installed game actually says, from its own scripts.</summary>
/// <param name="Classes">The class script, or null when no install was found.</param>
/// <param name="Roles">The weapon-to-activity map, or null.</param>
/// <remarks>
/// **Null means "no install", and answering null is the honest response.** A viewer with no TF2
/// draws what it can rather than refusing, so every member here degrades to "cannot say" rather
/// than throwing — and a player whose model cannot be named is simply not drawn, which
/// <see cref="PlayerProps.Add"/> treats as a reason to skip.
/// </remarks>
public sealed record GameAppearance(PlayerClassModels? Classes, WeaponRoles? Roles)
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
    public string? Hands(int playerClass) => Classes?.Hands(playerClass);
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
    /// <param name="bodygroup">
    /// Resolves a model's named body part to a body number — <c>EntityModelSet.WithBodygroup</c> in
    /// production. Passed in rather than reached for, because only the model set has the
    /// <c>.mdl</c> and only it knows which index a part's name has on this model.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void Add(
        IReadOnlyList<ScenePlayer> players,
        ICollection<SceneProp> into,
        IPlayerAppearance appearance,
        Func<string, string, int, int> bodygroup)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(bodygroup);

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
                    Speed = player.Speed,
                    Flags = player.Flags,
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
                    Body = Disguise.WearsMask(player)
                        ? bodygroup(model, Disguise.MaskBodygroup, 1)
                        : 0,

                    // **A player's gestures, which are the only animation layers they have**
                    // (B282). `tf_player.cpp:774` excludes `overlay_vars` from the player's send
                    // table, so the reload and the flinch arrive as `CTEPlayerAnimEvent` temp
                    // entities and the timeline turns them into slots.
                    Gestures = player.Gestures,
                },
                ClientSideAnimated: player.ClientSideAnimated));
        }
    }
}
