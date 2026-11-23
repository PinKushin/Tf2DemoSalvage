using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// What the engine actually does with a bound key, written down before implementing it.
/// </summary>
/// <remarks>
/// **Every claim below is read from `src/game/client/in_main.cpp` and `kbutton.h`**, which are
/// published in `source-sdk-2013`. This is client code, not the closed engine: the `+forward`
/// console command lands in `IN_ForwardDown`, and everything about how a held button behaves is
/// visible from there.
///
/// **Why this suite exists at all.** The owner's requirement is that a real TF2 config works
/// wholesale, and his reason for it is the thing that makes this hard:
///
/// > *"since we are going to take valve cfgs, we have to allow scripting or it wont work. valve
/// > configs are little state machines themselves"*
///
/// He is right, and the first attempt at D69 was wrong because of it. That attempt resolved a bind
/// statically — follow `+mfwd` to its alias body, take the first command it recognises, record
/// `w -> FlyForward` — which reads a script as though it were a table. It is not. `alias` runs at
/// runtime and **redefines other aliases as it goes**, so the meaning of a name depends on what has
/// been pressed. A static reader gets the common case right by luck and cannot get the mechanism
/// right at all.
///
/// The structures are small and the semantics are strange in specific ways, so they are pinned here
/// with citations rather than inferred later from behaviour.
/// </remarks>
public sealed class ConfigConsoleConformanceTests
{
    /// <summary>
    /// A null-cancelling movement script, copied from the owner's own <c>autoexec.cfg</c>.
    /// </summary>
    /// <remarks>
    /// This is the specimen the whole feature is measured against. It is ordinary in competitive
    /// play — it makes opposite keys behave when both are held, instead of the engine's default of
    /// cancelling to a standstill.
    /// </remarks>
    private const string NullCancel = """
        alias +mfwd   "-back;      +forward;   alias checkfwd   +forward"
        alias -mfwd   "-forward;   checkback;  alias checkfwd   none"
        alias +mback  "-forward;   +back;      alias checkback  +back"
        alias -mback  "-back;      checkfwd;   alias checkback  none"
        alias checkfwd  none
        alias checkback none
        alias none ""

        bind "w" "+mfwd"
        bind "s" "+mback"
        """;

    [Test]
    public void KeyUp_AKeyBoundToAPlusCommand_RunsTheMinusCounterpart()
    {
        // **The `+`/`-` pair is the entire basis of Source's input scripting.** `in_main.cpp`
        // registers both halves of every button:
        //
        //     void IN_ForwardDown( const CCommand &args ) {KeyDown(&in_forward, args[1] );}
        //     void IN_ForwardUp  ( const CCommand &args ) {KeyUp  (&in_forward, args[1] );}
        //
        // A key bound to `+forward` therefore runs `+forward` on press and `-forward` on release,
        // and a key bound to a plain command runs it once and does nothing on release.
        ConfigConsole console = new();
        console.Load("bind \"w\" \"+forward\"");

        console.KeyDown("w");
        console.IsHeld(ViewerAction.FlyForward).ShouldBeTrue();

        console.KeyUp("w");
        console.IsHeld(ViewerAction.FlyForward).ShouldBeFalse();
    }

    [Test]
    public void KeyDown_TwoKeysBoundToOneButton_KeepItDownUntilBothRelease()
    {
        // **`kbutton_t` tracks TWO keys, not a boolean.** `kbutton.h` in full:
        //
        //     struct kbutton_t
        //     {
        //         // key nums holding it down
        //         int   down[ 2 ];
        //         // low bit is down state
        //         int   state;
        //     };
        //
        // and `KeyUp` refuses to release while the other slot is occupied:
        //
        //     if (b->down[0] || b->down[1])
        //         return;      // some other key is still holding it down
        //
        // **A `bool` would fail this and it is not a contrived case** — binding both an arrow key
        // and a WASD key to the same movement is common, and so is the left/right shift pair.
        ConfigConsole console = new();
        console.Load("bind \"w\" \"+forward\"\nbind \"UPARROW\" \"+forward\"");

        console.KeyDown("w");
        console.KeyDown("UPARROW");
        console.KeyUp("w");

        console.IsHeld(ViewerAction.FlyForward)
            .ShouldBeTrue("UPARROW is still holding the button down");

        console.KeyUp("UPARROW");
        console.IsHeld(ViewerAction.FlyForward).ShouldBeFalse();
    }

    [Test]
    public void KeyDown_AThirdKeyOnOneButton_IsRefusedRatherThanTracked()
    {
        // **`down[2]` has exactly two slots and the third press is dropped**, with a developer
        // warning rather than an error:
        //
        //     else
        //     {
        //         if ( c[0] )
        //             DevMsg( 1,"Three keys down for a button '%c' '%c' '%c'!\n", ... );
        //         return;
        //     }
        //
        // The consequence is the interesting half: the third key was never recorded, so **its
        // release is a key-up with no matching key-down** and is ignored too. The button survives.
        ConfigConsole console = new();
        console.Load(
            """
            bind "w" "+forward"
            bind "UPARROW" "+forward"
            bind "k" "+forward"
            """);

        console.KeyDown("w");
        console.KeyDown("UPARROW");
        console.KeyDown("k");
        console.KeyUp("k");

        console.IsHeld(ViewerAction.FlyForward)
            .ShouldBeTrue("the third key was never tracked, so releasing it releases nothing");
    }

    [Test]
    public void KeyUp_WithoutAMatchingKeyDown_IsIgnored()
    {
        // **"menu pass through", in Valve's own comment.** A key pressed while a menu had focus
        // arrives here only on release:
        //
        //     else
        //         return;      // key up without coresponding down (menu pass through)
        //
        // (The typo is Valve's.) This viewer has the same hazard — a key released after an overlay
        // window took focus — so the behaviour is worth having rather than merely worth matching.
        ConfigConsole console = new();
        console.Load("bind \"w\" \"+forward\"");

        Should.NotThrow(() => console.KeyUp("w"));
        console.IsHeld(ViewerAction.FlyForward).ShouldBeFalse();
    }

    [Test]
    public void KeyDown_ARepeatOfAKeyAlreadyHeld_ChangesNothing()
    {
        // Auto-repeat fires while a key is held. `KeyDown` discards it up front:
        //
        //     if (k == b->down[0] || k == b->down[1])
        //         return;      // repeating key
        //
        // **Without this the second slot fills with a duplicate**, and then one release leaves the
        // button stuck down forever — the classic "my character keeps walking" bug.
        ConfigConsole console = new();
        console.Load("bind \"w\" \"+forward\"");

        console.KeyDown("w");
        console.KeyDown("w");
        console.KeyDown("w");
        console.KeyUp("w");

        console.IsHeld(ViewerAction.FlyForward).ShouldBeFalse("one release must undo any repeats");
    }

    [Test]
    public void KeyState_PressedThisFrame_IsAHalf()
    {
        // **Source gives partial credit for a key that was not down for the whole frame.**
        // `CInput::KeyState` reads the impulse bits set by `KeyDown`/`KeyUp` (`state |= 1 + 2` and
        // `state |= 4`) and returns:
        //
        //     impulsedown && !impulseup  ->  down ? 0.5  : 0.0   // pressed and held this frame
        //     !impulsedown && !impulseup ->  down ? 1.0  : 0.0   // held the entire frame
        //     impulsedown &&  impulseup  ->  down ? 0.75 : 0.25  // re-pressed / tapped
        //
        // This is why a tap moves you slightly rather than not at all, and it exists because the
        // key changed state partway through a frame that is about to be integrated whole.
        ConfigConsole console = new();
        console.Load("bind \"w\" \"+forward\"");

        console.KeyDown("w");

        console.KeyState(ViewerAction.FlyForward).ShouldBe(0.5f);
    }

    [Test]
    public void KeyState_HeldForAWholeFrame_IsOne()
    {
        ConfigConsole console = new();
        console.Load("bind \"w\" \"+forward\"");

        console.KeyDown("w");
        console.KeyState(ViewerAction.FlyForward).ShouldBe(0.5f, "the frame it was pressed in");

        console.KeyState(ViewerAction.FlyForward).ShouldBe(1f, "and every frame after");
    }

