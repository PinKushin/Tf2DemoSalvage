using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// Reading a real TF2 config, which D69 requires to work wholesale.
/// </summary>
/// <remarks>
/// **The fixtures are lifted verbatim from `tf/cfg/config_default.cfg`.** Authoring them from this
/// parser's own idea of the syntax would prove the two agree and nothing else —
/// `docs/memory/put-the-real-file-in-the-fixture.md`. The tab-separated, quoted form below is
/// exactly how the shipped file is written.
/// </remarks>
public sealed class SourceConfigTests
{
    /// <summary>The opening of TF2's own default config, copied byte for byte.</summary>
    private const string ShippedOpening = """
        // If the user doesn't have a config.cfg when they run, this gets executed the first time
        // It doesn't execute if they have their own config.cfg saved out.

        unbindall

        bind "`"			"toggleconsole"
        bind "w"			"+forward"
        bind "s"			"+back"
        bind "a"			"+moveleft"
        bind "d"			"+moveright"
        bind "SPACE"			"+jump"
        bind "CTRL"			"+duck"
        bind "'"			"+moveup"
        bind "/"			"+movedown"
        bind "MOUSE1"			"+attack"
        bind "MOUSE2"			"+attack2"
        """;

    [Test]
    public void ReadBinds_TheShippedConfig_FindsEveryBind()
    {
        IReadOnlyList<(string Key, string Command)> binds = SourceConfig.ReadBinds(ShippedOpening);

        binds.Count.ShouldBe(11);
        binds.ShouldContain(("w", "+forward"));
        binds.ShouldContain(("SPACE", "+jump"));
        binds.ShouldContain(("'", "+moveup"));
        binds.ShouldContain(("MOUSE1", "+attack"));
    }

    [Test]
    public void ReadBinds_CommandsThisViewerDoesNotImplement_AreKeptRatherThanDropped()
    {
        // **Ignoring happens at the mapping step, not here.** `toggleconsole` and `+duck` mean
        // nothing to this viewer, but the reader reports them so a caller can tell "your config had
        // 200 binds and 8 applied" from "your config did not load" — which otherwise look the same.
        IReadOnlyList<(string Key, string Command)> binds = SourceConfig.ReadBinds(ShippedOpening);

        binds.ShouldContain(("`", "toggleconsole"));
        binds.ShouldContain(("CTRL", "+duck"));
    }

    [Test]
    public void ReadBinds_Unquoted_ParsesToo()
    {
        // `config_default.cfg` quotes everything; a hand-edited autoexec.cfg usually does not. A
        // parser that required quotes would silently read half of a real config.
        IReadOnlyList<(string Key, string Command)> binds =
            SourceConfig.ReadBinds("bind w +forward\nbind SPACE +jump");

        binds.ShouldBe([("w", "+forward"), ("SPACE", "+jump")]);
    }

    [Test]
    public void ReadBinds_Unbindall_DiscardsWhatCameBefore()
    {
        // The shipped file opens with it, and mastercomfig packs use it. Ignoring it would leave
        // bindings in place that the file went out of its way to clear.
        IReadOnlyList<(string Key, string Command)> binds = SourceConfig.ReadBinds(
            """
            bind "w" "+forward"
            unbindall
            bind "s" "+back"
            """);

        binds.ShouldBe([("s", "+back")]);
    }

    [Test]
    public void ReadBinds_AKeyBoundTwice_KeepsBothInOrderSoTheLastWins()
    {
        // A Source config executes top to bottom, and `exec`'d files layer the same way. Collapsing
        // to a dictionary in the reader would pick arbitrarily; the caller applies in order.
        IReadOnlyList<(string Key, string Command)> binds = SourceConfig.ReadBinds(
            "bind \"w\" \"+forward\"\nbind \"w\" \"+back\"");

        binds.Count.ShouldBe(2);
        binds[^1].ShouldBe(("w", "+back"));
    }

    [Test]
    public void ReadBinds_Comments_AreStripped()
    {
        IReadOnlyList<(string Key, string Command)> binds = SourceConfig.ReadBinds(
            """
            // bind "w" "+forward"
            bind "s" "+back"   // trailing
            """);

        binds.ShouldBe([("s", "+back")]);
    }

