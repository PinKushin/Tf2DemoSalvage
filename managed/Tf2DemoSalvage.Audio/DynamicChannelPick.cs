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
public readonly record struct BusyChannel(
    int Source,
    int EntChannel,
    bool SameSound,
    bool FromLocalPlayer,
    int Volume,
    uint SamplesLeft,
    int SampleRate,
    bool Looped,
    float DistanceSquared);

/// <summary>A dynamic sound asking for a channel.</summary>
/// <param name="Source">The entity it plays from.</param>
/// <param name="EntChannel">Its channel.</param>
/// <param name="FromLocalPlayer">Whether its source is the local player.</param>
/// <param name="Looped">Whether its wave loops.</param>
/// <param name="DistanceSquared">Its origin's squared distance from the listener.</param>
public readonly record struct ChannelRequest(int Source, int EntChannel, bool FromLocalPlayer, bool Looped, float DistanceSquared);

/// <summary>Where a start is heard from, which the channel steal weighs (B730); default is at the listener, not the player.</summary>
/// <param name="Origin">The sound's origin, or null when the caller has none, as at the listener.</param>
/// <param name="Listener">The listener now.</param>
/// <param name="FromLocalPlayer">Whether its source is the local player, `svc_ServerInfo`'s player slot + 1.</param>
/// <param name="VoiceSteal">The user's `voice_steal` as the start reads it, or null for its default "2".</param>
public readonly record struct SoundPlace(
    (float X, float Y, float Z)? Origin, (float X, float Y, float Z) Listener, bool FromLocalPlayer, int? VoiceSteal = null)
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
/// engine.dll <c>FUN_18002a480</c>, build 11191711. **Not ported, because nothing here makes them:** a channel waiting
/// out a delayed start (flag 0x20 at <c>+0x13c</c>), whose exact matches the engine gathers and hands back only when two
/// or more wait, and <c>SND_DO_NOT_OVERWRITE_EXISTING_ON_CHANNEL</c>. No start here carries that flag, and none waits:
/// <c>SoundPresenter</c> starts every sound at its tick and does not read <c>SceneSound.DelaySeconds</c> (open, B730).
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
    public static int Pick(ReadOnlySpan<BusyChannel?> slots, ChannelRequest request, int steal)
    {
        int free = None;
        int quietestSame = None;
        int quietestVolume = 0xffff;
        uint quietestLeft = uint.MaxValue;
        int sameCount = 0;
        Span<int> candidates = stackalloc int[Math.Min(slots.Length, Channels)];
        int candidateCount = 0;

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
                    return slot;
                }
            }

            if (IsVoiceOrStream(busy.EntChannel) || (busy.FromLocalPlayer && !request.FromLocalPlayer))
            {
                continue;
            }

            if (busy.SameSound)
            {
                sameCount++;
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

        if (steal > 1 && quietestSame >= 0 && sameCount >= (request.Looped ? 3 : 4))
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
