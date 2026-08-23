using System;

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

        console.Load("bind \"j\" \"playpause\"");
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
        engineOrder.Load(["bind \"w\" \"+mfwd\"", "alias +mfwd \"+forward\""]);

        ConfigConsole reversed = new();
        reversed.Load(["alias +mfwd \"+forward\"", "bind \"w\" \"+mfwd\""]);

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

        console.Bindings().KeyFor(ViewerAction.FlyFast).ShouldBe("SHIFT", "before the config runs");

        console.Load("bind \"SHIFT\" \"+duck\"");

        console.Unbound().ShouldNotContain(ViewerAction.FlyFast);

        console.KeyDown("SHIFT");
        console.IsHeld(ViewerAction.FlyFast).ShouldBeTrue("+duck does nothing here, so ours stands");
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
        console.IsHeld(ViewerAction.FlyFast).ShouldBeFalse("the config spoke, and it wins");
        console.Unbound().ShouldContain(ViewerAction.FlyFast, "and the loss is reported");
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

        console.Unbound().ShouldNotContain(ViewerAction.FlyFast);
        console.Bindings().KeyFor(ViewerAction.FlyFast).ShouldBe("CTRL");
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
        intent.Fast.ShouldBeFalse();
    }
}