    [Test]
    public void KeyState_TappedWithinOneFrame_IsAQuarterAndThenZero()
    {
        // A press and release between two reads. The button is up by the time anyone looks, so a
        // `bool` reports nothing happened and the input is silently lost. Source reports 0.25.
        ConfigConsole console = new();
        console.Load("bind \"w\" \"+forward\"");

        console.KeyDown("w");
        console.KeyUp("w");

        console.KeyState(ViewerAction.FlyForward).ShouldBe(0.25f, "pressed and released this frame");
        console.KeyState(ViewerAction.FlyForward).ShouldBe(0f);
    }

    [Test]
    public void KeyState_ReadTwiceInOneFrame_ClearsTheImpulseOnTheFirstRead()
    {
        // **Reading is destructive, and this is a genuine quirk rather than an inference.** The last
        // thing `CInput::KeyState` does before returning is
        //
        //     // clear impulses
        //     key->state &= 1;
        //
        // so the second read in the same frame sees a plain held button. The engine gets away with
        // it because `CreateMove` reads each button exactly once; anything that reads twice
        // silently loses the partial credit. Pinned here so that if this viewer ever reads twice,
        // the surprise is a failing test rather than movement that is subtly wrong.
        ConfigConsole console = new();
        console.Load("bind \"w\" \"+forward\"");

        console.KeyDown("w");

        console.KeyState(ViewerAction.FlyForward).ShouldBe(0.5f);
        console.KeyState(ViewerAction.FlyForward).ShouldBe(1f, "the impulse was consumed");
    }

    [Test]
    public void Execute_AnAliasRedefiningAnotherAlias_TakesEffectImmediately()
    {
        // **This is the state machine the owner named, at its smallest.** `checkfwd` means `none`
        // until `+mfwd` runs, and means `+forward` afterwards — the same name, two meanings,
        // decided by what has been pressed.
        //
        // A static reader cannot represent this. It has to pick one meaning for `checkfwd` and
        // whichever it picks is wrong half the time.
        ConfigConsole console = new();
        console.Load(NullCancel);

        console.Resolve("checkfwd").ShouldBe("none", "before anything is pressed");

        console.KeyDown("w");

        console.Resolve("checkfwd").ShouldBe("+forward", "+mfwd redefined it on the way through");
    }

    [Test]
    public void KeyUp_TheNullCancelScript_RestoresTheKeyStillHeld()
    {
        // **The behaviour the whole script exists to produce, end to end.**
        //
        // Hold W, then hold S: `+mback` runs `-forward; +back`, so you go backwards — the newer key
        // wins instead of the two cancelling. Now release S: `-mback` runs `-back; checkfwd`, and
        // `checkfwd` is `+forward` because `+mfwd` set it when W went down. Forward resumes without
        // W being touched.
        //
        // **No amount of static resolution produces this.** It needs the alias table to be mutable
        // while the keys are being pressed, which is the requirement in one sentence.
        ConfigConsole console = new();
        console.Load(NullCancel);

        console.KeyDown("w");
        console.IsHeld(ViewerAction.FlyForward).ShouldBeTrue();

        console.KeyDown("s");
        console.IsHeld(ViewerAction.FlyForward).ShouldBeFalse("+mback ran -forward");
        console.IsHeld(ViewerAction.FlyBack).ShouldBeTrue();

        console.KeyUp("s");
        console.IsHeld(ViewerAction.FlyBack).ShouldBeFalse();
        console.IsHeld(ViewerAction.FlyForward)
            .ShouldBeTrue("checkfwd resumed it, and W was never released");
    }

    [Test]
    public void Execute_ASemicolonSeparatedLine_RunsEveryCommandInOrder()
    {
        // `bind "x" "+forward; +moveright"` is legal and common — jump-throw scripts and
        // class-switch binds are built out of it.
        ConfigConsole console = new();
        console.Load("bind \"x\" \"+forward; +moveright\"");

        console.KeyDown("x");

        console.IsHeld(ViewerAction.FlyForward).ShouldBeTrue();
        console.IsHeld(ViewerAction.FlyRight).ShouldBeTrue();
    }

    [Test]
    public void KeyUp_ACompoundBind_ReleasesOnlyItsFirstCommand()
    {
        // **This test originally asserted the opposite, and the SDK says otherwise.** The guess was
        // that the engine flips every `+` in the line to `-`. It flips exactly one character:
        //
        //     binding.m_pDeactivateCommand = strdup( press_command_str.String() );
        //     binding.m_pDeactivateCommand[0] = '-';           // in_sixense_gesture_bindings.cpp
        //
        //     Q_snprintf( cmdbuf, sizeof( cmdbuf ), "%s", state.cmd );
        //     if ( !data.bState ) { cmdbuf[0] = '-'; }         // in_steamcontroller.cpp
        //
        // Two independent binding layers, both testing only `[0]` and both writing only `[0]`. So
        // `"+forward; +moveright"` releases as `"-forward; +moveright"` and moveright is stuck down.
        //
        // **Reproduced on purpose rather than fixed.** It is a well-known Source footgun — it is why
        // competitive configs wrap compound binds in an alias — and the requirement here is that a
        // pasted config behave the way it does in the game it was written for. Silently improving on
        // the engine would make this viewer a third thing to learn.
        ConfigConsole console = new();
        console.Load("bind \"x\" \"+forward; +moveright\"");

        console.KeyDown("x");
        console.IsHeld(ViewerAction.FlyForward).ShouldBeTrue();
        console.IsHeld(ViewerAction.FlyRight).ShouldBeTrue();

        console.KeyUp("x");

        console.IsHeld(ViewerAction.FlyForward).ShouldBeFalse("its `+` became `-`");
        console.IsHeld(ViewerAction.FlyRight)
            .ShouldBeTrue("only the first character is flipped, so this one never got a release");
    }

    [Test]
    public void KeyUp_ABindThatIsNotAPlusCommand_RunsNothing()
    {
        // The Steam controller path guards the entire release on the same test:
        //
        //     if ( ( data.bState && !state.bAwaitingDebounce ) || state.cmd[0] == '+' )
        //
        // — so on release (`!data.bState`) a command that does not start with `+` is not run at all.
        // It is not that `-explode` does nothing; it is that nothing is issued.
        ConfigConsole console = new();
        int triggered = 0;

        // `demo_togglepause` is Valve's own — "Toggles demo playback" — and replaced this project's
        // invented `playpause` in B214. Any non-`+` command serves this test; the point is the
        // release, not the name.
        console.Load("bind \"j\" \"demo_togglepause\"");
        console.Triggered += (_, _) => triggered++;

        console.KeyDown("j");
        console.KeyUp("j");

        triggered.ShouldBe(1, "the press fires once and the release issues no command at all");
    }

    [Test]
    public void Applied_BindsLoadedBeforeTheirAliases_AreStillCounted()
    {
        // **This is the file order the engine actually uses**, and it is the reverse of the order
        // that makes a naive counter work. `valve.rc` execs `config.cfg` and then `autoexec.cfg`, so
        // the binds arrive before the aliases they name.
        //
        // Counting at bind time therefore reports the movement binds as unrecognised — measured at
        // 5 against 13 on the owner's real config. The viewer would still fly correctly and its own
        // diagnostic would say the config barely loaded, which is the worst kind of wrong: a number
        // that misdirects rather than an error that stops you.
        ConfigConsole engineOrder = new();
        engineOrder.Load("bind \"w\" \"+mfwd\"");
        engineOrder.Load("alias +mfwd \"+forward\"");

        ConfigConsole reversed = new();
        reversed.Load("alias +mfwd \"+forward\"");
        reversed.Load("bind \"w\" \"+mfwd\"");

        engineOrder.Applied.ShouldBe(1);
        engineOrder.Applied.ShouldBe(reversed.Applied, "the order the files arrive in cannot matter");
    }

