using System;
using System.IO;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>
/// The LZMA decoder TF2's map lumps are compressed with.
/// </summary>
/// <remarks>
/// **Every fixture here was produced by liblzma, not by this project.** A hand-built LZMA stream
/// would be testing this codebase's reading of the format against itself — the failure recorded in
/// <c>fixtures-are-the-weak-point</c>, where hand-written fixtures caused more bugs than the
/// decoders did. An independent encoder cannot agree with a wrong decoder by accident.
///
/// The three payloads exercise different halves of the algorithm, which matters because a decoder
/// that only ever sees one of them can be badly wrong and still pass:
///
/// - <c>Blocks</c> repeats a 37-byte block, so it is almost all matches at a constant distance.
///   3000 bytes compress to 85 — the rep-distance path carries the whole file.
/// - <c>Noise</c> is a congruential sequence with no structure, so it is almost all literals.
///   2048 bytes *expand* to 2106, which is the literal path being exercised end to end.
/// - <c>Runs</c> sits between the two.
///
/// The generator is <c>make-lzma-fixtures.py</c>; the properties byte is 0x5D in all three, which
/// is lc=3, lp=0, pb=2 — the same settings a shipped TF2 map uses.
/// </remarks>
public sealed class LzmaTests
{
    /// <summary>Offset of the raw stream inside a Valve lump: 4 magic, 4 + 4 sizes, 5 properties.</summary>
    private const int BodyOffset = 17;

    private const int PropertiesOffset = 12;

    [Test]
    public void Decode_MatchHeavyStream_ReturnsTheOriginalBytes()
    {
        byte[] lump = Convert.FromHexString(BlocksLump);

        byte[] decoded = Decode(lump, 3000);

        decoded.ShouldBe(Blocks(3000));
    }

    [Test]
    public void Decode_LiteralHeavyStream_ReturnsTheOriginalBytes()
    {
        byte[] lump = Convert.FromHexString(NoiseLump);

        byte[] decoded = Decode(lump, 2048);

        decoded.ShouldBe(Noise(2048));
    }

    [Test]
    public void Decode_RunStream_ReturnsTheOriginalBytes()
    {
        byte[] lump = Convert.FromHexString(RunsLump);

        byte[] decoded = Decode(lump, 4096);

        decoded.ShouldBe(Runs(4096));
    }

    [Test]
    public void Decode_MatchHeavyFixture_ActuallyCompressed()
    {
        // Guards the experiment. If the "match-heavy" fixture were not match-heavy, the test above
        // would be a second literal test wearing a different name, and the rep-distance path would
        // have no coverage at all while appearing to have two tests.
        Convert.FromHexString(BlocksLump).Length.ShouldBeLessThan(3000 / 10);
    }

    [Test]
    public void Decode_LiteralHeavyFixture_ActuallyExpanded()
    {
        // The same guard from the other side: a stream that compressed well would not be
        // exercising the literal path.
        Convert.FromHexString(NoiseLump).Length.ShouldBeGreaterThan(2048);
    }

    [Test]
    public void Decode_TruncatedStream_FailsAsBadData()
    {
        // A map cut short mid-download. Running off the end of the input must be an
        // InvalidDataException, not an IndexOutOfRangeException from inside the range decoder.
        byte[] lump = Convert.FromHexString(RunsLump);
        byte[] cut = lump[..(lump.Length - 40)];

        Should.Throw<InvalidDataException>(() => Decode(cut, 4096));
    }

    [Test]
    public void Decode_OutputLongerThanTheStreamHolds_FailsAsBadData()
    {
        // The declared size and the stream are two independent numbers, and a hostile map can make
        // them disagree. Asking for more than the stream produces must fail rather than return a
        // buffer half full of zeroes.
        byte[] lump = Convert.FromHexString(BlocksLump);

        Should.Throw<InvalidDataException>(() => Decode(lump, 3000 * 4));
    }

