using System;

namespace Tf2DemoSalvage.Fuzz;

/// <summary>
/// Thrown when a fuzz target observes a violation of the property in D8 - i.e. the parser did
/// something other than succeed or fail in a documented way.
/// </summary>
/// <remarks>
/// A distinct type on purpose: libFuzzer reports any escaping exception as a crash, so without
/// one it is impossible to tell "the parser threw something undocumented" from "the harness
/// itself is broken". This type always means the former.
/// </remarks>
public sealed class FuzzPropertyViolationException : Exception
{
    public FuzzPropertyViolationException()
    {
    }

    public FuzzPropertyViolationException(string message)
        : base(message)
    {
    }

    public FuzzPropertyViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