    [Test]
    public void Applied_ABindNamingNothingWeImplement_IsNotCounted()
    {
        // The control for the test above: if `Applied` counted every bind, it would agree with the
        // order-independence assertion while measuring nothing at all.
        ConfigConsole console = new();
        console.Load("bind \"w\" \"+forward\"\nbind \"c\" \"+duck\"\nbind \"`\" \"toggleconsole\"");

        console.Bound.ShouldBe(3);
        console.Applied.ShouldBe(1, "only +forward names an action this viewer has");
    }

    [Test]
    public void KeyDown_AKeyTheConfigGivesToAnUnimplementedCommand_KeepsOurDefault()
    {
        // **Taken from the owner's real `config.cfg`, which does exactly this**: `bind "SHIFT"
        // "+duck"`. There is no crouch in this viewer, so without a fallback Shift would run a
        // command that does nothing and fly-fast would have no key at all.
        //
        // **The first version of this test asserted exactly that**, on the reasoning that the player
        // said Shift is duck and overriding them would be the viewer claiming to know better. Then
        // the real config was loaded and the diagnostic printed
        //
        //     no key reaches: ResetCamera, PlayPause, FlyFast
        //
        // — and `resetcamera` and `playpause` are *this project's own* command names. TF2 has no
        // concept of either, so no config can ever bind them; it just uses `f` and `k` for its own
        // purposes and three controls disappear. **A config cannot express a preference about a
        // feature the game does not have**, so reading its silence as one was the error.
        //
        // Nothing is lost by falling back, which is what makes this safe rather than a guess: the
        // config's command for that key does nothing here, so the key was inert either way.
        ConfigConsole console = ConfigConsole.WithDefaults();

        console.Bindings().KeyFor(ViewerAction.FlyWalk).ShouldBe("SHIFT", "before the config runs");

        console.Load("bind \"SHIFT\" \"+duck\"");

        console.Unbound().ShouldNotContain(ViewerAction.FlyWalk);

        console.KeyDown("SHIFT");
        console.IsHeld(ViewerAction.FlyWalk).ShouldBeTrue("+duck does nothing here, so ours stands");
    }

    [Test]
    public void KeyDown_AKeyTheConfigGivesToAnImplementedCommand_LosesOurDefault()
    {
        // **The control, and the line the fallback must not cross.** `+forward` is a command this
        // viewer implements, so a config putting it on Shift is a statement it can act on — and
        // Shift must stop being fly-fast rather than doing both.
        //
        // Without this case the rule above would be indistinguishable from "the defaults always
        // win", which would make the whole feature a no-op.
        ConfigConsole console = ConfigConsole.WithDefaults();

        console.Load("bind \"SHIFT\" \"+forward\"");

        console.KeyDown("SHIFT");

        console.IsHeld(ViewerAction.FlyForward).ShouldBeTrue();
        console.IsHeld(ViewerAction.FlyWalk).ShouldBeFalse("the config spoke, and it wins");
        console.Unbound().ShouldContain(ViewerAction.FlyWalk, "and the loss is reported");
    }

    [Test]
    public void Intent_AfterUnbindallWithNoMovementBinds_WasdStillFlies()
    {
        // **B775's second hypothesis, closed by this test**: a user whose config runs `unbindall` and then binds
        // nothing to movement. TF2 would leave W dead; this viewer keeps its own default for a key the config left
        // unbound, so the free camera still flies (`CommandFor`'s fallback). A Discord report of a free camera that
        // "couldn't move" on cp_badlands raised the question; this is the answer for the config half of it.
        ConfigConsole console = ConfigConsole.WithDefaults();
        console.Load("unbindall\nbind \"MOUSE1\" \"+attack\"\nbind \"`\" \"toggleconsole\"");

        console.KeyDown("w").ShouldBeTrue();
        console.KeyDown("d").ShouldBeTrue();

        FlightInput intent = console.Intent();
        intent.Forward.ShouldBe(0.5f, "pressed partway through this frame");
        intent.Right.ShouldBe(0.5f);
    }

    [Test]
    public void Unbound_TheShippedDefaults_LeaveNothingUnreachable()
    {
        // The control for the test above. Without it, `Unbound` returning everything always would
        // satisfy that assertion while measuring nothing.
        ConfigConsole.WithDefaults().Unbound().ShouldBeEmpty();
    }

    [Test]
    public void Unbound_AnActionRehomedByTheConfig_IsNotReported()
    {
        // Rebinding is the normal case and must not look like a loss: moving fly-fast to CTRL
        // leaves it reachable, just elsewhere.
        ConfigConsole console = ConfigConsole.WithDefaults();

        console.Load("bind \"SHIFT\" \"+duck\"\nbind \"CTRL\" \"+speed\"");

        console.Unbound().ShouldNotContain(ViewerAction.FlyWalk);
        console.Bindings().KeyFor(ViewerAction.FlyWalk).ShouldBe("CTRL");
    }

    [Test]
    public void Intent_AKeyTappedBetweenFrames_StillMovesTheCamera()
    {
        // **The reason `Intent` reads `KeyState` rather than `IsHeld`.** A key pressed and released
        // between two frames is up by the time anything looks at it, so a boolean reports nothing
        // happened and the input vanishes. Source scores it 0.25.
        //
        // This is the assertion that makes the fractional state production code rather than a
        // decorative capability — without it, `KeyState` would be a tested no-op that nothing calls.
        ConfigConsole console = new();
        console.Load("bind \"w\" \"+forward\"");

        console.KeyDown("w");
        console.KeyUp("w");

        console.Intent().Forward.ShouldBe(0.25f);
    }

    [Test]
    public void Intent_TheFrameAKeyGoesDown_IsHalfAndThenFull()
    {
        ConfigConsole console = new();
        console.Load("bind \"w\" \"+forward\"");

        console.KeyDown("w");

        console.Intent().Forward.ShouldBe(0.5f, "pressed partway through this frame");
        console.Intent().Forward.ShouldBe(1f, "held for the whole of the next one");
    }

    [Test]
    public void Intent_OppositeKeysHeldTogether_Cancel()
    {
        // The engine's own default, and the behaviour a null-cancel script exists to replace. Worth
        // pinning because it is the control for that script: if both configs behaved the same, the
        // null-cancel tests above would prove nothing.
        ConfigConsole console = new();
        console.Load("bind \"w\" \"+forward\"\nbind \"s\" \"+back\"");

        console.KeyDown("w");
        console.KeyDown("s");
        console.Intent();

        console.Intent().Forward.ShouldBe(0f, "with no script, opposite keys sum to a standstill");
    }

    [Test]
    public void Intent_TheOtherAxes_AreUnaffectedByAForwardPress()
    {
        // The bystander. Without it, "moved the camera forward" and "moved the camera" are the same
        // observation.
        ConfigConsole console = new();
        console.Load(
            """
            bind "w" "+forward"
            bind "d" "+moveright"
            bind "'" "+moveup"
            bind "SHIFT" "+speed"
            """);

        console.KeyDown("w");

        FlightInput intent = console.Intent();

        intent.Forward.ShouldBe(0.5f);
        intent.Right.ShouldBe(0f);
        intent.Up.ShouldBe(0f);
        intent.Walk.ShouldBeFalse();
    }

    [Test]
    public void Load_AClientCvarLine_SetsIt()
    {
        // `name value` on a line that names a ConVar sets it; later lines win, as for binds.
        ConfigConsole console = new();

        console.Load("tf_hud_target_id_alpha 40\ntf_hud_target_id_alpha \"55\"");

        console.ConVar("tf_hud_target_id_alpha").ShouldBe("55");
    }

    /// <summary>`cl_voice_filter`, the engine's own client ConVar (B549 item 4): a config's line reaches the voice plan.</summary>
    [Test]
    public void Load_AVoiceFilterLine_SetsIt()
    {
        ConfigConsole console = new();

        console.Load("cl_voice_filter \"lic\"");

        console.ConVar("cl_voice_filter").ShouldBe("lic");
    }

    [Test]
    public void Load_AReplicatedCvarLine_IsRefused()
    {
        // FCVAR_REPLICATED is "server setting enforced on clients" (iconvar.h); the client's config cannot set it.
        ConfigConsole console = new();

        console.Load("mp_tournament 1");

        console.ConVar("mp_tournament").ShouldBeNull();
    }

