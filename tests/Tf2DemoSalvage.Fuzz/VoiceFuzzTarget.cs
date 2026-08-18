using System;
using System.Globalization;

using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Fuzz;

/// <summary>Which voice codec a fuzz run drives.</summary>
public enum VoiceCodec
{
    /// <summary>libopus, the modern codec.</summary>
    Opus,

    /// <summary>libcelt 0.11.3, TF2's <c>vaudio_celt</c>.</summary>
    Celt,

    /// <summary>libspeex 1.2.1, the oldest of the three.</summary>
    Speex,
}

/// <summary>
/// The voice decoders, fed arbitrary bytes.
/// </summary>
/// <remarks>
/// **This is the only target in the project where a finding is memory corruption rather than an
/// exception.** Every other one drives managed code, where the worst case is a wrong value or a
/// thrown type; these three hand a caller-controlled buffer to a C library over P/Invoke. A bug in
/// that path is an out-of-bounds read or write, and libFuzzer detects it by the process dying
/// rather than by anything asserted here.
///
/// **The input is genuinely untrusted, which is what makes it worth the effort.** Voice frames
/// arrive inside <c>svc_VoiceData</c> in a demo file, so anyone who hands you a <c>.dem</c> chooses
/// these bytes. The decoders were written against frames TF2 produced; nothing had ever fed them a
/// frame TF2 would not produce.
///
/// **Three targets rather than one, and the corpora stay separate.** <c>Program</c> already notes
/// that sharing a corpus lets inputs shaped for one decoder dominate another's, and that applies
/// with force here: an Opus frame begins with a TOC byte whose layout means nothing to CELT, and a
/// corpus of those would explore Opus's dispatch while leaving CELT's mode table untouched.
///
/// **The decoder instance is reused across inputs deliberately.** CELT and Speex carry inter-frame
/// state — that is what makes them predictive codecs — so a fresh decoder per input would test only
/// the first frame of a stream and never the paths that depend on history. Reuse also matches how
/// this project decodes for real: one decoder per talker, many frames through it. The cost is that
/// a reproducer may need the preceding inputs, which is the honest trade for reaching stateful code
/// at all.
/// </remarks>
public static class VoiceFuzzTarget
{
    /// <summary>
    /// The largest frame worth offering, in bytes.
    /// </summary>
    /// <remarks>
    /// Real TF2 voice frames are 64, 128 or 192 bytes. This is well above that so oversized input
    /// is still explored — an oversized frame is exactly the shape that overruns a fixed output
    /// buffer — while stopping libFuzzer spending its budget growing one input for ever.
    /// </remarks>
    private const int MaximumFrameBytes = 4096;

    [ThreadStatic]
    private static OpusVoiceDecoder? _opus;

    [ThreadStatic]
    private static CeltVoiceDecoder? _celt;

    [ThreadStatic]
    private static SpeexVoiceDecoder? _speex;

    /// <summary>Decodes <paramref name="data"/> as one frame and checks the outcome is documented.</summary>
    /// <param name="codec">Which decoder to drive.</param>
    /// <param name="data">Arbitrary bytes.</param>
    /// <exception cref="FuzzPropertyViolationException">The decoder broke its contract.</exception>
    public static void Consume(VoiceCodec codec, ReadOnlySpan<byte> data)
    {
        if (data.Length is 0 or > MaximumFrameBytes)
        {
            // An empty frame is a documented ArgumentException for two of the three, which is a
            // contract already covered by the unit tests; skipping it here spends the budget on
            // frames that actually reach the codec.
            return;
        }

        short[] samples;

        try
        {
            samples = Decode(codec, data);
        }
        catch (ArgumentException)
        {
            // Documented: the frame is empty or otherwise unusable as an argument.
            return;
        }
        catch (InvalidOperationException)
        {
            // Documented, and the ordinary outcome for random bytes: the codec looked at the frame
            // and refused it. This is the case that must NOT be treated as a finding, or every run
            // would report thousands.
            return;
        }
        catch (Exception error)
        {
            // Anything else contradicts the three exceptions these decoders document.
            throw new FuzzPropertyViolationException(
                $"{codec} threw {error.GetType().Name} on a {data.Length}-byte frame: " +
                error.Message,
                error);
        }

        // **Reaching here means the codec ACCEPTED the frame, and that is the interesting case.**
        // A returned buffer of an implausible size is the signature of a decode that wrote outside
        // what it promised — the kind of overrun that does not always segfault, and would otherwise
        // pass silently because libFuzzer only sees a crash.
        //
        // **This branch is reached constantly rather than being a rare edge, which was measured
        // rather than assumed.** Tightening the bound to 1 for one run reddened the Opus and CELT
        // suites immediately: random bytes are ACCEPTED and decoded into real samples. That is not
        // a defect — these are codecs built to survive bit errors on a lossy link, so refusing
        // little and concealing damage is their design. It is why they are worth fuzzing: the
        // budget goes into genuine decode paths rather than bouncing off a validation check.
        if (samples.Length is 0 or > MaximumFrameBytes * 8)
        {
            throw new FuzzPropertyViolationException(
                $"{codec} accepted a {data.Length}-byte frame and returned " +
                $"{samples.Length.ToString(CultureInfo.InvariantCulture)} samples, which is not a " +
                "plausible frame length.");
        }
    }

    private static short[] Decode(VoiceCodec codec, ReadOnlySpan<byte> data) => codec switch
    {
        VoiceCodec.Opus => (_opus ??= new OpusVoiceDecoder()).Decode(data),
        VoiceCodec.Celt => (_celt ??= new CeltVoiceDecoder()).Decode(data),
        VoiceCodec.Speex => (_speex ??= new SpeexVoiceDecoder()).Decode(data),
        _ => throw new ArgumentOutOfRangeException(nameof(codec)),
    };
}
