using System;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Which of the 64 dynamic channels a new sound gets, and which one it steals when all are busy (B730).
/// </summary>
/// <remarks>
/// Read in disassembly and decompile, x64 <c>engine.dll</c> build 11191711. <c>SND_PickDynamicChannel</c> is
/// <c>FUN_18002a480</c> (the only reader of <c>voice_steal</c>'s value, <c>DAT_1804a64b8 + 0x58</c>, at
/// <c>0x18002a8c9</c> and <c>0x18002a957</c>; its one caller is <c>S_StartDynamicSound</c>, <c>FUN_18002e6e0</c>). It walks
/// channels 0..0x3f (the 0x140-byte table at <c>0x18048de20</c>), and for each with an sfx:
/// <code>
/// 1. entchannel != CHAN_AUTO: want = entchannel, or the busy channel's own when entchannel is CHAN_REPLACE (-1) and it
///    is not 2, 5 or 7 (mask 0xa4). Same source (not -1) and want: SND_DO_NOT_OVERWRITE → NULL; not delay-started → THIS.
/// 2. busy entchannel CHAN_VOICE 2, CHAN_STREAM 5 or CHAN_VOICE2 7: never a candidate.
/// 3. sound services +0x48 (FUN_180018f30, source == player slot + 1) true for the busy source and false for the new: skip.
/// 4. same sfx: count it; its volume (int)max(0, the 12 speaker volumes); keep the quietest, a tie to fewer samples left.
/// 5. a candidate.
/// </code>
/// A channel with no sfx is free, the first remembered. After the walk (<c>0x18002a703-</c>):
/// <code>
/// a. voice_steal &gt; 1, a quietest same sound, and at least 4 of the sound (3 when the new wave loops): that channel —
///    unless the new sound is farther from the listener than it is, when the new sound gets NULL (CHAN_WEAPON 1 still
///    takes it). A new sound at the listener (distance² &lt;= 0) takes it. This runs BEFORE the free channel.
/// b. the first free channel.
/// c. voice_steal 0: the first candidate. Else the first candidate below volume 5; else the least time left, samples left
///    over the sample rate (a looping wave counts its rate, one second), the first on a tie.
/// d. NULL: nothing starts.
/// </code>
/// "Samples left" is the source's sample count (+0x40) less the mixer's position, or the source's sample rate (+0x28,
/// the value <c>S_StartDynamicSound</c> checks against 11025/22050/44100) when it loops (+0x58); <c>snd_show</c>
/// (<c>FUN_180030870</c>) prints the same quotient as <c>timeleft</c> beside <c>looped</c>.
/// </remarks>
public sealed class DynamicChannelPickConformanceTests
{
    private const int Auto = 0;
    private const int Weapon = 1;
    private const int Voice = 2;
    private const int Item = 3;
    private const int Body = 4;
    private const int Stream = 5;
    private const int Voice2 = 7;
    private const int Replace = -1;

    /// <summary>A loud, long, unrelated busy channel: a candidate that loses to anything quieter or nearer its end.</summary>
    private static BusyChannel Busy(
        int source = 50,
        int channel = Auto,
        bool same = false,
        int volume = 200,
        uint left = 44100,
        bool looped = false,
        float distanceSquared = 100f,
        bool localPlayer = false,
        bool delayed = false) =>
        new(source, channel, same, localPlayer, volume, left, 44100, looped, distanceSquared, delayed);

    private static ChannelRequest Request(
        int source = 1, int channel = Auto, bool looped = false, float distanceSquared = 100f, bool localPlayer = false) =>
        new(source, channel, localPlayer, looped, distanceSquared);

    private static BusyChannel?[] Full(Func<int, BusyChannel?> at)
    {
        BusyChannel?[] slots = new BusyChannel?[DynamicChannelPick.Channels];

        for (int slot = 0; slot < slots.Length; slot++)
        {
            slots[slot] = at(slot);
        }

        return slots;
    }

    [Test]
    public void Channels_Engine_Are64()
    {
        // 0x18002a70f: the walk ends past 0x3f; the static ones start at 0x40 (FUN_18002f4c0).
        DynamicChannelPick.Channels.ShouldBe(64);
    }

    [Test]
    public void VoiceSteal_Default_Is2()
    {
        // FUN_180002180: ConVar("voice_steal", 0x18036019c = "2", flags 0).
        EngineConVars.ByName("voice_steal").Default.ShouldBe("2");
    }

