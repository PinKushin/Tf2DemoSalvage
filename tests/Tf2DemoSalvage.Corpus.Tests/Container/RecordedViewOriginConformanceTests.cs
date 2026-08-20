using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

// Namespaced away from `Tf2DemoSalvage.Corpus.Tests.*` because inside it the name `Corpus` binds
// to the namespace rather than to the helper class.
namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// A demo's recorded view origin is the recorder's ENTITY origin, not their eye.
/// </summary>
/// <remarks>
/// **The SDK cannot answer this one, and that is why it is measured rather than cited.** The half
/// of the engine that FILLS <c>democmdinfo_t</c> is <c>cl_demo.cpp</c>, which is not in
/// <c>source-sdk-2013</c>. What the SDK does give is the relationship a live client uses —
/// <c>baseentity_shared.cpp</c>:
///
/// <code>
/// Vector CBaseEntity::EyePosition( void )
/// {
///     return GetAbsOrigin() + GetViewOffset();
/// }
/// </code>
///
/// — so the question was whether the recorder wrote down <c>GetAbsOrigin()</c> or
/// <c>EyePosition()</c>. **It writes the origin.** Measured 2026-08-19 against every point-of-view
/// demo in the corpus: the recorded view and the recorder's own networked origin agree to the
/// hundredth at every tick, with a median difference of exactly zero over 3,807 ticks on the 2007
/// specimen alone.
///
/// **The obvious hypothesis was the wrong one**, and it is worth saying why it was plausible: a
/// recorder writing down "where the camera is" would write the eye. It does not — the client adds
/// the view offset when it draws, so the demo carries the position and the drawing code carries
/// the height.
///
/// **The consequence for a first-person camera is that it must add the offset itself, and the
/// offset is per class.** <c>tf_gamerules.cpp:1330</c> lists them: 65 for a scout, 68 for a
/// soldier, demoman and pyro, and so on, with the generic <c>VEC_VIEW</c> at 72. A camera that
/// used one number for everyone would sit a few units wrong for six of the nine classes.
///
/// **A second thing falls out of this for free.** The recorded view and the entity stream are
/// decoded by two completely unrelated paths — a fixed-layout struct in the command prologue, and
/// a delta-compressed bit stream against a networked schema — and they agree exactly. That makes
/// this a check on the entity decode as much as on the container, which is the
/// <c>two-recordings-of-one-value</c> pattern: a value stored twice by unrelated routes tests the
/// decode against the engine rather than against our own reading of it.
///
/// Only point-of-view demos can answer it. A SourceTV recording has no local player and leaves the
/// structure zeroed — see <c>docs/findings/01-container.md</c>.
/// </remarks>
public sealed class RecordedViewOriginConformanceTests
{
    [Test]
    public void RecordedView_OnAPointOfViewDemo_IsTheRecordersOriginNotTheirEye()
    {
        List<string> measured = [];

        foreach (string path in Corpus.Files().Where(IsPointOfView))
        {
            if (Recorder(path) is not { } slot)
            {
                continue;
            }

            DemoTimeline timeline = TimelineCache.For(path);

            // The median rise over every tick where both paths spoke, so one odd frame — a
            // respawn, a teleport, a tick either path lacks — cannot decide the answer.
            List<float> rises = [];
            List<string> samples = [];

            foreach ((int tick, RecordedView view) in Views(path))
            {
                // **ScenePlayer is a record STRUCT**, so FirstOrDefault returns a zeroed player
                // rather than null and `is null` would never fire — a default whose Z is 0 would
                // then be measured as a rise equal to the camera height and quietly support the
                // conclusion. See docs/memory/nullable-pattern-on-a-struct-is-dead-code.md.
                List<ScenePlayer> matches =
                [
                    .. timeline.PlayersAt(tick)
                        .Where(player => player.EntityIndex == slot + 1),
                ];

                if (matches.Count == 0)
                {
                    continue;
                }

                rises.Add(view.Origin.Z - matches[0].Z);

                if (samples.Count < 3)
                {
                    samples.Add(
                        $"tick {tick} view z {view.Origin.Z:0.##} entity z {matches[0].Z:0.##}");
                }
            }

            if (rises.Count == 0)
            {
                continue;
            }

            rises.Sort();
            float median = rises[rises.Count / 2];
            measured.Add(
                $"{Path.GetFileName(path)}: median rise {median:0.##} over {rises.Count} ticks, " +
                $"sample {string.Join(" | ", samples.Take(3))}");

            // **A median of exactly zero has two explanations and they are not alike**: the view
            // really is the entity origin, or BOTH numbers are zero because one of the two paths
            // produced nothing. The samples are written out BEFORE any assertion so the numbers
            // survive a failure — an assertion message that fires first would hide them.
            TestContext.Out.WriteLine(measured[^1]);

            // **The measured answer: the recorded view IS the entity origin, exactly.** Not
            // approximately — the two agree to the hundredth, sample after sample, and they are
            // not zero (the 2007 demo sits at z −287.88 at every tick checked), so this is
            // agreement rather than two absent values.
            //
            // The hypothesis that failed here was the obvious one: that a recorder writes down
            // where the camera is, which is the eye. It writes GetAbsOrigin() instead, and the
            // client adds the view offset when it draws.
            median.ShouldBe(
                0f,
                0.01f,
                $"{Path.GetFileName(path)}: the recorded view sits {median:0.##} from the " +
                $"recorder's origin, so it is no longer simply the entity origin");
        }

        // A positive control. Every assertion above is inside a loop that a bad filter, a missing
        // server info or an entity-index mismatch would empty — and an empty loop passes.
        measured.ShouldNotBeEmpty(
            "no point-of-view demo could be matched to its recorder, so nothing was measured");

        TestContext.Out.WriteLine(string.Join(Environment.NewLine, measured));
    }

    /// <summary>The recording player's slot, from <c>svc_ServerInfo</c>.</summary>
    /// <remarks>
    /// Named by the demo rather than inferred. Picking whichever entity moves most like the camera
    /// would be an instrument that agrees with the hypothesis by construction.
    /// </remarks>
    private static int? Recorder(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        NetDecodeState state = new()
        {
            NetworkProtocol = (ushort)Corpus.Header(path).NetworkProtocol,
        };

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (INetMessage message in NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                if (message is ServerInfoMessage info)
                {
                    return info.PlayerSlot;
                }
            }
        }

        return null;
    }

    /// <summary>Every packet's tick and recorded view, in stream order.</summary>
    private static IEnumerable<(int Tick, RecordedView View)> Views(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        return
        [
            .. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))
                .Where(command =>
                    command.Type == DemoCommandType.Packet &&
                    command.Prologue.Length >= RecordedView.SizeBytes)
                .Select(command => (command.Tick, RecordedView.Parse(command.Prologue.Span))),
        ];
    }

    private static bool IsPointOfView(string path) =>
        !string.Equals(
            Corpus.Header(path).ClientName, "SourceTV Demo", StringComparison.Ordinal);
}
