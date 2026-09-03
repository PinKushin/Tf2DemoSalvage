namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// The animation events a <c>CTEPlayerAnimEvent</c> can carry, as <c>PlayerAnimEvent_t</c>.
/// </summary>
/// <remarks>
/// **Append-only across TF2's whole history**, so these ordinals are the wire values for every
/// protocol, not just one build's. Members 0–29 are identical in <c>hl2sdk/orangebox</c> (2007–11),
/// <c>source-sdk-2013</c> and the current <c>hl2sdk/tf2</c>; everything from
/// <see cref="DoubleJumpCrouch"/> (30) onward was appended later. An Orange Box demo cannot carry a
/// value ≥ 30, and its narrower <c>m_iEvent</c> field cannot even represent one, so the range is
/// self-enforcing. Full story in <c>docs/findings/25-gesture-layer.md</c>.
/// </remarks>
public enum PlayerAnimEvent
{
    /// <summary>Weapon primary fire.</summary>
    AttackPrimary = 0,

    /// <summary>Weapon secondary fire.</summary>
    AttackSecondary = 1,

    /// <summary>Grenade throw (the base HL2MP path, not TF's own grenades).</summary>
    AttackGrenade = 2,

    /// <summary>Reload start.</summary>
    Reload = 3,

    /// <summary>Reload loop, one iteration per shell for a pump weapon.</summary>
    ReloadLoop = 4,

    /// <summary>Reload end.</summary>
    ReloadEnd = 5,

    /// <summary>Jump — drives the main sequence, not a gesture layer.</summary>
    Jump = 6,

    /// <summary>Swim — drives the main sequence.</summary>
    Swim = 7,

    /// <summary>Death — drives the main sequence (and <c>Assert(0)</c> in the base: unsupported).</summary>
    Die = 8,

    /// <summary>Flinch, hit in the chest.</summary>
    FlinchChest = 9,

    /// <summary>Flinch, hit in the head.</summary>
    FlinchHead = 10,

    /// <summary>Flinch, hit in the left arm.</summary>
    FlinchLeftArm = 11,

    /// <summary>Flinch, hit in the right arm.</summary>
    FlinchRightArm = 12,

    /// <summary>Flinch, hit in the left leg.</summary>
    FlinchLeftLeg = 13,

    /// <summary>Flinch, hit in the right leg.</summary>
    FlinchRightLeg = 14,

    /// <summary>Scout's air dash — a gesture over the jump.</summary>
    DoubleJump = 15,

    /// <summary>Cancel — clears state, no gesture.</summary>
    Cancel = 16,

    /// <summary>Respawn — clears all animation state, no gesture.</summary>
    Spawn = 17,

    /// <summary>Snap the feet yaw to the current value, no gesture.</summary>
    SnapYaw = 18,

    /// <summary>Play a specific activity as the main sequence, given by <c>m_nData</c>.</summary>
    Custom = 19,

    /// <summary>Play a specific activity as a gesture, given by <c>m_nData</c>.</summary>
    CustomGesture = 20,

    /// <summary>Play a specific sequence as the main sequence, given by <c>m_nData</c>.</summary>
    CustomSequence = 21,

    /// <summary>Play a specific sequence as a gesture — commented out in the SDK, a no-op.</summary>
    CustomGestureSequence = 22,

    /// <summary>Weapon pre-fire: minigun windup, sniper aim start.</summary>
    AttackPre = 23,

    /// <summary>Weapon post-fire: minigun winddown.</summary>
    AttackPost = 24,

    /// <summary>Grenade 1 draw — no handler in this SDK, a no-op.</summary>
    Grenade1Draw = 25,

    /// <summary>Grenade 2 draw — no handler, a no-op.</summary>
    Grenade2Draw = 26,

    /// <summary>Grenade 1 throw — no handler, a no-op.</summary>
    Grenade1Throw = 27,

    /// <summary>Grenade 2 throw — no handler, a no-op.</summary>
    Grenade2Throw = 28,

