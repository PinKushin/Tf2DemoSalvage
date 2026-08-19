using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// <c>BspCubemaps</c> — <c>LUMP_CUBEMAPS</c> 42, where a map's reflections are placed.
/// </summary>
/// <remarks>
/// **A cubemap is placed, not named.** <c>dcubemapsample_t</c> (<c>bspfile.h:992</c>) is three ints
/// and a byte, and the comment beside it is the specification for everything downstream:
/// <c>"the filename for the vtf file is derived from the position"</c>. So reading this lump is
/// half of resolving <c>$envmap "env_cubemap"</c>, which 79 of cp_process_final's 410 materials ask
/// for and none of which currently reflects. B55.
///
/// **The record is 16 bytes and the declaration says 13.** Three 4-byte ints and one unsigned char
/// is thirteen bytes of content; C++ pads the struct to its own four-byte alignment, and the lump
/// is written with <c>SwapLumpToDisk&lt;dcubemapsample_t&gt;</c>, which writes <c>sizeof</c>. So the
/// three padding bytes are on disk.
///
/// **This file originally used 13 and every test in it passed.** The fixtures were built to the
/// same belief as the reader, so they could only ever confirm it — the whole suite was one
/// hypothesis wearing ten assertions. What falsified it was
/// <c>CubemapPlacementTests</c> reading a map vbsp compiled: the first cubemap came out at
/// <c>(0, 0, 608)</c>, entirely plausible, and the second at
/// <c>(-2147483648, -2147483642, 1879048200)</c>.
///
/// So the fixtures here carry SEVERAL entries deliberately — a stride bug is invisible at one — and
/// the real-map test is the control none of them can be.
/// </remarks>
public sealed class BspCubemapsTests
{
    private const int HeaderSize = 1036;
    private const int LumpCubemaps = 42;

    [Test]
    public void APlacementIsReadAtItsPosition()
    {
        IReadOnlyList<BspCubemap> cubemaps = BspCubemaps.Read(
            Map(Sample(544, 1952, 929, size: 1)));

        cubemaps.Count.ShouldBe(1);
        cubemaps[0].X.ShouldBe(544);
        cubemaps[0].Y.ShouldBe(1952);
        cubemaps[0].Z.ShouldBe(929);
    }

    [Test]
    public void EveryPlacementIsReadAndNoneIsShiftedByPadding()
    {
        // **The stride, and the only arrangement that can measure it.** With one entry a 16-byte
        // reader gets the right answer; with three it reads the second from three bytes early and
        // gets numbers that are still perfectly plausible coordinates.
        //
        // The values are chosen so that a misread cannot coincide: each coordinate is distinct and
        // none is a byte-shifted view of a neighbour.
        IReadOnlyList<BspCubemap> cubemaps = BspCubemaps.Read(
            Map(
                Sample(100, 200, 300, size: 1),
                Sample(-400, 500, -600, size: 1),
                Sample(700, -800, 900, size: 1)));

        cubemaps.Count.ShouldBe(3);

        cubemaps[1].X.ShouldBe(-400);
        cubemaps[1].Y.ShouldBe(500);
        cubemaps[1].Z.ShouldBe(-600);

        cubemaps[2].X.ShouldBe(700);
        cubemaps[2].Y.ShouldBe(-800);
        cubemaps[2].Z.ShouldBe(900);
    }

    [Test]
    public void ANegativeCoordinateSurvives()
    {
        // The origin is a SIGNED int — a map's coordinates run either side of zero, and roughly
        // half of any real map is negative. Read as unsigned, a cubemap at -600 lands at
        // 4,294,966,696 and the nearest-cubemap search sends every surface near it to whichever
        // one happens to be closest to the far corner of the world.
        BspCubemaps.Read(Map(Sample(-1, -2, -3, size: 1)))[0]
            .ShouldBe(new BspCubemap(-1, -2, -3, 1));
    }

    [Test]
    public void ASizeOfZeroBecomesTheDefaultOfThirtyTwo()
    {
        // **The inverted default.** bspfile.h:997 spells out both halves — `0 - default` and
        // `otherwise, 1<<(size-1)` — so zero is an escape value rather than a value, and
        // DEFAULT_CUBEMAP_SIZE is 32 (vbsp/cubemap.cpp:280).
        //
        // Passing zero through the shift is not merely wrong, it is spectacular: `1 << -1` in C# is
        // `1 << 31`, because the shift count is masked to five bits. A cubemap claiming to be two
        // billion pixels square.
        BspCubemaps.Read(Map(Sample(0, 0, 0, size: 0)))[0].Size.ShouldBe(32);
    }

    [Test]
    public void ASizeIsOneShiftedByOneLessThanItsCode()
    {
        // Both ends of the range that actually appears, and neither is 32 — a reader that returned
        // the default unconditionally would pass the test above and fail here.
        BspCubemaps.Read(Map(Sample(0, 0, 0, size: 1)))[0].Size.ShouldBe(1);
        BspCubemaps.Read(Map(Sample(0, 0, 0, size: 7)))[0].Size.ShouldBe(64);
        BspCubemaps.Read(Map(Sample(0, 0, 0, size: 8)))[0].Size.ShouldBe(128);
    }