    [Test]
    public void Pick_FreeChannelsAfterABusyOne_TakesTheFirstFree()
    {
        BusyChannel?[] slots = new BusyChannel?[DynamicChannelPick.Channels];
        slots[0] = Busy();
        slots[2] = Busy();

        DynamicChannelPick.Pick(slots, Request(), steal: 2).ShouldBe(1);
    }

    [Test]
    public void Pick_ANamedChannelBusyOnTheSameSource_ReusesItWhenFreeOnesRemain()
    {
        BusyChannel?[] slots = new BusyChannel?[DynamicChannelPick.Channels];
        slots[3] = Busy(source: 1, channel: Body);

        DynamicChannelPick.Pick(slots, Request(source: 1, channel: Body), steal: 2).ShouldBe(3);
    }

    [Test]
    public void Pick_AutoOnASourceWithABusyAutoChannel_TakesAFreeOne()
    {
        BusyChannel?[] slots = new BusyChannel?[DynamicChannelPick.Channels];
        slots[0] = Busy(source: 1, channel: Auto);

        DynamicChannelPick.Pick(slots, Request(source: 1, channel: Auto), steal: 2).ShouldBe(1);
    }

    [Test]
    public void Pick_SourceMinusOneOnItsOwnChannel_IsNoMatch()
    {
        BusyChannel?[] slots = new BusyChannel?[DynamicChannelPick.Channels];
        slots[0] = Busy(source: -1, channel: Body);

        DynamicChannelPick.Pick(slots, Request(source: -1, channel: Body), steal: 2).ShouldBe(1);
    }

    [TestCase(Body, 0)]
    [TestCase(Voice, 1)]
    [TestCase(Stream, 1)]
    [TestCase(Voice2, 1)]
    public void Pick_ChanReplaceOverABusyChannel_MatchesItUnlessVoiceOrStream(int busy, int expected)
    {
        BusyChannel?[] slots = new BusyChannel?[DynamicChannelPick.Channels];
        slots[0] = Busy(source: 1, channel: busy);

        DynamicChannelPick.Pick(slots, Request(source: 1, channel: Replace), steal: 2).ShouldBe(expected);
    }

    [Test]
    public void Pick_AllBusy_TakesTheFirstBelowVolumeFive()
    {
        BusyChannel?[] slots = Full(slot => slot switch
        {
            10 => Busy(volume: 5),
            20 => Busy(volume: 4),
            30 => Busy(volume: 0),
            _ => Busy(),
        });

        DynamicChannelPick.Pick(slots, Request(), steal: 2).ShouldBe(20);
    }

    [Test]
    public void Pick_AllBusyAndLoud_TakesTheLeastTimeLeft()
    {
        // Slot 9's 22050 samples at 11025 Hz are two seconds, as slot 5's 88200 at 44100 are: the quotient decides, not
        // the count, and slot 40 is under one second.
        BusyChannel?[] slots = Full(slot => slot switch
        {
            5 => Busy(left: 88200),
            9 => new BusyChannel(50, Auto, false, false, 200, 22050, 11025, false, 100f),
            40 => Busy(left: 44099),
            _ => Busy(left: 1_000_000),
        });

        DynamicChannelPick.Pick(slots, Request(), steal: 2).ShouldBe(40);
    }

    [Test]
    public void Pick_ALoopNearItsEnd_CountsAsOneSecondLeft()
    {
        // A loop with 10 samples to go is scored by its rate: one second, so a one-shot with 0.9 s left goes first.
        BusyChannel?[] slots = Full(slot => slot switch
        {
            7 => Busy(left: 10, looped: true),
            8 => Busy(left: 39690),
            _ => Busy(left: 1_000_000),
        });

        DynamicChannelPick.Pick(slots, Request(), steal: 2).ShouldBe(8);
    }

    [Test]
    public void Pick_TwoEquallyNearTheirEnd_TakesTheFirst()
    {
        BusyChannel?[] slots = Full(slot => slot is 12 or 13 ? Busy(left: 100) : Busy());

        DynamicChannelPick.Pick(slots, Request(), steal: 2).ShouldBe(12);
    }

