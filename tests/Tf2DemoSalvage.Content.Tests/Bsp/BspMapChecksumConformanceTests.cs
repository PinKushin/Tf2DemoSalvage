using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The checksum a server sends so a client can tell whether it has the same map.
/// </summary>
/// <remarks>
/// **Written before this project compared anything.** `svc_ServerInfo` carries `mapCRC`, decoded
/// here since the container work as `ServerInfoMessage.MapCrc` and described in its own doc comment
/// as *"used by the client to detect a mismatched map"* — and never once compared against the map
/// actually loaded.
///
/// **The cost of not comparing it, measured 2026-08-27/28.** Three visual defects were reported
/// against a 2017 badlands demo rendered on the 2026 `cp_badlands.bsp`: roller doors drawing as grey
/// rock, players appearing from nowhere, doors flickering. Two were real bugs in unrelated work and
/// one was never a code problem at all, and nothing could separate them except the owner's memory of
/// a different map. See D113 and `docs/findings/41`.
///
/// **Everything needed is published**, which is why this is a conformance question rather than a
/// design one: `CRC_MapFile` gives the algorithm and `checksum_crc.cpp` gives the CRC variant.
/// </remarks>
public sealed class BspMapChecksumConformanceTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private const string BspLib = "src/utils/common/bsplib.cpp";
    private const string Checksum = "src/tier1/checksum_crc.cpp";

    /// <summary>That the map CRC covers every lump except the entities.</summary>
    /// <remarks>
    /// **The exclusion is the load-bearing part and it is deliberate.** A server may edit the entity
    /// lump — that is how `stripper`-style plugins and map fixups work — and its clients must still
    /// match. So a checksum over the WHOLE file would report a mismatch for every server running a
    /// modified entity list, which is most competitive servers.
    ///
    /// **The bytes are read from the lump's own `fileofs`/`filelen`, in header order**, not
    /// sequentially through the file. Lumps are not necessarily stored in index order, so walking
    /// the file start to end gives a different byte sequence and therefore a different CRC.
    /// </remarks>
    [Test]
    public void Sdk_TheMapCrc_CoversEveryLumpButTheEntities()
    {
        string source = Flat(Sdk(BspLib));

        Match crc = Regex.Match(
            source,
            @"static bool CRC_MapFile\(CRC32_t \*crcvalue, const char \*pszFileName\)\s*\{(.*?)\n\}",
            RegexOptions.Singleline,
            Limit);

        crc.Success.ShouldBeTrue("CRC_MapFile is how a map is checksummed");

        string body = crc.Groups[1].Value;

        body.ShouldContain("// CRC across all lumps except for the Entities lump");

        Match loop = Regex.Match(
            body,
            @"for \( int l = 0; l < HEADER_LUMPS; \+\+l \)\s*\{\s*"
            + @"if \(l == LUMP_ENTITIES\)\s*continue;\s*"
            + @"curLump = &g_pBSPHeader->lumps\[l\];",
            RegexOptions.Singleline,
            Limit);

        loop.Success.ShouldBeTrue("every lump index but LUMP_ENTITIES, in header order");

        // The bytes come from the lump's recorded offset and length, not from a sequential read.
        body.ShouldContain("unsigned int nSize = curLump->filelen;");
        body.ShouldContain("g_pFileSystem->Seek( fp, curLump->fileofs, FILESYSTEM_SEEK_HEAD );");
    }

    /// <summary>That nothing is decompressed or transformed before being hashed.</summary>
    /// <remarks>
    /// **Raw bytes, in 1K chunks, straight into the CRC.** A Source BSP may store lumps LZMA
    /// compressed, and `CRC_MapFile` neither knows nor cares — it seeks to `fileofs` and reads
    /// `filelen` bytes. Decompressing first would give a different answer from every real client.
    /// </remarks>
    [Test]
    public void Sdk_TheMapCrc_HashesTheRawLumpBytes()
    {
        string source = Flat(Sdk(BspLib));

        Match chunks = Regex.Match(
            source,
            @"// Now read in 1K chunks\s*while \( nSize > 0 \).*?"
            + @"CRC32_ProcessBuffer\( crcvalue, chunk, nBytesRead \);",
            RegexOptions.Singleline,
            Limit);

        chunks.Success.ShouldBeTrue("the lump's bytes go into the CRC unaltered");
    }

    /// <summary>That the CRC is the ordinary reflected CRC-32, not a Valve variant.</summary>
    /// <remarks>
    /// **This is the assertion that lets a standard library be used instead of a hand-rolled
    /// table.** Three facts identify it, and all three are in `checksum_crc.cpp`:
    ///
    /// * `CRC32_INIT_VALUE` and `CRC32_XOR_VALUE` are both `0xFFFFFFFF` — initialise to all ones,
    ///   invert at the end.
    /// * the step is `ulCrc = pulCRCTable[*pb++ ^ (unsigned char)ulCrc] ^ (ulCrc >> 8)` — a
    ///   right-shifting, reflected table walk.
    /// * `pulCRCTable[1]` is `0x77073096`, which is the reflected polynomial `0xEDB88320`.
    ///
    /// Together those are CRC-32/ISO-HDLC, exactly what <c>System.IO.Hashing.Crc32</c> computes. If
    /// any of the three changed, this test fails and the implementation would have to grow its own
    /// table — which is the whole reason to assert them rather than assume.
    /// </remarks>
    [Test]
    public void Sdk_TheCrcVariant_IsStandardReflectedCrc32()
    {
        string source = Flat(Sdk(Checksum));

        source.ShouldContain("#define CRC32_INIT_VALUE 0xFFFFFFFFUL");
        source.ShouldContain("#define CRC32_XOR_VALUE 0xFFFFFFFFUL");

        // The reflected table's second entry identifies polynomial 0xEDB88320.
        source.ShouldContain("0x00000000, 0x77073096, 0xee0e612c, 0x990951ba,");

        Match step = Regex.Match(
            source,
            @"ulCrc = pulCRCTable\[\*pb\+\+ \^ \(unsigned char\)ulCrc\] \^ \(ulCrc >> 8\);",
            RegexOptions.None,
            Limit);

        step.Success.ShouldBeTrue("a right-shifting reflected table walk");
    }

    /// <summary>That this project's CRC agrees with Valve's on a known vector.</summary>
    /// <remarks>
    /// **The link between the citations above and the code.** CRC-32/ISO-HDLC of the nine bytes
    /// <c>123456789</c> is <c>0xCBF43926</c> — the standard check value every implementation of this
    /// variant publishes. If <c>System.IO.Hashing.Crc32</c> were a different variant, this catches
    /// it without needing a map.
    ///
    /// **Byte order matters and is asserted separately.** `Crc32.Hash` returns the digest
    /// little-endian, so reading it as a `uint` needs `ToUInt32LittleEndian` — a detail that would
    /// otherwise byte-swap every comparison and make every map look mismatched.
    /// </remarks>
    [Test]
    public void Crc32_ForTheStandardCheckVector_IsCbf43926()
    {
        BspMapChecksum.Crc32Of("123456789"u8).ShouldBe(0xCBF43926u);
    }

    private static string Sdk(string relativePath) =>
        Skip.Unless(SourceSdk.Text(relativePath), SourceSdk.Missing);

    private static string Flat(string source) =>
        Regex.Replace(source, @"[ \t]+", " ", RegexOptions.None, Limit);
}
