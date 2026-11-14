using System;

namespace Tf2DemoSalvage.Audio;

/// <summary>One busy dynamic channel, as <see cref="DynamicChannelPick"/> weighs it.</summary>
/// <param name="Source">The entity it plays from.</param>
/// <param name="EntChannel">Its channel, from <c>soundflags.h</c>.</param>
/// <param name="SameSound">Whether it plays the sfx the new sound would.</param>
/// <param name="FromLocalPlayer">Whether its source is the local player (player slot + 1).</param>
/// <param name="Volume">The loudest of its current speaker volumes, at least 0, truncated: 0..255.</param>
/// <param name="SamplesLeft">Its wave's sample count less the mixer's position.</param>
/// <param name="SampleRate">Its wave's sample rate.</param>
/// <param name="Looped">Whether its wave loops.</param>
/// <param name="DistanceSquared">Its origin's squared distance from the listener.</param>
/// <param name="DelayedStart">Whether it was started with a delay, either sign: flag 0x20 at <c>ch +0x13c</c> (B732).</param>
public readonly record struct BusyChannel(
    int Source,
    int EntChannel,
    bool SameSound,
    bool FromLocalPlayer,
    int Volume,
    uint SamplesLeft,
    int SampleRate,
    bool Looped,
    float DistanceSquared,
    bool DelayedStart = false);

/// <summary>A dynamic sound asking for a channel.</summary>
/// <param name="Source">The entity it plays from.</param>
/// <param name="EntChannel">Its channel.</param>
/// <param name="FromLocalPlayer">Whether its source is the local player.</param>
/// <param name="Looped">Whether its wave loops.</param>
/// <param name="DistanceSquared">Its origin's squared distance from the listener.</param>
public readonly record struct ChannelRequest(int Source, int EntChannel, bool FromLocalPlayer, bool Looped, float DistanceSquared);

/// <summary>
/// Where a start is heard from, which the channel steal weighs (B730), and when it starts (B732); default is at the
/// listener, not the player, and at once.
/// </summary>
/// <param name="Origin">The sound's origin, or null when the caller has none, as at the listener.</param>
/// <param name="Listener">The listener now.</param>
/// <param name="FromLocalPlayer">Whether its source is the local player, `svc_ServerInfo`'s player slot + 1.</param>
/// <param name="VoiceSteal">The user's `voice_steal` as the start reads it, or null for its default "2".</param>
/// <param name="Delay">
/// `params.delay` as `CL_DispatchSound` resolved it — for `SND_DELAY`, by <see cref="IAudioSink.DelayFor"/> — above 0
/// the start waits, below 0 it starts that far in (B732).
/// </param>
/// <param name="FromServer">`params.fromserver`: a network sound, whose delay holds the server clock's latch (B732).</param>
public readonly record struct SoundPlace(
    (float X, float Y, float Z)? Origin,
    (float X, float Y, float Z) Listener,
    bool FromLocalPlayer,
    int? VoiceSteal = null,
    float Delay = 0f,
    bool FromServer = false)
{
    /// <summary>A point's squared distance from <see cref="Listener"/>; 0 for none.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The squared distance, summed y, x, z as FUN_18002a480 does.</returns>
    public float DistanceSquared((float X, float Y, float Z)? point) =>
        point is { } at
            ? ((at.Y - Listener.Y) * (at.Y - Listener.Y)) + ((at.X - Listener.X) * (at.X - Listener.X)) +
              ((at.Z - Listener.Z) * (at.Z - Listener.Z))
            : 0f;
}

/// <summary>
/// <c>SND_PickDynamicChannel</c>: which of the 64 dynamic channels a new sound takes, and which it steals when it must
/// (B730). The rules, verbatim with their addresses, are <c>DynamicChannelPickConformanceTests</c>'s remarks.
/// </summary>
/// <remarks>
/// engine.dll <c>FUN_18002a480</c>, build 11191711. A channel started with a delay (B732) is gathered, not reused, when it
/// matches exactly; the caller frees each gathered one when the new sound starts. **Not ported, because nothing here makes
/// it:** <c>SND_DO_NOT_OVERWRITE_EXISTING_ON_CHANNEL</c> (an exact match gives no channel, 0x18002a56c), which
/// <c>SceneSound</c> does not carry.
/// </remarks>
public static class DynamicChannelPick
{
    /// <summary><c>MAX_DYNAMIC_CHANNELS</c>: the walk ends past 0x3f (<c>0x18002a70f</c>).</summary>
    public const int Channels = 64;

    /// <summary>No channel: the sound does not start.</summary>
    public const int None = -1;

    private const int AutoChannel = SoundScript.AutoChannel;
    private const int ReplaceChannel = SoundScript.ReplaceChannel;
    private const int WeaponChannel = SoundScript.WeaponChannel;

    /// <summary>Below this volume a busy channel is taken at once (<c>(int)vol &lt; 5</c>).</summary>
    private const int Quiet = 5;

    /// <summary>The slot the sound takes, or <see cref="None"/>.</summary>
    /// <param name="slots">The dynamic channels in slot order; null is free.</param>
    /// <param name="request">The new sound.</param>
    /// <param name="steal"><c>voice_steal</c>, "2" by default.</param>
    /// <returns>A slot: one already the sound's to reuse, a free one, or one to steal.</returns>
    public static int Pick(ReadOnlySpan<BusyChannel?> slots, ChannelRequest request, int steal) =>
        Pick(slots, request, steal, stackalloc int[Channels], out _);

