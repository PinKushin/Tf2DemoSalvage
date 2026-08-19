using System.Collections.Generic;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// What the client does with sound beyond playing it, none of which happens here.
/// </summary>
/// <remarks>
/// **Audio is the part of the sweep that nearly got skipped**, and the reason is worth recording: a
/// gap in rendering is visible in a screenshot and a gap in audio is not audible in anything this
/// project currently produces, because there is no playback to be missing from. That makes these the
/// easiest entries to never notice.
///
/// **They are also the ones a review tool loses most from.** The entity batch already records that
/// sounds carry a position nothing uses; these are the two systems layered above that. A soundscape
/// is why a room sounds like a room, and a caption is a machine-readable transcript of every sound
/// the game can make — which for a demo is a free description of events that no parsing can
/// otherwise supply.
///
/// Both are file formats plus a client system, so both are fully specifiable from the SDK today. No
/// decompiler is needed for either, which puts them ahead of the HUD on cost.
/// </remarks>
public sealed class UnimplementedAudioConformanceTests
{
    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void Audio_ClosedCaptions_AreACompiledHashLookupOverBlocks()
    {
        // captioncompiler.h. A .dat caption file is a header, a directory of CaptionLookup_t entries
        // sorted by a CRC of the sound name, and a blocked payload of UTF-16 text. Blocks are
        // 1 << MAX_BLOCK_BITS bytes so the client can page one in without loading the file.
        //
        // **The lookup key is a hash of the SOUND NAME, which is why this matters here.** A demo
        // names every sound it plays. Joining that to the caption file turns a stream of sound
        // indices into a readable list of what happened - "Sentry going up", "Incoming!" - with no
        // audio decoding at all and no inference about game state.
        //
        // That is a text-trace feature, not a rendering one, and it is cheap: the file is public, the
        // format is fixed, and the join key is already in hand.
        IReadOnlyDictionary<string, int> caption =
            SourceSdk.Constants("src/public/captioncompiler.h");

        caption["COMPILED_CAPTION_VERSION"].ShouldBe(1);

        // **Measured through the struct parser, which is a different route into the same file.**
        //
        // The obvious assertion here was MAX_BLOCK_SIZE == (1 << MAX_BLOCK_BITS), and it is worthless:
        // both sides come out of one parse of one macro, so it holds however wrong that parse is. It
        // is the constant-reader agreeing with itself — an experiment insensitive to the manipulation,
        // by the "wrong instrument" route.
        //
        // The header size is independent of all of that. Six ints under #pragma pack(1) is 24 bytes,
        // and that number is produced by the C layout engine from the member list rather than by the
        // macro reader. It is also the number a reader of the .dat file actually needs.
        string text = SourceSdk.Text("src/public/captioncompiler.h").ShouldNotBeNull();
        CLayoutAttempt header = CStruct.Attempt(text, "CompiledCaptionHeader_t");

        header.Refused.ShouldBeNull();
        header.Layout.ShouldNotBeNull().Size.ShouldBe(24);

        Assert.Ignore(
            "closed captions are not read. The .dat format is a hash directory keyed on sound name " +
            "(captioncompiler.h), and a demo names every sound it plays — joining them gives a " +
            "readable event list with no audio decoding at all.");
    }

    [Test]
    public void Audio_ASoundscape_GivesARoomItsAmbienceAndReverb()
    {
        // c_soundscape.cpp. The server sends an index into scripts/soundscapes_manifest.txt; the
        // client looks up a definition that names looping ambient sounds, positions them at
        // dsp_player-selected points, and crossfades over soundscape_fadetime when the player moves
        // between regions. Definitions nest up to MAX_SOUNDSCAPE_RECURSION deep.
        //
        // **Nothing about this is in the demo except the index**, which is the point: the audible
        // difference between a spawn room and an outdoor point is entirely client-side, driven by a
        // file that ships with the map's game. A viewer that plays only the sounds in the stream
        // reproduces the events and none of the space they happened in.
        //
        // Recorded as a whole system rather than as a missing field because there is no field to
        // read - the gap is that the manifest is never opened.
        IReadOnlyDictionary<string, int> soundscape =
            SourceSdk.Constants("src/game/client/c_soundscape.cpp");

        soundscape["MAX_SOUNDSCAPE_RECURSION"].ShouldBe(8);

        Assert.Ignore(
            "soundscapes are not implemented. The demo carries only an index; the ambience, its " +
            "placement and the crossfade all live in scripts/soundscapes_manifest.txt, which is " +
            "never opened.");
    }
}