    /// <summary>A voice-command gesture; <c>m_nData</c> is the activity to play.</summary>
    VoiceCommandGesture = 29,

    /// <summary>Double jump while crouched — commented out in the SDK, a no-op.</summary>
    DoubleJumpCrouch = 30,

    /// <summary>Stun begin (holds — no auto-kill).</summary>
    StunBegin = 31,

    /// <summary>Stun middle (holds — no auto-kill).</summary>
    StunMiddle = 32,

    /// <summary>Stun end.</summary>
    StunEnd = 33,

    /// <summary>PassTime throw begin (holds).</summary>
    PasstimeThrowBegin = 34,

    /// <summary>PassTime throw middle (holds).</summary>
    PasstimeThrowMiddle = 35,

    /// <summary>PassTime throw end.</summary>
    PasstimeThrowEnd = 36,

    /// <summary>CYOA PDA intro (holds).</summary>
    CyoaPdaBegin = 37,

    /// <summary>CYOA PDA idle (holds).</summary>
    CyoaPdaMiddle = 38,

    /// <summary>CYOA PDA outro.</summary>
    CyoaPdaEnd = 39,

    /// <summary>Super primary fire (Mannpower/rune melee, etc).</summary>
    AttackPrimarySuper = 40,
}

/// <summary>The seven gesture slots, as <c>GESTURE_SLOT_*</c> in <c>multiplayer_animstate.h</c>.</summary>
/// <remarks>
/// A slot holds at most one gesture at a time — a new event in a slot replaces whatever was there
/// (<c>RestartGesture</c>). That is why the slot matters: two events in the same slot cannot stack,
/// so a reload interrupts a fire, but a flinch (a different slot) plays over either.
/// </remarks>
public enum GestureSlot
{
    /// <summary>Attacks and reloads — the busiest slot.</summary>
    AttackAndReload = 0,

    /// <summary>Grenade throws.</summary>
    Grenade = 1,

    /// <summary>Jumps (the double-jump air dash).</summary>
    Jump = 2,

    /// <summary>Swimming.</summary>
    Swim = 3,

    /// <summary>Flinches from taking damage.</summary>
    Flinch = 4,

    /// <summary>Scripted-scene (VCD) gestures.</summary>
    Vcd = 5,

    /// <summary>Everything else played by activity or sequence: stuns, PassTime, CYOA.</summary>
    Custom = 6,
}

/// <summary>What a player is doing when an event fires, enough to pick the activity variant.</summary>
/// <param name="InDuck">
/// <c>m_fFlags &amp; FL_DUCKING</c>. The SDK also treats a player as not ducking when the model has no
/// crouch form of the activity; that fallback belongs to the model layer, which already applies it
/// (<c>PlayerAnimation.For</c>), so this is the raw flag.
/// </param>
/// <param name="InSwim">
/// Waist-deep or more. The SDK reads this two ways — <c>m_bInSwim</c> and
/// <c>GetWaterLevel() &gt;= WL_Waist</c> — and they agree in practice; waist-deep is the single
/// signal the demo gives.
/// </param>
/// <param name="InAirWalk">Rising fast enough to air-walk (TF's reload-airwalk variants).</param>
/// <param name="IsLoser">In the loser state, which has its own double-jump gesture.</param>
/// <param name="IsMinigun">Holding a minigun, which has stand/crouch/swim fire and windup gestures.</param>
/// <param name="IsSniperZoomed">A scoped sniper rifle or bow, which fires a deployed gesture.</param>
/// <param name="NData">The <c>m_nData</c> payload, an activity number for the two dynamic events.</param>
/// <param name="WeaponSlot">
/// The held weapon's role, which every gesture activity is suffixed by. Carried as a placeholder
/// through the mapping and filled by the scene, since only the installed game says what a weapon's
/// role is. See <c>EntityModelSet.LayersFor</c>.
/// </param>
public readonly record struct GestureContext(
    bool InDuck = false,
    bool InSwim = false,
    bool InAirWalk = false,
    bool IsLoser = false,
    bool IsMinigun = false,
    bool IsSniperZoomed = false,
    int NData = 0,

    // **The weapon slot the activity is suffixed with, and without it a gesture resolves to
    // NOTHING** (B284). Every gesture activity in a TF2 player model carries it — measured on
    // `scout.mdl`: `ACT_MP_RELOAD_STAND_PRIMARY`, `ACT_MP_RELOAD_STAND_SECONDARY`,
    // `ACT_MP_JUMP_LAND_primary`, `ACT_MP_ATTACK_STAND_ITEM2`. The unsuffixed name that this map
    // returned matches no sequence on any class, so no reload ever produced a layer.
    //
    // The same suffix the MAIN sequence has always used (`PlayerActivityState.NameOf`), from the
    // same place: what the installed game says the held weapon's role is.
    string WeaponSlot = "PRIMARY");