    [Test]
    public void ReadBinds_ASlashInsideQuotes_IsNotAComment()
    {
        // `/` is a real key — TF2 binds it to +movedown — and a doubled slash can appear in a cvar
        // value. Stripping to the first `//` regardless would turn a value into a different value
        // rather than into an error.
        SourceConfig.ReadBinds("bind \"/\" \"+movedown\"")
            .ShouldBe([("/", "+movedown")]);

        SourceConfig.ReadBinds("hostname \"a // b\"\nbind \"s\" \"+back\"")
            .ShouldBe([("s", "+back")]);
    }

    [Test]
    public void ReadBinds_Nothing_IsEmptyRatherThanThrowing()
    {
        SourceConfig.ReadBinds(null).ShouldBeEmpty();
        SourceConfig.ReadBinds(string.Empty).ShouldBeEmpty();
        SourceConfig.ReadBinds("   \n\n// only a comment\n").ShouldBeEmpty();
    }

    [Test]
    public void ReadBinds_AMalformedBind_IsSkippedRatherThanHalfRead()
    {
        // `bind` with no command binds nothing in Source either. Taking the key and an empty command
        // would unbind it, which is a different instruction from the one written.
        SourceConfig.ReadBinds("bind\nbind \"w\"").ShouldBeEmpty();
    }

    [Test]
    public void ApplySourceConfig_TheShippedConfig_RebindsWhatItRecognises()
    {
        // **The whole point of D69, end to end.** A TF2 config names actions in TF2's vocabulary, so
        // it must land without translation.
        KeyBindings bindings = new();

        int applied = bindings.ApplySourceConfig(ShippedOpening);

        // Eleven binds in the fixture; nine name actions this viewer has. The two left out are
        // `toggleconsole` and `+duck`, which is the ratio the feature is really about — a real
        // config is mostly commands we ignore, and it still lands the ones that matter.
        applied.ShouldBe(9, "forward, back, left, right, up, down, jump, and the two attacks");
        bindings.KeyFor(ViewerAction.FlyForward).ShouldBe("w");
        bindings.KeyFor(ViewerAction.SwitchCameraMode).ShouldBe("SPACE");
        bindings.KeyFor(ViewerAction.FlyUp).ShouldBe("'");
        bindings.KeyFor(ViewerAction.CycleTargetForward).ShouldBe("MOUSE1");
    }

    [Test]
    public void ApplySourceConfig_ARebindingConfig_MovesTheAction()
    {
        // Somebody who plays with ESDF, which is the case the feature exists for: they should not
        // have to say so twice.
        KeyBindings bindings = new();

        bindings.ApplySourceConfig(
            """
            bind "e" "+forward"
            bind "d" "+back"
            bind "s" "+moveleft"
            bind "f" "+moveright"
            """);

        bindings.KeyFor(ViewerAction.FlyForward).ShouldBe("e");
        bindings.KeyFor(ViewerAction.FlyLeft).ShouldBe("s");
        bindings.KeyFor(ViewerAction.FlyRight).ShouldBe("f");
    }

    [Test]
    public void ApplySourceConfig_AConfigOfNothingWeImplement_AppliesNoneAndSaysSo()
    {
        // **The count is what lets a caller say something true.** A config full of `mat_*` lines and
        // one full of nothing are both "loaded"; only the number distinguishes them, and without it
        // a misspelt path and a working load look identical.
        KeyBindings bindings = new();

        int applied = bindings.ApplySourceConfig(
            """
            mat_picmip -10
            cl_interp 0.0152
            alias "+jumpthrow" "+jump;-attack"
            exec autoexec.cfg
            """);

        applied.ShouldBe(0);
        bindings.KeyFor(ViewerAction.FlyForward).ShouldBe("w", "and the defaults are untouched");
    }