    [Test]
    public void AMapWithNoCubemapLumpReadsAsEmptyRatherThanThrowing()
    {
        // Older maps and tool-only maps carry none, and a map with no reflections is a map that
        // draws matte — not a map that fails to open.
        BspCubemaps.Read(Map()).ShouldBeEmpty();
    }

    [Test]
    public void ATruncatedFinalRecordIsDroppedRatherThanReadPastTheEnd()
    {
        // A lump whose length is not a whole number of records is corruption, and the answer is the
        // records that are whole. Reading the partial one would compose an origin from whatever
        // follows the lump in the file.
        byte[] whole = Sample(11, 22, 33, size: 2);
        byte[] truncated = new byte[whole.Length + 7];
        whole.CopyTo(truncated, 0);

        IReadOnlyList<BspCubemap> cubemaps = BspCubemaps.Read(Map(truncated));

        cubemaps.Count.ShouldBe(1);
        cubemaps[0].X.ShouldBe(11);
    }

    [Test]
    public void ACubemapsTextureNameIsBuiltFromTheMapAndThePosition()
    {
        // vbsp builds it, so this is transcription rather than invention
        // (vbsp/cubemap.cpp:511, via GeneratePatchedName( "c", info, false, ... )):
        //
        //     Q_snprintf( pBuffer, nMaxLen, "maps/%s/%s%s%d_%d_%d", info.m_pMapName,
        //         pMaterialName, pSeparator, info.m_pOrigin[0], ... );
        //
        // with pSeparator empty for a TEXTURE name and "_" for a MATERIAL name. The material form
        // is the one this project has already seen without reading the lump — MapAssetsTests
        // records `maps/cp_process_final/icarus/glasschrome001_544_1952_929.vmt` in the map's own
        // pakfile, and those three numbers are this origin.
        BspCubemaps.TextureName("cp_process_final", new BspCubemap(544, 1952, 929, 32))
            .ShouldBe("maps/cp_process_final/c544_1952_929");
    }

    [Test]
    public void ATextureNameIsLowercasedTheWayVbspWroteIt()
    {
        // GeneratePatchedName ends with Q_strlower, so the name in the pakfile is lowercase
        // whatever case the map was compiled under. A lookup that preserves the caller's case
        // misses the file on any case-sensitive path — and the archives this project reads are
        // matched by name, not by a filesystem.
        BspCubemaps.TextureName("CP_Process_Final", new BspCubemap(1, 2, 3, 32))
            .ShouldBe("maps/cp_process_final/c1_2_3");
    }

    [Test]
    public void ANegativePositionKeepsItsSignInTheName()
    {
        // vbsp formats with %d, so a negative coordinate carries its minus into the filename. The
        // separator is an underscore and the sign is a hyphen, which is why they do not collide.
        BspCubemaps.TextureName("koth_viaduct", new BspCubemap(-544, 0, -929, 32))
            .ShouldBe("maps/koth_viaduct/c-544_0_-929");
    }

    /// <summary>
    /// One <c>dcubemapsample_t</c>: three little-endian ints, one byte, three bytes of padding.
    /// </summary>
    /// <remarks>
    /// **This helper was 13 bytes wide and every test in this file passed against a reader that was
    /// wrong.** That is the point worth keeping: a fixture built to match the belief under test
    /// cannot falsify it, however many cases are written on top. The error was found by
    /// <c>CubemapPlacementTests</c> reading a map vbsp actually compiled.
    ///
    /// The padding is deliberately filled with a recognisable pattern rather than zeroes, so that a
    /// reader accidentally including it in a coordinate produces something obviously wrong instead
    /// of something plausibly small.
    /// </remarks>
    private static byte[] Sample(int x, int y, int z, byte size)
    {
        byte[] record = new byte[BspCubemaps.Stride];

        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(0), x);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), y);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(8), z);
        record[12] = size;
        record[13] = 0xDE;
        record[14] = 0xAD;
        record[15] = 0xBE;

        return record;
    }

    /// <summary>A BSP carrying only a cubemap lump, or none at all.</summary>
    private static byte[] Map(params byte[][] samples)
    {
        List<byte> payload = [];

        foreach (byte[] sample in samples)
        {
            payload.AddRange(sample);
        }

        byte[] file = new byte[HeaderSize + payload.Count];

        Encoding.ASCII.GetBytes("VBSP").CopyTo(file, 0);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), 21);

        if (payload.Count > 0)
        {
            payload.CopyTo(file, HeaderSize);

            int entry = 8 + (LumpCubemaps * 16);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(entry), HeaderSize);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(entry + 4), payload.Count);
        }

        return file;
    }
}