    /// <remarks>
    /// **A cheat ConVar is refused while `sv_cheats` is 0** (B718, measured on live TF2 during demo playback, 2026-11-10):
    /// *"Can't use cheat cvar r_screenfademinsize in multiplayer, unless the server has sv_cheats set to 1."* and it kept 0.
    /// </remarks>
    [Test]
    public void Load_ACheatCvarWithCheatsOff_IsRefused()
    {
        ConfigConsole console = new();

        console.Load("r_screenfademinsize 12");

        console.ConVar("r_screenfademinsize").ShouldBeNull();
        console.RefusedCheats.ShouldBe(["r_screenfademinsize"]);
    }

    /// <remarks>
    /// **`sv_cheats 1` from the console is accepted during playback, and then the cheat ConVar applies** (B718, measured).
    /// `sv_cheats` is replicated, and still the console takes it while a demo plays.
    /// </remarks>
    [Test]
    public void Load_SvCheatsThenACheatCvar_SetsBoth()
    {
        ConfigConsole console = new();

        console.Load("sv_cheats 1\nr_screenfademinsize 12");

        console.ConVar("sv_cheats").ShouldBe("1");
        console.ConVar("r_screenfademinsize").ShouldBe("12");
        console.RefusedCheats.ShouldBeEmpty();
    }

    /// <remarks>
    /// **The viewer's own fullbright key works with cheats off; a config's bind to the same ConVar does not** (D253, D246).
    /// The built-in `CTRL+1` is the viewer's feature, as its menu is; `x` bound by a config is TF2's command and is refused,
    /// and the refusal ends the clause, so nothing after it runs as a game command.
    /// </remarks>
    [Test]
    public void KeyDown_TheViewersFullbrightKeyAndAConfigsBind_OnlyTheViewersSetsIt()
    {
        ConfigConsole console = ConfigConsole.WithDefaults();

        console.KeyDown("CTRL+1");
        console.ConVar("mat_fullbright").ShouldBe("1", "the viewer's own key");

        console.Load("bind \"x\" \"mat_fullbright 2\"");
        console.KeyDown("x").ShouldBeTrue("a refused cheat is still the console's answer");

        console.ConVar("mat_fullbright").ShouldBe("1", "the config's bind is refused with cheats off");
        console.RefusedCheats.ShouldBe(["mat_fullbright"]);
    }

    /// <remarks>
    /// **`mat_wireframe` is `FCVAR_CHEAT` in TF2** (`engine.dll` `0x180007b94`, flags `0x4000`; *read*), so a config's set
    /// meets D253's refusal (B791). Before, it was undeclared and nothing marked it a cheat.
    /// </remarks>
    [Test]
    public void Load_MatWireframeWithCheatsOff_IsRefused()
    {
        ConfigConsole console = new();

        console.Load("mat_wireframe 1");

        console.ConVar("mat_wireframe").ShouldBeNull();
        console.RefusedCheats.ShouldBe(["mat_wireframe"]);
    }

    /// <remarks>
    /// **A config's bind to `mat_wireframe` is TF2's command, refused with cheats off; the viewer's default key is not** (B791;
    /// the default key's exemption is B792's question and unchanged here). Before, `mat_wireframe 1` fell through to the
    /// viewer's wireframe one-shot and toggled it, and a bare `mat_wireframe` did the same, whatever `sv_cheats` said.
    /// </remarks>
    [Test]
    public void KeyDown_AConfigsBindsToMatWireframeWithCheatsOff_TriggerNothingAndTheDefaultKeyStillDoes()
    {
        ConfigConsole console = ConfigConsole.WithDefaults();
        List<ViewerAction> triggered = [];
        console.Triggered += (_, e) => triggered.Add(e.Action);

        console.Load(
            "bind \"x\" \"mat_wireframe 1\"\nbind \"y\" \"mat_wireframe\"\nalias \"wire\" \"mat_wireframe\"\nbind \"z\" \"wire\"\n" +
            "bind \"u\" \"MAT_WIREFRAME 1\"\nbind \"v\" \"Mat_Wireframe\"");
        console.KeyDown("x");
        console.KeyDown("y");
        console.KeyDown("z");
        console.KeyDown("u");
        console.KeyDown("v");

        triggered.ShouldBeEmpty("none toggles, whatever the case: the console finds a ConVar case-insensitively (B791 review 8)");
        console.RefusedCheats.ShouldBe(
            ["mat_wireframe", "mat_wireframe"], "the sets are refused; the bare name is no set, TF2 prints the value");

        console.KeyDown("CTRL+w");

        triggered.ShouldBe([ViewerAction.Wireframe], "the viewer's own default key");
    }

    /// <remarks>
    /// **With `sv_cheats 1` a config's bare `mat_wireframe` is the viewer's one-shot and toggles** (B788 review 4): TF2
    /// prints the value instead, which is B792's owner question; this pins what ours does until it is answered.
    /// </remarks>
    [Test]
    public void KeyDown_AConfigsBareMatWireframeBindWithCheatsOn_TriggersTheWireframeOneShot()
    {
        ConfigConsole console = ConfigConsole.WithDefaults();
        List<ViewerAction> triggered = [];
        console.Triggered += (_, e) => triggered.Add(e.Action);

        console.Load("sv_cheats 1\nbind \"y\" \"mat_wireframe\"");
        console.KeyDown("y");

        triggered.ShouldBe([ViewerAction.Wireframe]);
        console.RefusedCheats.ShouldBeEmpty();
    }

    /// <remarks>With `sv_cheats 1` a bind's `mat_wireframe 1` is a ConVar set like any other, announced for the host (D215).</remarks>
    [Test]
    public void KeyDown_AConfigsBindToMatWireframeWithCheatsOn_AnnouncesTheSet()
    {
        ConfigConsole console = new();
        List<string> set = [];
        List<ViewerAction> triggered = [];
        console.ConVarSet += (_, e) => set.Add($"{e.Name} {e.Value}");
        console.Triggered += (_, e) => triggered.Add(e.Action);

        console.Load("sv_cheats 1\nbind \"x\" \"mat_wireframe 1\"");
        console.KeyDown("x");

        set.ShouldBe(["mat_wireframe 1"]);
        triggered.ShouldBeEmpty();
        console.RefusedCheats.ShouldBeEmpty();
    }

    /// <remarks>
    /// **`toggle` and `incrementvar` on `mat_wireframe` are sets too, refused with cheats off** (B791 review 5, Sonnet): both
    /// reach `SetConVar`, so neither announces a set nor triggers the one-shot.
    /// </remarks>
    [TestCase("toggle mat_wireframe")]
    [TestCase("incrementvar mat_wireframe 0 1 1")]
    [TestCase("toggle MAT_WIREFRAME")]
    [TestCase("incrementvar Mat_Wireframe 0 1 1")]
    public void KeyDown_AConfigsToggleOfMatWireframeWithCheatsOff_IsRefused(string command)
    {
        ConfigConsole console = new();
        List<string> set = [];
        List<ViewerAction> triggered = [];
        console.ConVarSet += (_, e) => set.Add($"{e.Name} {e.Value}");
        console.Triggered += (_, e) => triggered.Add(e.Action);

        console.Load($"bind \"x\" \"{command}\"");
        console.KeyDown("x");

        set.ShouldBeEmpty();
        triggered.ShouldBeEmpty();
        console.RefusedCheats.ShouldBe(["mat_wireframe"]);
    }

    /// <remarks>
    /// **A config line read once the settings are attached is announced under the ConVar's own name too** (B791 review 10,
    /// Fable): `MAT_WIREFRAME 1` from a cfg exec'd at run time reaches the settings as `mat_wireframe 1`.
    /// </remarks>
    [Test]
    public void Load_AMixedCaseLineAfterTheSettingsAreAttached_AnnouncesTheConVarsOwnName()
    {
        ConfigConsole console = new();
        List<string> set = [];
        console.ConVarSet += (_, e) => set.Add($"{e.Name} {e.Value}");
        console.ReadSettingsFrom(_ => null);

        console.Load("sv_cheats 1\nMAT_WIREFRAME 1\nCL_SHOWFPS 1");

        set.ShouldBe(
            ["sv_cheats 1", "mat_wireframe 1", "cl_showfps 1"], "a non-cheat ConVar in another case is set too (B791 review 12)");
    }