    [Test]
    public void Pick_EveryBusyChannelVoiceOrStream_StartsNothing()
    {
        BusyChannel?[] slots = Full(slot => Busy(channel: (slot % 3) switch { 0 => Voice, 1 => Stream, _ => Voice2 }, volume: 0));

        DynamicChannelPick.Pick(slots, Request(), steal: 2).ShouldBe(DynamicChannelPick.None);
    }

    [Test]
    public void Pick_TheLocalPlayersQuietChannelForAnotherSource_IsPassedOver()
    {
        BusyChannel?[] slots = Full(slot => slot switch
        {
            3 => Busy(volume: 0, localPlayer: true),
            6 => Busy(volume: 1),
            _ => Busy(),
        });

        DynamicChannelPick.Pick(slots, Request(), steal: 2).ShouldBe(6);
        DynamicChannelPick.Pick(slots, Request(localPlayer: true), steal: 2).ShouldBe(3, "the local player's own sound may take it");
    }

    [Test]
    public void Pick_FourOfTheSameSoundWithAFreeChannel_StealsTheQuietest()
    {
        BusyChannel?[] slots = new BusyChannel?[DynamicChannelPick.Channels];
        slots[0] = Busy(same: true, volume: 90, distanceSquared: 50f);
        slots[1] = Busy(same: true, volume: 30, distanceSquared: 50f);
        slots[2] = Busy(same: true, volume: 60, distanceSquared: 50f);
        slots[4] = Busy(same: true, volume: 40, distanceSquared: 50f);

        DynamicChannelPick.Pick(slots, Request(distanceSquared: 10f), steal: 2).ShouldBe(1);
    }

    [Test]
    public void Pick_ThreeOfTheSameSound_TakesTheFreeChannel()
    {
        BusyChannel?[] slots = new BusyChannel?[DynamicChannelPick.Channels];
        slots[0] = Busy(same: true, volume: 90);
        slots[1] = Busy(same: true, volume: 30);
        slots[2] = Busy(same: true, volume: 60);

        DynamicChannelPick.Pick(slots, Request(distanceSquared: 10f), steal: 2).ShouldBe(3);
        DynamicChannelPick.Pick(slots, Request(distanceSquared: 10f, looped: true), steal: 2)
            .ShouldBe(1, "a looping wave allows three");
    }

    [Test]
    public void Pick_SameSoundsEquallyQuiet_StealsTheOneWithFewerSamplesLeft()
    {
        BusyChannel?[] slots = new BusyChannel?[DynamicChannelPick.Channels];
        slots[0] = Busy(same: true, volume: 30, left: 500, distanceSquared: 50f);
        slots[1] = Busy(same: true, volume: 30, left: 400, distanceSquared: 50f);
        slots[2] = Busy(same: true, volume: 30, left: 400, distanceSquared: 50f);
        slots[3] = Busy(same: true, volume: 30, left: 600, distanceSquared: 50f);

        DynamicChannelPick.Pick(slots, Request(distanceSquared: 10f), steal: 2).ShouldBe(1);
    }

    [TestCase(Auto, DynamicChannelPick.None)]
    [TestCase(Item, DynamicChannelPick.None)]
    [TestCase(Weapon, 1)]
    public void Pick_TheQuietestSameSoundNearerThanTheNewOne_RefusesUnlessAWeapon(int channel, int expected)
    {
        BusyChannel?[] slots = new BusyChannel?[DynamicChannelPick.Channels];
        slots[0] = Busy(same: true, volume: 90, distanceSquared: 50f);
        slots[1] = Busy(same: true, volume: 30, distanceSquared: 50f);
        slots[2] = Busy(same: true, volume: 60, distanceSquared: 50f);
        slots[3] = Busy(same: true, volume: 40, distanceSquared: 50f);

        DynamicChannelPick.Pick(slots, Request(channel: channel, distanceSquared: 51f), steal: 2).ShouldBe(expected);
        DynamicChannelPick.Pick(slots, Request(channel: channel, distanceSquared: 50f), steal: 2)
            .ShouldBe(1, "as near is not nearer");
        DynamicChannelPick.Pick(slots, Request(channel: channel, distanceSquared: 0f), steal: 2)
            .ShouldBe(1, "a sound at the listener takes it");
    }

