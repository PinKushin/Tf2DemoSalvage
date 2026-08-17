using System;
using System.Collections.Generic;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The haptics user-message block, which the SDK declares and this project reconstructed.
/// </summary>
/// <remarks>
/// **Every TF2 demo carries these**, because the server sends them to every client whether or not
/// anyone owns a force-feedback device. They appear after the game's own user-message list, which is
/// what made an id table look shifted by four.
///
/// This project worked the block out by scanning binaries, and got it exactly right — six messages,
/// that order, those sizes. **It is also declared in the SDK**, at
/// `src/public/haptics/haptic_msgs.cpp`, which a finding here recorded as not existing: *"Nothing in
/// the SDK's TF2 game code hints at it"*. The search had been for the word in TF2's game directory,
/// and the file is one level up in `public/`.
///
/// So this test exists to hold the claim to the source rather than to a memory of a binary scan. The
/// sizes matter most: `Register` states each message's length, which is the expensive thing to
/// recover any other way — `-1` is variable, and a number is a fixed byte count.
/// </remarks>
public sealed class HapticMessageConformanceTests
{
    /// <summary>Where the block is registered.</summary>
    private const string HapticMessages = "src/public/haptics/haptic_msgs.cpp";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void TheBlockIsSixMessagesInThisOrderWithTheseSizes()
    {
        string source = SourceSdk.Text(HapticMessages).ShouldNotBeNull();

        // Asserted as an ordered list because the ORDER is the finding: these are appended after the
        // game's own messages, so each one's position decides its id. HapSetDrag being fourth is
        // where the +4 that puzzled an earlier investigation comes from.
        List<(string Name, int Size)> block =
        [
            ("SPHapWeapEvent", 4),
            ("HapDmg", -1),
            ("HapPunch", -1),
            ("HapSetDrag", -1),
            ("HapSetConst", -1),
            ("HapMeleeContact", 0),
        ];

        int at = source.IndexOf("void RegisterHapticMessages", StringComparison.Ordinal);

        at.ShouldBeGreaterThan(0, "the registration function should be present");

        foreach ((string name, int size) in block)
        {
            // Each registration must appear AFTER the previous one, which is what makes this an
            // ordering check rather than six independent presence checks.
            int found = source.IndexOf($"\"{name}\"", at, StringComparison.Ordinal);

            found.ShouldBeGreaterThan(at, $"{name} should follow the previous registration");

            // The size argument sits on the same line as the name.
            int lineEnd = source.IndexOf('\n', found);
            string line = source[found..lineEnd];

            line.ShouldContain(size.ToString(System.Globalization.CultureInfo.InvariantCulture));

            at = found;
        }
    }

    [Test]
    public void TheHapticsCodeIsInThePublicTreeNotTheGameTree()
    {
        // **The reason the earlier search missed it**, pinned so the correction keeps its
        // explanation. The registrations are in public/, shared by every game; TF2 additionally has
        // its own client-side haptics file, so "not in TF2's game code" was not true either.
        SourceSdk.Text(HapticMessages).ShouldNotBeNull();
        SourceSdk.Text("src/game/client/tf/c_tf_haptics.cpp").ShouldNotBeNull();
    }
}