    /// <remarks>
    /// **With cheats on, a mixed-case `toggle` toggles** (B791 review 9): the current value is found case-insensitively, so
    /// `toggle MAT_WIREFRAME` announces `mat_wireframe 1` as the lower-case one does.
    /// </remarks>
    [Test]
    public void KeyDown_AMixedCaseToggleOfMatWireframeWithCheatsOn_AnnouncesTheSet()
    {
        ConfigConsole console = new();
        List<string> set = [];
        console.ConVarSet += (_, e) => set.Add($"{e.Name} {e.Value}");

        console.Load("sv_cheats 1\nbind \"x\" \"toggle MAT_WIREFRAME\"\nbind \"y\" \"MAT_WIREFRAME 2\"");
        console.KeyDown("x");
        console.KeyDown("y");

        set.ShouldBe(["mat_wireframe 1", "mat_wireframe 2"], "announced under the ConVar's own name, so the settings find it");
    }

    /// <remarks>`sv_cheats` is read as the engine reads it, `(int)atof`: 0.5 is off, so a cheat ConVar is still refused.</remarks>
    [TestCase("0.5", false)]
    [TestCase("1abc", true)]
    [TestCase("2", true)]
    public void Load_SvCheatsAsTheEngineReadsIt_DecidesTheCheat(string value, bool applies)
    {
        ConfigConsole console = new();

        console.Load($"sv_cheats {value}\nr_screenfademinsize 12");

        console.ConVar("r_screenfademinsize").ShouldBe(applies ? "12" : null);
    }

    /// <remarks>
    /// **`sv_cheats 0` reverts every cheat ConVar to its default** (B718, measured: `r_screenfademinsize` 12 went back to 0). A
    /// client ConVar that is not a cheat keeps its value.
    /// </remarks>
    [Test]
    public void Load_SvCheatsBackToZero_RevertsTheCheatCvarsAndNothingElse()
    {
        ConfigConsole console = new();

        console.Load("sv_cheats 1\nr_screenfademinsize 12\ncl_detaildist 900\nsv_cheats 0");

        console.ConVar("r_screenfademinsize").ShouldBe("0");
        console.ConVar("cl_detaildist").ShouldBe("900");
    }

    [Test]
    public void KeyDown_AnAliasThatSetsACvar_SetsIt()
    {
        // A cvar set inside an alias body takes effect when the alias runs, not when it is defined.
        ConfigConsole console = new();

        console.Load("alias minhud \"cl_hud_minmode 1\"\nbind \"m\" \"minhud\"");
        console.ConVar("cl_hud_minmode").ShouldBeNull();

        console.KeyDown("m");

        console.ConVar("cl_hud_minmode").ShouldBe("1");
    }

    [Test]
    public void KeyDown_TheOwnersSelfRewritingToggle_SetsTheCvarOnThenOffOnAlternatePresses()
    {
        // The owner's own shape (D215): an alias that redefines the name the key is bound to. Each run-time set is
        // announced, because in Source the ConVar's value IS the setting and the viewer has to apply it.
        ConfigConsole console = new();
        List<string> set = [];
        console.ConVarSet += (_, e) => set.Add($"{e.Name} {e.Value}");

        console.Load("""
            alias sboardtoggle    "sboardtoggleon"
            alias sboardtoggleon  "+showscores; cl_showpos 1; alias sboardtoggle sboardtoggleoff"
            alias sboardtoggleoff "-showscores; cl_showpos 0; alias sboardtoggle sboardtoggleon"
            bind tab sboardtoggle
            """);

        console.KeyDown("tab");
        console.KeyUp("tab");
        console.KeyDown("tab");

        set.ShouldBe(["cl_showpos 1", "cl_showpos 0"]);
        console.ConVar("cl_showpos").ShouldBe("0");
    }

    [Test]
    public void KeyDown_TwoKeysBoundToSetAndClear_AnnounceEachValue()
    {
        ConfigConsole console = new();
        List<string> set = [];
        console.ConVarSet += (_, e) => set.Add($"{e.Name} {e.Value}");

        console.Load("bind x \"cl_showpos 1\"\nbind y \"cl_showpos 0\"");
        console.KeyDown("x");
        console.KeyDown("y");

        set.ShouldBe(["cl_showpos 1", "cl_showpos 0"]);
    }

    [Test]
    public void Load_ACvarLine_AnnouncesNothing()
    {
        // A config's lines are read at startup, under the viewer's own settings file and launch options (ClientConVar's
        // precedence); only a command run once the viewer is up applies over them.
        ConfigConsole console = new();
        int announced = 0;
        console.ConVarSet += (_, _) => announced++;

        console.Load("cl_showpos 1");

        announced.ShouldBe(0);
    }

    /// <summary>Binds <c>k</c> to <paramref name="command"/>, presses it once per count, and returns what was set.</summary>
    private static List<string> Press(string setup, string command, int presses = 1)
    {
        ConfigConsole console = new();
        List<string> set = [];
        console.ConVarSet += (_, e) => set.Add($"{e.Name} {e.Value}");
        console.Load($"{setup}\nbind k \"{command}\"");

        for (int i = 0; i < presses; i++)
        {
            console.KeyDown("k");
            console.KeyUp("k");
        }

        return set;
    }

    // incrementvar, engine.dll FUN_1801a7fe0: exactly 5 tokens; current is the cvar's FLOAT value; new = current +
    // delta; new > max -> min, new < min -> max, else new; executes "%s %f".
    [TestCase("cl_showpos 0", "incrementvar cl_showpos 0 2 1", "cl_showpos 1.000000")]
    [TestCase("cl_showpos 2", "incrementvar cl_showpos 0 2 1", "cl_showpos 0.000000")]
    [TestCase("cl_showpos 0", "incrementvar cl_showpos 0 2 -1", "cl_showpos 2.000000")]
    [TestCase("cl_showpos 1", "incrementvar cl_showpos 0 2 1", "cl_showpos 2.000000")]
    [TestCase("cl_showpos 1", "incrementvar cl_showpos 0 2 -1", "cl_showpos 0.000000")]
    public void KeyDown_IncrementVar_WrapsAtTheBoundsAndKeepsEquality(string setup, string command, string expected) =>
        Press(setup, command).ShouldBe([expected]);

    // multvar, engine.dll FUN_1801a8bc0: new = current * factor; new > max -> max; new <= min -> min; else new. Clamps.
    [TestCase("cl_showpos 2", "multvar cl_showpos 1 3 2", "cl_showpos 3.000000")]
    [TestCase("cl_showpos 2", "multvar cl_showpos 1 4 0.5", "cl_showpos 1.000000")]
    [TestCase("cl_showpos 2", "multvar cl_showpos 2 8 1", "cl_showpos 2.000000")]
    [TestCase("cl_showpos 2", "multvar cl_showpos 1 8 2", "cl_showpos 4.000000")]
    [TestCase("cl_showpos 2", "multvar cl_showpos 1 4 2", "cl_showpos 4.000000")]
    public void KeyDown_MultVar_ClampsAtTheBounds(string setup, string command, string expected) =>
        Press(setup, command).ShouldBe([expected]);

    // Both: exactly 5 tokens else Usage and nothing else; `cvar "%s" not found` sets nothing.
    [TestCase("incrementvar cl_showpos 0 2")]
    [TestCase("incrementvar cl_showpos 0 2 1 9")]
    [TestCase("incrementvar no_such_cvar 0 2 1")]
    [TestCase("multvar cl_showpos 0 2")]
    [TestCase("multvar no_such_cvar 0 2 1")]
    // toggle, engine.dll FUN_180177cd0: fewer than 2 tokens is Usage; FUN_180176f20: "%s is not a valid cvar".
    [TestCase("toggle")]
    [TestCase("toggle no_such_cvar")]
    [TestCase("toggle no_such_cvar 1 2")]
    public void KeyDown_AMalformedOrUnknownCvarCommand_SetsNothing(string command) =>
        Press(string.Empty, command).ShouldBeEmpty();

