using System;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>A user message the HUD chat hooks, with its body as sent — `CBaseHudChat` reads it with its own `bf_read`.</summary>
/// <param name="Tick">The tick of the packet that carried it.</param>
/// <param name="Type">The user message id: <see cref="SayText"/>, <see cref="SayText2"/> or <see cref="TextMsg"/>.</param>
/// <param name="Body">The body's bytes.</param>
/// <remarks>The ids are the head of TF2's registration table, stable in every era (see `UserMessageNames`).</remarks>
public sealed record SceneUserMessage(int Tick, int Type, ReadOnlyMemory<byte> Body)
{
    /// <summary>`SayText`.</summary>
    public const int SayText = 3;

    /// <summary>`SayText2`.</summary>
    public const int SayText2 = 4;

    /// <summary>`TextMsg`.</summary>
    public const int TextMsg = 5;
}
