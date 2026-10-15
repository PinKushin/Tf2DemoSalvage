using System;
using System.Collections.Generic;

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

    [Test]
    public void Load_AReplicatedCvarLine_IsRefused()
    {
        // FCVAR_REPLICATED is "server setting enforced on clients" (iconvar.h); the client's config cannot set it.
        ConfigConsole console = new();

        console.Load("mp_tournament 1");

        console.ConVar("mp_tournament").ShouldBeNull();
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
}