/// <summary>A gesture to start: which slot, what to play, and whether it removes itself.</summary>
/// <param name="Slot">The gesture slot it occupies.</param>
/// <param name="ActivityName">
/// The <c>ACT_*</c> activity name to resolve against the model, or <see langword="null"/> when the
/// activity is dynamic — see <paramref name="ActivityNumber"/>.
/// </param>
/// <param name="ActivityNumber">
/// The raw activity ordinal from <c>m_nData</c>, for the two events that carry one
/// (<see cref="PlayerAnimEvent.CustomGesture"/>, <see cref="PlayerAnimEvent.VoiceCommandGesture"/>),
/// or <see langword="null"/> otherwise. Exactly one of this and <paramref name="ActivityName"/> is
/// set. Resolving an ordinal to a name needs the era's activity list and is left to the caller.
/// </param>
/// <param name="AutoKill">Whether the gesture removes itself at the end rather than holding.</param>
public readonly record struct GestureTrigger(
    GestureSlot Slot,
    string? ActivityName,
    int? ActivityNumber,
    bool AutoKill);

/// <summary>
/// Which gesture a <c>PlayerAnimEvent_t</c> starts — <c>DoAnimationEvent</c>, decode-side.
/// </summary>
/// <remarks>
/// This is <c>CTFPlayerAnimState::DoAnimationEvent</c> over
/// <c>CMultiPlayerAnimState::DoAnimationEvent</c>, read as a pure mapping. Not every event starts a
/// gesture: jump, swim, death, spawn, snap-yaw and the custom-sequence events drive the MAIN
/// sequence or clear state rather than layering, and this project already computes the main sequence
/// from player velocity and flags (<c>PlayerActivityState</c>) — so those return
/// <see langword="null"/> here, not a gesture. Several events are dead in the SDK too — the
/// grenade-draw/throw ordinals have no handler, and <c>CustomGestureSequence</c> and
/// <c>DoubleJumpCrouch</c> are commented out — and a null for those is the faithful result, not a
/// gap.
/// </remarks>
public static class PlayerGestureEvent
{
    /// <summary>The gesture an event starts, or <see langword="null"/> when it starts none.</summary>
    /// <param name="anEvent">The event, as read from <c>m_iEvent</c>.</param>
    /// <param name="context">Enough player state to choose the activity variant.</param>
    public static GestureTrigger? Map(PlayerAnimEvent anEvent, GestureContext context) => anEvent switch
    {
        PlayerAnimEvent.AttackPrimary => Named(GestureSlot.AttackAndReload, PrimaryFireActivity(context)),

        PlayerAnimEvent.AttackPrimarySuper => Named(
            GestureSlot.AttackAndReload,
            SwimDuckStand(
                context, "ACT_MP_ATTACK_STAND_PRIMARY_SUPER", "ACT_MP_ATTACK_CROUCH_PRIMARY_SUPER", "ACT_MP_ATTACK_SWIM_PRIMARY_SUPER")),

        // Secondary tests GetWaterLevel() >= WL_Waist directly, and swim overrides the duck choice.
        PlayerAnimEvent.AttackSecondary => Named(
            GestureSlot.AttackAndReload,
            SwimDuckStand(
                context, "ACT_MP_ATTACK_STAND_SECONDARYFIRE", "ACT_MP_ATTACK_CROUCH_SECONDARYFIRE", "ACT_MP_ATTACK_SWIM_SECONDARYFIRE")),

        PlayerAnimEvent.AttackGrenade => Named(GestureSlot.Grenade, "ACT_MP_ATTACK_STAND_GRENADE"),

        // Pre-fire holds by default (sniper aim-start) and only a minigun's windup auto-kills.
        PlayerAnimEvent.AttackPre => Named(
            GestureSlot.AttackAndReload,
            MinigunSwimDuckStand(
                context, "ACT_MP_ATTACK_STAND_PREFIRE", "ACT_MP_ATTACK_CROUCH_PREFIRE", "ACT_MP_ATTACK_SWIM_PREFIRE"),
            autoKill: context.IsMinigun),

        PlayerAnimEvent.AttackPost => Named(
            GestureSlot.AttackAndReload,
            MinigunSwimDuckStand(
                context, "ACT_MP_ATTACK_STAND_POSTFIRE", "ACT_MP_ATTACK_CROUCH_POSTFIRE", "ACT_MP_ATTACK_SWIM_POSTFIRE")),

        PlayerAnimEvent.Reload => Reload(
            context, "ACT_MP_RELOAD_STAND_{0}", "ACT_MP_RELOAD_CROUCH_{0}", "ACT_MP_RELOAD_SWIM_{0}", "ACT_MP_RELOAD_AIRWALK_{0}"),
        PlayerAnimEvent.ReloadLoop => Reload(
            context, "ACT_MP_RELOAD_STAND_{0}_LOOP", "ACT_MP_RELOAD_CROUCH_{0}_LOOP", "ACT_MP_RELOAD_SWIM_{0}_LOOP", "ACT_MP_RELOAD_AIRWALK_{0}_LOOP"),
        PlayerAnimEvent.ReloadEnd => Reload(
            context, "ACT_MP_RELOAD_STAND_{0}_END", "ACT_MP_RELOAD_CROUCH_{0}_END", "ACT_MP_RELOAD_SWIM_{0}_END", "ACT_MP_RELOAD_AIRWALK_{0}_END"),

        PlayerAnimEvent.FlinchChest => Named(GestureSlot.Flinch, "ACT_MP_GESTURE_FLINCH_CHEST"),
        PlayerAnimEvent.FlinchHead => Named(GestureSlot.Flinch, "ACT_MP_GESTURE_FLINCH_HEAD"),
        PlayerAnimEvent.FlinchLeftArm => Named(GestureSlot.Flinch, "ACT_MP_GESTURE_FLINCH_LEFTARM"),
        PlayerAnimEvent.FlinchRightArm => Named(GestureSlot.Flinch, "ACT_MP_GESTURE_FLINCH_RIGHTARM"),
        PlayerAnimEvent.FlinchLeftLeg => Named(GestureSlot.Flinch, "ACT_MP_GESTURE_FLINCH_LEFTLEG"),
        PlayerAnimEvent.FlinchRightLeg => Named(GestureSlot.Flinch, "ACT_MP_GESTURE_FLINCH_RIGHTLEG"),

        PlayerAnimEvent.DoubleJump => Named(
            GestureSlot.Jump,
            context.IsLoser ? "ACT_MP_DOUBLEJUMP_LOSERSTATE" : "ACT_MP_DOUBLEJUMP"),

        // The two events whose activity is carried on the wire rather than fixed by the event.
        PlayerAnimEvent.CustomGesture => Numbered(GestureSlot.Custom, context.NData),
        PlayerAnimEvent.VoiceCommandGesture => Numbered(GestureSlot.AttackAndReload, context.NData),

        // BEGIN and MIDDLE hold (bAutoKill=false); END takes the default true.
        PlayerAnimEvent.StunBegin => Named(GestureSlot.Custom, "ACT_MP_STUN_BEGIN", autoKill: false),
        PlayerAnimEvent.StunMiddle => Named(GestureSlot.Custom, "ACT_MP_STUN_MIDDLE", autoKill: false),
        PlayerAnimEvent.StunEnd => Named(GestureSlot.Custom, "ACT_MP_STUN_END"),

        PlayerAnimEvent.PasstimeThrowBegin => Named(GestureSlot.Custom, "ACT_MP_PASSTIME_THROW_BEGIN", autoKill: false),
        PlayerAnimEvent.PasstimeThrowMiddle => Named(GestureSlot.Custom, "ACT_MP_PASSTIME_THROW_MIDDLE", autoKill: false),
        PlayerAnimEvent.PasstimeThrowEnd => Named(GestureSlot.Custom, "ACT_MP_PASSTIME_THROW_END"),

        PlayerAnimEvent.CyoaPdaBegin => Named(GestureSlot.Custom, "ACT_MP_CYOA_PDA_INTRO", autoKill: false),
        PlayerAnimEvent.CyoaPdaMiddle => Named(GestureSlot.Custom, "ACT_MP_CYOA_PDA_IDLE", autoKill: false),
        PlayerAnimEvent.CyoaPdaEnd => Named(GestureSlot.Custom, "ACT_MP_CYOA_PDA_OUTRO"),

        // Everything else drives the main sequence, clears state, or is dead in the SDK — no gesture.
        _ => null,
    };

