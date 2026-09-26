using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

using Tf2DemoSalvage.Scene.Hud;

// gdi32 is a system library; nothing else is loaded through these imports.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Tf2DemoSalvage.Fonts;

/// <summary>The GDI calls `CWin32Font` makes, made — the adapter half of <see cref="VguiWin32Font"/>.</summary>
/// <remarks>
/// Each call is the one `vguimatsurface.dll` makes, with its arguments (`CWin32Font_Create` 0x180017b70,
/// `CWin32Font_GetCharABCWidths` 0x180017ec0, `CWin32Font_GetCharRGBA` 0x1800181e0, the custom-font half of
/// `AddCustomFontFile` 0x180008710). Nothing here decides anything; the rules are in the portable class.
/// **One addition:** `GdiFlush` before the bitmap is read, since GDI may batch the draw and the engine reads the bits
/// through its own pointer at a moment this port cannot reproduce.
/// </remarks>
public sealed partial class GdiVgui : IVguiGdi, IDisposable
{
    private const int FrPrivate = 0x10;
    private const int MmText = 1;
    private const int TaUpdateCp = 1;
    private const int Opaque = 2;
    private const int Transparent = 1;
    private const int EtoOpaque = 2;
    private const uint GgoGray8Bitmap = 6;

    private readonly List<FontState> _fonts = [];

    /// <inheritdoc/>
    public bool AddFontResource(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return AddFontResourceExA(path, FrPrivate, 0) > 0;
    }

    /// <inheritdoc/>
    public unsafe bool FamilyExists(string family)
    {
        ArgumentNullException.ThrowIfNull(family);

        nint dc = CreateCompatibleDC(0);
        LogFontA query = default;
        byte[] name = Encoding.Default.GetBytes(family);

        // DEFAULT_CHARSET, as `Create` asks.
        query.CharSet = 1;
        name.AsSpan(0, Math.Min(name.Length, 31)).CopyTo(new Span<byte>(query.FaceName, 32));

        int* found = stackalloc int[1];

        // The return is the callback's last answer, which says nothing about whether it ran; the flag does.
        _ = EnumFontFamiliesExA(dc, &query, &Found, (nint)found, 0);
        DeleteDC(dc);

        return *found != 0;
    }

    [UnmanagedCallersOnly]
    private static unsafe int Found(nint logFont, nint textMetric, uint fontType, nint parameter)
    {
        *(int*)parameter = 1;
        return 0;
    }

    /// <inheritdoc/>
    public VguiGdiFont? CreateFont(string face, int tall, int weight, bool italic, bool underline, bool strikeout, int charset, int quality)
    {
        ArgumentNullException.ThrowIfNull(face);

        nint font = CreateFontA(tall, 0, 0, 0, weight, italic ? 1u : 0, underline ? 1u : 0, strikeout ? 1u : 0, (uint)charset, 0, 0, (uint)quality, 0, face);

        if (font == 0)
        {
            return null;
        }

        nint dc = CreateCompatibleDC(0);

        // `Create` ignores every one of these returns; so does this.
        _ = SetMapMode(dc, MmText);
        SelectObject(dc, font);
        _ = SetTextAlign(dc, TaUpdateCp);

        FontState state = new(dc, font);

        _fonts.Add(state);

        if (!GetTextMetricsA(dc, out TextMetricA metrics))
        {
            return null;
        }

        return new VguiGdiFont(state, metrics.Height, metrics.Ascent, metrics.MaxCharWidth);
    }

    /// <inheritdoc/>
    public void CreateBitmap(VguiGdiFont font, int wide, int tall)
    {
        FontState state = State(font);
        BitmapInfoHeader header = new() { Size = 40, Width = wide, Height = -tall, Planes = 1, BitCount = 32 };

        state.Bitmap = CreateDIBSection(state.Dc, ref header, 0, out nint bits, 0, 0);
        state.Bits = bits;
        state.Wide = wide;
        state.Tall = tall;
        SelectObject(state.Dc, state.Bitmap);
    }

    /// <inheritdoc/>
    public (int A, int B, int C)? GetCharAbcWidths(VguiGdiFont font, char character)
    {
        FontState state = State(font);

        if (GetCharABCWidthsW(state.Dc, character, character, out Abc widths) || GetCharABCWidthsA(state.Dc, character, character, out widths))
        {
            return (widths.A, (int)widths.B, widths.C);
        }

        return null;
    }

    /// <inheritdoc/>
    public int? GetTextExtent(VguiGdiFont font, char character)
    {
        FontState state = State(font);
        byte[] ansi = Encoding.Default.GetBytes([character]);

        return GetTextExtentPoint32A(state.Dc, ansi, ansi.Length, out Size size) ? size.Cx : null;
    }

    /// <inheritdoc/>
    public VguiGrayGlyph? GetGlyphOutlineGray8(VguiGdiFont font, int character)
    {
        FontState state = State(font);
        Mat2 identity = new() { M11 = 1 << 16, M22 = 1 << 16 };

        SelectObject(state.Dc, state.Font);

        uint size = GetGlyphOutlineA(state.Dc, (uint)character, GgoGray8Bitmap, out GlyphMetrics metrics, 0, null, ref identity);

        if ((int)size <= 0)
        {
            return null;
        }

        byte[] levels = new byte[size];

        GetGlyphOutlineA(state.Dc, (uint)character, GgoGray8Bitmap, out metrics, size, levels, ref identity);

        return new VguiGrayGlyph(levels, (int)metrics.BlackBoxX, (int)metrics.BlackBoxY, metrics.OriginY);
    }

