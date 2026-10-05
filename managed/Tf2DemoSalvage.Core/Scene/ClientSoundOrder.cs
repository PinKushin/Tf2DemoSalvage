using System;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>Where on a client frame a sound the client emits itself is made — `CHLClient::OnRenderStart`'s order (B505).</summary>
/// <remarks>
/// `cdll_client_int.cpp:2137-2255`: game events are fired while the packet is parsed, before the frame; then
/// `ProcessOnDataChangedEvents`, `SimulateEntities` (`C_BaseAnimating::Simulate` → `DoAnimationEvents`), `PhysicsSimulate`,
/// and last `engine->FireEvents()`, the temp entities.
/// </remarks>
public enum ClientSoundPhase
{
    /// <summary>A game event's handler, during the packet's parse (a HUD sound).</summary>
    Network = 0,

    /// <summary>`ProcessOnDataChangedEvents` (a medigun's patch).</summary>
    DataChanged = 1,

    /// <summary>`SimulateEntities` (animation events, footsteps).</summary>
    Simulate = 2,

    /// <summary>`PhysicsSimulate` (physics impacts, friction).</summary>
    Physics = 3,

    /// <summary>`engine->FireEvents()` (explosions, impacts, the tracer whiz).</summary>
    TempEntities = 4,
}

/// <summary>A client sound's place within its tick: its phase, its temp entity's place in the stream, and its place within that.</summary>
/// <param name="Phase">The frame phase.</param>
/// <param name="TempEntity">For <see cref="ClientSoundPhase.TempEntities"/>, the temp entity's 1-based place among all of the demo's; else 0.</param>
/// <param name="Within">Its place among the sounds one temp entity makes (a pellet's index, a ricochet before its impact).</param>
public readonly record struct ClientSoundOrder(ClientSoundPhase Phase, int TempEntity, int Within) : IComparable<ClientSoundOrder>
{
    /// <inheritdoc/>
    public int CompareTo(ClientSoundOrder other)
    {
        int byPhase = Phase.CompareTo(other.Phase);

        if (byPhase != 0)
        {
            return byPhase;
        }

        int byEntity = TempEntity.CompareTo(other.TempEntity);

        return byEntity != 0 ? byEntity : Within.CompareTo(other.Within);
    }

    /// <summary>Whether one comes before another.</summary>
    public static bool operator <(ClientSoundOrder left, ClientSoundOrder right) => left.CompareTo(right) < 0;

    /// <summary>Whether one comes after another.</summary>
    public static bool operator >(ClientSoundOrder left, ClientSoundOrder right) => left.CompareTo(right) > 0;

    /// <summary>Whether one comes before or with another.</summary>
    public static bool operator <=(ClientSoundOrder left, ClientSoundOrder right) => left.CompareTo(right) <= 0;

    /// <summary>Whether one comes after or with another.</summary>
    public static bool operator >=(ClientSoundOrder left, ClientSoundOrder right) => left.CompareTo(right) >= 0;
}
