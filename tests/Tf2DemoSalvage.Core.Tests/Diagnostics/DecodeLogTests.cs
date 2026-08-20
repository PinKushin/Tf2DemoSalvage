using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Core.Diagnostics;

namespace Tf2DemoSalvage.Core.Tests.Diagnostics;

/// <summary>
/// The sink Core reports losses through, and the separate one it reports counts through.
/// </summary>
/// <remarks>
/// **The whole point of this class is that a silent fallback is banned**, so the thing worth
/// testing is that a message actually leaves the library — not that the method can be called. Every
/// catch in Core that carries on rather than throwing routes through here, and a sink that dropped
/// what it was given would restore exactly the silence this replaced: a catch with a justifying
/// comment and no output.
///
/// The two channels are separate deliberately. A count is not a warning, and routing "read 4,812
/// ambient samples" to the warning channel trains a reader to ignore the word — so a test that
/// treated them as one would pass while the distinction quietly collapsed.
///
/// Not parallelisable with anything else that logs: the sinks are static, which is the right shape
/// for one process and one log and the wrong shape for concurrent tests. Restored in a finally, so
/// a failing assertion does not leave a sink attached to a dead closure.
/// </remarks>
[NonParallelizable]
public sealed class DecodeLogTests
{
    [Test]
    public void Lost_WithASinkAttached_DeliversTheCategoryAndTheMessage()
    {
        // The category is what lets a reader filter a log to one subsystem, so it travels
        // separately rather than being prefixed onto the message.
        List<(string Category, string Message)> written = Capture(
            () => DecodeLog.Lost("entities", "a snapshot at tick 900 would not decode"));

        written.ShouldHaveSingleItem();
        written[0].Category.ShouldBe("entities");
        written[0].Message.ShouldBe("a snapshot at tick 900 would not decode");
    }

    [Test]
    public void Lost_WithAnException_NamesItsTypeAndItsMessage()
    {
        // **The type matters as much as the text.** "Unexpected end of stream" and "the property
        // index is past the end of its class" are different diagnoses that can carry similar
        // prose, and the exception type is what separates a truncation from a desynchronisation.
        List<(string Category, string Message)> written = Capture(
            () => DecodeLog.Lost(
                "entities",
                "decoding a snapshot at tick 900",
                new InvalidDataException("the stream desynchronised")));

        written.ShouldHaveSingleItem();
        written[0].Message.ShouldBe(
            "decoding a snapshot at tick 900: InvalidDataException: the stream desynchronised");
    }

    [Test]
    public void Note_GoesToTheOtherChannel_SoACountIsNotAWarning()
    {
        // The distinction this class exists to keep. A note must not appear on the loss channel,
        // and the assertion is stated both ways because a single sink receiving everything would
        // satisfy either half alone.
        List<(string, string)> losses = [];
        List<(string, string)> notes = [];

        Action<string, string>? sink = DecodeLog.Sink;
        Action<string, string>? previousNotes = DecodeLog.Notes;

        try
        {
            DecodeLog.Sink = (category, message) => losses.Add((category, message));
            DecodeLog.Notes = (category, message) => notes.Add((category, message));

            DecodeLog.Note("assets", "read 4,812 ambient samples");
        }
        finally
        {
            DecodeLog.Sink = sink;
            DecodeLog.Notes = previousNotes;
        }

        notes.ShouldHaveSingleItem();
        losses.ShouldBeEmpty();
    }

    [Test]
    public void Lost_WithNoSink_DoesNothingRatherThanThrowing()
    {
        // **A library consumer has not asked for a logging dependency**, so nothing attached must
        // mean nothing happens. Throwing here would turn "carry on, but say so" back into "fail",
        // which is the behaviour this class was written to replace.
        Action<string, string>? sink = DecodeLog.Sink;

        try
        {
            DecodeLog.Sink = null;

            Should.NotThrow(() => DecodeLog.Lost("entities", "nobody is listening"));
            Should.NotThrow(
                () => DecodeLog.Lost("entities", "nor here", new InvalidDataException("x")));
        }
        finally
        {
            DecodeLog.Sink = sink;
        }
    }

    [Test]
    public void Lost_WithANullException_IsRefused()
    {
        // The overload's whole value is the exception's type and message, so a null is a caller
        // bug rather than a degraded case worth handling.
        Should.Throw<ArgumentNullException>(
            () => DecodeLog.Lost("entities", "something", null!));
    }

    /// <summary>Runs an action with a capturing sink attached, and restores what was there.</summary>
    private static List<(string Category, string Message)> Capture(Action action)
    {
        List<(string Category, string Message)> written = [];
        Action<string, string>? previous = DecodeLog.Sink;

        try
        {
            DecodeLog.Sink = (category, message) => written.Add((category, message));
            action();
        }
        finally
        {
            DecodeLog.Sink = previous;
        }

        return written;
    }
}
