using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Core.Tests.SceneConVars;

/// <summary>
/// That a real demo's replicated ConVars reach the timeline, and that a vanilla one changes nothing.
/// </summary>
/// <remarks>
/// **This is the assertion the unit tests cannot make.** `ServerConVarsTests` proves the type works
/// when handed messages the test wrote; it says nothing about whether production ever hands it any.
/// That gap has shipped three no-ops in this project with a green suite each time — the dumper's
/// kill annotation, the kill feed's headshot field, and `m_flPlaybackRate`. So: one assertion
/// against a real file, reading what the decode actually produced.
///
/// **The SourceTV specimens are the right sample and the POVs are not.** The era STV recordings
/// carry `net_setconvar` with six to nine values — `sv_skyname`, `mp_timelimit`, `think_limit`,
/// `sv_turbophysics` — while the era POVs carry none, because they are the owner's own solo
/// recordings against a server that changed nothing. A test on a POV would pass against a decoder
/// that dropped the message entirely.
/// </remarks>
public sealed class CorpusServerConVarTests
{
    /// <summary>An era SourceTV recording, which carries the message. See the class remarks.</summary>
    private const string StvDemo = "tf2-2011-build4604-stv-koth_viaduct";

    /// <summary>A solo POV recording, whose server changed nothing at all.</summary>
    private const string PovDemo = "tf2-2013-build1729296-pov-cp_badlands";

    [Test]
    public void ServerConVars_OnAnStvDemo_CarryWhatTheServerActuallySent()
    {
        DemoTimeline timeline = TimelineOf(StvDemo);

        // **`sv_skyname` rather than a count**, because a count is satisfied by any six values and
        // this names one the server genuinely set. It is also the one that proves the pairing
        // survived: a decoder that lost the name/value alignment would answer with a tick limit or
        // a physics flag here.
        timeline.ServerConVars.Value("sv_skyname")
            .ShouldNotBeNullOrWhiteSpace("this recording's server named its skybox");
    }

    /// <summary>That a vanilla server leaves every movement ConVar at Valve's default.</summary>
    /// <remarks>
    /// **The control, and it is doing two jobs.** It says the decode did not invent values, and it
    /// says what "not a mod" looks like — which is what `Changed` has to distinguish a jump server
    /// from. Without it, "took the server's values" and "took anything at all" are the same
    /// observation on a demo that changed nothing.
    /// </remarks>
    [Test]
    public void ServerConVars_OnAVanillaDemo_LeaveEveryMovementConVarAtItsDefault()
    {
        DemoTimeline timeline = TimelineOf(StvDemo);

        timeline.ServerConVars.Changed.ShouldBeEmpty(
            "no era specimen was recorded on a server that changed movement");

        timeline.ServerConVars.Number("sv_maxspeed").ShouldBe(320f);
        timeline.ServerConVars.Number("sv_specspeed").ShouldBe(3f);
        timeline.ServerConVars.Number("cl_forwardspeed").ShouldBe(450f);
    }

    /// <summary>That a demo carrying no such message still answers with the defaults.</summary>
    /// <remarks>
    /// **Not a duplicate of the test above — a different input.** The STV demo sends the message
    /// and changes nothing in it; this one never sends the message at all, which is the path where
    /// a null <c>ServerConVars</c> would throw rather than answer. Both must give 320.
    /// </remarks>
    [Test]
    public void ServerConVars_OnADemoThatSendsNoConVars_AreValvesDefaults()
    {
        DemoTimeline timeline = TimelineOf(PovDemo);

        timeline.ServerConVars.Number("sv_maxspeed").ShouldBe(320f);
        timeline.ServerConVars.Changed.ShouldBeEmpty();
    }

    /// <summary>That every ConVar the server sent is answerable, not only the declared few.</summary>
    /// <remarks>
    /// **A real match demo sends forty values and this project declares eight.** Keeping the rest
    /// is what makes "was this a mod" answerable at all, so dropping them would be a silent loss —
    /// and the era specimens are the cheap place to notice it, because their handful includes
    /// `think_limit` and `sv_turbophysics`, which nothing here declares.
    ///
    /// The camera's end of this is asserted in `Presentation.Tests`: this suite deliberately does
    /// not reference `Presentation`, which would tie the corpus run to Windows and to the measurement
    /// boxes it is meant to stay portable to.
    /// </remarks>
    [Test]
    public void ServerConVars_ForANameNothingDeclares_KeepWhatTheServerSent()
    {
        DemoTimeline timeline = TimelineOf(StvDemo);

        timeline.ServerConVars.Value("sv_turbophysics")
            .ShouldNotBeNull("an undeclared ConVar the server sent is kept, not dropped");
    }

    private static DemoTimeline TimelineOf(string name)
    {
        DecodedDemo decoded = DecodedDemo.Read(Corpus.Demo(name), NullLogger.Instance);

        return decoded.Timeline ?? throw new InvalidOperationException(
            $"{name} decoded without a timeline, so there is nothing to read ConVars from");
    }
}