    /// <inheritdoc/>
    public byte[] DrawGlyph(VguiGdiFont font, char character, int penX, int clearWide, int clearTall)
    {
        FontState state = State(font);
        Rect clear = new() { Right = clearWide, Bottom = clearTall };

        // `GetCharRGBA` ignores every one of these returns; so does this.
        SelectObject(state.Dc, state.Font);
        _ = SetBkColor(state.Dc, 0);
        _ = SetTextColor(state.Dc, 0xffffff);
        _ = SetBkMode(state.Dc, Opaque);
        MoveToEx(state.Dc, penX, 0, 0);
        ExtTextOutW(state.Dc, 0, 0, EtoOpaque, ref clear, null, 0, 0);
        ExtTextOutW(state.Dc, 0, 0, 0, 0, character.ToString(), 1, 0);
        _ = SetBkMode(state.Dc, Transparent);
        GdiFlush();

        byte[] bytes = new byte[state.Wide * state.Tall * 4];

        Marshal.Copy(state.Bits, bytes, 0, bytes.Length);

        return bytes;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (FontState state in _fonts)
        {
            DeleteDC(state.Dc);
            DeleteObject(state.Bitmap);
            DeleteObject(state.Font);
        }

        _fonts.Clear();
    }

    private static FontState State(VguiGdiFont font)
    {
        ArgumentNullException.ThrowIfNull(font);

        return (FontState)font.Handle;
    }

    private sealed class FontState(nint dc, nint font)
    {
        public nint Dc { get; } = dc;

        public nint Font { get; } = font;

        public nint Bitmap { get; set; }

        public nint Bits { get; set; }

        public int Wide { get; set; }

        public int Tall { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct LogFontA
    {
        public int Height;
        public int Width;
        public int Escapement;
        public int Orientation;
        public int Weight;
        public byte Italic;
        public byte Underline;
        public byte StrikeOut;
        public byte CharSet;
        public byte OutPrecision;
        public byte ClipPrecision;
        public byte Quality;
        public byte PitchAndFamily;
        public fixed byte FaceName[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextMetricA
    {
        public int Height;
        public int Ascent;
        public int Descent;
        public int InternalLeading;
        public int ExternalLeading;
        public int AveCharWidth;
        public int MaxCharWidth;
        public int Weight;
        public int Overhang;
        public int DigitizedAspectX;
        public int DigitizedAspectY;
        public byte FirstChar;
        public byte LastChar;
        public byte DefaultChar;
        public byte BreakChar;
        public byte Italic;
        public byte Underlined;
        public byte StruckOut;
        public byte PitchAndFamily;
        public byte CharSet;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Abc
    {
        public int A;
        public uint B;
        public int C;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GlyphMetrics
    {
        public uint BlackBoxX;
        public uint BlackBoxY;
        public int OriginX;
        public int OriginY;
        public short CellIncX;
        public short CellIncY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Mat2
    {
        public int M11;
        public int M12;
        public int M21;
        public int M22;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
    private static partial int AddFontResourceExA(string name, int flags, nint reserved);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int EnumFontFamiliesExA(nint dc, LogFontA* logFont, delegate* unmanaged<nint, nint, uint, nint, int> callback, nint parameter, uint flags);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
    private static partial nint CreateFontA(
        int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut,
        uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);

    [LibraryImport("gdi32.dll")]
    private static partial int SetMapMode(nint dc, int mode);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint handle);

    [LibraryImport("gdi32.dll")]
    private static partial uint SetTextAlign(nint dc, uint align);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTextMetricsA(nint dc, out TextMetricA metrics);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, ref BitmapInfoHeader info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCharABCWidthsW(nint dc, uint first, uint last, out Abc widths);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCharABCWidthsA(nint dc, uint first, uint last, out Abc widths);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTextExtentPoint32A(nint dc, byte[] text, int length, out Size size);

    [LibraryImport("gdi32.dll")]
    private static partial uint GetGlyphOutlineA(nint dc, uint character, uint format, out GlyphMetrics metrics, uint size, byte[]? buffer, ref Mat2 transform);

    [LibraryImport("gdi32.dll")]
    private static partial uint SetBkColor(nint dc, uint colour);

    [LibraryImport("gdi32.dll")]
    private static partial uint SetTextColor(nint dc, uint colour);

    [LibraryImport("gdi32.dll")]
    private static partial int SetBkMode(nint dc, int mode);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveToEx(nint dc, int x, int y, nint previous);

    [LibraryImport("gdi32.dll", EntryPoint = "ExtTextOutW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ExtTextOutW(nint dc, int x, int y, uint options, ref Rect rect, string? text, uint count, nint spacing);

    [LibraryImport("gdi32.dll", EntryPoint = "ExtTextOutW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ExtTextOutW(nint dc, int x, int y, uint options, nint rect, string text, uint count, nint spacing);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GdiFlush();
}
