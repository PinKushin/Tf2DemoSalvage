using System;
using System.Diagnostics.CodeAnalysis;

using NUnit.Framework;

namespace Tf2DemoSalvage.SdkReference;

/// <summary>
/// Stops the calling test because this machine lacks something it needs, without failing it.
/// </summary>
/// <remarks>
/// **One place decides what an absent prerequisite does, because ninety-one files were each
/// deciding it themselves.** Every conformance suite that reads Team Fortress 2 or
/// <c>source-sdk-2013</c> carried its own gate — its own list of Steam library roots, its own
/// existence check, its own reason string — and the gate is written from memory each time.
///
/// **Getting one wrong has already cost two red CI runs**, on separate pushes on 2026-08-27. CI is
/// the machine without the game and is the only place the no-install path runs at all, so a suite
/// that asserts rather than skips reports a missing environment as a defect in the code. The
/// message it prints is true; the conclusion drawn from it is not.
///
/// **A skip is neither a pass nor a failure, and that is the property being preserved.** It still
/// counts toward the suite total, so <c>build/gate.sh</c>'s exact count floors keep working
/// unchanged — a test that skips has not gone missing. What it does not do is redden a build over
/// a fact about the machine.
///
/// **Nothing here decides WHETHER to skip.** <see cref="GameInstall"/> and <see cref="SourceSdk"/>
/// answer that, each returning null when their subject is absent; this type only turns that null
/// into the right kind of stop. Keeping the two apart is what lets a test that has to survey
/// several maps ask <see cref="GameInstall.Find"/> and carry on past the ones it does not have,
/// rather than abandoning the ones it does.
/// </remarks>
public static class Skip
{
    /// <summary>Stops the calling test, reporting it as skipped for the reason given.</summary>
    /// <param name="reason">What is missing, and ideally how to supply it.</param>
    /// <exception cref="IgnoreException">Always; that is how NUnit records a skip.</exception>
    /// <remarks>
    /// <c>[DoesNotReturn]</c> is what makes this usable as a statement rather than as a thrown
    /// expression: the compiler treats everything after a call as unreachable, so a caller needs no
    /// <c>throw new InvalidOperationException("unreachable")</c> to satisfy definite assignment.
    /// <c>MapCache.RequirePath</c> carried exactly that line before this existed.
    /// </remarks>
    [DoesNotReturn]
    public static void Because(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Assert.Ignore(reason);

        // Unreachable: Assert.Ignore throws. Stated so the attribute above is a fact about this
        // method rather than a promise about somebody else's.
        throw new InvalidOperationException(reason);
    }

    /// <summary>The value, or skips the calling test when there is none.</summary>
    /// <typeparam name="T">What was being looked for — a path, a file, a handle.</typeparam>
    /// <param name="found">The result of looking, or null when it was not there.</param>
    /// <param name="reason">What is missing, and ideally how to supply it.</param>
    /// <returns>Exactly <paramref name="found"/>, known to be non-null.</returns>
    /// <remarks>
    /// The whole gate in one expression, which is the point: a caller writes
    /// <c>Skip.Unless(GameInstall.Root, GameInstall.Missing)</c> and cannot accidentally write an
    /// assertion instead. The null-state analysis flows through <see cref="Because"/>'s
    /// <c>[DoesNotReturn]</c>, so the return needs no null-forgiving operator.
    /// </remarks>
    public static T Unless<T>(T? found, string reason)
        where T : class
    {
        if (found is null)
        {
            Because(reason);
        }

        return found;
    }
}
