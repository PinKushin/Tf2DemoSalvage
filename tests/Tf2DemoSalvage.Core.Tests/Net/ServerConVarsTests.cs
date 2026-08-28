using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// That a value the server replicated wins over Valve's default, and nothing else does.
/// </summary>
/// <remarks>
/// **This is the half D106 is actually about.** `NET_SetConVar` has been decoded and round-tripped
/// since the container work; nothing consumed it, so a server that raised `sv_maxspeed` sent the
/// new value, this project decoded it, and every reader used a baked constant instead. The failure
/// is silent by construction — the value arrives, is correct, and is ignored.
///
/// **The precedence is Valve's, not a preference.** `iconvar.h` documents `FCVAR_REPLICATED` as
/// *"server setting enforced on clients"*, and *"At signon, the values of all such ConVars are sent
/// from the server to the client"*. So the server's value is not merely a hint that loses to a
/// local setting: it replaces it. For the movement speeds the point is sharper still, because they
/// are also `FCVAR_CHEAT` — a player cannot change them at all without `sv_cheats`, so the watcher's
/// own config has no business in the answer.
///
/// **The owner's framing of why this matters more than the arithmetic:** *"the cvars can change by
/// server, some mods will change move speed and all the other settings for the most part, like
/// jailbreak. the only mods we might currently work with are DM and MGE, because those keep most
/// things constant, but jump, surf, and other mods might not run right."*
/// </remarks>
public sealed class ServerConVarsTests
{
    [Test]
    public void Value_WithNothingReplicated_IsValvesDeclaredDefault()
    {
        ServerConVars server = new();

        server.Value("sv_maxspeed").ShouldBe("320");
        server.Number("sv_maxspeed").ShouldBe(320f);
    }

    [Test]
    public void Value_AfterTheServerSentOne_IsTheServersValue()
    {
        ServerConVars server = new();

        server.Apply(Message(("sv_maxspeed", "520")));

        server.Number("sv_maxspeed").ShouldBe(520f);
    }

    /// <summary>That one changed ConVar does not disturb the others.</summary>
    /// <remarks>
    /// **The control that makes the test above mean something.** With a single ConVar in play,
    /// "applied the server's value" and "replaced everything" are the same observation — the third
    /// route to an insensitive test in the global standards, no control. `sv_specspeed` is the
    /// bystander that must survive.
    /// </remarks>
    [Test]
    public void Value_AfterOneConVarChanged_LeavesTheOthersAtTheirDefaults()
    {
        ServerConVars server = new();

        server.Apply(Message(("sv_maxspeed", "520")));

        server.Number("sv_specspeed").ShouldBe(3f);
        server.Number("cl_forwardspeed").ShouldBe(450f);
    }

    /// <summary>That a later message wins, because a server may change a ConVar mid-match.</summary>
    /// <remarks>
    /// `iconvar.h`: *"If a value is changed while a server is active, it's replicated to all
    /// connected clients"*. So this is not merely last-write-wins by convenience — the engine sends
    /// a second message precisely when the value has moved, and a reader that kept the first would
    /// be showing the wrong half of the demo.
    /// </remarks>
    [Test]
    public void Value_AfterASecondMessageForTheSameName_TakesTheLater()
    {
        ServerConVars server = new();

        server.Apply(Message(("sv_maxspeed", "520")));
        server.Apply(Message(("sv_maxspeed", "400")));

        server.Number("sv_maxspeed").ShouldBe(400f);
    }

    /// <summary>That a name nobody declared is carried anyway, and reported as unknown.</summary>
    /// <remarks>
    /// **A real match demo sends forty values and this project declares eight.** Refusing the other
    /// thirty-two would throw while decoding an ordinary demo, and dropping them silently would
    /// discard the record of what the server actually was — which is the interesting half for the
    /// mod question. So they are kept and answerable; only <see cref="ServerConVars.Number"/> on an
    /// undeclared name is an error, because there is no default to fall back to.
    /// </remarks>
    [Test]
    public void Value_ForANameNothingDeclares_IsWhatTheServerSent()
    {
        ServerConVars server = new();

        server.Apply(Message(("mp_tournament", "1")));

        server.Value("mp_tournament").ShouldBe("1");
    }

    [Test]
    public void Value_ForANameNeitherDeclaredNorSent_IsNull()
    {
        new ServerConVars().Value("mp_tournament").ShouldBeNull();
    }

    /// <summary>That the movement speeds a jump server changes are all reachable.</summary>
    /// <remarks>
    /// Named rather than swept: these are the ones a mod moves, and the free camera derives its
    /// speed from two of them. A test over `EngineConVars.All` would pass by walking whatever
    /// happens to be declared — see the walking-test entry in the memory directory.
    /// </remarks>
    [Test]
    public void Value_ForEveryMovementConVarAServerCanChange_TakesTheServersValue()
    {
        ServerConVars server = new();

        server.Apply(Message(
            ("sv_maxspeed", "1000"),
            ("sv_specspeed", "6"),
            ("sv_specaccelerate", "10"),
            ("cl_forwardspeed", "900"),
            ("cl_sidespeed", "900"),
            ("cl_upspeed", "640")));

        server.Number("sv_maxspeed").ShouldBe(1000f);
        server.Number("sv_specspeed").ShouldBe(6f);
        server.Number("sv_specaccelerate").ShouldBe(10f);
        server.Number("cl_forwardspeed").ShouldBe(900f);
        server.Number("cl_sidespeed").ShouldBe(900f);
        server.Number("cl_upspeed").ShouldBe(640f);
    }