    [Test]
    public void Decode_ShorterOutputThanTheStreamHolds_StopsWhereAsked()
    {
        // Not an error: the caller's size is authoritative, and stopping early is how a decoder
        // that never reads an end marker terminates.
        byte[] lump = Convert.FromHexString(BlocksLump);

        Decode(lump, 500).ShouldBe(Blocks(500));
    }

    [Test]
    public void Decode_ImpossiblePropertiesByte_IsRefused()
    {
        // lc, lp and pb are packed into one byte as lc + 9*(lp + 5*pb), so the largest legal value
        // is 224. Anything above it is not a properties byte, and taking it literally would size
        // the literal probability table from a nonsense shift count.
        byte[] lump = Convert.FromHexString(BlocksLump);
        lump[PropertiesOffset] = 225;

        Should.Throw<InvalidDataException>(() => Decode(lump, 3000));
    }

    [Test]
    public void Decode_NegativeOutputLength_IsRefused()
    {
        byte[] lump = Convert.FromHexString(BlocksLump);

        Should.Throw<ArgumentOutOfRangeException>(() => Decode(lump, -1));
    }

    [Test]
    public void Decode_ZeroLengthOutput_ReturnsNothing()
    {
        Decode(Convert.FromHexString(BlocksLump), 0).Length.ShouldBe(0);
    }

    /// <summary>Runs the decoder over a Valve lump's properties and body.</summary>
    private static byte[] Decode(byte[] lump, int outputLength) => ValveLzma.Decode(
        lump.AsSpan(PropertiesOffset, 5),
        lump.AsSpan(Math.Min(BodyOffset, lump.Length)),
        outputLength);

    /// <summary>The generator's <c>pattern_runs</c>, byte for byte.</summary>
    private static byte[] Runs(int length)
    {
        byte[] bytes = new byte[length];

        for (int index = 0; index < length; index++)
        {
            bytes[index] = (byte)((index / 8) + 7);
        }

        return bytes;
    }

    /// <summary>The generator's <c>pattern_noise</c>, byte for byte.</summary>
    private static byte[] Noise(int length)
    {
        byte[] bytes = new byte[length];
        long state = 1;

        for (int index = 0; index < length; index++)
        {
            state = ((state * 1103515245) + 12345) & 0x7FFFFFFF;
            bytes[index] = (byte)((state >> 16) & 0xFF);
        }

        return bytes;
    }

    /// <summary>The generator's <c>pattern_blocks</c>, byte for byte.</summary>
    private static byte[] Blocks(int length)
    {
        byte[] bytes = new byte[length];

        for (int index = 0; index < length; index++)
        {
            bytes[index] = (byte)(((index % 37) * 11) + 3);
        }

        return bytes;
    }

    // RunsLump: 4096 bytes -> 288 bytes on disk, properties 5d00000100
    private const string RunsLump =
        "4c5a4d41001000000f0100005d000001000003ea7e35455d9c40be9a88be724c" +
        "1dc4f2f1f90fba8fd62a210109b13d756f2b8b09a6944950d7db571433d8d579" +
        "81fffd892ddb666d3f1ca9e5c462fbd611cbe5dcc93bdc8beb4526d5e308cc2a" +
        "8a2476ff2b251e4b1a3300faa4a9dad432a35e2480ab71d5fb5cd468e103fb15" +
        "5903e906c2245eec152edaa21a6288ecabd7896457d3cd6e5d4971d2a095eb00" +
        "2b063df39ce654c5680f439c515bf4133f710e19a407cb95280b16359b91eba8" +
        "dc72906a7a8aaf8746238e821552cf4b952e0acc09e8663285d797b64fe08a25" +
        "7636e3e1edf613242660d6e17edef8a9fc6c163fc2de02bc03b883827000fb25" +
        "e12dfd2164d30ffe5734025f0843881f8b3fbacdfa09855953dbffffaf8fce80";

