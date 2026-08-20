using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;

// Namespaced to match its neighbour rather than to the assembly: inside
// `Tf2DemoSalvage.Corpus.Tests.*` the name `Corpus` binds to the namespace rather than to the
// helper class, so every reference to it fails to compile.
namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// The recorded camera, read from every demo the corpus holds.
/// </summary>
/// <remarks>
/// **A unit test cannot say whether these are the right 76 bytes.** The fixture for
/// <c>RecordedViewConformanceTests</c> is written from the SDK's field order, so it proves the
/// reader agrees with my reading of <c>demoformat.h</c> — and would pass just as well if the
/// prologue in a real file were laid out differently, or if the offset the container hands over
/// were off by four. Only a real recording can answer that, which is the whole reason this file
/// exists alongside the conformance one.
///
/// The claims here are deliberately ones that a wrong offset fails:
///
/// - a camera stays inside the world, which is ±16384 units in Source;
/// - a camera MOVES over a recording, so reading a constant field — or a run of zeroes — fails;
/// - pitch and yaw stay in the range angles are actually sent in.
///
/// Reading four bytes off would take a float out of the middle of another and produce enormous or
/// denormal numbers, which the bounds catch. Reading a fixed offset when the flags say otherwise
/// produces plausible numbers in the wrong place, which is what the conformance test covers and
/// this one cannot.
/// </remarks>
public sealed class CorpusRecordedViewTests
{
    /// <summary>Source's world half-extent: a map cannot exceed ±16384 units.</summary>
    private const float WorldHalfExtent = 16384f;

    [Test]
    public void RecordedView_EveryDemo_KeepsItsCameraInsideTheWorld()
    {
        // A wrong offset reads a float spanning two fields and lands far outside any map, so this
        // is the cheap check that the structure is where it is believed to be.
        foreach (string path in Corpus.Files())
        {
            IReadOnlyList<RecordedView> views = Views(path);

            views.ShouldNotBeEmpty($"{Path.GetFileName(path)} has no packet commands");

            foreach (RecordedView view in views)
            {
                float[] parts =
                [
                    view.Origin.X, view.Origin.Y, view.Origin.Z,
                ];

                foreach (float part in parts)
                {
                    float.IsFinite(part).ShouldBeTrue(
                        $"{Path.GetFileName(path)}: origin component {part} is not finite");

                    Math.Abs(part).ShouldBeLessThanOrEqualTo(
                        WorldHalfExtent,
                        $"{Path.GetFileName(path)}: origin {view.Origin} is outside the world");
                }
            }
        }
    }

    [Test]
    public void RecordedView_APointOfViewDemo_HasACameraThatMoves()
    {
        // **The assertion that catches reading the wrong field entirely.** A run of zeroes, a
        // constant, or a field that happens to hold a flag all produce a camera that never moves —
        // and every one of those would satisfy the bounds check above.
        foreach (string path in Corpus.Files().Where(IsPointOfView))
        {
            IReadOnlyList<RecordedView> views = Views(path);

            views.Select(view => view.Origin).Distinct().Count()
                .ShouldBeGreaterThan(
                    1, $"{Path.GetFileName(path)}: the recorded camera never moved");
        }
    }

    [Test]
    public void RecordedView_ASourceTvDemo_CarriesNoRecordedViewAtAll()
    {
        // **Measured 2026-08-19, and it is a constraint on the whole first-person feature rather
        // than a curiosity.** A SourceTV recording has no local player, so there is no client view
        // to write down: every democmdinfo_t in one is zeroed. The camera a viewer shows for an
        // STV demo has to come from the spectated entity's own position instead, which is a
        // different mechanism entirely.
        //
        // Stated as its own test rather than as an exclusion from the one above, because "the
        // corpus happens not to exercise this" and "the format does not carry it here" are
        // different claims and only the second is worth relying on. It also fails loudly if a
        // future STV demo does carry a view, which would mean the rule is narrower than this.
        IReadOnlyList<string> sourceTv = [.. Corpus.Files().Where(path => !IsPointOfView(path))];

        // A positive control: an empty sweep would pass this vacuously, and the corpus is supposed
        // to hold several SourceTV recordings.
        sourceTv.ShouldNotBeEmpty("the corpus has no SourceTV demo to check");

        foreach (string path in sourceTv)
        {
            foreach (RecordedView view in Views(path))
            {
                view.Origin.ShouldBe(
                    (0f, 0f, 0f), $"{Path.GetFileName(path)} carries a recorded view");
            }
        }
    }

    /// <summary>Whether a demo was recorded by a player rather than by SourceTV.</summary>
    /// <remarks>
    /// The client name, which SourceTV always writes as <c>SourceTV Demo</c>. Taken from the
    /// header rather than from the file name: the corpus names its files by point of view as a
    /// convention, and a convention is not evidence. <c>z1800.dem</c> is the case that shows the
    /// difference — its name says nothing and its header says SourceTV.
    /// </remarks>
    private static bool IsPointOfView(string path) =>
        !string.Equals(
            Corpus.Header(path).ClientName, "SourceTV Demo", StringComparison.Ordinal);

    [Test]
    public void RecordedView_EveryDemo_KeepsItsAnglesInTheRangeAnglesAreSentIn()
    {
        // Pitch is clamped to ±90 by the engine and yaw wraps at ±360. A float read from the wrong
        // place passes the world-bounds check often enough to be worth a second, narrower one.
        foreach (string path in Corpus.Files())
        {
            foreach (RecordedView view in Views(path))
            {
                Math.Abs(view.Angles.Pitch).ShouldBeLessThanOrEqualTo(
                    90.001f, $"{Path.GetFileName(path)}: pitch {view.Angles.Pitch}");

                Math.Abs(view.Angles.Yaw).ShouldBeLessThanOrEqualTo(
                    360.001f, $"{Path.GetFileName(path)}: yaw {view.Angles.Yaw}");
            }
        }
    }

    /// <summary>Every packet command's recorded view, in stream order.</summary>
    private static IReadOnlyList<RecordedView> Views(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        return
        [
            .. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))
                .Where(command =>
                    command.Type is DemoCommandType.Packet or DemoCommandType.Signon &&
                    command.Prologue.Length >= RecordedView.SizeBytes)
                .Select(command => RecordedView.Parse(command.Prologue.Span)),
        ];
    }
}