    [Test]
    public void Pick_FourOfTheSameSoundWithVoiceStealOne_TakesTheFreeChannel()
    {
        BusyChannel?[] slots = new BusyChannel?[DynamicChannelPick.Channels];

        for (int slot = 0; slot < 4; slot++)
        {
            slots[slot] = Busy(same: true, volume: 30 + slot);
        }

        DynamicChannelPick.Pick(slots, Request(), steal: 1).ShouldBe(4);
        DynamicChannelPick.Pick(slots, Request(), steal: 2).ShouldBe(0, "the control: at 2 the quietest is stolen");
    }

    [Test]
    public void Pick_AllBusyWithVoiceStealZero_TakesTheFirstCandidate()
    {
        BusyChannel?[] slots = Full(slot => slot switch
        {
            0 => Busy(channel: Voice),
            1 => Busy(left: 1_000_000),
            _ => Busy(volume: 0, left: 1),
        });

        DynamicChannelPick.Pick(slots, Request(), steal: 0).ShouldBe(1);
    }

    // **A channel started with a delay** (flag 0x20 at `ch +0x13c`, set by `S_StartDynamicSound` FUN_18002e6e0 at
    // 0x18002efbc for a positive delay and 0x18002f05d for a negative one, and cleared only by `S_FreeChannel`'s memset,
    // FUN_180045cc0) is weighed apart (B732). An exact (source, entchannel) match on one is not handed back (0x18002a57a): it
    // is gathered (0x18002a587-0x18002a59a) and the walk goes on past it, counting it as nothing else. After the walk, with
    // any gathered, each is told to free itself when the new sound starts (`ch +0xa8`, 0x18002a725-0x18002a82f), and with two
    // or more the one with the least samples left, the first on a tie, is handed back (0x18002a810-0x18002a8bd). One
    // gathered falls through to the usual rules.

    [Test]
    public void Pick_ExactMatchStartedWithADelay_IsGatheredAndAFreeChannelTaken()
    {
        BusyChannel?[] slots = new BusyChannel?[DynamicChannelPick.Channels];
        slots[0] = Busy(source: 1, channel: Body, delayed: true);
        Span<int> waiting = stackalloc int[DynamicChannelPick.Channels];

        DynamicChannelPick.Pick(slots, Request(source: 1, channel: Body), steal: 2, waiting, out int count).ShouldBe(1);
        count.ShouldBe(1);
        waiting[0].ShouldBe(0);

        slots[0] = Busy(source: 1, channel: Body);
        DynamicChannelPick.Pick(slots, Request(source: 1, channel: Body), steal: 2, waiting, out count)
            .ShouldBe(0, "the control: not delay-started, the match is reused");
        count.ShouldBe(0);
    }

    [Test]
    public void Pick_TwoExactMatchesStartedWithADelay_TakesTheOneWithLeastLeft()
    {
        BusyChannel?[] slots = new BusyChannel?[DynamicChannelPick.Channels];
        slots[2] = Busy(source: 1, channel: Body, delayed: true, left: 30_000);
        slots[5] = Busy(source: 1, channel: Body, delayed: true, left: 20_000);
        slots[9] = Busy(source: 1, channel: Body, delayed: true, left: 20_000);
        Span<int> waiting = stackalloc int[DynamicChannelPick.Channels];

        DynamicChannelPick.Pick(slots, Request(source: 1, channel: Body), steal: 2, waiting, out int count)
            .ShouldBe(5, "the least left, the first on a tie, though channel 0 is free");
        count.ShouldBe(3);
        waiting[..count].ToArray().ShouldBe([2, 5, 9]);
    }

    [Test]
    public void Pick_FourOfTheSameSoundOneStartedWithADelay_NeedsAFifth()
    {
        BusyChannel?[] slots = new BusyChannel?[DynamicChannelPick.Channels];

        for (int slot = 0; slot < 4; slot++)
        {
            slots[slot] = Busy(same: true, volume: 30 + slot, delayed: slot == 3);
        }

        // 0x18002a8f1-0x18002a900: `setnz bl; add ebx, 4`, the byte set by a same-sfx channel with 0x20 (0x18002a602-0x18002a60c).
        DynamicChannelPick.Pick(slots, Request(), steal: 2).ShouldBe(4, "four with one delayed is short of five");

        slots[4] = Busy(same: true, volume: 90);
        DynamicChannelPick.Pick(slots, Request(), steal: 2).ShouldBe(0, "five: the quietest is stolen");
        DynamicChannelPick.Pick(slots, Request(looped: true), steal: 2).ShouldBe(0, "a loop needs three regardless");
    }
}