    // NoiseLump: 2048 bytes -> 2106 bytes on disk, properties 5d00000100
    private const string NoiseLump =
        "4c5a4d4100080000290800005d0000010000631f8c26b25b763e4edeea1609d3" +
        "c4872460befe151998b352c85f58a837164bc050b048f50f1459b1d0683e20d4" +
        "c746be5840a56730e9d798c6bb94dece221eb49bfe852950ca020063e183a119" +
        "4825d1d418a106a5abfd48ba76aef8a501b2fc2e3551906d44ab04c83dad96b1" +
        "e893793e29d81ba86afd7c84b720a7601c368dcc004e4116f8bc7f75cb2b3048" +
        "7813aed6890c369d82f13dda79bdaa8caa7cd9f15dda7a75e59063316ad2fa19" +
        "66b20bb6733c74824881ea483279ed6e9d3dbfb5cc725bd53bdeba264cd66189" +
        "ebe87fcf19896433f516a899ca97648fc2c3527c1e690a1b2cd720e2a17162e2" +
        "c00a0519daf487e84411f9065126bd9dec0e7cf231c728d0f2d6cfbce643be15" +
        "72eee127796e9ada17597c77aa03507cde676285fed1713c9c04215187f8d05d" +
        "c7c0a00681d98f9fc3713b4f8467ba090cec5e15a34b17a2cff6d65b55b2d147" +
        "7b58a4c46cadcf42fa41949e497e5b229de660abc02a0a97801b8f78a35c2170" +
        "4cf6fff9fd124b9d78da765ba234c34bf7ef20940f35704b55588fd0492c808f" +
        "429e368a609bf8fa8cc7b550c25b642403e40498f4418d8af2bc1941ab9481f0" +
        "3f1ccee134485044c02badef4574ad3beaf7931cd7759d6e86eaa9c464ea128d" +
        "487e59f301edb21b50a3943a2baa1a34b247634bc395bba4eb550a5c7ca35014" +
        "5bf10604ed3d07f84e891a6166fee4482869849533afde74b8d8757c0da3557f" +
        "ffba9678f5b894236dbe16c9336c3d547743894e503f9353b4f2a16f7836d696" +
        "c109c04814f093a3b71e0cab3d11f23b9212fc4df216d9285eb71b73c85c2446" +
        "43330f79b66e08757baefd9dc69228c255da43336fc9230e31419344e5008d3a" +
        "93dcd5392d912b1ac460025a2b3a5681607949acbae1c704b816e2ec552c9e42" +
        "3b7dad5ba0e0a721b403a99f687d0d98bafb32b99992c4c22a4a6e28cb6fbc30" +
        "73c9f88185cdfdb8f217ae532f11a9221ba4c25ef1d20754bdd3315a411a8dea" +
        "757638f1be6981d5e88eae36d21b6a9f11b621b4a48aa5a6ee1a2dbacbff6598" +
        "89d59264e2e238638e2bb6f61b330f3211bad2efbeb1b91882b6ef66443e5d6d" +
        "aeca094eed88795de14fdb08b3e471ff9e0ce87fa6a8e5e2db4c732134dde53e" +
        "98ce18ebb5adb5d28b362289950860996371071c084741eb4691b00b887e4023" +
        "5cd13f32bb46bc9ec02a94529f49d29967612eaf161715c930dcce3732967671" +
        "d3b37fac8733e008c9b69d132b93a0c8a74b8b2c42b691538151547e7c84e21a" +
        "0742026f818b4cac0780c284cb055dc5fcb251eebdde43a8dd5961b2490a588e" +
        "cda0cb29111eb02c75d5929e2b8759e00cde740835fec2e793362f01a52714c0" +
        "3b5988368368a55f78fdedb662207bf727e5fb118c3958d556c8d74cdc78a423" +
        "864d43286e2db23f82054ea05eb5fbc887e63801fdda20298381a8e4f0c12768" +
        "639ae51beb903a5e3028b096e8cefe9d2a91ea9952c40155aa2a5f54973959eb" +
        "45490784214e1833890419fc9ec0c3466e3fc9f406c43119a8277baecf897442" +
        "a0080ddc43f150af1b0617b73cae30ab09dcff77a1a9428aa47dadb364dd3c7e" +
        "37f4aea38beadaa3c10fa539410ded14f5168e312171a96aa4ac6b5d663ff87b" +
        "51c0a888caef123ff6a0c4b34c8af3f3d0587725a79c6f08e1b80f5c09c83c64" +
        "46de8e5d709d370a653a9f93e2dc8387c5ebcab4a00ae3a4af352004363eff57" +
        "086e7860adbe73794d008779aaad7f6cbf9c971e53ce5fbec77faa5b007b891e" +
        "37e3b861981075b0118e93810e9dd6777bfc9cdb05cd2fa7389e4825e4babfbe" +
        "88744c250e32c849ac9e6307feac7585a8cc59b57e237decc73ae43db1328d4f" +
        "00d26f59862a8557f745b513e65369a2bb095c99803a1cdd6b4c3cd1edb8ecd6" +
        "022850911323ebbcd1cce3464f3e83ba34dbd12bcbd8f3d23d7d22ad8a4b12ae" +
        "b0ad973bf6681b9c949bc0fd94c42d8a77245393ee4ff7d51ed2ea413063db3c" +
        "ea63d047bf7e4ff1fcc95330ead8f5a31e1c19a054451497ddf346d83d11a682" +
        "901f83d2c9acd6e02e4a348a627fd248e3a216b24017ce200770eb0c85970190" +
        "d61b925b3376301766c854d53fe23ebfc6cd82258bf66b66d5272292fce9b96c" +
        "e27ed49198757ffc5d0ecea7c5b156e26244644082f51c811c7a95be5becee64" +
        "0b3e2c236f6bf74f195d3da7ac38c5d8f875c9e607c052644d78ec16896284cd" +
        "b564f3d84197bcbcf9e6cf57a608d960be103d1107c84de8cb4709450806f693" +
        "1a7c4cea93e9c410ed915e6dcdf44f05f48bd2e2d7563373970cf6db01783624" +
        "05b133af65a2925291156579cc5e9899b2be960bb5aed0a9008aaa36ac8f1aaf" +
        "3a37b4ca8cb821e313d9969625aee02a3d3d1fe4e6e92e123d6bf4e5f761b53e" +
        "8ca23bda36a1bfc78dbadb6f79833da502f1462af6de81dc8023ca464352d04e" +
        "b8238f954a3ab970ed1b651e23cf77351cce94408b0755421f6169c7a323030e" +
        "aab7d1fd0783f3541c67c93e18fd6e4084775fd47b344d8e104ca9aa15d55c40" +
        "1b452f892a4632d12ac2f5f5b5eca4a855ad99b71d353c94c72ec63ffe18900d" +
        "e14d8acf3b5075e48560320f8cffec7673652bb003b8d5a25f60b89bbd01e907" +
        "c7c441053e9fe3ac0d2867001d418be9c3375ba9136f5bebd724477a58280ea7" +
        "bd992a991475eb19e8b83d05423a6839e74d2e34a28f1c94f8945e4718e039a8" +
        "9c3c6f320e9944df559c118ce15e69ee59f6596c292ad0c05929c6c6884d9525" +
        "af086933b6e6ff3b0e9263c035702a1eff367529b3c60ff9d86ecc771aeae17c" +
        "6ee74b44cd9b0d29629556b0bf8fd93ddb8fd26ba0b30a41c2dd47586bacd83b" +
        "b90b162ac3935d423ed980ca37bb286193e62fd34ae722995f33234e7e2108d5" +
        "aec569aba89a15841630f28cdd280eea2e7b31c5ffffec140000";

    // BlocksLump: 3000 bytes -> 85 bytes on disk, properties 5d00000100
    private const string BlocksLump =
        "4c5a4d41b80b0000440000005d00000100000183fa0aaf5ca8db11fa4c277574" +
        "4528cd9afa665a2efafe6bf81af328b4a2e746be6c441657c4232f0ea8085234" +
        "b866b6ed5e5103f1cc8d822778241e1ffff7ea0000";
}