    /// <summary>That a value the server sends in a form no number can be read from is refused.</summary>
    /// <remarks>
    /// **Loudly, not as zero.** A `sv_maxspeed` of zero is a camera that will not move and a symptom
    /// with no cause attached; the engine would refuse the assignment outright. This is the same
    /// reasoning as `EngineConVar.Number` throwing rather than defaulting.
    /// </remarks>
    [Test]
    public void Number_WhenTheServerSentSomethingUnparseable_Throws()
    {
        ServerConVars server = new();

        server.Apply(Message(("sv_maxspeed", "fast")));

        Should.Throw<FormatException>(() => server.Number("sv_maxspeed"));
    }

    [Test]
    public void Number_ForANameNothingDeclares_Throws()
    {
        ServerConVars server = new();

        server.Apply(Message(("mp_tournament", "1")));

        Should.Throw<KeyNotFoundException>(() => server.Number("mp_tournament"));
    }

    /// <summary>That what the server changed can be reported, for the mod question.</summary>
    /// <remarks>
    /// The owner's open question is whether jump and surf demos replay correctly, and the answer
    /// starts with knowing which of the values this project depends on a server moved. A viewer that
    /// silently used the right numbers would still leave that unanswerable.
    /// </remarks>
    [Test]
    public void Changed_AfterAServerMovedTwoDeclaredValues_NamesJustThose()
    {
        ServerConVars server = new();

        server.Apply(Message(
            ("sv_maxspeed", "1000"), ("mp_tournament", "1"), ("sv_specspeed", "6")));

        server.Changed.ShouldBe(["sv_maxspeed", "sv_specspeed"], ignoreOrder: true);
    }

    /// <summary>That a server re-sending Valve's own value is not reported as a change.</summary>
    /// <remarks>
    /// **Measured on real demos, and it is the common case.** A competitive server sends forty
    /// values and most match the defaults — `sv_maxspeed 320` among them. Reporting those as
    /// changes would make every demo look like a mod.
    /// </remarks>
    [Test]
    public void Changed_WhenTheServerResentTheDefault_IsEmpty()
    {
        ServerConVars server = new();

        server.Apply(Message(("sv_maxspeed", "320")));

        server.Changed.ShouldBeEmpty();
    }

    /// <summary>That reading a value while a message arrives does not corrupt the settings.</summary>
    /// <remarks>
    /// **A real defect the gate caught, 2026-08-27, in production rather than in a test.** The
    /// viewer decodes a demo off the UI thread, so `svc_SetConVar` is applied from there, while the
    /// free camera reads `sv_maxspeed` every frame on the UI thread. `Number` used to memoise its
    /// parse into a plain `Dictionary`, which made the READ a write:
    ///
    /// <code>
    /// System.InvalidOperationException : Operations that change non-concurrent collections must
    /// have exclusive access. A concurrent update was performed on this collection and corrupted
    /// its state.
    /// </code>
    ///
    /// out of `FreeFlightPath.SpeedPerSecond` in `Movement_Forward_TravelsSpeedTimesDuration` — a
    /// test with nothing to do with threading, which is how this class of fault surfaces.
    ///
    /// **The fix is structural, so this test is one-sided by design.** Settings are published as a
    /// whole immutable snapshot and readers never write, so there is no interleaving that can fail;
    /// against the old code the same loop throws. A pass is not a probabilistic pass, it is the
    /// absence of any mutation to race with.
    ///
    /// **Measured in both directions rather than assumed.** With the lazy memoisation restored
    /// exactly as it was, this failed three runs out of three with the exception quoted above; with
    /// the snapshot in place it passed four out of four. That asymmetry is what makes it a test
    /// rather than a hope.
    ///
    /// **The size of the condition was chosen by measurement too, and the first attempt was too
    /// small to see anything.** A single reader over 20,000 iterations could not distinguish the
    /// broken code from the fixed — four readers over 200,000 can. The assertion was never the
    /// problem; the condition was.
    ///
    /// **Both halves of the answer are asserted, not just the absence of a throw.** A reader must
    /// see either the value before a message or the value after it — never a half-applied mixture
    /// and never a torn parse — so every observation is required to be one of the two.
    /// </remarks>
    [Test]
    public void Number_WhileAMessageIsBeingApplied_IsAlwaysOneOfTheTwoValues()
    {
        const int Readers = 4;
        const int Iterations = 200_000;

        ServerConVars server = new();

        HashSet<float>[] seen = [.. Enumerable.Range(0, Readers).Select(_ => new HashSet<float>())];

        using Barrier start = new(Readers + 1);

        // Tasks rather than raw threads, so a fault is captured and rethrown here instead of
        // terminating the test host — and so no `catch` is needed to observe one.
        Task writer = Task.Run(() =>
        {
            start.SignalAndWait();

            for (int at = 0; at < Iterations; at++)
            {
                server.Apply(Message(("sv_maxspeed", at % 2 == 0 ? "320" : "520")));
            }
        });

        Task[] readers =
        [
            .. Enumerable.Range(0, Readers).Select(which => Task.Run(() =>
            {
                start.SignalAndWait();

                for (int at = 0; at < Iterations; at++)
                {
                    seen[which].Add(server.Number("sv_maxspeed"));
                }
            })),
        ];

        Task.WaitAll([writer, .. readers]);

        // 320 is the default and the even value, so a reader may legitimately never observe 520 if
        // it outruns the writer. What none may observe is a third number.
        foreach (HashSet<float> observed in seen)
        {
            observed.ShouldBeSubsetOf([320f, 520f]);
            observed.ShouldNotBeEmpty();
        }
    }

    private static SetConVarMessage Message(params (string Name, string Value)[] pairs)
    {
        List<KeyValuePair<string, string>> variables = [];

        foreach ((string name, string value) in pairs)
        {
            variables.Add(new KeyValuePair<string, string>(name, value));
        }

        return new SetConVarMessage(variables);
    }
}