    [Test]
    public void ApplySourceConfigs_ANullCancellingMovementScript_ResolvesThroughTheAlias()
    {
        // **Copied from the owner's own `autoexec.cfg`**, which is where this requirement was found:
        // `config.cfg` binds `w` to `+mfwd` and nothing in it says what `+mfwd` means. Competitive
        // configs bind movement to aliases as a matter of course, so a reader that only understood
        // engine commands would ignore every movement bind and report success.
        //
        // Fifteen synthetic tests passed before this was noticed, all built from
        // `config_default.cfg` — which binds movement directly and therefore cannot contain an
        // alias. Only the real file could show it.
        KeyBindings bindings = new();

        int applied = bindings.ApplySourceConfigs(
        [
            """
            alias +mfwd   "-back;      +forward;   alias checkfwd   +forward"
            alias -mfwd   "-forward;   checkback;  alias checkfwd   none"
            alias +mback  "-forward;   +back;      alias checkback  +back"
            """,
            """
            bind "w" "+mfwd"
            bind "s" "+mback"
            """,
        ]);

        applied.ShouldBe(2);
        bindings.KeyFor(ViewerAction.FlyForward).ShouldBe("w");
        bindings.KeyFor(ViewerAction.FlyBack).ShouldBe("s");
    }

    [Test]
    public void ApplySourceConfigs_TheFirstRecognisedCommandInABody_Wins()
    {
        // `+mfwd` expands to `-back; +forward; alias checkfwd +forward`. `-back` is a release
        // command with no action here, `+forward` is the one that matters, and the trailing `alias`
        // clause DEFINES something rather than running it — so taking every word in the body would
        // find `+forward` inside a clause that never executes.
        SourceConfig.Body("-back;      +forward;   alias checkfwd   +forward")
            .ShouldBe(["-back", "+forward", "alias"]);
    }

    [Test]
    public void ApplySourceConfigs_ACircularAlias_TerminatesRatherThanHanging()
    {
        // Null-cancel scripts redefine aliases from inside one another, so a config that loops is a
        // plausible mistake — and hanging at startup is the worst place to discover it.
        KeyBindings bindings = new();

        int applied = bindings.ApplySourceConfigs(
        [
            "alias +a \"+b\"\nalias +b \"+a\"\nbind \"z\" \"+a\"",
        ]);

        applied.ShouldBe(0);
        bindings.KeyFor(ViewerAction.FlyForward).ShouldBe("w", "and the defaults survive");
    }

    [Test]
    public void ReadAliases_LaterDefinitions_Win()
    {
        IReadOnlyDictionary<string, string> aliases = SourceConfig.ReadAliases(
            "alias +mfwd \"+forward\"\nalias +mfwd \"+back\"");

        aliases["+mfwd"].ShouldBe("+back");
    }

    [Test]
    public void ActionOf_TheCommandsTf2Uses_Resolve()
    {
        KeyBindings.ActionOf("+forward").ShouldBe(ViewerAction.FlyForward);
        KeyBindings.ActionOf("+jump").ShouldBe(ViewerAction.SwitchCameraMode);
        KeyBindings.ActionOf("+moveup").ShouldBe(ViewerAction.FlyUp);
        KeyBindings.ActionOf("+speed").ShouldBe(ViewerAction.FlyWalk);
    }

    [Test]
    public void ActionOf_ACommandWeDoNotImplement_IsNullRatherThanThrowing()
    {
        // Every one of the hundreds of commands in a real config arrives here and has to be waved
        // through. This is the "ignoring is the feature" rule at its narrowest point.
        KeyBindings.ActionOf("+duck").ShouldBeNull();
        KeyBindings.ActionOf("toggleconsole").ShouldBeNull();
        KeyBindings.ActionOf("").ShouldBeNull();
        KeyBindings.ActionOf(null).ShouldBeNull();
    }

    [Test]
    public void Commands_EveryAction_HasOne()
    {
        // An action with no command name cannot be bound from a config at all, and it would fail
        // silently — the line is simply ignored like any unknown command.
        foreach (ViewerAction action in Enum.GetValues<ViewerAction>())
        {
            KeyBindings.Commands.ContainsKey(action).ShouldBeTrue($"{action} has no command name");
        }
    }
}