    /// <summary>Primary fire's activity, whose swim variant exists only on the minigun path.</summary>
    private static string PrimaryFireActivity(GestureContext context)
    {
        if (context.IsMinigun)
        {
            return SwimDuckStand(
                context, "ACT_MP_ATTACK_STAND_PRIMARYFIRE", "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE", "ACT_MP_ATTACK_SWIM_PRIMARYFIRE");
        }

        if (context.IsSniperZoomed)
        {
            return context.InDuck
                ? "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE_DEPLOYED"
                : "ACT_MP_ATTACK_STAND_PRIMARYFIRE_DEPLOYED";
        }

        // The plain-weapon else branch has no swim form: a soldier firing in water still stands.
        return context.InDuck ? "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE" : "ACT_MP_ATTACK_STAND_PRIMARYFIRE";
    }

    /// <summary>The reload triple, which shares one stand/crouch/swim/airwalk shape.</summary>
    /// <remarks>
    /// Air-walk is the TF override's own case and beats the base stand/crouch/swim choice entirely;
    /// below it the base picks duck first, then swim, then stand.
    /// </remarks>
    private static GestureTrigger Reload(
        GestureContext context, string stand, string crouch, string swim, string airwalk)
    {
        if (context.InAirWalk)
        {
            return Named(GestureSlot.AttackAndReload, airwalk);
        }

        if (context.InDuck)
        {
            return Named(GestureSlot.AttackAndReload, crouch);
        }

        return Named(GestureSlot.AttackAndReload, context.InSwim ? swim : stand);
    }

    /// <summary>Swim beats duck beats stand — the order the attack cases use.</summary>
    private static string SwimDuckStand(GestureContext context, string stand, string crouch, string swim)
    {
        if (context.InSwim)
        {
            return swim;
        }

        return context.InDuck ? crouch : stand;
    }

    /// <summary>Like <see cref="SwimDuckStand"/>, but the swim variant needs a minigun too.</summary>
    private static string MinigunSwimDuckStand(GestureContext context, string stand, string crouch, string swim)
    {
        if (context.InSwim && context.IsMinigun)
        {
            return swim;
        }

        return context.InDuck ? crouch : stand;
    }

    private static GestureTrigger Named(GestureSlot slot, string activity, bool autoKill = true) =>
        new(slot, activity, ActivityNumber: null, autoKill);

    private static GestureTrigger Numbered(GestureSlot slot, int nData, bool autoKill = true) =>
        new(slot, ActivityName: null, nData, autoKill);
}