    // toggle name alone: the INT value, (current == 0) ? 1 : 0. Unset, the declared default ("0") is current.
    [TestCase("", "cl_showpos 1")]
    [TestCase("cl_showpos 0", "cl_showpos 1")]
    [TestCase("cl_showpos 2", "cl_showpos 0")]
    [TestCase("cl_showpos 1.000000", "cl_showpos 0")]
    public void KeyDown_ToggleWithNoValues_FlipsTheIntValue(string setup, string expected) =>
        Press(setup, "toggle cl_showpos").ShouldBe([expected]);

    // toggle name v1..vn: exact byte compare of the STRING value; match vi -> v(i+1), last or no match -> v1.
    [TestCase("cl_showpos 1", "toggle cl_showpos 0 1 2", "cl_showpos 2")]
    [TestCase("cl_showpos 2", "toggle cl_showpos 0 1 2", "cl_showpos 0")]
    [TestCase("cl_showpos 7", "toggle cl_showpos 0 1 2", "cl_showpos 0")]
    [TestCase("cl_showpos 1", "toggle cl_showpos 1.0 3", "cl_showpos 1.0")]
    [TestCase("cl_showpos 1.0", "toggle cl_showpos 1 3", "cl_showpos 1")]
    public void KeyDown_ToggleWithValues_CyclesByExactString(string setup, string command, string expected) =>
        Press(setup, command).ShouldBe([expected]);

    [Test]
    public void KeyDown_TheOwnersToggleAndIncrementBinds_FlipAndCycle()
    {
        // The owner's kind of bind (D215): on and off on alternate presses; 0,1,2,0 from the default.
        Press(string.Empty, "toggle cl_showpos", presses: 3).ShouldBe(["cl_showpos 1", "cl_showpos 0", "cl_showpos 1"]);
        Press(string.Empty, "incrementvar cl_showpos 0 2 1", presses: 3)
            .ShouldBe(["cl_showpos 1.000000", "cl_showpos 2.000000", "cl_showpos 0.000000"]);
    }

    // ---- a key's bind runs, whatever it is (B547) ----
    //
    // The engine hands every key to the binding system: `Key_Event` looks up the key's binding and queues it through
    // the command buffer, `+` binds getting the key number and their `-` half on release (the closed engine's
    // keys.cpp; the shape is Quake's `Key_Event` in keys.c, which it descends from - interpolated, the binary was not
    // read for this). Nothing there tells a keyboard key from a mouse button, so nothing here may.
    [TestCase("toggle cl_showpos", true)]
    [TestCase("cl_showpos 1", true)]
    [TestCase("+forward", true)]
    [TestCase("resetspeed", false)]
    [TestCase("+strafe", false)]
    public void KeyDown_WhatTheBindDid_IsReportedSoTheHostCanTakeTheKey(string command, bool expected)
    {
        // `resetspeed` is one of the viewer's own one-shots, whose key the host routes itself; `+strafe` is TF2's and
        // means nothing here. Neither did anything of the console's, so neither may take the key from the window.
        ConfigConsole console = new();
        console.Load($"bind k \"{command}\"");

        console.KeyDown("k").ShouldBe(expected);
    }

    [Test]
    public void KeyDown_AKeyWithAViewerDefaultReboundToToggle_RunsTheToggleNotTheDefault()
    {
        // The default fallback (B214) is for a bind that means nothing here; a `toggle` means something, so the
        // user's config wins (D101). It fell back to `resetspeed`, and the key did not toggle.
        ConfigConsole console = ConfigConsole.WithDefaults();
        string home = KeyBindings.Defaults[ViewerAction.ResetSpeed];
        List<string> set = [];
        List<ViewerAction> fired = [];
        console.ConVarSet += (_, e) => set.Add($"{e.Name} {e.Value}");
        console.Triggered += (_, e) => fired.Add(e.Action);
        console.Load($"bind \"{home}\" \"toggle cl_showpos\"");

        console.KeyDown(home).ShouldBeTrue();

        set.ShouldBe(["cl_showpos 1"]);
        fired.ShouldBeEmpty();
    }

    [Test]
    public void KeyDown_TheScoreboardToggleAlias_HoldsOnOnePressAndReleasesOnTheNext()
    {
        // A toggle script of the owner's kind (a-config-is-a-program.md): the alias redefines itself as it runs, so
        // one key means `+showscores` on one press and `-showscores` on the next.
        ConfigConsole console = new();
        console.Load(
            """
            alias +sb "+showscores; alias tog_sb -sb"
            alias -sb "-showscores; alias tog_sb +sb"
            alias tog_sb +sb
            bind TAB tog_sb
            """);

        console.KeyDown("TAB").ShouldBeTrue();
        console.KeyUp("TAB");
        console.IsHeld(ViewerAction.ShowScores).ShouldBeTrue("the first press holds the scoreboard up");

        console.KeyDown("TAB").ShouldBeTrue();
        console.KeyUp("TAB");
        console.IsHeld(ViewerAction.ShowScores).ShouldBeFalse("the second press takes it down");
    }

    // ---- B820: one dispatcher, and `exec` ----------------------------------------------------------------------------
    // Read from engine.dll (Ghidra): `Cmd_ExecuteCommand` (FUN_1801678a0) runs every line, from a file, a key or an alias
    // body, and `Cmd_Exec_f` (FUN_1801672f0) reads `//MOD/cfg/<name>`. The full read is B820 in docs/RISKS.md.

    /// <summary>A client ConVar no test elsewhere depends on, set by the files below.</summary>
    private const string Alpha = "tf_hud_target_id_alpha";

    /// <summary>A console whose `exec` reads from the given files, recording each path it asked for.</summary>
    private static ConfigConsole WithFiles(List<string> asked, params (string Path, string Text)[] files)
    {
        Dictionary<string, string> byPath = new(StringComparer.Ordinal);

        foreach ((string path, string text) in files)
        {
            byPath[path] = text;
        }

        return new ConfigConsole
        {
            FindConfig = path =>
            {
                asked.Add(path);
                return byPath.TryGetValue(path, out string? text) ? System.Text.Encoding.UTF8.GetBytes(text) : null;
            },
        };
    }

    /// <remarks>Each line of the file is executed before the next is read, so a nested file runs where its `exec` stood.</remarks>
    [Test]
    public void Load_AnExecLine_RunsTheFileWhereTheLineStands()
    {
        List<string> asked = [];
        ConfigConsole console = WithFiles(asked, ("cfg/a.cfg", $"{Alpha} 100"));

        console.Load($"{Alpha} 50\nexec a");
        console.ConVar(Alpha).ShouldBe("100");

        console.Load($"exec a\n{Alpha} 50");
        console.ConVar(Alpha).ShouldBe("50");

        asked.ShouldBe(["cfg/a.cfg", "cfg/a.cfg"]);
    }

    /// <remarks>`V_DefaultExtension` (FUN_180282920) appends `.cfg` only when the last path segment has no dot.</remarks>
    [TestCase("exec comfig/comfig", "cfg/comfig/comfig.cfg")]
    [TestCase("exec a.txt", "cfg/a.txt")]
    [TestCase("exec a.b/c", "cfg/a.b/c.cfg")]
    [TestCase("exec A.VPK", "cfg/A.VPK")]
    [TestCase("exec \"a\\b\"", "cfg/a\\b.cfg")]
    [TestCase("exec a\r", "cfg/a.cfg")]
    public void Exec_AName_IsLookedUpUnderCfgWithCfgAddedOnlyWithoutAnExtension(string line, string path)
    {
        List<string> asked = [];
        ConfigConsole console = WithFiles(asked);

        console.Load(line);

        asked.ShouldBe([path]);
    }

