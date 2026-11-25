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

    /// <remarks>
    /// **A cheat the settings hold at `nan` is reverted too** (B823 review 1, the open item). `sv_cheats`' change callback
    /// (`engine.dll` x64 build 1007 FUN_18013d920) calls `RevertFlaggedConVars( FCVAR_CHEAT )`, and `ConVar::Revert` sets the
    /// default (`src/tier1/convar.cpp:1076-1081`), whose callbacks run when the STRING changed: `V_strcmp( pszOldValue,
    /// m_pszString ) != 0` (`:844`). `nan` is not `0`, so it reverts; a float compare finds no difference with a NaN. The
    /// control is a value already at its default string, which is not announced.
    /// </remarks>
    [Test]
    public void Load_SvCheatsBackToZeroWithACheatAtNan_AnnouncesItsRevert()
    {
        Dictionary<string, string> settings = new(StringComparer.Ordinal)
        {
            ["r_screenfademinsize"] = "nan",
            ["r_screenfademaxsize"] = "0",
        };
        ConfigConsole console = new();
        console.ReadSettingsFrom(name => settings.GetValueOrDefault(name));
        List<string> set = [];
        console.ConVarSet += (_, e) => set.Add($"{e.Name} {e.Value}");

        console.Stuff("sv_cheats 1");
        console.Stuff("sv_cheats 0");

        set.ShouldContain("r_screenfademinsize 0");
        set.ShouldNotContain(entry => entry.StartsWith("r_screenfademaxsize", StringComparison.Ordinal));
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
    // toggle, engine.dll build 1007 FUN_180179390: fewer than 2 tokens is Usage; its validity check, build 1007
    // FUN_1801785e0: "%s is not a valid cvar".
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
    [TestCase("resetspeed", true)]
    [TestCase("+strafe", false)]
    public void KeyDown_WhatTheBindDid_IsReportedSoTheHostCanTakeTheKey(string command, bool expected)
    {
        // `resetspeed` is one of the viewer's own one-shots, which the host performs from the console's `Triggered` since
        // B825, so it takes the key: a menu shortcut on the same key must not fire it again. Until B825 it did not, and
        // the host matched the key itself. `+strafe` is TF2's and means nothing here, so it may not take the key.
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
    /// and so is a single backslash. A file's line has `\\` made `\` before `exec` sees it (FUN_180177310), so the double
    /// backslash case is written as four. Each refusal is TF2's own line (B820 review 1, Fable, Grok: reading nothing alone
    /// passed with `exec` not dispatched). CR and LF cannot reach a path here: a line ends at LF and the tokenizer trims a CR (`exec a\r` above).
    /// </remarks>
    [TestCase("exec", "exec <filename>: execute a script file")]
    [TestCase("exec a b", "exec <filename>: execute a script file")]
    [TestCase("exec ../a", "exec cfg/../a.cfg: invalid path.")]
    [TestCase("exec \"c:a\"", "exec cfg/c:a.cfg: invalid path.")]
    [TestCase("exec \"a\\\\\\\\b\"", "exec cfg/a\\\\b.cfg: invalid path.")]
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
    /// **Every command that changes the binds or the aliases counts, and nothing else does** (B821): the host takes its
    /// projection again when the count moves, so a missed `unbind` leaves a stale key named and a counted ConVar set
    /// rebuilds it for nothing. `bind k` with no command and `unbind` with no key change nothing. The defaults the console
    /// starts with count zero.
    /// </remarks>
    [TestCase("bind k echo", 1)]
    [TestCase("unbind w", 1)]
    [TestCase("unbindall", 1)]
    [TestCase("alias a b", 1)]
    [TestCase("alias a b\nalias a c", 2)]
    [TestCase("fps_max 60", 0)]
    [TestCase("bind k", 0)]
    [TestCase("unbind", 0)]
    public void BindsRevision_AfterACommand_CountsOnlyWhatChangesBindsOrAliases(string lines, int expected)
    {
        ConfigConsole console = ConfigConsole.WithDefaults();
        console.BindsRevision.ShouldBe(0, "the control: the defaults count zero");

        console.Load(lines);

        console.BindsRevision.ShouldBe(expected);
    }

    /// <remarks>
    /// Above Valve (D265): with a leading UTF-8 byte order mark, TF2's first command does not run (`Unknown command`,
    /// measured live, B822); the viewer strips it on `exec` too, not only on the startup configs. The control has no mark.
    /// </remarks>
    [Test]
    public void Exec_AFileWithAUtf8ByteOrderMark_RunsItsFirstCommand()
    {
        List<string> asked = [];
        ConfigConsole console = WithFiles(asked, ("cfg/marked.cfg", char.ConvertFromUtf32(0xFEFF) + $"{Alpha} 3"), ("cfg/plain.cfg", $"{Alpha} 5"));

        console.Load("exec plain");
        console.ConVar(Alpha).ShouldBe("5", "the control has no mark");

        console.Load("exec marked");
        console.ConVar(Alpha).ShouldBe("3");
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
        sets.Count.ShouldBeInRange(
            ConfigConsole.MaximumAliasRuns / 2,
            ConfigConsole.MaximumAliasRuns + 1,
            "the stop itself (B820 reviews 3 and 4, Fable, Sonnet): 2^19 sets without it, and the budget's worth with it");
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
    /// **A load from a handler keeps the outer line's alias budget too** (B820 review 4, Fable): a handler that loads on every
    /// set would otherwise zero it each time, and `a` would run 2^19 sets.
    /// </remarks>
    [Test]
    public void Load_ALoadFromAHandlerInsideAnAliasFanOut_KeepsTheOuterAliasBudget()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        console.ReadSettingsFrom(_ => null);
        int sets = 0;
        console.ConVarSet += (_, _) =>
        {
            sets++;
            console.Load(string.Empty);
        };

        Should.CompleteIn(() => console.Load($"alias a \"a;a;{Alpha} 1\"\na"), TimeSpan.FromSeconds(10));

        log.Count($"more than {ConfigConsole.MaximumAliasRuns} alias runs from one line").ShouldBe(1);
        sets.ShouldBeLessThanOrEqualTo(ConfigConsole.MaximumAliasRuns + 1);
    }

    /// <summary>
    /// An alias `wa` whose body runs 150 of `wb`, each running 240 unknown commands: 36,151 clauses a line, inside the engine's
    /// 510-byte command and under the per-line alias budget; 30 such lines pass the command's budget. <paramref name="lead"/>
    /// goes first in `wa`'s body.
    /// </summary>
    private static string WideAliases(string lead = "") =>
        $"alias wb \"{string.Join(';', Enumerable.Repeat("x", 240))}\"\n" +
        $"alias wa \"{lead}{string.Join(';', Enumerable.Repeat("wb", 150))}\"";

    /// <remarks>
    /// **…and one command runs at most <see cref="ConfigConsole.MaximumCommandClauses"/> clauses in all** (B820 reviews 4 and 5,
    /// Sonnet): the per-line budget resets on every line of an exec'd file and counts alias runs, not what a body does, so 30
    /// lines each under it still stop at the command's. Said once, and the next command runs.
    /// </remarks>
    [Test]
    public void Exec_AFileWhoseLinesStayUnderTheLineBudget_StopsAtTheCommandBudget()
    {
        RecordingLogger log = new();
        ConfigConsole console = WithFiles([], ("cfg/big.cfg", string.Join('\n', Enumerable.Repeat("wa", 30))));
        console.Log = log;

        console.Load($"{WideAliases()}\nexec big\n{Alpha} 8");

        log.Count($"more than {ConfigConsole.MaximumCommandClauses} commands run from one command").ShouldBe(1);
        log.Count($"more than {ConfigConsole.MaximumAliasRuns} alias runs from one line").ShouldBe(0);
        log.Count("overflows the tokenizer buffer").ShouldBe(0, "every command is inside the engine's length");
        console.ConVar(Alpha).ShouldBe("8", "the next command has a budget of its own (B820 review 5, Fable)");
    }

    /// <remarks>
    /// **A load from a handler keeps the outer command's clause count** (B820 review 5, Sonnet, Fable): the handler's `Load`
    /// runs commands of its own, and without the restore the exec tree above never reaches the command's budget.
    /// </remarks>
    [Test]
    public void Load_ALoadFromAHandlerInsideALongExecTree_KeepsTheOuterClauseCount()
    {
        RecordingLogger log = new();
        ConfigConsole console = WithFiles([], ("cfg/big.cfg", string.Join('\n', Enumerable.Repeat("wa", 30))));
        console.Log = log;
        console.ReadSettingsFrom(_ => null);
        int handled = 0;
        console.ConVarSet += (_, _) =>
        {
            handled++;
            console.Load("x");
        };

        console.Load($"{WideAliases($"{Alpha} 1;")}\nexec big");

        handled.ShouldBeGreaterThan(0, "the control: the handler loaded inside the tree");
        log.Count($"more than {ConfigConsole.MaximumCommandClauses} commands run from one command").ShouldBe(1);
    }

    /// <remarks>
    /// **A command of 511 bytes or more is refused** (`CCommandBuffer::InsertCommand`, `src/tier1/commandbuffer.cpp:120-126`),
    /// with TF2's line; 510 runs. An alias definition is one such command, which is what bounds what one alias run can do.
    /// </remarks>
    [TestCase(510, "5", ' ', "", "")]
    [TestCase(511, null, ' ', "", "")]
    [TestCase(510, null, 'é', "", "")]
    [TestCase(509, null, ' ', "  ", "")]
    [TestCase(509, "5", ' ', "\r\n\t\t", "")]
    [TestCase(509, null, ' ', "\t\t", "")]
    [TestCase(510, "5", ' ', "", "\r")]
    [TestCase(510, "5", ' ', "", "// a comment the buffer does not count")]
    [TestCase(510, null, ' ', "", " // the space before a comment is counted")]
    public void Load_ACommandAtAndPastTheBuffersLength_RunsOnlyUnderIt(int length, string? expected, char pad, string lead, string tail)
    {
        // `GetNextCommandLength` measures the raw command (B820 review 6, Fable, Sonnet): bytes (the 'é' case is 510
        // characters), leading spaces counted, a `//` comment not. The file's line reader (`FUN_180177310`, review 7) ends
        // a line at its CR and drops the tabs that start a line after a break, so neither is counted; the file's first line
        // keeps them (its first byte is copied whatever it is). The refused command's line is TF2's.
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        string command = $"{Alpha} 5 \"";
        string padding = new(pad, length - command.Length - 1);

        console.Load(lead + command + padding + "\"" + tail);

        // The value is `ArgS`, the quoted padding with it (B823, `SourceConfig.ConVarValue`).
        console.ConVar(Alpha).ShouldBe(expected is null ? null : $"{expected} \"{padding}\"");
        log.Count("WARNING: Command too long... ignoring!").ShouldBe(expected is null ? 1 : 0);
    }

    /// <remarks>
    /// **A lone byte is one byte of the 511** (B823 review 1, Sonnet): a byte that is no UTF-8 character reaches the console
    /// as U+DC00 + byte (`SourceConfig.Tokens`), and `GetNextCommandLength` counts it once, as it counts every byte.
    /// </remarks>
    [TestCase(510, true)]
    [TestCase(511, false)]
    public void Load_ACommandOfLoneBytesAtTheBuffersLength_CountsEachAsOneByte(int length, bool runs)
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        string command = $"{Alpha} 5 \"";

        console.Load(command + new string('\uDCE9', length - command.Length - 1) + "\"");

        (console.ConVar(Alpha) is not null).ShouldBe(runs);
        log.Count("WARNING: Command too long... ignoring!").ShouldBe(runs ? 0 : 1);
    }

    /// <remarks>
    /// **A config loaded from a handler inside the viewer's own key is a config** (B820 review 7, Fable): the built-in
    /// `CTRL+1` sets the cheat `mat_fullbright` with cheats off (D253), and a load its set sets off refuses its own cheat line.
    /// </remarks>
    [Test]
    public void Load_FromAHandlerInsideTheViewersOwnKey_RefusesItsCheats()
    {
        ConfigConsole console = ConfigConsole.WithDefaults();
        console.ConVarSet += (_, e) =>
        {
            if (e.Name == "mat_fullbright")
            {
                console.Load("mat_wireframe 1");
            }
        };

        console.KeyDown("CTRL+1");

        console.ConVar("mat_fullbright").ShouldBe("1", "the control: the viewer's own key set its cheat");
        console.RefusedCheats.ShouldContain("mat_wireframe");
    }

    /// <remarks>
    /// **…and so is launch text** (B820 review 10, Fable): `Stuff` clears `_viewerIssued` as `Load` does, so a launch line run
    /// from a handler inside the viewer's own key meets TF2's cheat refusal.
    /// </remarks>
    [Test]
    public void Stuff_FromAHandlerInsideTheViewersOwnKey_RefusesItsCheats()
    {
        ConfigConsole console = ConfigConsole.WithDefaults();
        console.ConVarSet += (_, e) =>
        {
            if (e.Name == "mat_fullbright")
            {
                console.Stuff("mat_wireframe 1");
            }
        };

        console.KeyDown("CTRL+1");

        console.ConVar("mat_fullbright").ShouldBe("1", "the control: the viewer's own key set its cheat");
        console.RefusedCheats.ShouldContain("mat_wireframe");
    }

    /// <remarks>
    /// **A file line is cut at 1023 bytes and the rest read as the next line** (`Cmd_Exec_f`'s line reader, `engine.dll`
    /// FUN_180177310, B820 review 7): a 1023-byte line of spaces then `{Alpha} 6` runs the ConVar as a line of its own.
    /// </remarks>
    [Test]
    public void Load_ALineLongerThanTheReadersCap_ContinuesAsTheNextLine()
    {
        // Exactly 1023 bytes end in `{Alpha} 1`; the next byte, `2`, starts the next line (B820 review 8, Sonnet): a cap of
        // 1022 leaves `{Alpha} ` with no value, and one of 1024 makes the value 12. Empty `;` commands pad the front.
        ConfigConsole console = new();
        string command = $"{Alpha} 1";

        console.Load(new string(';', 1023 - command.Length) + command + "2");

        console.ConVar(Alpha).ShouldBe("1");
    }

    /// <remarks>
    /// **A file line that is not a `bind` has `\"` made `"` and `\\` made `\`** (FUN_180177310's two replacements, B820
    /// review 7); a `bind` line keeps them. A line is a `bind` when the word starts it once spaces and tabs are skipped AND
    /// byte 4 of the line itself is a space or tab: ` bind` (byte 4 is `n`) is unescaped, `bind\tk` is not. The control: the
    /// plain form binds.
    /// </remarks>
    [TestCase("bind k \"+forward\"", true)]
    [TestCase("bind k \\\"+forward\\\"", false)]
    [TestCase("bind\tk \\\"+forward\\\"", false)]
    [TestCase(" bind k \\\"+forward\\\"", true)]
    [TestCase("     bind k \\\"+forward\\\"", false)]
    public void Load_ABindLine_KeepsItsEscapesAndAnyOtherLineLosesThem(string line, bool forward)
    {
        ConfigConsole console = new();

        console.Load(line);
        console.KeyDown("k");

        console.IsHeld(ViewerAction.FlyForward).ShouldBe(forward);
    }

    /// <remarks>
    /// The unescape on a ConVar line, and its order (B820 review 8, Sonnet): `\"` is made `"` first, then `\\` `\`, so
    /// `"a\\"` becomes `"a\"`, the value `a\`; the other order would end at `"a""` and the value `a`.
    /// </remarks>
    [Test]
    public void Load_ALineThatIsNotABind_UndoesItsEscapesQuoteFirst()
    {
        ConfigConsole console = new();

        console.Load($"{Alpha} \\\"5\\\"");
        console.ConVar(Alpha).ShouldBe("5");

        console.Load("cl_voice_filter \"a\\\\\"");
        console.ConVar("cl_voice_filter").ShouldBe("a\\");
    }

    /// <remarks>
    /// **A NUL ends the file** (FUN_180177310 returns null at a 0 byte and `Cmd_Exec_f` stops; B820 review 8, Fable); and a
    /// non-ASCII character starting a line after a break is skipped, a signed `char` below 0x20.
    /// </remarks>
    [Test]
    public void Load_AFileWithANul_StopsThereAndALineAfterABreakDropsItsNonAscii()
    {
        ConfigConsole console = new();

        console.Load($"{Alpha} 1\né{Alpha} 2\n\0\n{Alpha} 3");

        console.ConVar(Alpha).ShouldBe("2", "é was skipped, so the second line ran; the third is after the NUL");
    }

    /// <remarks>
    /// **A file that begins with a NUL runs nothing** (B820 review 9, Fable): `Cmd_Exec_f` loops while a line's `strlen` is
    /// above 0, and its first line is the NUL copied whatever it is.
    /// </remarks>
    [Test]
    public void Load_AFileThatBeginsWithANul_RunsNothing()
    {
        ConfigConsole console = new();

        console.Load($"\0\n{Alpha} 1");

        console.ConVar(Alpha).ShouldBeNull();
    }

    /// <remarks>
    /// **A NUL that stops a line's copy ends the file after that line runs** (B820 review 9): `Cmd_Exec_f` adds and executes
    /// the line in the buffer, then the reader's null return makes the next line empty and the loop ends.
    /// </remarks>
    [Test]
    public void Load_ANulInsideALine_RunsTheLineBeforeItAndNothingAfter()
    {
        ConfigConsole console = new();

        console.Load($"{Alpha} 1\0{Alpha} 2\n{Alpha} 3");

        console.ConVar(Alpha).ShouldBe("1");
    }

    /// <remarks>
    /// **The launch line runs its one-shots, as `stuffcmds` does** (B829; the owner's pick, "Run them like TF2"): TF2 runs
    /// every `+command` on the line once the configs are read, `+screenshot` included, where B820 review 9 had made them idle.
    /// An `exec` on the line runs its file's one-shots too, as everything `stuffcmds` adds runs. The viewer's replay of the
    /// line after a game folder is chosen (`oneShots: false`) is not a second launch, so it fires none. The control: the
    /// same text loaded as a config fires none (B820 review 1).
    /// </remarks>
    [Test]
    public void Stuff_AOneShot_FiresUnlessItIsAReplay()
    {
        ConfigConsole console = new() { FindConfig = path => path == "cfg/shot.cfg" ? "togglefullscreen"u8.ToArray() : null };
        List<ViewerAction> fired = [];
        console.Triggered += (_, e) => fired.Add(e.Action);

        console.Load("screenshot\nexec shot");
        fired.ShouldBeEmpty();

        console.Stuff("screenshot\nexec shot", oneShots: false);
        fired.ShouldBeEmpty();

        console.Stuff("screenshot\nexec shot", oneShots: true);
        fired.ShouldBe([ViewerAction.Screenshot, ViewerAction.FullScreen]);

        console.Load("screenshot");
        fired.Count.ShouldBe(2, "a config loaded after the line is no longer the line's");
    }

    /// <remarks>
    /// **A config a handler loads inside the launch line fires none, and the line's later one-shots still fire** (B829
    /// review 1, Fable): the flag is each load's own and restored after it. The control: the line's own two fire.
    /// </remarks>
    [Test]
    public void Stuff_ALoadFromAHandlerInsideTheLine_FiresNoneAndTheLineGoesOn()
    {
        ConfigConsole console = new();
        List<ViewerAction> fired = [];
        console.Triggered += (_, e) =>
        {
            fired.Add(e.Action);

            if (e.Action == ViewerAction.FullScreen)
            {
                console.Load("resetcamera");
            }
        };

        console.Stuff("togglefullscreen\nscreenshot", oneShots: true);

        fired.ShouldBe([ViewerAction.FullScreen, ViewerAction.Screenshot]);
    }

    /// <remarks>
    /// **A ConVar line sets `ArgS` with the engine's trim** (B823, `engine.dll` build 1007 FUN_180178310): a trailing non-ASCII character
    /// is stripped as a signed `char` below `' '`, a leading one kept whole, and two words are one value.
    /// </remarks>
    [TestCase("cl_voice_filter café", "caf")]
    [TestCase("cl_voice_filter éa", "éa")]
    [TestCase("cl_voice_filter \"café\"", "café")]
    [TestCase("cl_voice_filter a b", "a b")]
    public void Load_AConVarLine_SetsArgSTrimmedAsTheEngine(string line, string expected)
    {
        ConfigConsole console = new();

        console.Load(line);

        console.ConVar("cl_voice_filter").ShouldBe(expected);
    }

    /// <remarks>
    /// **An exec'd Latin-1 file reaches the tokenizer as its bytes** (B823 review 1, Sonnet): `exec` decodes through
    /// `Tf2ConfigFiles.Decode`, so `E9` — `é` in Latin-1, no UTF-8 character alone — is the lone byte U+DCE9 and the ConVar
    /// holds `63 61 66 E9 78`, as TF2's would. Replacement decoding would have set `caf�x`.
    /// </remarks>
    [Test]
    public void Exec_ALatin1File_SetsItsBytes()
    {
        ConfigConsole console = new()
        {
            FindConfig = path => path == "cfg/latin.cfg" ? System.Text.Encoding.Latin1.GetBytes("cl_voice_filter caféx") : null,
        };

        console.Load("exec latin");

        console.ConVar("cl_voice_filter").ShouldBe("caf\uDCE9x");
    }

    /// <remarks>
    /// **`Define` runs before the ConVar set's `ArgS`** (B823 review 1, Grok): `bind k screenshot` is three arguments to
    /// `Define`, not a value collapsed for a ConVar, and a bound viewer command that carries arguments still runs its
    /// command. With `bind` no longer taken by `Define`, this fails. The guard that once kept the collapse to ConVars was
    /// removed in review 2 (Sonnet, Grok): nothing past the set reads a non-ConVar's arguments, so no test could fail for it.
    /// </remarks>
    [Test]
    public void Load_ABindAndAMultiArgumentViewerCommand_RunTheirCommands()
    {
        ConfigConsole console = new();
        List<ViewerAction> fired = [];
        console.Triggered += (_, e) => fired.Add(e.Action);

        console.Load("bind k screenshot\nbind j \"screenshot now please\"");
        console.KeyDown("k");
        console.KeyDown("j");

        fired.ShouldBe([ViewerAction.Screenshot, ViewerAction.Screenshot]);
    }

    /// <remarks>
    /// **An alias body keeps a lone byte as its byte** (B823): `Cmd_Alias_f` (`engine.dll` build 1007 FUN_180168060) joins arguments 2 on
    /// with spaces, so `alias x cl_voice_filter éa b` stores the bytes `C3 20 A9 61 20 62` after the name, and running
    /// it sets those bytes — a space now inside the character — where the same line typed directly sets `éa b`. A
    /// spelling that re-encoded a lone byte (Latin-1's `Ã` is `C3 83`) would set different bytes.
    /// </remarks>
    [Test]
    public void Load_AnAliasWithANonAsciiWord_RunsItsBytesAgain()
    {
        ConfigConsole console = new();

        console.Load("cl_voice_filter éa b");
        console.ConVar("cl_voice_filter").ShouldBe("éa b", "the control is the line typed directly");

        console.Load("alias x cl_voice_filter éa b\nx");
        console.ConVar("cl_voice_filter").ShouldBe("\uDCC3 \uDCA9a b");
    }

    /// <remarks>
    /// **A launch line cut for a long command also says `Cbuf_AddText: buffer overflow`** (B830 review 3): `stuffcmds` adds
    /// through `engine.dll` build 1007 FUN_180166e30 (`engine/cmd.cpp`), which prints it when `AddText` returns false, after
    /// `InsertCommand`'s `Command too long` (`src/tier1/commandbuffer.cpp:124`). The control: a launch line that fits says
    /// neither.
    /// </remarks>
    [Test]
    public void Stuff_ALaunchLineWithACommandOf511Bytes_SaysTheBufferOverflowed()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };

        console.Stuff("toggle cl_showfps");
        log.Count("Cbuf_AddText: buffer overflow").ShouldBe(0, "the control");

        console.Stuff("toggle cl_showfps\nalias a " + new string('x', 600) + "\nfps_max 60");

        log.Count("WARNING: Command too long... ignoring!").ShouldBe(1);
        log.Count("Cbuf_AddText: buffer overflow").ShouldBe(1);
    }

    /// <remarks>
    /// **Each config line is its own `Cbuf_AddText`** (B830 review 4, Fable): `Cmd_Exec_f` (`engine.dll` build 1007
    /// FUN_1801672f0) adds every line through FUN_180166e30, so a refused line says `Cbuf_AddText: buffer overflow`, and the
    /// next line, added on its own, still runs. The control: the line after it sets `fps_max`.
    /// </remarks>
    [Test]
    public void Load_ALineWithACommandOf511Bytes_SaysTheBufferOverflowedAndRunsTheNextLine()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };

        console.Load("alias a " + new string('x', 600) + "\nfps_max 60");

        log.Count("WARNING: Command too long... ignoring!").ShouldBe(1);
        log.Count("Cbuf_AddText: buffer overflow").ShouldBe(1);
        console.ConVar("fps_max").ShouldBe("60", "the control: the next line is added on its own");
    }

    /// <remarks>
    /// **A load from a handler reports its own refusal, and the line that raised it reports none** (B830 review 5, Fable,
    /// Sonnet): both add text at depth 0, so a count compared before and after made the outer line say the overflow line
    /// for the inner one too. The control: the inner line's own refusal is reported.
    /// </remarks>
    [Test]
    public void Load_ALoadFromAHandlerWithACommandOf511Bytes_SaysTheBufferOverflowedOnce()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        console.ReadSettingsFrom(_ => null);
        bool loaded = false;
        console.ConVarSet += (_, _) =>
        {
            if (!loaded)
            {
                loaded = true;
                console.Load("alias a " + new string('x', 600));
            }
        };

        console.Load($"{Alpha} 1");

        loaded.ShouldBeTrue("the handler ran");
        log.Count("WARNING: Command too long... ignoring!").ShouldBe(1, "the control: the inner line is refused");
        log.Count("Cbuf_AddText: buffer overflow").ShouldBe(1);
    }

    /// <remarks>
    /// **An alias body's refusal is its own adder's to report, once** (B835; B830 review 6, Fable): `alias` joins its words
    /// with spaces, and `(` splits a word with no space in the line, so a 504-byte definition holds a 557-byte body, refused
    /// when `a` runs. `Cmd_ExecuteCommand` (`engine.dll` build 1007 FUN_1801678a0) adds an alias's body through
    /// FUN_180166e30, which says `Cbuf_AddText: buffer overflow` when `AddText` refuses; the exec'd file's line that a
    /// handler ran it inside does not say it again. The control: the body's command is refused.
    /// </remarks>
    [Test]
    public void Load_AnAliasBodyOf511BytesRunByAHandlerInsideAnExecdLine_SaysTheBufferOverflowedOnce()
    {
        RecordingLogger log = new();
        ConfigConsole console = new()
        {
            Log = log,
            FindConfig = path => path == "cfg/f.cfg" ? System.Text.Encoding.ASCII.GetBytes($"{Alpha} 1") : null,
        };
        console.ReadSettingsFrom(_ => null);
        string definition = "alias a " + string.Concat(Enumerable.Repeat(new string('w', 15) + "(", 31));
        definition.Length.ShouldBeLessThan(ConfigConsole.MaximumCommandLength - 1, "the definition itself fits");
        console.Load(definition);
        bool ran = false;
        console.ConVarSet += (_, _) =>
        {
            if (!ran)
            {
                ran = true;
                console.Load("a");
            }
        };

        console.Load("exec f.cfg");

        ran.ShouldBeTrue("the handler ran");
        log.Count("WARNING: Command too long... ignoring!").ShouldBe(1, "the control: the body's command is refused");
        log.Count("Cbuf_AddText: buffer overflow").ShouldBe(1);
    }

    /// <remarks>
    /// **A key whose bind runs an alias with a refused body says the overflow line once** (B835): the alias adds its body
    /// through FUN_180166e30 (`engine.dll` build 1007), which says `Cbuf_AddText: buffer overflow` for it. The key's own
    /// adder, FUN_180166500 from `Key_Event`, never refuses: no bind's text reaches 511 bytes. The control: a key whose
    /// alias fits runs it and says nothing.
    /// </remarks>
    [Test]
    public void KeyDown_ABindWhoseAliasBodyIsRefused_SaysTheBufferOverflowedOnce()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        console.Load("alias a " + string.Concat(Enumerable.Repeat(new string('w', 15) + "(", 31)));
        console.Load($"alias b {Alpha} 1\nbind k a\nbind j b");

        console.KeyDown("j").ShouldBeTrue("the control: the alias that fits ran");
        console.ConVar(Alpha).ShouldBe("1", "the control: its body ran");
        log.Count("Cbuf_AddText: buffer overflow").ShouldBe(0, "the control: an alias that fits");

        console.KeyDown("k");
        log.Count("WARNING: Command too long... ignoring!").ShouldBe(1);
        log.Count("Cbuf_AddText: buffer overflow").ShouldBe(1);
    }

    /// <summary>A command that queues <paramref name="size"/> bytes, its terminator included, and changes nothing seen.</summary>
    private static string Filler(int size) => "alias f " + new string('x', size - 1 - "alias f ".Length);

    /// <summary>Fillers that queue <paramref name="size"/> bytes in all, none over 400, one per line.</summary>
    private static string Fillers(int size)
    {
        List<string> lines = [];

        for (; size > 400; size -= 400)
        {
            lines.Add(Filler(400));
        }

        lines.Add(Filler(size));
        return string.Join('\n', lines);
    }

    /// <remarks>
    /// **The launch line fills the 8192-byte argument buffer, and the command that does not fit is refused with every
    /// command after it** (B834): `CCommandBuffer::InsertCommand` refuses `m_nArgSBufferSize + nCommandSize + 1 &gt;
    /// m_nMaxArgSBufferLength` (`src/tier1/commandbuffer.cpp:129-134`), 0x2000 in `engine.dll` x64 build 1007
    /// (FUN_180294fb0, FUN_180295740), and `stuffcmds` (FUN_1801683d0) adds the whole line at once through FUN_180166e30,
    /// which says `Cbuf_AddText: buffer overflow`. The control: the same line one byte shorter fills the buffer exactly and
    /// sets `fps_max`.
    /// </remarks>
    [TestCase(0, "60", 0)]
    [TestCase(1, null, 1)]
    public void Stuff_ALaunchLineAtTheArgumentBuffersEdge_RunsWhatFits(int over, string? fpsMax, int overflows)
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        string last = "fps_max 60";

        console.Stuff(Fillers(ConfigConsole.ArgumentBufferLength - (last.Length + 1) + over) + "\n" + last);

        console.ConVar("fps_max").ShouldBe(fpsMax);
        log.Count("Cbuf_AddText: buffer overflow").ShouldBe(overflows);
        log.Count("WARNING: Command too long... ignoring!").ShouldBe(0, "no command is too long");
    }

    /// <remarks>
    /// **An alias body fits only beside the rest of the text that ran it** (B834): `AddText` queues the whole text before
    /// any of it runs, so while `a` runs, the fillers after it still hold the buffer, and its body is refused when it does
    /// not fit beside them; the rest of that text still runs. The control: with 100 bytes fewer of fillers, it fits.
    /// </remarks>
    [TestCase(100, "1", 0)]
    [TestCase(0, null, 1)]
    public void Stuff_AnAliasBodyThatDoesNotFitBesideTheRestOfItsText_IsRefused(int room, string? alpha, int overflows)
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        string body = $"{Alpha} 1";
        console.Load($"alias a \"{body}\"");

        // `a` itself is dequeued before its body is added, so the fillers and `fps_max 60` are what is queued.
        console.Stuff("a\n" + Fillers(ConfigConsole.ArgumentBufferLength - (body.Length + 1) - 11 + 1 - room) + "\nfps_max 60");

        console.ConVar(Alpha).ShouldBe(alpha);
        console.ConVar("fps_max").ShouldBe("60", "the rest of the launch line runs");
        log.Count("Cbuf_AddText: buffer overflow").ShouldBe(overflows);
    }

    /// <remarks>
    /// **`wait` puts the rest of its text off to the next tick** (B832): `AddText` (`src/tier1/commandbuffer.cpp:235-240`;
    /// `engine.dll` x64 build 1007 FUN_1802952a0) adds `m_nWaitDelayTicks`, 1, to the tick of every command after it, and
    /// `Cbuf_Execute` (FUN_180166a10) runs one tick's commands a call. Text run between calls is the next call's tick, so the
    /// waited half runs at the second call after it. The control: before any call, only the first half has run.
    /// </remarks>
    [Test]
    public void Load_ALineWithAWait_RunsTheRestAtTheSecondExecute()
    {
        ConfigConsole console = new();

        console.Load($"{Alpha} 1; wait; {Alpha} 2");
        console.ConVar(Alpha).ShouldBe("1", "the control: the first half runs at once");

        console.Execute(waitAllowed: true);
        console.ConVar(Alpha).ShouldBe("1", "the waited half is a tick later");

        console.Execute(waitAllowed: true);
        console.ConVar(Alpha).ShouldBe("2");
    }

    /// <remarks>
    /// **`wait N` waits N ticks, a bare `wait` one, and `wait 0` none** (`int nDelay = pArgS ? atoi( pArgS ) :
    /// m_nWaitDelayTicks;`, `commandbuffer.cpp:237`), and the name is matched in any case (`Q_stricmp`).
    /// </remarks>
    [TestCase("wait 3", 4)]
    [TestCase("\"wait\" 3", 4)]
    [TestCase("WAIT", 2)]
    [TestCase("wait 0", 0)]
    [TestCase("wait x", 0)]
    public void Load_AWaitWithAnArgument_WaitsThatManyTicks(string wait, int executes)
    {
        ConfigConsole console = new();

        console.Load($"{Alpha} 1; {wait}; {Alpha} 2");

        for (int call = 0; call < executes; call++)
        {
            console.ConVar(Alpha).ShouldBe("1", $"before call {call + 1}");
            console.Execute(waitAllowed: true);
        }

        console.ConVar(Alpha).ShouldBe("2");
    }

    /// <remarks>
    /// **What waits goes after the rest of the text that ran it** (B832): an alias body is added while commands are
    /// processing, its first half ahead of the rest of its caller's text, its waited half at the later tick
    /// (`InsertCommand`, `commandbuffer.cpp:148-155`). So `w; tf_hud_target_id_alpha 3` leaves 3, then 2.
    /// </remarks>
    [Test]
    public void KeyDown_AnAliasWithAWait_RunsItsWaitedHalfAfterTheRestOfTheBind()
    {
        ConfigConsole console = new();
        console.Load($"alias w \"{Alpha} 1; wait; {Alpha} 2\"\nbind k \"w; {Alpha} 3\"");

        console.KeyDown("k").ShouldBeTrue();
        console.ConVar(Alpha).ShouldBe("3");

        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);
        console.ConVar(Alpha).ShouldBe("2");
    }

    /// <remarks>
    /// **Waited commands run in tick order, not the order they were added** (B832): `InsertCommandAtAppropriateTime`
    /// (`commandbuffer.cpp:95-105`) links each before the first with a later tick. So a set three ticks off, added first,
    /// lands after one a tick off: 1 after two calls, 3 after four.
    /// </remarks>
    [Test]
    public void Load_WaitsAddedOutOfTickOrder_RunInTickOrder()
    {
        ConfigConsole console = new();
        console.Load($"wait 3; {Alpha} 3\nwait; {Alpha} 1");

        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);
        console.ConVar(Alpha).ShouldBe("1");

        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);
        console.ConVar(Alpha).ShouldBe("3");
    }

    /// <remarks>
    /// **A text's waited commands are linked before any of it runs** (B832 review 1, Sonnet): `AddText` inserts the whole
    /// text first, so `x`'s waited `2`, added when `x` runs, goes after the key's own waited `3` at the same tick
    /// (`InsertCommandAtAppropriateTime` links before the first LATER tick). So 2 is last.
    /// </remarks>
    [Test]
    public void KeyDown_AnAliasWaitingAsLongAsItsCaller_RunsItsWaitedHalfLast()
    {
        ConfigConsole console = new();
        console.Load($"alias x \"wait 5; {Alpha} 2\"\nbind k \"x; wait 5; {Alpha} 3\"");

        console.KeyDown("k").ShouldBeTrue();

        for (int call = 0; call < 6; call++)
        {
            console.Execute(waitAllowed: true);
        }

        console.ConVar(Alpha).ShouldBe("2");
    }

    /// <remarks>
    /// **A key's text is linked by tick, so a negative `wait` runs what follows it first** (B832 review 1, Sonnet):
    /// `Key_Event` adds while nothing is processing, and `InsertCommand` then links each command by its tick
    /// (`commandbuffer.cpp:148-151`). A config line is added while its `exec` is processing, so it runs in order
    /// (`InsertImmediateCommand`). The control: the same text as a config line leaves 2.
    /// </remarks>
    [Test]
    public void KeyDown_ANegativeWait_RunsTheCommandAfterItFirst()
    {
        ConfigConsole control = new();
        control.Load($"{Alpha} 1; wait -1; {Alpha} 2");
        control.ConVar(Alpha).ShouldBe("2", "the control: a config line runs in order");

        ConfigConsole console = new();
        console.Load($"bind k \"{Alpha} 1; wait -1; {Alpha} 2\"");

        console.KeyDown("k").ShouldBeTrue();

        console.ConVar(Alpha).ShouldBe("1");
    }

    /// <remarks>
    /// **A key's negative-`wait` command runs ahead of a command already due, the rest after it** (B832 review 2, Fable,
    /// Grok): `InsertCommand` links the key's commands by tick among those queued (`commandbuffer.cpp:95-105`), so with
    /// `9` due at the current tick, `wait -1; 2` runs 2 (a tick earlier), then 9, and 9 is left.
    /// </remarks>
    [Test]
    public void KeyDown_ANegativeWaitBesideADueCommand_RunsInTickOrder()
    {
        ConfigConsole console = new();
        console.Load($"bind k \"wait -1; {Alpha} 2\"\nwait; {Alpha} 9");
        console.Execute(waitAllowed: true);

        console.KeyDown("k").ShouldBeTrue();

        console.ConVar(Alpha).ShouldBe("9");
    }

    /// <remarks>
    /// **A command a key's own alias puts off to a tick the key's text also holds runs after the key's** (B832 review 3,
    /// Fable): the key's whole text is linked first, `al` at T-1 and `9` at T, and `al`'s `2`, put off to T when it runs,
    /// is linked after `9` (`InsertCommandAtAppropriateTime`), so 2 is left.
    /// </remarks>
    [Test]
    public void KeyDown_AnAliasPuttingOffToATickItsKeyHolds_RunsAfterTheKeys()
    {
        ConfigConsole console = new();
        console.Load($"alias al \"wait; {Alpha} 2\"\nbind k \"wait -1; al; wait; {Alpha} 9\"");
        List<string> sets = [];
        console.ConVarSet += (_, set) => sets.Add(set.Value);

        console.KeyDown("k").ShouldBeTrue();

        sets.ShouldBe(["9", "2"], "both ran, 9 first (B832 review 4, Grok)");
    }

    /// <remarks>
    /// **A key a handler presses inside a command runs inside it, in order** (B832 review 4, Fable, Sonnet): only a key
    /// pressed from outside every command is linked by tick, as `Key_Event`'s is. Inside a config line, `1; wait -1; 2`
    /// runs in order and leaves 2. The control: the same key pressed on its own leaves 1.
    /// </remarks>
    [Test]
    public void KeyDown_FromAHandlerInsideACommand_RunsInOrder()
    {
        ConfigConsole control = new();
        control.Load($"bind k \"{Alpha} 1; wait -1; {Alpha} 2\"");
        control.KeyDown("k");
        control.ConVar(Alpha).ShouldBe("1", "the control: on its own, by tick");

        ConfigConsole console = new();
        console.Load($"bind k \"{Alpha} 1; wait -1; {Alpha} 2\"");
        console.ReadSettingsFrom(_ => null);
        bool pressed = false;
        List<string> alpha = [];
        console.ConVarSet += (_, set) =>
        {
            if (set.Name == Alpha)
            {
                alpha.Add(set.Value);
            }
            else if (!pressed && set.Name == "fps_max")
            {
                pressed = true;
                console.KeyDown("k");
            }
        };

        console.Load("fps_max 60");

        pressed.ShouldBeTrue("the control: the handler ran");
        alpha.ShouldBe(["1", "2"], "both ran, in order (B832 review 5, Grok)");
    }

    /// <remarks>
    /// **`Execute` does nothing inside a command the console runs** (B832 review 4, Fable, Sonnet): that command stands in
    /// for one `Cbuf_Execute` already running, and `IsProcessingCommands` guards the call. A handler inside a config line
    /// calling it twice leaves a set waited a tick unrun. The control: two calls after the line run it.
    /// </remarks>
    [Test]
    public void Execute_FromAHandlerInsideACommand_DoesNothing()
    {
        ConfigConsole console = new();
        console.ReadSettingsFrom(_ => null);
        console.Load($"wait; {Alpha} 2");
        bool called = false;
        string? alpha = null;
        console.ConVarSet += (_, set) =>
        {
            if (set.Name == Alpha)
            {
                alpha = set.Value;
            }
            else if (!called && set.Name == "fps_max")
            {
                called = true;
                console.Execute(waitAllowed: true);
                console.Execute(waitAllowed: true);
            }
        };

        console.Load("fps_max 60");
        called.ShouldBeTrue("the control: the handler ran");
        alpha.ShouldBeNull();

        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);
        alpha.ShouldBe("2", "the control: two calls after the line run it");
    }

    /// <remarks>
    /// **A throw drops the rest of a key's text due now** (B832 review 4, Fable, Sonnet): as a run text's rest is, rather
    /// than left queued to fire at some later run, and its bytes with it. The controls: its first command ran, and what
    /// it put off to a later tick still runs.
    /// </remarks>
    [Test]
    public void KeyDown_AHandlerThatThrows_DropsTheRestOfTheKeysText()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        console.Load($"bind k \"{Alpha} 1; {Alpha} 2; wait; {Alpha} 3\"");
        List<string> sets = [];
        EventHandler<ConVarSetEventArgs> throwing = (_, _) => throw new InvalidOperationException("a handler failed");
        console.ConVarSet += (_, set) => sets.Add(set.Value);
        console.ConVarSet += throwing;

        Should.Throw<InvalidOperationException>(() => console.KeyDown("k"));
        console.ConVarSet -= throwing;
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        // Its first command ran, the rest due now was dropped, and what it put off to a later tick still ran (B832 review 5,
        // Fable).
        sets.ShouldBe(["1", "3"], "1 ran, 2 was dropped, 3 (a tick later) still ran");

        // Its bytes went with it (B832 review 5, Sonnet): a launch line of exactly 8192 bytes still fits.
        console.Stuff(Fillers(ConfigConsole.ArgumentBufferLength));
        log.Count("Cbuf_AddText: buffer overflow").ShouldBe(0);
    }

    /// <remarks>
    /// **A throw drops the key's own text only** (B832 review 6, Fable): a config's command due at the same run stays
    /// queued and runs at the next `Execute`. The control: the key's command ran before the throw.
    /// </remarks>
    [Test]
    public void KeyDown_AHandlerThatThrows_KeepsOtherTextsQueuedCommands()
    {
        ConfigConsole console = new();
        console.Load($"bind k \"wait -1; {Alpha} 1\"\nwait; fps_max 60");
        console.Execute(waitAllowed: true);
        bool threw = false;
        EventHandler<ConVarSetEventArgs> throwing = (_, set) =>
        {
            if (set.Name == Alpha)
            {
                threw = true;
                throw new InvalidOperationException("a handler failed");
            }
        };
        console.ConVarSet += throwing;

        Should.Throw<InvalidOperationException>(() => console.KeyDown("k"));
        console.ConVarSet -= throwing;
        threw.ShouldBeTrue("the control: the key's command ran first, as its tick is earlier");
        console.Execute(waitAllowed: true);

        console.ConVar("fps_max").ShouldBe("60");
    }

    /// <remarks>
    /// **A key's commands share one set of budgets** (B832 review 4, Fable, Sonnet): run from the queue one by one, they
    /// still run under the budget the key's command began, as when they ran as one text, so a bind of many fanning-out
    /// aliases says the alias budget's line once.
    /// </remarks>
    [Test]
    public void KeyDown_ABindOfManyFanningOutAliases_SpendsOneBudget()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        console.Load("alias a \"a;a;a;a\"\nbind k \"a;a;a\"");

        console.KeyDown("k");

        log.Count($"more than {ConfigConsole.MaximumAliasRuns} alias runs").ShouldBe(1);
    }

    /// <remarks>
    /// **A throw drops what the load added that is due now, and keeps what it put off** (B840): `al` links its `1` (T-1)
    /// ahead of the line's `2` (T), then its `fps_max` set throws. Both are the load's and due, so both go, their bytes with
    /// them; the line's `3`, put off to T+3, stays queued and runs at its tick.
    /// </remarks>
    [Test]
    public void Load_AThrowInsideAnAlias_DropsWhatTheLoadAddedDueNow()
    {
        ConfigConsole console = new();
        console.Load($"alias al \"fps_max 1; wait; {Alpha} 1\"");
        console.ReadSettingsFrom(_ => null);
        List<string> alpha = [];
        EventHandler<ConVarSetEventArgs> throwing = (_, set) =>
        {
            if (set.Name == "fps_max")
            {
                throw new InvalidOperationException("a handler failed");
            }
        };
        console.ConVarSet += (_, set) =>
        {
            if (set.Name == Alpha)
            {
                alpha.Add(set.Value);
            }
        };
        console.ConVarSet += throwing;

        Should.Throw<InvalidOperationException>(() => console.Load($"wait -2; al; wait 2; {Alpha} 2; wait 3; {Alpha} 3"));
        console.ConVarSet -= throwing;

        for (int call = 0; call < 4; call++)
        {
            console.Execute(waitAllowed: true);
        }

        alpha.ShouldBe(["3"], "the 1 and 2 due now were dropped; the 3 put off ran at its tick");
    }

    /// <remarks>
    /// **A command a running text's alias puts off is linked among that text's commands by tick** (B840, B832 review 3,
    /// Fable): a config line is added while its `exec` processes, so `InsertImmediateCommand` puts `al` (T-2) and `2` (T)
    /// at the queue's front, and `al`'s `1`, put off to T-1, is linked by `InsertCommandAtAppropriateTime` scanning from
    /// the head: before `2`. So 1 runs, then 2, and 2 is left.
    /// </remarks>
    [Test]
    public void Load_AnAliasPuttingOffAheadOfItsLinesNextCommand_RunsBeforeIt()
    {
        ConfigConsole console = new();
        console.Load($"alias al \"wait; {Alpha} 1\"");
        List<string> sets = Recorded(console);

        console.Load($"wait -2; al; wait 2; {Alpha} 2");
        console.Execute(waitAllowed: true);

        sets.ShouldBe(["1", "2"], "both ran, 1 first (B840 review 1, Sonnet)");
    }

    /// <remarks>
    /// **The innermost running text's commands come first in the queue** (B840 review 1, Sonnet): `InsertImmediateCommand`
    /// puts a text added while a command runs before the outer text's commands left. The line runs `A` (T-4) before its
    /// `3` (T); `A` runs `B` (T-6) before its `2` (T-4); `B`'s `1`, put off to T-5, is linked before `A`'s `2`, the first
    /// later tick from the head, and not before the line's `3`. So 1, 2, 3; and every byte is released after.
    /// </remarks>
    [Test]
    public void Load_ACommandPutOffInsideTwoRunningTexts_IsLinkedAmongTheInnermosts()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        console.Load($"alias B \"wait; {Alpha} 1\"\nalias A \"wait -2; B; wait 2; {Alpha} 2\"");
        List<string> sets = Recorded(console);

        console.Load($"wait -4; A; wait 4; {Alpha} 3");
        console.Execute(waitAllowed: true);

        sets.ShouldBe(["1", "2", "3"]);
        console.Stuff(Fillers(ConfigConsole.ArgumentBufferLength));
        log.Count("Cbuf_AddText: buffer overflow").ShouldBe(0, "every queued byte was released");
    }

    /// <remarks>
    /// **A command put off to a tick equal to a running command's goes past it** (B840 review 1, Sonnet):
    /// `InsertCommandAtAppropriateTime` stops at the first LATER tick, so `al`'s `1` at T goes after the line's `2` at T.
    /// </remarks>
    [Test]
    public void Load_ACommandPutOffToARunningCommandsTick_RunsAfterIt()
    {
        ConfigConsole console = new();
        console.Load($"alias al \"wait; {Alpha} 1\"");
        List<string> sets = Recorded(console);

        console.Load($"wait -1; al; wait; {Alpha} 2");
        console.Execute(waitAllowed: true);

        sets.ShouldBe(["2", "1"]);
    }

    /// <remarks>
    /// **A command put off ahead of the outer text's next sits ahead of its own text's later ones** (B840 review 1, Fable):
    /// `InsertImmediateCommand` links before the next-command handle, which does not move while one text is added, so
    /// `al`'s `x` and `y` (T-5) go before the line's `4` (T) and its `2`, put off to T-3, is linked from the head before
    /// `4` too, between them. So 1, 2, 3, 4.
    /// </remarks>
    [Test]
    public void Load_AnAliasPuttingOffBetweenItsOwnCommands_RunsInTheEnginesOrder()
    {
        ConfigConsole console = new();
        console.Load($"alias al \"{Alpha} 1; wait 2; {Alpha} 2; wait -2; {Alpha} 3\"");
        List<string> sets = Recorded(console);

        console.Load($"wait -5; al; wait 5; {Alpha} 4");

        sets.ShouldBe(["1", "2", "3", "4"]);
    }

    /// <remarks>
    /// **Only the queue's head is taken, so a command put off at the head holds back the ones behind it** (B840 review 1,
    /// Fable): `DequeueNextCommand` stops at the head when it is not due (`commandbuffer.cpp:302-305`). With nothing else
    /// queued, `1; wait 2; 2; wait -2; 3` queues 1 (T), 2 (T+2) and 3 (T) in that order, so 1 runs now and 2 and 3 at T+2.
    /// </remarks>
    [Test]
    public void Load_ACommandBehindOnePutOff_WaitsForIt()
    {
        ConfigConsole console = new();
        List<string> sets = Recorded(console);

        console.Load($"{Alpha} 1; wait 2; {Alpha} 2; wait -2; {Alpha} 3");
        sets.ShouldBe(["1"], "the control: 3 is behind 2");

        for (int call = 0; call < 3; call++)
        {
            console.Execute(waitAllowed: true);
        }

        sets.ShouldBe(["1", "2", "3"]);
    }

    /// <remarks>
    /// **Aliases that put each other off run from the one queue, not one inside another** (B840 review 1, Fable): TF2's
    /// queue is flat, so `r` and `s` taking turns never nest on the stack, and they end at the alias depth bound (B820)
    /// where a run of each inside the last overflowed it. The control: the turns ran, setting `fps_max`.
    /// </remarks>
    [Test]
    public void Load_TwoAliasesPuttingEachOtherOff_EndWithoutNesting()
    {
        ConfigConsole console = new();
        console.Load("alias r \"wait -2; s; wait 2; fps_max 1\"\nalias s \"wait; r\"");

        console.Load("r");

        console.ConVar("fps_max").ShouldBe("1");
    }

    /// <remarks>
    /// **A `wait` loop runs every tick for ever** (B840 review 2, Fable): `CCommandBuffer` has no depth or budget, so
    /// `alias lp "x; wait; lp"` runs once a `Cbuf_Execute`; one put off past the run is a command of its own, at depth 0
    /// with budgets of its own. It runs once at the load and once more for each call after the first: 5000 in 5000
    /// calls, past the alias depth bound of 20 and the alias budget of 4096.
    /// </remarks>
    [Test]
    public void Execute_AWaitLoop_RunsEveryTickPastTheDepthBound()
    {
        ConfigConsole console = new();
        console.Load($"alias lp \"incrementvar {Alpha} 0 100000 1; wait; lp\"");
        console.Load($"{Alpha} 0\nlp");

        // Past the alias budget too (B840 review 3, Sonnet): 5000 runs, beyond 4096 alias runs carried on one budget.
        for (int call = 0; call < 5000; call++)
        {
            console.Execute(waitAllowed: true);
        }

        console.ConVar(Alpha).ShouldBe("5000.000000", "incrementvar writes its result as %f");
    }

    /// <remarks>
    /// **A load stands where TF2's `exec` stands: linked by tick behind what is queued, at the tick it runs at** (B840
    /// review 2, Fable): with `2` (T+2) and `3` (T, held behind it) queued and the tick at T+2, the load's `wait 2; 4` is
    /// added at T+2 after both run, so `4` waits for T+4. Added at the tick `3` left behind (T), it would land at T+2 and
    /// run inside the load.
    /// </remarks>
    [Test]
    public void Load_BehindQueuedCommands_AddsItsTextAtTheTickItRunsAt()
    {
        ConfigConsole console = new();
        List<string> sets = Recorded(console);
        console.Load($"{Alpha} 1; wait 2; {Alpha} 2; wait -2; {Alpha} 3");
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        console.Load($"wait 2; {Alpha} 4");
        sets.ShouldBe(["1", "2", "3"], "the control: 4 waits two more ticks");

        // Not a tick early either (B840 review 3, Grok): the calls run T+2 and T+3 with nothing, then T+4.
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);
        sets.ShouldBe(["1", "2", "3"], "nothing at T+2 or T+3");
        console.Execute(waitAllowed: true);
        sets.ShouldBe(["1", "2", "3", "4"]);
    }

    /// <remarks>
    /// **What an alias queued ahead of a load puts off is linked behind the load** (B840 review 2, Fable): TF2's queue is
    /// `2`, `al`, then the `exec`; `al` links its `9` at T+2 after the `exec`, so 2, 3 (the load), 9.
    /// </remarks>
    [Test]
    public void Load_BehindAnAliasThatPutsOff_RunsBeforeWhatItPutOff()
    {
        ConfigConsole console = new();
        console.Load($"alias al \"wait 2; {Alpha} 9\"");
        List<string> sets = Recorded(console);
        console.Load($"{Alpha} 1; wait 2; {Alpha} 2; wait -2; al");
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        console.Load($"{Alpha} 3");

        sets.ShouldBe(["1", "2", "3", "9"]);
    }

    /// <remarks>
    /// **A throw from a command queued ahead of a load drops the load with it** (B840 review 3, Fable): the command that
    /// stands for the load is the load's, due now, so it goes; a later `Execute` does not run the load's text.
    /// </remarks>
    [Test]
    public void Load_BehindACommandThatThrows_IsDroppedWithIt()
    {
        ConfigConsole console = new();
        console.Load("bind k \"wait; fps_max 1\"");
        List<string> sets = Recorded(console);
        console.KeyDown("k");
        console.Execute(waitAllowed: true);
        EventHandler<ConVarSetEventArgs> throwing = (_, set) =>
        {
            if (set.Name == "fps_max")
            {
                throw new InvalidOperationException("a handler failed");
            }
        };
        console.ConVarSet += throwing;

        Should.Throw<InvalidOperationException>(() => console.Load($"{Alpha} 5"));
        console.ConVarSet -= throwing;
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        sets.ShouldBeEmpty("the load's text never ran");
    }

    /// <remarks>
    /// **A text's commands put off to one tick share one set of budgets; each later tick has its own** (B840 reviews 3 and
    /// 4, Fable): one `b` is 585 alias runs (73 of `b`, `c`, `d` and 512 of `e`, each of which adds 1). Eight put off to
    /// one tick share the 4096 alias runs and are cut short of 4096 adds, as a text run at once is; eight each a tick apart
    /// are eight runs, each within its budget, and add 4096 with no budget line. The engine has no budget; the viewer's is
    /// per run (B820).
    /// </remarks>
    [TestCase("wait; b; b; b; b; b; b; b; b", 2, true, "3584.000000")]
    [TestCase("wait; b; wait; b; wait; b; wait; b; wait; b; wait; b; wait; b; wait; b", 9, false, "4096.000000")]
    public void Load_PutOffAliasesFanningOut_ShareABudgetPerTick(string text, int calls, bool cut, string adds)
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        console.Load(
            $"alias e \"incrementvar {Alpha} 0 100000 1\"\nalias d \"e;e;e;e;e;e;e;e\"\nalias c \"d;d;d;d;d;d;d;d\"\nalias b \"c;c;c;c;c;c;c;c\"\n{Alpha} 0");

        console.Load(text);

        for (int call = 0; call < calls; call++)
        {
            console.Execute(waitAllowed: true);
        }

        (log.Count($"more than {ConfigConsole.MaximumAliasRuns} alias runs") > 0).ShouldBe(cut);
        // Cut at run 4097, the eighth `b`'s first `c`: seven whole `b`s, 3584 adds (B840 review 5, Fable).
        console.ConVar(Alpha).ShouldBe(adds);
    }

    /// <remarks>
    /// **A config's TF2 one-shot put off by `wait` runs at its tick** (B841): waited two ticks, it acts at the third
    /// `Execute` and not before, as TF2 runs it during the session; at the load itself it does nothing (B820 review 1). The
    /// viewer's own names are unknown to TF2, and TF2 only prints a bare ConVar, so they do nothing at any tick.
    /// `thirdperson` is a cheat command, refused without `sv_cheats` (B841 review 2, Fable). A buffered `+jump`, `+attack`
    /// or `+attack2` only sets its button: the spectator's camera answers a KEY whose binding is exactly that, in
    /// `ClientModeShared::HandleSpectatorKeyInput` (`clientmode_shared.cpp:747-761`), and moves nothing for a config's
    /// (review 8, Fable).
    /// </remarks>
    [TestCase("screenshot", 1)]
    [TestCase("demo_togglepause", 1)]
    [TestCase("firstperson", 1)]
    [TestCase("+jump", 0)]
    [TestCase("+attack", 0)]
    [TestCase("+attack2", 0)]
    [TestCase("sv_cheats 1; thirdperson", 1)]
    [TestCase("thirdperson", 0)]
    [TestCase("togglefullscreen", 0)]
    [TestCase("resetcamera", 0)]
    [TestCase("opendemo", 0)]
    [TestCase("resetspeed", 0)]
    [TestCase("mat_surfacecolours", 0)]
    [TestCase("cl_showfps", 0)]
    public void Execute_AConfigsOneShotPutOffByWait_RunsAtItsTickWhenTf2HasIt(string command, int raised)
    {
        ConfigConsole console = new();
        int triggered = 0;
        console.Triggered += (_, _) => triggered++;

        console.Load($"{command}; wait 2; {command}");
        triggered.ShouldBe(0, "the control: a config's one-shot does nothing at the load");
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);
        triggered.ShouldBe(0, "not before its tick");
        console.Execute(waitAllowed: true);

        triggered.ShouldBe(raised);
    }

    /// <remarks>
    /// **A config's waited `screenshot` takes none while a demo plays back, unless `cl_playback_screenshots` allows it**
    /// (B841 review 9, Fable): `engine.dll` build 1007 FUN_180091070 returns at once while the demo player plays back and
    /// the ConVar is 0, its default. The control: with no demo, it takes one.
    /// </remarks>
    [TestCase(false, "", 1)]
    [TestCase(true, "", 0)]
    [TestCase(true, "cl_playback_screenshots 1; ", 1)]
    public void Execute_AWaitedScreenshotWhileADemoPlaysBack_IsTakenOnlyWhenAllowed(bool playingBack, string allow, int raised)
    {
        ConfigConsole console = new() { PlayingBack = () => playingBack };
        int triggered = 0;
        console.Triggered += (_, _) => triggered++;

        console.Load($"{allow}wait; screenshot");
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        triggered.ShouldBe(raised);
    }

    /// <remarks>
    /// **A config's waited `firstperson` or `thirdperson` changes nothing on a SourceTV demo** (B841 review 9, Fable,
    /// Sonnet): TF2 draws it through the HLTV camera, which no branch of reads the third-person flag those two commands
    /// flip (`view.cpp:660-662`, `hltvcamera.cpp:510-545`). The control: on a POV demo they act.
    /// </remarks>
    [TestCase("firstperson", false, 1)]
    [TestCase("firstperson", true, 0)]
    [TestCase("sv_cheats 1; thirdperson", false, 1)]
    [TestCase("sv_cheats 1; thirdperson", true, 0)]
    public void Execute_AWaitedViewCommandOnASourceTvDemo_ChangesNothing(string command, bool hltv, int raised)
    {
        ConfigConsole console = new() { HltvPlayback = () => hltv };
        int triggered = 0;
        console.Triggered += (_, _) => triggered++;

        console.Load($"wait; {command}");
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        triggered.ShouldBe(raised);
    }

    /// <remarks>
    /// **A key's run takes a waited config command off the queue at its tick, and it still acts** (B841 review 1, Fable):
    /// `Key_Event` only adds text and the next `Cbuf_Execute` runs both (`commandbuffer.cpp:148-151, 294-329`), so whichever
    /// call runs it, the `screenshot` is the config's put-off one. A load after it is put off by nothing, and does nothing.
    /// </remarks>
    [Test]
    public void KeyDown_BetweenFramesAtAWaitedConfigOneShotsTick_RunsItAndItActs()
    {
        ConfigConsole console = new();
        int triggered = 0;
        console.Triggered += (_, _) => triggered++;

        console.Load("bind w +forward; wait; screenshot");
        console.Execute(waitAllowed: true);
        console.KeyDown("w");
        triggered.ShouldBe(1);

        console.Execute(waitAllowed: true);
        console.Load("screenshot");

        triggered.ShouldBe(1, "a later load's one-shot still does nothing");
    }

    /// <remarks>
    /// **What a put-off command adds is put off too** (B841 review 2, Fable, Sonnet): an alias body and an `exec`'s lines run
    /// at the waited command's tick, so their one-shot acts there. Not before: nothing at the load, nor at the first run.
    /// </remarks>
    [TestCase("alias x screenshot; wait 2; x")]
    [TestCase("wait 2; exec shot")]
    public void Execute_AWaitedAliasOrExecHoldingAOneShot_ActsAtItsTick(string text)
    {
        ConfigConsole console = new() { FindConfig = path => path == "cfg/shot.cfg" ? "screenshot"u8.ToArray() : null };
        int triggered = 0;
        console.Triggered += (_, _) => triggered++;

        console.Load(text);
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);
        triggered.ShouldBe(0, "not before its tick");
        console.Execute(waitAllowed: true);

        triggered.ShouldBe(1);
    }

    /// <remarks>
    /// **A one-shot that runs inside the load does nothing, however it got there** (B841 review 2, Fable, Sonnet): a `wait 0`
    /// or a negative `wait` does not put it past the load's run, and nor does a `wait` inside an alias that a negative one
    /// brought back into it. `CCommandBuffer` runs all three while the load is processing.
    /// </remarks>
    [TestCase("wait 0; screenshot")]
    [TestCase("wait -1; screenshot")]
    [TestCase("alias x \"wait; screenshot\"; wait -3; x")]
    public void Load_AOneShotRunInsideTheLoadAfterAWait_DoesNothing(string text)
    {
        ConfigConsole console = new();
        int triggered = 0;
        console.Triggered += (_, _) => triggered++;

        console.Load(text);

        for (int run = 0; run < 5; run++)
        {
            console.Execute(waitAllowed: true);
        }

        triggered.ShouldBe(0);
    }

    /// <remarks>
    /// **A reread of the configs fires no one-shot, waited or not** (B841 reviews 2 and 3, Fable, Sonnet): like the launch
    /// line's replay it is no second launch (B829), so the first read's waited one-shot fires once. The control: read twice
    /// as two loads, it fires twice.
    /// </remarks>
    [TestCase(true, 1)]
    [TestCase(false, 2)]
    public void Load_ARereadOfAWaitedOneShot_FiresOnlyTheFirstReads(bool reread, int raised)
    {
        ConfigConsole console = new();
        int triggered = 0;
        console.Triggered += (_, _) => triggered++;

        console.Load("wait; screenshot");
        console.Load("wait; screenshot", replaying: reread);
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        triggered.ShouldBe(raised);
    }

    /// <remarks>
    /// **A reread after the first read's waited one-shot ran does not run it again** (B841 review 3, Fable): a folder chosen
    /// late in the session reads the configs again, and the one-shot it queues is the reread's.
    /// </remarks>
    [Test]
    public void Load_ARereadAfterTheWaitedOneShotRan_DoesNotFireItAgain()
    {
        ConfigConsole console = new();
        int triggered = 0;
        console.Triggered += (_, _) => triggered++;

        console.Load("wait; screenshot");
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);
        triggered.ShouldBe(1, "the control: the first read's fired");

        console.Load("wait; screenshot", replaying: true);
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        triggered.ShouldBe(1);
    }

    /// <remarks>
    /// **A load from a handler leaves the waited command's later lines put off** (B841 review 3, Sonnet): the waited
    /// `exec`'s second line still acts, though a handler loaded a config at the first. Two restores hold it, `AtStartup`'s
    /// and `RunEntry`'s, so it reddens only with both removed (review 4, Fable).
    /// </remarks>
    [Test]
    public void Load_FromAHandlerInsideAWaitedExecsFirstLine_TheSecondLineStillActs()
    {
        ConfigConsole console = new() { FindConfig = path => path == "cfg/shot.cfg" ? "screenshot\nscreenshot"u8.ToArray() : null };
        int triggered = 0;
        console.Triggered += (_, _) =>
        {
            if (++triggered == 1)
            {
                console.Load("cl_showfps 0");
            }
        };

        console.Load("wait; exec shot");
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        triggered.ShouldBe(2);
    }

    /// <remarks>
    /// **`thirdperson`'s cheat check is made when it runs** (B841 review 3, Sonnet), as `Cmd_ExecuteCommand` (`engine.dll`
    /// build 1007 FUN_1801678a0) makes it: cheats on at the load and off by its tick refuse it. The control: left on, it
    /// acts.
    /// </remarks>
    [TestCase(true, 0)]
    [TestCase(false, 1)]
    public void Execute_AWaitedThirdPersonWithCheatsTurnedOffBeforeItsTick_IsRefused(bool turnedOff, int raised)
    {
        ConfigConsole console = new();
        int triggered = 0;
        console.Triggered += (_, _) => triggered++;

        console.Load("sv_cheats 1; wait 2; thirdperson");
        console.Execute(waitAllowed: true);

        if (turnedOff)
        {
            console.Load("sv_cheats 0");
        }

        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        triggered.ShouldBe(raised);
    }

    /// <remarks>
    /// **A config a handler loads inside a waited command is put off by nothing** (B841 review 1, Fable, Gemini): its own
    /// one-shot does nothing at its load, though the command whose handler loaded it was put off.
    /// </remarks>
    [Test]
    public void Load_FromAHandlerInsideAWaitedOneShot_ItsOneShotsDoNothing()
    {
        ConfigConsole console = new();
        int triggered = 0;
        console.Triggered += (_, _) =>
        {
            if (++triggered == 1)
            {
                console.Load("screenshot");
            }
        };

        console.Load("wait; screenshot");
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        triggered.ShouldBe(1);
    }

    /// <remarks>
    /// **The viewer's replay of the launch line fires no one-shot, waited or not** (B829, B841 review 1, Sonnet): it is no
    /// second launch. The launch itself fires both.
    /// </remarks>
    [TestCase(true, 2)]
    [TestCase(false, 0)]
    public void Stuff_AWaitedOneShot_FiresOnTheLaunchAndNotOnTheReplay(bool oneShots, int raised)
    {
        ConfigConsole console = new();
        int triggered = 0;
        console.Triggered += (_, _) => triggered++;

        console.Stuff("screenshot\nwait\nscreenshot", oneShots);
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        triggered.ShouldBe(raised);
    }

    /// <remarks>
    /// **A set announces the value it replaced** (B839, review 1, Sonnet), as the console read it just before, under the
    /// ConVar's own name whatever case it was typed in: the first set replaced nothing (its default), the second the
    /// first's 3.
    /// </remarks>
    [Test]
    public void KeyDown_ASetAfterAnother_AnnouncesTheValueItReplaced()
    {
        ConfigConsole console = new();
        List<(string Name, string Value, string? Before)> sets = [];
        console.ConVarSet += (_, set) => sets.Add((set.Name, set.Value, set.Before));
        console.Load("bind j \"HUD_DeathNotice_Time 3\"\nbind k \"hud_deathnotice_time 9\"");

        console.KeyDown("j");
        console.KeyDown("k");

        sets.ShouldBe([("hud_deathnotice_time", "3", null), ("hud_deathnotice_time", "9", "3")]);
    }

    /// <summary>The settings attached, and every value set to <see cref="Alpha"/> recorded in order.</summary>
    private static List<string> Recorded(ConfigConsole console)
    {
        console.ReadSettingsFrom(_ => null);
        List<string> sets = [];
        console.ConVarSet += (_, set) =>
        {
            if (set.Name == Alpha)
            {
                sets.Add(set.Value);
            }
        };

        return sets;
    }

    /// <remarks>
    /// **A command runs at its own tick, so a `wait` in what it runs counts from there** (B832 review 2, Fable, Sonnet):
    /// `DequeueNextCommand` sets `m_nCurrentTick` to the command's tick (`commandbuffer.cpp:307`). A key's `wait -1; al`
    /// runs `al` a tick early, and its `wait` puts `2` back to the key's own tick, which the same `Cbuf_Execute` runs.
    /// </remarks>
    [Test]
    public void KeyDown_AWaitInsideANegativeWaitsCommand_CountsFromItsTick()
    {
        ConfigConsole console = new();
        console.Load($"alias al \"wait; {Alpha} 2\"\nbind k \"wait -1; al\"");

        console.KeyDown("k").ShouldBeTrue();

        console.ConVar(Alpha).ShouldBe("2");
    }

    /// <remarks>
    /// **A command run from the queue can wait again** (B832): it runs at its tick, and a `wait` in its alias puts the
    /// rest one tick past that.
    /// </remarks>
    [Test]
    public void Execute_AWaitedAliasThatWaits_RunsItsRestATickLater()
    {
        ConfigConsole console = new();
        console.Load($"alias x \"{Alpha} 1; wait; {Alpha} 2\"\nwait; x");

        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);
        console.ConVar(Alpha).ShouldBe("1");

        console.Execute(waitAllowed: true);
        console.ConVar(Alpha).ShouldBe("2");
    }

    /// <remarks>
    /// **A waited command runs as what added it** (B832): a config line's set, run later from the queue, is still a
    /// config's, so it is not announced before the settings attach (D215). The control: a key's waited set is announced.
    /// </remarks>
    [TestCase(true, 0)]
    [TestCase(false, 1)]
    public void Execute_AWaitedSet_IsAnnouncedAsWhatAddedIt(bool fromConfig, int announced)
    {
        ConfigConsole console = new();
        int sets = 0;
        console.Load($"bind k \"wait; {Alpha} 1\"");
        console.ConVarSet += (_, _) => sets++;

        if (fromConfig)
        {
            console.Load($"wait; {Alpha} 1");
        }
        else
        {
            console.KeyDown("k");
        }

        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        console.ConVar(Alpha).ShouldBe("1", "the control: the waited set ran");
        sets.ShouldBe(announced);
    }

    /// <remarks>
    /// **Only a key bound exactly to a spectator button moves the camera** (B844): `HandleSpectatorKeyInput`
    /// (`clientmode_shared.cpp:747-761`) compares the key's whole binding with `Q_strcmp` and swallows the key, so the
    /// button is not set. Every other route to `+attack` is the button command, which only sets the button: an alias, a
    /// longer binding, a binding in another case, the launch line. The control: the exact binding with nobody to spectate
    /// sets the button too.
    /// </remarks>
    [TestCase("bind k +attack", true, true, false)]
    [TestCase("bind k +attack", false, false, true)]
    [TestCase("alias go +attack\nbind k go", true, false, true)]
    [TestCase("bind k \"+attack; +use\"", true, false, true)]
    [TestCase("bind k +ATTACK", true, false, true)]
    public void KeyDown_ASpectatorButtonByEveryRoute_MovesTheCameraOnlyForAnExactBinding(
        string config, bool spectating, bool cycles, bool held)
    {
        ConfigConsole console = new() { Spectating = () => spectating };
        List<ViewerAction> raised = [];
        console.Triggered += (_, e) => raised.Add(e.Action);
        console.Load(config);

        console.KeyDown("k");

        (raised.Contains(ViewerAction.CycleTargetForward), console.IsHeld(ViewerAction.CycleTargetForward)).ShouldBe((cycles, held));
    }

    /// <remarks>
    /// **Each of the three exact bindings is the spectator's, and the key is taken** (B844 review 1, Fable, Sonnet): the key
    /// answers true, raises its own action, and sets no button. With nobody wiring `Spectating` no key is the spectator's.
    /// </remarks>
    [TestCase("+attack", ViewerAction.CycleTargetForward)]
    [TestCase("+attack2", ViewerAction.CycleTargetReverse)]
    [TestCase("+jump", ViewerAction.SwitchCameraMode)]
    public void KeyDown_AKeyBoundExactlyToASpectatorButton_RaisesItsActionAndIsTaken(string button, ViewerAction expected)
    {
        ConfigConsole console = new() { Spectating = static () => true };
        ConfigConsole unwired = new();
        List<ViewerAction> raised = [];
        console.Triggered += (_, e) => raised.Add(e.Action);
        unwired.Triggered += (_, e) => raised.Add(e.Action);
        console.Load($"bind k {button}");
        unwired.Load($"bind k {button}");

        (console.KeyDown("k"), console.IsHeld(expected)).ShouldBe((true, false));
        unwired.KeyDown("k");

        raised.ShouldBe([expected]);
        unwired.IsHeld(expected).ShouldBeTrue("nobody wired: the bind's text ran, which only sets the button");
    }

    /// <remarks>
    /// **A key bound exactly to `+strafe` turns the auto-director on while spectating** (B844 review 1):
    /// `SetAutoDirector( true )` (`clientmode_shared.cpp:762-769`) is `spec_autodirector.SetValue( 1 )`
    /// (`hltvcamera.cpp:868-871`). It sets 1, it does not toggle, so a second press leaves it on. The control: with nobody
    /// spectating the key runs its own `+strafe`, which this viewer does not implement, and the value stays 0.
    /// </remarks>
    [TestCase("k", "+strafe", true, "1")]
    [TestCase("k", "+strafe", false, "0")]
    [TestCase("k", "+STRAFE", true, "0")]
    [TestCase("SPACE", "+strafe", true, "1")]
    public void KeyDown_AKeyBoundExactlyToPlusStrafe_TurnsTheAutoDirectorOn(string key, string binding, bool spectating, string expected)
    {
        ConfigConsole console = ConfigConsole.WithDefaults();
        console.Spectating = () => spectating;
        List<ViewerAction> raised = [];
        console.Triggered += (_, e) => raised.Add(e.Action);
        console.Load($"spec_autodirector 0\nbind {key} {binding}");

        bool taken = console.KeyDown(key);
        console.KeyUp(key);
        console.KeyDown(key);

        console.ConVar("spec_autodirector").ShouldBe(expected);

        // The swallowed key is the spectator's, and SPACE's default (`+jump`) does not run beside it (review 2, Fable, Sonnet).
        (taken || expected == "0", raised.Count).ShouldBe((true, 0));
    }

    /// <remarks>
    /// **`spec_mode` takes a mode only with exactly one argument; the other two ignore theirs** (B844 review 2, Fable,
    /// Sonnet): `args.ArgC() == 2` (`SpectatorGUI.cpp:847`), so `spec_mode 5 6` cycles as the bare command does, and
    /// `spec_next x` reads no argument (`:786-805`).
    /// </remarks>
    [TestCase("spec_next x", ViewerAction.CycleTargetForward)]
    [TestCase("spec_mode 5 6", ViewerAction.SwitchCameraMode)]
    public void KeyDown_ASpectatorCommandWithArgumentsItIgnores_StillActs(string command, ViewerAction expected)
    {
        ConfigConsole console = new();
        List<ViewerAction> raised = [];
        console.Triggered += (_, e) => raised.Add(e.Action);
        console.Load($"bind k \"{command}\"");

        console.KeyDown("k");

        raised.ShouldBe([expected]);
    }

    /// <remarks>
    /// **An alias cannot take a spectator command's name** (B844 review 2, Fable): `Cmd_ExecuteCommand` finds the ConCommand
    /// before any alias (`engine.dll` build 1007 FUN_1801678a0), so `spec_next` still cycles and the alias's body never runs.
    /// </remarks>
    [Test]
    public void KeyDown_AnAliasNamedLikeASpectatorCommand_RunsTheCommand()
    {
        ConfigConsole console = new();
        List<ViewerAction> raised = [];
        console.Triggered += (_, e) => raised.Add(e.Action);
        console.Load("alias spec_next \"cl_showfps 2\"\nbind k spec_next");

        console.KeyDown("k");

        (raised.Count, raised[0], console.ConVar("cl_showfps")).ShouldBe((1, ViewerAction.CycleTargetForward, null));
    }

    /// <remarks>
    /// **A config's own `+attack` sets the button at load** (B844 review 1, Fable, Sonnet): run from the buffer it is the
    /// button command, which the load's one-shot rule does not take.
    /// </remarks>
    [Test]
    public void Load_PlusAttack_SetsTheButtonAndMovesNoCamera()
    {
        ConfigConsole console = new() { Spectating = static () => true };
        int raised = 0;
        console.Triggered += (_, _) => raised++;

        console.Load("+attack");

        (raised, console.IsHeld(ViewerAction.CycleTargetForward)).ShouldBe((0, true));
    }

    /// <remarks>
    /// **A spectator command bound to a key with a default replaces the default** (B844 review 1, Fable, Sonnet): MOUSE1's
    /// default is `+attack`, and `bind MOUSE1 spec_prev` must cycle backwards. `spec_mode 5` sets a mode TF2 names, which
    /// the viewer does not implement (B851), so it does nothing rather than cycle.
    /// </remarks>
    [TestCase("spec_prev", new[] { ViewerAction.CycleTargetReverse })]
    [TestCase("spec_mode 5", new ViewerAction[0])]
    public void KeyDown_AKeyWithADefaultBoundToASpectatorCommand_RunsTheCommand(string command, ViewerAction[] expected)
    {
        ConfigConsole console = ConfigConsole.WithDefaults();
        console.Spectating = static () => true;
        List<ViewerAction> raised = [];
        console.Triggered += (_, e) => raised.Add(e.Action);
        console.Load($"bind MOUSE1 \"{command}\"");

        console.KeyDown("MOUSE1");

        raised.ShouldBe(expected);
    }

    /// <remarks>
    /// **The launch line's `+attack` only sets the button** (B844): `stuffcmds` runs it as a command, never as a key.
    /// </remarks>
    [Test]
    public void Stuff_PlusAttack_SetsTheButtonAndMovesNoCamera()
    {
        ConfigConsole console = new() { Spectating = static () => true };
        int raised = 0;
        console.Triggered += (_, _) => raised++;

        console.Stuff("+attack");

        (raised, console.IsHeld(ViewerAction.CycleTargetForward)).ShouldBe((0, true));
    }

    /// <remarks>
    /// **`spec_next`, `spec_prev` and `spec_mode` are TF2's own commands** (B844, `clientmode_shared.cpp:749-759`), so a key
    /// bound to one moves the camera as the spectator's key does, in any case.
    /// </remarks>
    [TestCase("spec_next", ViewerAction.CycleTargetForward)]
    [TestCase("SPEC_PREV", ViewerAction.CycleTargetReverse)]
    [TestCase("spec_mode", ViewerAction.SwitchCameraMode)]
    public void KeyDown_AKeyBoundToASpectatorCommand_RaisesItsAction(string command, ViewerAction expected)
    {
        ConfigConsole console = new();
        List<ViewerAction> raised = [];
        console.Triggered += (_, e) => raised.Add(e.Action);
        console.Load($"bind k {command}");

        console.KeyDown("k");

        raised.ShouldBe([expected]);
    }

    /// <remarks>
    /// **A config's `spec_next` does nothing at load, and acts when a `wait` put it off** (B844 with B841): TF2 runs its
    /// configs before a demo plays, and `CCommandBuffer` runs a put-off command at its tick.
    /// </remarks>
    [TestCase("spec_next", 0)]
    [TestCase("wait; spec_next", 1)]
    [TestCase("wait; spec_prev", 1)]
    [TestCase("wait; spec_mode", 1)]
    public void Execute_AConfigsSpectatorCommand_ActsOnlyWhenAWaitPutItOff(string config, int expected)
    {
        ConfigConsole console = new();
        int raised = 0;
        console.Triggered += (_, _) => raised++;

        console.Load(config);
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        raised.ShouldBe(expected);
    }

    /// <remarks>
    /// **A config's waited one-shot still does nothing; the launch line's still runs** (B832 review 1, Fable): each runs at
    /// a later `Execute` as the text that added it, a config's at load (B820 review 1) and the launch line's as `stuffcmds`
    /// runs it (B829).
    /// </remarks>
    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public void Execute_AWaitedOneShot_RunsAsTheTextThatAddedIt(bool launchLine, int triggered)
    {
        ConfigConsole console = new();
        int raised = 0;
        console.Triggered += (_, _) => raised++;

        if (launchLine)
        {
            console.Stuff("wait\ntogglefullscreen");
        }
        else
        {
            console.Load("wait; togglefullscreen");
        }

        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        raised.ShouldBe(triggered);
    }

    /// <remarks>
    /// **A waited command from the viewer's own key is still the viewer's** (B832 review 1, Fable; D253): `SPACE`'s
    /// built-in `+jump`, replaced by a config's alias that waits before a cheat, sets it when it runs. The control: the
    /// same alias run from a config's own bind is refused.
    /// </remarks>
    [TestCase("SPACE", "1")]
    [TestCase("j", null)]
    public void Execute_AWaitedCheatFromTheViewersOwnKey_IsAdmitted(string key, string? fullbright)
    {
        ConfigConsole console = ConfigConsole.WithDefaults();
        console.Load("alias +jump \"wait; mat_fullbright 1\"\nalias -jump \"\"\nbind j +jump");

        console.KeyDown(key);
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        console.ConVar("mat_fullbright").ShouldBe(fullbright);
    }

    /// <remarks>
    /// **A `+` bind's text carries its key's number, and the number takes bytes** (B832 review 1, Fable): `Key_Event`
    /// (`engine.dll` build 1007 FUN_1801b6090) adds `"%s %i"` with the key's `ButtonCode_t`, so with nine bytes left
    /// `+forward` (nine with its terminator) would fit and `+forward 21` does not. The control: with room for the number,
    /// forward is held.
    /// </remarks>
    [TestCase(20, true, 0)]
    [TestCase(9, false, 1)]
    public void KeyDown_APlusBindBesideAFullBuffer_CountsItsKeyNumber(int room, bool held, int overflows)
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        console.Load("bind k +forward");
        console.Stuff("wait\n" + Fillers(ConfigConsole.ArgumentBufferLength - room));

        console.KeyDown("k");

        console.AnyHeld.ShouldBe(held);
        log.Count("Cbuf_AddText: buffer overflow").ShouldBe(overflows);
    }

    /// <remarks>
    /// **A key's `ButtonCode_t` is its place in TF2's table** (B832 review 2, Sonnet): `KEY_NONE` 0, the digits 1 to 10, the
    /// letters from 11, so `k` is 21; `SPACE` 65; `MOUSE1` 107; and a chord, which TF2 has no code for, none.
    /// </remarks>
    [TestCase("k", 21)]
    [TestCase("SPACE", 65)]
    [TestCase("MOUSE1", 107)]
    [TestCase("CTRL+e", null)]
    public void CodeOf_AKey_IsItsButtonCode(string key, int? code) => ButtonCodeNames.CodeOf(key).ShouldBe(code);

    /// <remarks>
    /// **`wait`'s number is read past the command, as `atoi( pArgS )` reads it** (B832 review 1, Fable): `pArgS` points into
    /// the whole text, so `wait ` then a line `5…` waits five, white space and the line feed skipped.
    /// </remarks>
    [Test]
    public void Stuff_AWaitWhoseNumberIsOnTheNextLine_WaitsThatMany()
    {
        ConfigConsole console = new();

        console.Stuff($"wait \n5\n{Alpha} 2");

        for (int call = 0; call < 5; call++)
        {
            console.ConVar(Alpha).ShouldBeNull($"before call {call + 1}");
            console.Execute(waitAllowed: true);
        }

        console.Execute(waitAllowed: true);
        console.ConVar(Alpha).ShouldBe("2");
    }

    /// <remarks>
    /// **With <c>sv_allow_wait_command</c> off, `wait` is no wait** (B832): `Cbuf_Execute` (`engine.dll` x64 build 1007
    /// FUN_180166a10) sets `m_bWaitEnabled` from it before each run, and `AddText` takes `wait` only when it is set. The
    /// control: the same line with it on waits.
    /// </remarks>
    [TestCase(false, "2")]
    [TestCase(true, "1")]
    public void Load_AWaitAfterAnExecute_WaitsOnlyWhenTheServerAllowsIt(bool allowed, string alpha)
    {
        ConfigConsole console = new();
        console.Execute(allowed);

        console.Load($"{Alpha} 1; wait; {Alpha} 2");

        console.ConVar(Alpha).ShouldBe(alpha);
    }

    /// <remarks>
    /// **<c>sv_allow_wait_command</c> is read as its integer, and is 1 until a server sets it** (B832): `Cbuf_Execute`
    /// (`engine.dll` x64 build 1007 FUN_180166a10) tests the ConVar's `m_nValue`, `(int)atof`, and its default string is
    /// "1" (FUN_18000b680). So "0.5" is off and "1abc" on.
    /// </remarks>
    [TestCase(null, true)]
    [TestCase("1", true)]
    [TestCase("1abc", true)]
    [TestCase("0", false)]
    [TestCase("0.5", false)]
    public void WaitAllowed_AServersValue_IsItsIntegerNotZero(string? value, bool allowed) =>
        ConfigConsole.WaitAllowed(value).ShouldBe(allowed);

    /// <remarks>
    /// **A key pressed while a wait's commands are due runs after them** (B832): TF2 queues the key's text behind what is
    /// already queued for its tick (`InsertCommandAtAppropriateTime`, `commandbuffer.cpp:95-105`), so the waited set lands
    /// first and the key's set wins.
    /// </remarks>
    [Test]
    public void KeyDown_AfterAWaitsCommandsAreDue_RunsAfterThem()
    {
        ConfigConsole console = new();
        console.Load($"bind k \"{Alpha} 3\"\n{Alpha} 1; wait; {Alpha} 2");
        console.Execute(waitAllowed: true);

        console.KeyDown("k").ShouldBeTrue();
        console.Execute(waitAllowed: true);

        console.ConVar(Alpha).ShouldBe("3");
    }

    /// <remarks>
    /// **A waited command holds the argument buffer until it runs** (B832, B834): it is queued at its later tick, its
    /// bytes counted, so a key whose bind does not fit beside them is refused and says `Cbuf_AddText: buffer overflow`
    /// (`Key_Event`'s FUN_180166500). The control: once the waited commands have run, the same key acts.
    /// </remarks>
    [Test]
    public void KeyDown_WhileWaitedCommandsFillTheBuffer_SaysTheBufferOverflowed()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        string bind = $"{Alpha} 3";
        console.Load($"bind k \"{bind}\"");
        // As launch-line text: one `AddText`, where a config's line would be cut at 1023 bytes.
        console.Stuff("wait\n" + Fillers(ConfigConsole.ArgumentBufferLength - bind.Length));

        console.KeyDown("k");
        console.ConVar(Alpha).ShouldBeNull();
        log.Count("Cbuf_AddText: buffer overflow").ShouldBe(1);

        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);
        console.KeyDown("k").ShouldBeTrue("the control: the waited commands ran and freed the buffer");
        console.ConVar(Alpha).ShouldBe("3");
    }

    /// <remarks>
    /// **A refusal in a file the launch line execs is the file line's, said once** (B830 review 4, Fable, Sonnet):
    /// `stuffcmds` added `exec long` whole, and `Cmd_Exec_f`'s own `Cbuf_AddText` for the file's line says it. Counted by
    /// depth, the launch line's call does not say it again.
    /// </remarks>
    [Test]
    public void Stuff_AnExecOfAFileWithACommandOf511Bytes_SaysTheBufferOverflowedOnce()
    {
        RecordingLogger log = new();
        ConfigConsole console = new()
        {
            Log = log,
            FindConfig = path => path == "cfg/long.cfg"
                ? System.Text.Encoding.ASCII.GetBytes("alias a " + new string('x', 600))
                : null,
        };

        console.Stuff("exec long.cfg");

        log.Count("WARNING: Command too long... ignoring!").ShouldBe(1);
        log.Count("Cbuf_AddText: buffer overflow").ShouldBe(1);
    }

    /// <remarks>
    /// **`bind` acts only at three arguments** (B823; `Key_Bind_f`, `engine.dll` x64 build 1007 FUN_1801b67a0): `argc - 2 &lt; 2`, else
    /// the usage line and nothing else. So `bind k screenshot extra`, or `bind k café` (five arguments by bytes), leaves
    /// the key as it was — not cleared — where the viewer had bound the third token.
    /// </remarks>
    [TestCase("bind k screenshot extra")]
    [TestCase("bind k café")]
    [TestCase("bind")]
    public void Bind_OtherThanTwoOrThreeArguments_PrintsUsageAndBindsNothing(string line)
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        List<ViewerAction> fired = [];
        console.Triggered += (_, e) => fired.Add(e.Action);

        console.Load("bind k screenshot");
        console.Load(line);
        console.KeyDown("k");

        log.Count("bind <key> [command] : attach a command to a key").ShouldBe(1);
        fired.ShouldBe([ViewerAction.Screenshot], "the earlier bind stands");
    }

    /// <remarks>
    /// **Two arguments print the key's binding and change nothing** (build 1007 FUN_1801b67a0 passes `argc == 2` to build 1007 FUN_1801b5ea0 as
    /// "show"): `"%s" = "%s"` for a bound key, `"%s" is not bound` for one that is not.
    /// </remarks>
    [Test]
    public void Bind_TwoArguments_PrintsTheBindingAndChangesNothing()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        List<ViewerAction> fired = [];
        console.Triggered += (_, e) => fired.Add(e.Action);

        console.Load("bind k screenshot\nbind k\nbind j");
        console.KeyDown("k");

        log.Count("\"k\" = \"screenshot\"").ShouldBe(1);
        log.Count("\"j\" is not bound").ShouldBe(1);
        log.Count("bind <key> [command]").ShouldBe(0);
        fired.ShouldBe([ViewerAction.Screenshot]);
    }

    /// <remarks>
    /// **`ESCAPE` is bound to `cancelselect` whatever the line says** (build 1007 FUN_1801b5ea0: key 0x46, `KEY_ESCAPE`, takes
    /// "cancelselect" in place of the command). The control is another key given the same command.
    /// </remarks>
    [Test]
    public void Bind_Escape_BindsCancelSelect()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        List<ViewerAction> fired = [];
        console.Triggered += (_, e) => fired.Add(e.Action);

        console.Load("bind F1 screenshot\nbind escape screenshot\nbind ESCAPE");
        console.KeyDown("F1");
        console.KeyDown("ESCAPE");

        fired.ShouldBe([ViewerAction.Screenshot], "F1 is the control; ESCAPE runs cancelselect, which this viewer does not know");
        log.Count("\"ESCAPE\" = \"cancelselect\"").ShouldBe(1, "bound, under a name that differs from the bind's only in case");
    }

    /// <remarks>
    /// **`unbind` acts only at two arguments** (B823 review 1, Fable; `Key_Unbind_f`, `engine.dll` x64 build 1007
    /// FUN_1801b6a30): `argc != 2` prints `unbind &lt;key&gt; : remove commands from a key`; `KEY_ESCAPE` prints `Can't unbind
    /// ESCAPE key`; any other key's binding becomes the empty string — not null — so `bind k` then shows `"k" = ""`.
    /// </remarks>
    [TestCase("unbind")]
    [TestCase("unbind k x")]
    public void Unbind_OtherThanTwoArguments_PrintsUsageAndUnbindsNothing(string line)
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        List<ViewerAction> fired = [];
        console.Triggered += (_, e) => fired.Add(e.Action);

        console.Load($"bind k screenshot\n{line}");
        console.KeyDown("k");

        log.Count("unbind <key> : remove commands from a key").ShouldBe(1);
        fired.ShouldBe([ViewerAction.Screenshot]);
    }

    /// <remarks>
    /// **A key name is matched without case** (B823 review 2, Grok): the bind table's comparer is `OrdinalIgnoreCase`
    /// (`ConfigConsole._binds`), so `bind K …` is pressed as `k`, and `bind escape …` is the key a press of `ESCAPE` or
    /// `escape` finds. *Interpolated* for the engine: its key-name lookup (an input-system call, vtable +0xe8) is not read.
    /// </remarks>
    [Test]
    public void KeyDown_AKeyBoundInAnotherCase_RunsTheBinding()
    {
        ConfigConsole console = new();
        List<ViewerAction> fired = [];
        console.Triggered += (_, e) => fired.Add(e.Action);

        console.Load("bind K screenshot\nbind escape screenshot");
        console.KeyDown("k");
        console.KeyDown("escape");

        fired.ShouldBe([ViewerAction.Screenshot], "k finds K; escape finds escape, which holds cancelselect");
    }

    /// <remarks>
    /// **`unbindall` spares ESCAPE and the console key** (B823 review 2, Sonnet; `Key_Unbindall_f`, `engine.dll` x64 build
    /// 1007 FUN_1801b6b10): every key from 0 to 0x27a whose binding is set and not empty becomes "", except 0x46
    /// (`KEY_ESCAPE`) and 0x39 (`KEY_BACKQUOTE`, the `` ` `` that opens the console). So `bind k` then shows `"k" = ""`.
    /// <see cref="ConfigConsole.Bound"/>, the bind lines that stand, is then the two spared (B823 review 3, Fable).
    /// </remarks>
    [Test]
    public void Unbindall_ABindingTable_EmptiesAllButEscapeAndTheConsoleKey()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };

        console.Load("bind ` toggleconsole\nbind escape x\nbind k screenshot\nunbindall");
        console.Bound.ShouldBe(2);

        console.Load("bind k\nbind `\nbind escape");

        log.Count("\"k\" = \"\"").ShouldBe(1);
        log.Count("\"`\" = \"toggleconsole\"").ShouldBe(1);
        log.Count("\"escape\" = \"cancelselect\"").ShouldBe(1);
    }

    /// <remarks>
    /// **`Applied of Bound` counts the same things: keys bound to a command** (B823 review 4, Fable, Sonnet). The load's log
    /// line reads "{Applied} of {Bound} binds applied", and `Applied` is per key, so `Bound` is the keys standing — a key
    /// bound twice is one, an unbound key none — not the `bind` lines seen. The control: one key does something here.
    /// </remarks>
    [Test]
    public void BoundAndApplied_AKeyBoundTwiceAndOneUnbound_CountStandingKeys()
    {
        ConfigConsole console = new();

        console.Load("bind k screenshot\nbind k screenshot\nbind j nonsense\nbind q screenshot\nunbind q");

        console.Bound.ShouldBe(2, "k and j");
        console.Applied.ShouldBe(1, "k alone reaches an action; q was unbound");
    }

    /// <remarks>
    /// **An unbound key is not applied, though the viewer's own default stands behind it** (B823 reviews 4 and 5): `F5` falls
    /// back to the viewer's `screenshot` when nothing the config left binds it, but `unbind F5` left no binding to count.
    /// </remarks>
    [Test]
    public void Applied_AKeyUnboundOverAViewerDefault_IsNotCounted()
    {
        ConfigConsole console = ConfigConsole.WithDefaults();

        console.Load("bind F5 screenshot");
        (console.Applied, console.Bound).ShouldBe((1, 1), "the control: the config bound F5");

        console.Load("unbind F5");

        (console.Applied, console.Bound).ShouldBe((0, 0), "F5's binding is gone, whatever answers for it");
        console.Bindings().KeyFor(ViewerAction.Screenshot).ShouldBe("F5", "the viewer's default still answers for it");
    }

    /// <remarks>
    /// **The viewer's own defaults are no config's binds** (B823 review 5, Fable, Sonnet): `WithDefaults` seeds the table, and
    /// a config with no `bind` line must log `0 of 0`, not `N of N` — an empty config must not look like a loaded one
    /// (<see cref="ConfigConsole.WithDefaults"/>'s remark).
    /// </remarks>
    [Test]
    public void BoundAndApplied_AConfigWithNoBindOverTheDefaults_AreZero()
    {
        ConfigConsole console = ConfigConsole.WithDefaults();

        console.Load("sensitivity 3");

        (console.Applied, console.Bound).ShouldBe((0, 0));
        console.Bindings().KeyFor(ViewerAction.Screenshot).ShouldBe("F5", "the control: the defaults were seeded");
    }

    /// <remarks>
    /// **`Applied` is the config's own command, not the default behind it** (B823 review 6, Fable, Sonnet): `bind F5
    /// nonsense` leaves F5 taking a screenshot by the viewer's fallback, but the config's bind did nothing here, so it is
    /// one bound and none applied. The control binds F5 to the screenshot itself.
    /// </remarks>
    [TestCase("bind F5 nonsense", 0, 1)]
    [TestCase("bind F5 screenshot", 1, 1)]
    public void BoundAndApplied_AConfigBindOverADefault_CountTheConfigsCommand(string line, int applied, int bound)
    {
        ConfigConsole console = ConfigConsole.WithDefaults();

        console.Load(line);

        (console.Applied, console.Bound).ShouldBe((applied, bound));
    }

    /// <remarks>
    /// **A bind counts however it ran** (B823 reviews 6 and 7, Sonnet, Fable): a key's command binding another key at run
    /// time, an alias whose body binds, and a key bound again after `unbindall` each leave one key standing that does
    /// something here. A key whose command is itself a `bind` does something — the console runs it — so it is applied.
    /// </remarks>
    [Test]
    public void BoundAndApplied_BindsAtRunTimeThroughAnAliasAndAfterUnbindall_CountEachStandingKey()
    {
        ConfigConsole console = new();

        console.Load("bind k \"bind j screenshot\"");
        (console.Applied, console.Bound).ShouldBe((1, 1), "k's command is a bind the console runs");

        console.KeyDown("k");
        (console.Applied, console.Bound).ShouldBe((2, 2), "a run-time bind: j now takes the screenshot");

        console.Load("alias b \"bind h screenshot\"\nb");
        (console.Applied, console.Bound).ShouldBe((3, 3), "a bind an alias ran");

        console.Load("unbindall\nbind j screenshot");
        (console.Applied, console.Bound).ShouldBe((1, 1), "unbindall emptied k, j and h; j bound again");
    }

    /// <remarks>
    /// **A bind that sets state is applied** (B823 review 7, Fable): `bind F5 "toggle cl_showpos"` over the defaults runs the
    /// toggle and takes F5 from the screenshot, as `CommandFor` already reads it (a command that expands to an action OR sets
    /// state does something), so `Applied` asks the same question.
    /// </remarks>
    [Test]
    public void BoundAndApplied_AStateSettingBindOverADefault_IsApplied()
    {
        ConfigConsole console = ConfigConsole.WithDefaults();
        List<ViewerAction> fired = [];
        console.Triggered += (_, e) => fired.Add(e.Action);

        console.Load("bind F5 \"toggle cl_showpos\"");

        (console.Applied, console.Bound).ShouldBe((1, 1));

        console.KeyDown("F5");
        fired.ShouldBeEmpty("the control: F5 runs the toggle, not the screenshot");
        console.ConVar("cl_showpos").ShouldBe("1");
    }

    /// <remarks>
    /// **`bind F5` prints the seeded default** (B823 review 6, Fable): TF2's default binds come from `config_default.cfg`
    /// into the same table, so its `bind F5` prints them; <see cref="ConfigConsole.WithDefaults"/> plays that file's part.
    /// </remarks>
    [Test]
    public void Bind_TwoArgumentsOnASeededDefault_PrintsTheDefault()
    {
        RecordingLogger log = new();
        ConfigConsole console = ConfigConsole.WithDefaults();
        console.Log = log;

        console.Load("bind F5");

        log.Count("\"F5\" = \"screenshot\"").ShouldBe(1);

        // The table, not the viewer's default (B823 review 7, Grok): rebound, F5 prints its new binding.
        console.Load("bind F5 \"toggle cl_showpos\"\nbind F5");

        log.Count("\"F5\" = \"toggle cl_showpos\"").ShouldBe(1);
        log.Count("\"F5\" = \"screenshot\"").ShouldBe(1, "printed once, before the rebind");

        // And a binding that does nothing here (B823 review 8, Fable): `CommandFor` falls back to the screenshot for
        // `nonsense`, so only a show that reads the table prints `nonsense`.
        console.Load("bind F5 nonsense\nbind F5");

        log.Count("\"F5\" = \"nonsense\"").ShouldBe(1);
        log.Count("\"F5\" = \"screenshot\"").ShouldBe(1, "still only the first show, before any rebind");
    }

    /// <remarks>
    /// **A key name TF2 does not know binds nothing and says so** (B826): `engine.dll` build 1007 FUN_1801b5ea0 asks the
    /// input system for the name (`inputsystem.dll` x64 FUN_180005880, through the interface's FUN_180003630) and, at -1,
    /// prints `"%s" isn't a valid key` and returns, for a show and a set alike. `ESC` is not one of TF2's names: its table
    /// has `ESCAPE`. The control: the same line with `ESCAPE` would bind; here `F1` takes the screenshot.
    /// </remarks>
    [TestCase("ESC")]
    [TestCase("NUMPAD3")]
    [TestCase("KP_9")]
    public void Bind_AKeyNameTf2DoesNotKnow_SaysItIsNotAValidKeyAndBindsNothing(string name)
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        List<ViewerAction> fired = [];
        console.Triggered += (_, e) => fired.Add(e.Action);

        console.Load($"bind F1 screenshot\nbind {name} screenshot\nbind {name}");
        console.KeyDown(name);
        console.KeyDown("F1");

        log.Count($"\"{name}\" isn't a valid key").ShouldBe(2, "the set and the show both say it");
        fired.ShouldBe([ViewerAction.Screenshot], $"F1 is the control; {name} is bound to nothing");
    }

    /// <remarks>
    /// **`unbind` says the same for a name TF2 does not know** (B826; `Key_Unbind_f`, `engine.dll` build 1007 FUN_1801b6a30:
    /// the lookup at -1 prints `"%s" isn't a valid key`, after the argument count, before the `ESCAPE` check).
    /// </remarks>
    [Test]
    public void Unbind_AKeyNameTf2DoesNotKnow_SaysItIsNotAValidKey()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };

        console.Load("unbind ESC");

        log.Count("\"ESC\" isn't a valid key").ShouldBe(1);
        log.Count("Can't unbind ESCAPE key").ShouldBe(0);
    }

    /// <remarks>
    /// **A name binds the key TF2's table names, whatever its case or spelling** (B826; `inputsystem.dll` x64
    /// FUN_180005880): the names compare ignoring ASCII case, and `aux&lt;n&gt;` is the legacy joystick spelling, `atoi` of
    /// the rest: below 29 is `JOY&lt;n+1&gt;`, 29 to 32 the four `POV_` buttons, and anything else no key. So `bind aux1`
    /// then `bind JOY2` shows the binding, and `bind kp_ins` is `KP_INS`.
    /// </remarks>
    [TestCase("aux1", "JOY2")]
    [TestCase("aux+1", "JOY2")]
    [TestCase("aux", "JOY1")]
    [TestCase("aux28", "JOY29")]
    [TestCase("AUX29", "POV_UP")]
    [TestCase("aux32", "POV_LEFT")]
    [TestCase("kp_ins", "KP_INS")]
    [TestCase("Escape", "ESCAPE")]
    [TestCase("rshift", "RSHIFT")]
    [TestCase("RCTRL", "RCTRL")]
    [TestCase("Ralt", "RALT")]
    public void Bind_ANameTheTableKnows_BindsTheKeyItNames(string typed, string key)
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };

        console.Load($"bind {typed} screenshot\nbind {key}");

        string expected = key == "ESCAPE" ? "cancelselect" : "screenshot";
        log.Count($"\"{key}\" = \"{expected}\"").ShouldBe(1);
        log.Count("isn't a valid key").ShouldBe(0);
    }

    /// <remarks>
    /// **`aux` past 32 is no key** (B826; `inputsystem.dll` x64 FUN_180005880 returns -1 for `atoi` of 33 and more).
    /// </remarks>
    [Test]
    public void Bind_AnAuxNamePastThirtyTwo_SaysItIsNotAValidKey()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };

        console.Load("bind aux33 screenshot");

        log.Count("\"aux33\" isn't a valid key").ShouldBe(1);
    }

    /// <remarks>
    /// **A chord binds, above Valve** (B826; `docs/ABOVE-VALVE.md`): TF2 has no modifier chords, so no TF2 config holds one,
    /// and the viewer's own defaults (`CTRL+e`) and menu are chords a config may rebind. A chord is a modifier
    /// (`CTRL`, `CONTROL`, `SHIFT`, `ALT`) and `+` before a TF2 key name. The control: a chord on a name TF2 does not
    /// know is refused like the name.
    /// </remarks>
    [Test]
    public void Bind_AChordOfAModifierAndAKeyTf2Knows_Binds()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };

        console.Load("bind CTRL+e screenshot\nbind CTRL+e\nbind CTRL+ESC screenshot\nbind x+e screenshot");

        log.Count("\"CTRL+e\" = \"screenshot\"").ShouldBe(1);
        log.Count("\"CTRL+ESC\" isn't a valid key").ShouldBe(1, "the control: a key TF2 does not know");
        log.Count("\"x+e\" isn't a valid key").ShouldBe(1, "the control: no modifier");
    }

    /// <remarks>
    /// **A chord's key is stored in the table's spelling** (B826 review 1, Fable): `CTRL+aux1` is `CTRL+JOY2`, the name the
    /// view resolves, so the two are one chord.
    /// </remarks>
    [Test]
    public void KeyOf_AChordOnAnAuxName_IsTheChordOnItsJoystickButton() =>
        ButtonCodeNames.KeyOf("CTRL+aux1").ShouldBe("CTRL+JOY2");

    /// <remarks>
    /// **A chord's shapes** (B826 reviews 1 and 2, Fable): modifiers are taken from the left, so two modifiers and an
    /// `aux` name with a `+` of its own are chords; a modifier matches ignoring ASCII case only, so `ſHIFT` (U+017F, which
    /// .NET's `OrdinalIgnoreCase` folds to `S`) is none, as TF2's `V_stricmp` would refuse it. The modifiers are stored in
    /// one spelling and order, `CTRL+SHIFT+ALT`, so one press is one entry (review 3, Fable); a chord whose key is itself a
    /// modifier (`CTRL+SHIFT`) is none, since the view could never match that press (review 4, Fable), and nor is one on a
    /// right-hand modifier (`CTRL+RSHIFT`, review 5, Gemini), though a bare bind on one is a TF2 key. A chord on any other
    /// key is kept, even one the view never fires (`CTRL+KP_ENTER`, `CTRL+MOUSE1`; reviews 6 and 7).
    /// </remarks>
    [TestCase("CTRL+SHIFT+e", "CTRL+SHIFT+e")]
    [TestCase("SHIFT+CTRL+e", "CTRL+SHIFT+e")]
    [TestCase("ALT+SHIFT+CTRL+e", "CTRL+SHIFT+ALT+e")]
    [TestCase("CTRL+CTRL+e", "CTRL+e")]
    [TestCase("CONTROL+e", "CTRL+e")]
    [TestCase("ctrl+aux+1", "CTRL+JOY2")]
    [TestCase("ALT+KP_ins", "ALT+KP_INS")]
    [TestCase("CTRL+KP_ENTER", "CTRL+KP_ENTER")]
    [TestCase("CTRL+mouse1", "CTRL+MOUSE1")]
    [TestCase("CTRL+SHIFT", null)]
    [TestCase("SHIFT+CTRL", null)]
    [TestCase("CTRL+RSHIFT", null)]
    [TestCase("SHIFT+rctrl", null)]
    [TestCase("ALT+RALT", null)]
    [TestCase("ſHIFT+e", null)]
    [TestCase("CTRL+", null)]
    [TestCase("CTRL++e", null)]
    [TestCase("+e", null)]
    public void KeyOf_AChord_IsItsModifiersAndItsKeyInTheTablesSpelling(string name, string? expected) =>
        ButtonCodeNames.KeyOf(name).ShouldBe(expected);

    /// <remarks>
    /// **Every key the viewer binds by default is one TF2 names, or a chord on one** (B826): a default on a name the
    /// console refuses could never be rebound or shown.
    /// </remarks>
    [Test]
    public void Defaults_EveryKey_IsATf2KeyName()
    {
        // Spelled as the console stores it, so a config's bind on the same press replaces the default rather than sitting
        // beside it (B826 review 4).
        foreach (string key in KeyBindings.Defaults.Values)
        {
            ButtonCodeNames.KeyOf(key).ShouldBe(key);
        }
    }

    /// <remarks>
    /// **A key that toggles `sv_cheats` is the config's** (B823 review 9, Fable): the console takes `sv_cheats` though it is
    /// replicated (B718), so `toggle`, `incrementvar` and `multvar` on it run, as TF2 runs them, and the key must not fall
    /// back to the viewer's screenshot. `sv_gravity`, replicated and no `sv_cheats`, is refused by the console and is the
    /// control: it names nothing the console runs, so F6 keeps its default.
    /// </remarks>
    [Test]
    public void KeyDown_AToggleOfSvCheatsOnAKey_TogglesCheatsAndCounts()
    {
        ConfigConsole console = ConfigConsole.WithDefaults();
        console.StartCheatsOff();
        List<ViewerAction> fired = [];
        console.Triggered += (_, e) => fired.Add(e.Action);

        console.Load("bind F5 \"toggle sv_cheats\"\nbind F6 \"toggle sv_gravity\"");

        (console.Applied, console.Bound).ShouldBe((1, 2), "the control: F6's sv_gravity is the server's");

        console.KeyDown("F5");
        fired.ShouldNotContain(ViewerAction.Screenshot);
        console.ConVar("sv_cheats").ShouldBe("1");
    }

    /// <remarks>
    /// **A plain set of a replicated ConVar is not applied** (B823 review 9, Gemini): `sv_gravity` is declared but the
    /// server's, so the console refuses `sv_gravity 800` and F6 keeps the viewer's default. `sv_cheats 1` on F5 is the
    /// control: replicated too, but the one the console takes (B718).
    /// </remarks>
    [Test]
    public void BoundAndApplied_ASetOfAReplicatedConVar_IsNotApplied()
    {
        ConfigConsole console = new();

        console.Load("bind F6 \"sv_gravity 800\"\nbind F5 \"sv_cheats 1\"");

        (console.Applied, console.Bound).ShouldBe((1, 2), "the control: F5's sv_cheats is taken");
    }

    /// <remarks>
    /// **`incrementvar sv_cheats` on a key, the same** (B823 review 9, Fable): five tokens, so `CvarCommand` assigns it
    /// and the console takes `sv_cheats 1.000000`.
    /// </remarks>
    [Test]
    public void KeyDown_AnIncrementvarOfSvCheatsOnAKey_SetsCheatsAndCounts()
    {
        ConfigConsole console = ConfigConsole.WithDefaults();
        console.StartCheatsOff();
        List<ViewerAction> fired = [];
        console.Triggered += (_, e) => fired.Add(e.Action);

        console.Load("bind F5 \"incrementvar sv_cheats 0 1 1\"");

        (console.Applied, console.Bound).ShouldBe((1, 1));

        console.KeyDown("F5");
        fired.ShouldNotContain(ViewerAction.Screenshot);
        console.ConVar("sv_cheats").ShouldBe("1.000000");
    }

    /// <remarks>
    /// **An alias with an empty body is applied, deliberately** (B823 review 8, Fable, Sonnet): `Applied` counts what a
    /// binding names, not what it changes. TF2 runs `none` when `k` is pressed; the bind owns the key and no viewer default
    /// stands behind it, so <c>CommandFor</c> taking it is parity, and `Applied` asks <c>CommandFor</c>'s question.
    /// </remarks>
    [Test]
    public void BoundAndApplied_ABindToAnAliasWithAnEmptyBody_IsApplied()
    {
        ConfigConsole console = new();

        console.Load("alias none \"\"\nbind k none\nbind j nothing");

        (console.Applied, console.Bound).ShouldBe((1, 2), "the control: j names nothing the console runs");
    }

    /// <remarks>
    /// **An emptied key is unbound to every reader** (B823 review 3, Sonnet): after `unbindall` the table holds `""` for
    /// `k`, and <see cref="ConfigConsole.Bindings"/> gives it no action (the menu's <c>ShowBindings</c> reads
    /// <c>KeyFor</c>) and no `%command%` (the HUD's <c>KeyForCommand</c>): `""` names no command a HUD asks, since
    /// `UTIL_ReplaceKeyBindings` skips `%%` (`cdll_util.cpp:891`).
    /// </remarks>
    [Test]
    public void Bindings_AfterUnbindall_NameNoActionAndNoCommandForTheEmptiedKey()
    {
        ConfigConsole console = new();

        console.Load("bind k screenshot\nbind j +jump");
        console.Bindings().KeyFor(ViewerAction.Screenshot).ShouldBe("k", "the control: bound before");

        console.Load("unbindall");
        KeyBindings bindings = console.Bindings();

        bindings.ActionsFor("k").ShouldBeEmpty();
        bindings.KeyFor(ViewerAction.Screenshot).ShouldNotBe("k", "screenshot goes back to the viewer's own key");
        bindings.KeyForCommand("+jump").ShouldBeNull();
        bindings.KeyForCommand("jump").ShouldBeNull();
    }

    [Test]
    public void Unbind_TwoArguments_LeavesTheKeyBoundToNothing()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };
        List<ViewerAction> fired = [];
        console.Triggered += (_, e) => fired.Add(e.Action);

        console.Load("bind k screenshot\nunbind k\nbind k");
        console.KeyDown("k");

        fired.ShouldBeEmpty();
        log.Count("\"k\" = \"\"").ShouldBe(1, "an unbound key holds the empty string, which `bind k` prints");
    }

    [Test]
    public void Unbind_Escape_PrintsAndKeepsItsBinding()
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };

        console.Load("bind escape x\nunbind Escape\nbind escape");

        log.Count("Can't unbind ESCAPE key").ShouldBe(1);
        log.Count("\"escape\" = \"cancelselect\"").ShouldBe(1);
    }

    /// <remarks>
    /// **A numeric ConVar reads its value as `atof`** (B823; `ConVar::InternalSetValue`, `src/tier1/convar.cpp:792`): the
    /// longest numeric prefix, 0 when there is none, and `inf` is infinity (C11 7.22.1.3), whose int is MSVC's
    /// `0x80000000`. `toggle name` flips that int (build 1007 FUN_180179390): zero becomes 1, anything else 0. `name 1 2`, whose value
    /// is `ArgS` `1 2`, is 1.
    /// </remarks>
    [TestCase("1 2", "0")]
    [TestCase(" 2x", "0")]
    [TestCase("abc", "1")]
    [TestCase("inf", "0")]
    public void Toggle_AConVarSetToText_ReadsItsNumberAsAtof(string value, string expected)
    {
        ConfigConsole console = new();

        console.Load($"cl_showpos \"{value}\"\ntoggle cl_showpos");

        console.ConVar("cl_showpos").ShouldBe(expected);
    }

    /// <remarks>
    /// **The launch line is not a file** (B820 review 8, Sonnet): `stuffcmds` hands its text to `Cbuf_AddText`, so a `\\` in
    /// a launch value stays two backslashes where a config line's would become one.
    /// </remarks>
    [Test]
    public void Stuff_ALaunchValueWithTwoBackslashes_KeepsThem()
    {
        ConfigConsole console = new();

        console.Stuff("cl_voice_filter \"a\\\\b\"");

        console.ConVar("cl_voice_filter").ShouldBe("a\\\\b");
    }

    /// <remarks>
    /// `ParseArgV0` finding an empty first token skips the command before `InsertCommand` measures it
    /// (`commandbuffer.cpp:230-232`): a blank command, or one whose first token is `""`, of any length.
    /// </remarks>
    [TestCase(" ")]
    [TestCase("\"\" ")]
    public void Load_ABlankCommandOfAnyLength_IsSkippedNotRefused(string start)
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };

        console.Load($"{start}{new string(' ', 600)};{Alpha} 3");

        log.Count("Command too long").ShouldBe(0);
        console.ConVar(Alpha).ShouldBe("3");
    }

    /// <remarks>
    /// **The commands after a refused one in the same text never run** (`CCommandBuffer::AddText` returns at the refusal,
    /// `src/tier1/commandbuffer.cpp:242-243`; B820 review 6, Fable): a file's line, which `Cmd_Exec_f` hands `Cbuf_AddText`
    /// on its own (FUN_180177310). The one before it does. (A bind or an alias cannot hold one: its own definition would be
    /// the command too long.)
    /// </remarks>
    [Test]
    public void Load_ALineWithACommandTooLong_RunsWhatCameBeforeItAndNothingAfter()
    {
        ConfigConsole console = new();

        console.Load($"+back;echo {new string('a', 520)};+forward");

        console.IsHeld(ViewerAction.FlyBack).ShouldBeTrue("the command before it ran");
        console.IsHeld(ViewerAction.FlyForward).ShouldBeFalse("the command after it is dropped with it");
    }

    /// <remarks>
    /// **A command keeps at most 64 arguments** (`COMMAND_MAX_ARGC`, `src/public/tier1/convar.h:206`; the tokenizer's loop,
    /// `src/tier1/convar.cpp:416`). `toggle` and the name are two, so values 1 to 62 are kept of 1 to 70; from 62, the last
    /// kept, it wraps to 1, where all 70 would go on to 63; from 61 it goes on to 62, which 63 kept would not (it would wrap,
    /// as `toggle` goes to the first value when the current is not in its list; B820 review 6, Sonnet).
    /// </remarks>
    [TestCase("62", "1")]
    [TestCase("61", "62")]
    public void Load_ACommandWithMoreThanSixtyFourArguments_KeepsSixtyFour(string from, string expected)
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };

        console.Load($"{Alpha} {from}\ntoggle {Alpha} {string.Join(' ', Enumerable.Range(1, 70))}");

        console.ConVar(Alpha).ShouldBe(expected);
        log.Count("CCommand::Tokenize: Encountered command which overflows the argument buffer.. Clamped!").ShouldBe(1);
    }

    /// <remarks>The warning comes once the 64th argument is taken, so exactly 64 says it and 63 does not (`convar.cpp:460-464`).</remarks>
    [TestCase(62, 1)]
    [TestCase(61, 0)]
    public void Load_ACommandOfSixtyFourArguments_SaysItIsClamped(int values, int said)
    {
        RecordingLogger log = new();
        ConfigConsole console = new() { Log = log };

        console.Load($"toggle {Alpha} {string.Join(' ', Enumerable.Range(1, values))}");

        log.Count("overflows the argument buffer.. Clamped!").ShouldBe(said);
    }

    /// <remarks>
    /// **Two keys on one action: the first bound has it, and a rebind keeps its place** (B823 review 9, Fable): the order
    /// is the console's by design, not a dictionary's. `b` is bound after `a`, then `a` is rebound, and `a` keeps it.
    /// </remarks>
    [Test]
    public void Bindings_TwoKeysOnOneAction_GiveItToTheFirstBound()
    {
        ConfigConsole console = new();

        console.Load("bind a +forward\nbind b +forward\nbind a +forward");

        console.Bindings().KeyFor(ViewerAction.FlyForward).ShouldBe("a");
    }

    /// <remarks>
    /// **An alias first reached at the depth bound is walked again from a shallower depth** (B820 review 4, Sonnet): reached at
    /// depth 19 through a chain, its body is cut; reached again at the top of the same bind, it must still count.
    /// Read through <see cref="ConfigConsole.Bindings"/>' actions per key: `Applied` counts any bind that runs an alias
    /// since B823 review 7 (`CommandFor`'s own question), so it no longer tells the two routes apart.
    /// **`y` is bound first** (B823 review 8, Fable): `Bindings` gives an action to the first key that reaches it, so with
    /// `x` first a depth cut that let `y` through stayed green; with `y` first, that fault takes the action from `x`. That
    /// power rests on the first-bound order, which `Bindings_TwoKeysOnOneAction_GiveItToTheFirstBound` pins (B823 review 9).
    /// </remarks>
    [Test]
    public void Bindings_AnAliasReachedDeepThenShallow_StillReachesItsAction()
    {
        ConfigConsole console = new();
        IEnumerable<string> chain = Enumerable.Range(1, 19)
            .Select(at => $"alias c{at} {(at < 19 ? $"c{at + 1}" : "s")}");

        console.Load(string.Join('\n', chain) + "\nalias s +forward\nbind y c1\nbind x \"c1;s\"");

        KeyBindings bindings = console.Bindings();
        bindings.ActionsFor("x").ShouldContain(ViewerAction.FlyForward, "x reaches s at the top (B820 review 5, Fable)");
        bindings.ActionsFor("y").ShouldNotContain(ViewerAction.FlyForward, "y's only route to s is the cut one");
    }

    /// <remarks>
    /// **A line keeps its alias count across an `exec` it runs** (B820 review 6, Sonnet): the nested file's lines take their
    /// own and the line gets its back. 20 `m` (201 runs each), the exec, then one more `m` pass 4096 on the one line.
    /// </remarks>
    [Test]
    public void Load_ALineThatExecsBetweenItsAliases_KeepsItsAliasCountAcrossTheExec()
    {
        RecordingLogger log = new();
        ConfigConsole console = WithFiles([], ("cfg/f.cfg", "n"));
        console.Log = log;

        console.Load(
            $"alias n \"\"\nalias m \"{string.Join(';', Enumerable.Repeat("n", 200))}\"\n" +
            $"{string.Join(';', Enumerable.Repeat("m", 20))};exec f;m");

        log.Count($"more than {ConfigConsole.MaximumAliasRuns} alias runs from one line").ShouldBe(1);
    }

    /// <remarks>
    /// **The static projection walks each alias once** (B820 review 3, Sonnet, Fable): `alias a "a;a;a;a"` bound to a key was
    /// 4^20 calls from `Applied` at load and from the key's lookup on every press, on `main` too.
    /// </remarks>
    [Test]
    public void KeyDown_AKeyBoundToAnAliasThatFansOut_EndsPromptly()
    {
        ConfigConsole console = new();

        Should.CompleteIn(
            () =>
            {
                // The load inside the limit too (B820 review 4, Grok), should it ever project eagerly.
                console.Load("alias a \"a;a;a;a;+forward\"\nbind x a");
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
        int handled = 0;
        console.ConVarSet += (_, _) =>
        {
            handled++;
            console.Load(string.Empty);
        };

        Should.CompleteIn(() => console.Load("exec loop"), TimeSpan.FromSeconds(10));

        handled.ShouldBeGreaterThan(0, "the control (B820 review 4, Grok): the handler loaded inside the tree");
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