    /// <summary>The slot the sound takes, or <see cref="None"/>, and the delay-started exact matches it passed over.</summary>
    /// <param name="slots">The dynamic channels in slot order; null is free.</param>
    /// <param name="request">The new sound.</param>
    /// <param name="steal"><c>voice_steal</c>, "2" by default.</param>
    /// <param name="waiting">
    /// Filled with the busy channels of the sound's own (source, entchannel) that were started with a delay
    /// (<c>local_288</c>), in slot order, which the caller frees when the new sound starts (<c>ch +0xa8</c>, B732).
    /// </param>
    /// <param name="waitingCount">How many <paramref name="waiting"/> holds.</param>
    /// <returns>As the other overload; with two or more waiting, the one with the least samples left.</returns>
    public static int Pick(ReadOnlySpan<BusyChannel?> slots, ChannelRequest request, int steal, Span<int> waiting, out int waitingCount)
    {
        int free = None;
        int quietestSame = None;
        int quietestVolume = 0xffff;
        uint quietestLeft = uint.MaxValue;
        int sameCount = 0;
        bool sameDelayed = false;
        Span<int> candidates = stackalloc int[Math.Min(slots.Length, Channels)];
        int candidateCount = 0;
        waitingCount = 0;

        for (int slot = 0; slot < candidates.Length; slot++)
        {
            if (slots[slot] is not { } busy)
            {
                if (free < 0)
                {
                    free = slot;
                }

                continue;
            }

            if (request.EntChannel != AutoChannel)
            {
                int wanted = request.EntChannel == ReplaceChannel && !IsVoiceOrStream(busy.EntChannel)
                    ? busy.EntChannel
                    : request.EntChannel;

                if (busy.Source == request.Source && request.Source != -1 && busy.EntChannel == wanted)
                {
                    // 0x18002a57a: one waiting out its delay is gathered and weighed as nothing else (B732).
                    if (!busy.DelayedStart)
                    {
                        // The early return comes before the walk's end, where alone `+0xa8` is set (0x18002a725-).
                        waitingCount = 0;

                        return slot;
                    }

                    waiting[waitingCount++] = slot;

                    continue;
                }
            }

            if (IsVoiceOrStream(busy.EntChannel) || (busy.FromLocalPlayer && !request.FromLocalPlayer))
            {
                continue;
            }

            if (busy.SameSound)
            {
                sameCount++;
                sameDelayed |= busy.DelayedStart;
                uint left = Left(busy);

                if (busy.Volume < quietestVolume || (busy.Volume == quietestVolume && left < quietestLeft))
                {
                    quietestVolume = busy.Volume;
                    quietestLeft = left;
                    quietestSame = slot;
                }
            }

            candidates[candidateCount++] = slot;
        }

        // 0x18002a7f0-0x18002a8bd: two or more waiting, the one with the least left, the first on a tie (B732).
        if (waitingCount > 1)
        {
            int shortest = waiting[0];

            foreach (int slot in waiting[1..waitingCount])
            {
                if (Left(slots[slot]!.Value) < Left(slots[shortest]!.Value))
                {
                    shortest = slot;
                }
            }

            return shortest;
        }

        // A copy started with a delay makes it five (`setnz bl; add ebx, 4`, 0x18002a8f1-0x18002a900); a loop, three.
        int copies = sameDelayed ? 5 : 4;

        if (request.Looped)
        {
            copies = 3;
        }

        if (steal > 1 && quietestSame >= 0 && sameCount >= copies)
        {
            // From 0x18002a8c9, which reads voice_steal: the quietest copy goes, unless it is nearer than the new one; a weapon takes it regardless.
            bool nearer = request.DistanceSquared > 0f && slots[quietestSame]!.Value.DistanceSquared < request.DistanceSquared;

            return nearer && request.EntChannel != WeaponChannel ? None : quietestSame;
        }

        if (free >= 0)
        {
            return free;
        }

        int least = None;
        float leastLeft = float.MaxValue;

        foreach (int slot in candidates[..candidateCount])
        {
            BusyChannel busy = slots[slot]!.Value;
            float score = 1f;

            if (steal != 0)
            {
                if (busy.Volume < Quiet)
                {
                    return slot;
                }

                score = (float)Left(busy) / busy.SampleRate;
            }

            if (score < leastLeft)
            {
                leastLeft = score;
                least = slot;
            }
        }

        return least;
    }

    /// <summary>CHAN_VOICE 2, CHAN_STREAM 5 and CHAN_VOICE2 7, which are never stolen (mask <c>0xa4</c>).</summary>
    private static bool IsVoiceOrStream(int channel) =>
        channel is SoundScript.VoiceChannel or SoundScript.StreamChannel or SoundScript.VoiceChannel2;

    /// <summary>Samples left; a looping wave's is its sample rate, one second (<c>source +0x28</c>).</summary>
    private static uint Left(BusyChannel busy) => busy.Looped ? (uint)busy.SampleRate : busy.SamplesLeft;
}