    /// <remarks>
    /// `exec` takes exactly one argument; the path is refused when it holds `\\`, `:`, `..`, CR or LF (FUN_180176d10), or
    /// any of twelve file types anywhere in it. Both tests are case-sensitive substring tests, so `A.VPK` above is read,
    /// and so is a single backslash. Each refusal is TF2's own line (B820 review 1, Fable, Grok: reading nothing alone
    /// passed with `exec` not dispatched). CR and LF cannot reach a path here: a line ends at LF and the tokenizer trims a CR (`exec a\r` above).
    /// </remarks>
    [TestCase("exec", "exec <filename>: execute a script file")]
    [TestCase("exec a b", "exec <filename>: execute a script file")]
    [TestCase("exec ../a", "exec cfg/../a.cfg: invalid path.")]
    [TestCase("exec \"c:a\"", "exec cfg/c:a.cfg: invalid path.")]
    [TestCase("exec \"a\\\\b\"", "exec cfg/a\\\\b.cfg: invalid path.")]
    [TestCase("exec a.vpk", "exec cfg/a.vpk: invalid file type.")]
    [TestCase("exec a.dll.cfg", "exec cfg/a.dll.cfg: invalid file type.")]
    [TestCase("exec a.exe", "exec cfg/a.exe: invalid file type.")]
    [TestCase("exec a.vbs", "exec cfg/a.vbs: invalid file type.")]
    [TestCase("exec a.com", "exec cfg/a.com: invalid file type.")]
    [TestCase("exec a.bat", "exec cfg/a.bat: invalid file type.")]
    [TestCase("exec a.ini", "exec cfg/a.ini: invalid file type.")]
    [TestCase("exec a.gcf", "exec cfg/a.gcf: invalid file type.")]
    [TestCase("exec a.sys", "exec cfg/a.sys: invalid file type.")]
    [TestCase("exec a.zip", "exec cfg/a.zip: invalid file type.")]
    [TestCase("exec a.blob", "exec cfg/a.blob: invalid file type.")]
    [TestCase("exec a.scr", "exec cfg/a.scr: invalid file type.")]
    public void Exec_ARefusedLine_ReadsNothingAndSaysWhy(string line, string said)
    {
        List<string> asked = [];
        RecordingLogger log = new();
        ConfigConsole console = WithFiles(asked);
        console.Log = log;

        console.Load(line);

        asked.ShouldBeEmpty();
        log.Lines.Select(entry => entry.Message).ShouldBe([said]);
    }

    /// <remarks>A file over 1,048,576 bytes is refused: `exec %s: file size larger than 1 MB!`.</remarks>
    [TestCase(1_048_576, "7", 0)]
    [TestCase(1_048_577, null, 1)]
    public void Exec_AFileAtAndOverOneMegabyte_RunsOnlyAtTheLimit(int size, string? expected, int said)
    {
        string line = $"{Alpha} 7\n";
        string text = line + new string(' ', size - line.Length);
        List<string> asked = [];
        RecordingLogger log = new();
        ConfigConsole console = WithFiles(asked, ("cfg/big.cfg", text));
        console.Log = log;

        console.Load("exec big");

        console.ConVar(Alpha).ShouldBe(expected);
        log.Count("exec big: file size larger than 1 MB!").ShouldBe(said);
    }

    /// <remarks>
    /// *Interpolated* budget (B820 review 1, Fable, Sonnet): two `exec self` lines would read 2^20 files under the depth bound
    /// alone; one command reads at most <see cref="ConfigConsole.MaximumExecs"/>, says so once, and the next command starts
    /// a fresh budget.
    /// </remarks>
    [Test]
    public void Exec_AFileThatExecsItselfTwice_StopsAtTheBudgetAndTheNextCommandGetsItsOwn()
    {
        List<string> asked = [];
        RecordingLogger log = new();
        ConfigConsole console = WithFiles(asked, ("cfg/loop.cfg", "exec loop\nexec loop"), ("cfg/a.cfg", $"{Alpha} 4"));
        console.Log = log;

        console.Load("exec loop\nexec a");

        asked.Count(path => path == "cfg/loop.cfg").ShouldBe(ConfigConsole.MaximumExecs);
        log.Count("more than 256 files from one command").ShouldBe(1);
        console.ConVar(Alpha).ShouldBe("4", "the second line is a new command with its own budget");
    }

    /// <remarks>
    /// `'%s' not present; not executing.` for a missing file, except when the argument AS TYPED holds `autoexec.cfg`,
    /// `joystick.cfg` or `game.cfg` (`V_stristr`, FUN_1802845d0): `exec autoexec` says it, `exec MyGame.CFG` does not.
    /// </remarks>
    [TestCase("exec missing", 1)]
    [TestCase("exec autoexec", 1)]
    [TestCase("exec autoexec.cfg", 0)]
    [TestCase("exec joystick.cfg", 0)]
    [TestCase("exec MyGame.CFG", 0)]
    public void Exec_AMissingFile_SaysSoUnlessItIsOneOfTheEnginesOptionalThree(string line, int said)
    {
        RecordingLogger log = new();
        ConfigConsole console = WithFiles([]);
        console.Log = log;

        console.Load(line);

        log.Count("not present; not executing.").ShouldBe(said);
    }

    /// <remarks>*Interpolated* bound: nothing in `Cmd_Exec_f` counts depth; this console stops a self-`exec` rather than hang.</remarks>
    [Test]
    public void Exec_AFileThatExecsItself_Ends()
    {
        List<string> asked = [];
        ConfigConsole console = WithFiles(asked, ("cfg/loop.cfg", "exec loop"));

        console.Load("exec loop");

        asked.Count.ShouldBe(ConfigConsole.MaximumExpansion);
    }

    /// <remarks>
    /// *Interpolated* budget (B820 review 2, Fable, Sonnet): `alias a "a;a"` and a line `a` is 2^20 runs under the depth
    /// bound alone, and a config line runs its alias at load. One command runs at most
    /// <see cref="ConfigConsole.MaximumAliasRuns"/>, says so once, and the next line starts a fresh budget.
    /// </remarks>
    [Test]
    public void Load_AnAliasThatRunsItselfTwice_StopsAtTheBudgetAndTheNextLineRuns()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        console.ReadSettingsFrom(_ => null);
        List<string> sets = [];
        console.ConVarSet += (_, e) => sets.Add(e.Value);

        console.Load($"alias a \"a;a;{Alpha} 1\"\na\n{Alpha} 8");

