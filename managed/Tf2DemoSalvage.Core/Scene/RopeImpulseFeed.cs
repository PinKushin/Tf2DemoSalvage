using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One rope's instantaneous force, as its entity message carried it (B478).</summary>
/// <param name="Tick">The tick its packet arrived on.</param>
/// <param name="EntityIndex">The rope.</param>
/// <param name="Impulse"><c>m_flImpulse</c> as <c>ReceiveMessage</c> sets it.</param>
public readonly record struct SceneRopeImpulse(int Tick, int EntityIndex, (float X, float Y, float Z) Impulse);

/// <summary>
/// Every <c>svc_EntityMessage</c> a <c>C_RopeKeyframe</c> takes as an impulse — <c>ReceiveMessage</c>
/// (`c_rope.cpp:2082-2095`) (B478).
/// </summary>
/// <remarks>
/// **The only sender is the <c>SetForce</c> input**: <c>CRopeKeyframe::InputSetForce</c> → <c>PropagateForce</c>, which
/// writes three floats to this rope and walks on down the chain through <c>m_hEndPoint</c> (`rope.cpp:520-546`). No
/// installed TF2 map wires that input and no corpus demo carries the message (every entity message measured is
/// <c>CBaseAnimating</c>'s <c>RemoveAllDecals</c>, B30), so this is the path a third-party map takes.
/// </remarks>
public sealed class RopeImpulseFeed
{
    /// <summary>The rope's server class.</summary>
    public const string RopeClassName = "CRopeKeyframe";

    private readonly List<SceneRopeImpulse> _impulses = [];

    /// <summary>Every impulse, in arrival order.</summary>
    public IReadOnlyList<SceneRopeImpulse> All => _impulses;

    /// <summary>Takes an entity message if it is a rope's own.</summary>
    /// <param name="message">The message.</param>
    /// <param name="entityClassName">The class of the entity it is addressed to, or null when there is none.</param>
    /// <param name="entityClassId">That entity's class id — the <c>GetClientClass()-&gt;m_ClassID</c> it is compared with.</param>
    /// <param name="tick">The tick its packet arrived on.</param>
    /// <returns>Whether it was an impulse.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public bool Record(EntityMessage message, string? entityClassName, int entityClassId, int tick)
    {
        ArgumentNullException.ThrowIfNull(message);

        // "message is for subclass" — any other class id goes to C_BaseEntity's handler.
        if (!string.Equals(entityClassName, RopeClassName, StringComparison.Ordinal) || message.ClassId != entityClassId)
        {
            return false;
        }

        ReadOnlySpan<byte> body = message.Body.Span;
        int floats = Math.Min(message.BodyBits, body.Length * 8) / 32;

        _impulses.Add(new SceneRopeImpulse(
            tick, message.EntityIndex, (Float(body, 0, floats), Float(body, 1, floats), Float(body, 2, floats))));

        return true;
    }

    /// <summary><c>bf_read::ReadFloat</c>, or 0 past the end as an overflowed read gives.</summary>
    private static float Float(ReadOnlySpan<byte> body, int index, int available) =>
        index < available ? BinaryPrimitives.ReadSingleLittleEndian(body[(index * 4)..]) : 0f;
}