        log.Count($"more than {ConfigConsole.MaximumAliasRuns} alias runs from one line").ShouldBe(1);
        sets.Count.ShouldBeInRange(2, ConfigConsole.MaximumAliasRuns + 1, "the stop itself (B820 review 3, Fable): 2^19 sets without it");
        sets[^1].ShouldBe("8", "the next line runs");
    }

    /// <remarks>
    /// **The alias budget is per line of an exec'd file too** (B820 review 3, Fable, Sonnet): one `exec` of a config tree does
    /// not share one budget among all its lines. Two lines of one file that each exhaust it say so twice.
    /// </remarks>
    [Test]
    public void Exec_AFileWhoseTwoLinesEachExhaustTheAliasBudget_GivesEachLineItsOwn()
    {
        RecordingLogger log = new();
        ConfigConsole console = WithFiles([], ("cfg/fan.cfg", "a\na"));
        console.Log = log;

        console.Load("alias a \"a;a\"\nexec fan");

        log.Count($"more than {ConfigConsole.MaximumAliasRuns} alias runs from one line").ShouldBe(2);
    }

    /// <remarks>
    /// **The static projection walks each alias once** (B820 review 3, Sonnet, Fable): `alias a "a;a;a;a"` bound to a key was
    /// 4^20 calls from `Applied` at load and from the key's lookup on every press, on `main` too.
    /// </remarks>
    [Test]
    public void KeyDown_AKeyBoundToAnAliasThatFansOut_EndsPromptly()
    {
        ConfigConsole console = new();
        console.Load("alias a \"a;a;a;a;+forward\"\nbind x a");

        Should.CompleteIn(
            () =>
            {
                console.Applied.ShouldBe(1);
                console.KeyDown("x");
            },
            TimeSpan.FromSeconds(10));
    }

    /// <remarks>
    /// **A load inside a load leaves the outer one a load** (B820 reviews 2 and 3, Sonnet, Fable): a handler that loads a
    /// config while one is loading must not end the outer load's quiet one-shots. The control: the handler ran.
    /// </remarks>
    [Test]
    public void Load_ALoadFromAHandlerDuringALoad_LeavesTheOuterOneShotsIdle()
    {
        ConfigConsole console = new();
        console.ReadSettingsFrom(_ => null);
        List<ViewerAction> fired = [];
        int handled = 0;
        console.Triggered += (_, e) => fired.Add(e.Action);
        console.ConVarSet += (_, _) =>
        {
            handled++;
            console.Load(string.Empty);
        };

        console.Load($"{Alpha} 1\nscreenshot");

        handled.ShouldBe(1);
        fired.ShouldBeEmpty();
    }

    /// <remarks>
    /// **…nor hand the outer command fresh budgets** (B820 review 3, Fable, Sonnet): a file that execs itself twice and sets a
    /// ConVar on each line, with a handler that loads on every set, still stops at <see cref="ConfigConsole.MaximumExecs"/>.
    /// </remarks>
    [Test]
    public void Load_ALoadFromAHandlerInsideAnExecTree_KeepsTheOuterBudget()
    {
        List<string> asked = [];
        ConfigConsole console = WithFiles(asked, ("cfg/loop.cfg", $"{Alpha} 1\nexec loop\nexec loop"));
        console.ReadSettingsFrom(_ => null);
        console.ConVarSet += (_, _) => console.Load(string.Empty);

        Should.CompleteIn(() => console.Load("exec loop"), TimeSpan.FromSeconds(10));

        asked.Count.ShouldBe(ConfigConsole.MaximumExecs);
    }

    [Test]
    public void KeyDown_AKeyBoundToExec_RunsTheFile()
    {
        List<string> asked = [];
        ConfigConsole console = WithFiles(asked, ("cfg/a.cfg", $"{Alpha} 33"));
        console.Load("bind k \"exec a\"");

        console.KeyDown("k");

        console.ConVar(Alpha).ShouldBe("33");
    }

    /// <remarks>A config line is dispatched as a key's is: a name with no command of its own runs its alias.</remarks>
    [Test]
    public void Load_ALineNamingAnAlias_RunsIt()
    {
        ConfigConsole console = new();

        console.Load($"alias set_alpha \"{Alpha} 70\"\nset_alpha");

        console.ConVar(Alpha).ShouldBe("70");
    }

    /// <remarks>`bind`, `unbind` and `unbindall` are ConCommands, so an alias body reaches them as a file line does.</remarks>
    [Test]
    public void KeyDown_AnAliasThatBinds_BindsTheKey()
    {
        ConfigConsole console = new();
        console.Load("alias rebind \"bind k +forward\"\nbind j rebind");

        console.KeyDown("j");
        console.KeyDown("k");

        console.IsHeld(ViewerAction.FlyForward).ShouldBeTrue();
    }

    [Test]
    public void KeyDown_AnAliasThatUnbinds_UnbindsTheKey()
    {
        ConfigConsole console = new();
        console.Load("bind k +forward\nalias drop \"unbind k\"\nbind j drop");
        console.KeyDown("k");
        console.IsHeld(ViewerAction.FlyForward).ShouldBeTrue("the control: the bind landed (B820 review 1, Grok)");
        console.KeyUp("k");

        console.KeyDown("j");
        console.KeyDown("k");

        console.IsHeld(ViewerAction.FlyForward).ShouldBeFalse();
    }

    [Test]
    public void KeyDown_AnAliasThatUnbindsAll_UnbindsEveryKey()
    {
        ConfigConsole console = new();
        console.Load("bind k +forward\nalias clear unbindall\nbind j clear");

        console.KeyDown("j");

        console.Bound.ShouldBe(0);
        console.KeyDown("k").ShouldBeFalse("nothing is bound to k now");
    }

    /// <remarks>
    /// **A key with a viewer default still runs a bound `exec` or `bind`** (B820 review 1, Fable): the default stands in only
    /// for a bind that does nothing here, and those do something.
    /// </remarks>
    [Test]
    public void KeyDown_ADefaultKeyBoundToExec_RunsTheFileNotTheDefault()
    {
        List<string> asked = [];
        ConfigConsole console = ConfigConsole.WithDefaults();
        console.FindConfig = path =>
        {
            asked.Add(path);
            return path == "cfg/a.cfg" ? System.Text.Encoding.UTF8.GetBytes($"{Alpha} 12") : null;
        };
        string key = console.Bindings().KeyFor(ViewerAction.Screenshot);
        List<ViewerAction> fired = [];
        console.Triggered += (_, e) => fired.Add(e.Action);
        console.Load($"bind \"{key}\" \"exec a\"");

        console.KeyDown(key);

        console.ConVar(Alpha).ShouldBe("12");
        fired.ShouldBeEmpty();
    }

    /// <remarks>
    /// *Interpolated* (B820 review 1, Fable, Sonnet): TF2 runs its configs at startup, before a demo plays, so a config's view
    /// or playback one-shot has nothing to act on; a `+` button is state and is kept. The control: the same one-shot from a
    /// key fires.
    /// </remarks>
    [Test]
    public void Load_AOneShotLine_DoesNothingAndAButtonLineHolds()
    {
        ConfigConsole console = new();
        List<ViewerAction> fired = [];
        console.Triggered += (_, e) => fired.Add(e.Action);

        console.Load("screenshot\ntogglefullscreen\ncl_showpos\n+forward\nbind k screenshot");

        fired.ShouldBeEmpty();
        console.IsHeld(ViewerAction.FlyForward).ShouldBeTrue();

        console.KeyDown("k");
        fired.ShouldBe([ViewerAction.Screenshot]);
    }

    /// <remarks>
    /// **A key's `exec` is a run-time command** (B820 review 1, Sonnet, Fable): its ConVar sets are announced as a key's are,
    /// and a `Load` before it leaves nothing behind that would silence them.
    /// </remarks>
    [Test]
    public void KeyDown_AKeysExecAfterALoad_AnnouncesItsConVarSets()
    {
        List<string> asked = [];
        ConfigConsole console = WithFiles(asked, ("cfg/a.cfg", $"{Alpha} 21"), ("cfg/b.cfg", $"{Alpha} 3"));
        List<string> set = [];
        console.ConVarSet += (_, e) => set.Add($"{e.Name} {e.Value}");
        console.Load("exec b\nbind k \"exec a\"");

        console.KeyDown("k");

        set.ShouldBe([$"{Alpha} 21"], "the load's own set is not announced; the key's is");
    }

    /// <remarks>
    /// The static projection takes the dispatcher's order (B820): an alias named as a ConVar is not what a key bound to that
    /// name runs, so the bind names nothing this viewer implements.
    /// </remarks>
    [Test]
    public void Applied_ABindToAnAliasNamedAsAConVar_IsNotCounted()
    {
        ConfigConsole console = new();

        console.Load($"alias {Alpha} +forward\nbind k {Alpha}");

        console.Applied.ShouldBe(0);
    }

    /// <remarks>An alias is consulted only when no command or ConVar has the name, so it cannot shadow a ConVar.</remarks>
    [Test]
    public void Load_AnAliasNamedAsAConVar_DoesNotShadowIt()
    {
        ConfigConsole console = new();

        console.Load($"alias {Alpha} \"{Alpha} 5\"\n{Alpha} 60");

        console.ConVar(Alpha).ShouldBe("60");
    }

    /// <remarks>…except a `+` or `-` name, where the alias wins: `alias +forward` replaces the button.</remarks>
    [Test]
    public void KeyDown_AnAliasNamedAsAPlusCommand_ShadowsIt()
    {
        ConfigConsole console = new();
        console.Load("alias +forward +back\nbind w +forward");

        console.KeyDown("w");

        console.IsHeld(ViewerAction.FlyBack).ShouldBeTrue();
        console.IsHeld(ViewerAction.FlyForward).ShouldBeFalse();
    }
}
