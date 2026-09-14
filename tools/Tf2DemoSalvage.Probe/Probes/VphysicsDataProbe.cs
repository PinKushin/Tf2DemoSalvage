using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s data as it stands once loaded — the tables and settings the file itself holds as zeros (B369).
/// </summary>
/// <remarks>
/// **Some of IVP's numbers are written when the library loads**: the OV tree's level tables at <c>18012d680</c> and below
/// <c>18012d7c8</c>, and globals such as <c>DAT_18012d64c</c>, read as zeros in the file. This loads the game's x64
/// <c>vphysics.dll</c> and prints each entry's bits and value at its image address.
///
/// **The control first**: <c>DAT_1800ea9b8</c> must read the double <c>1</c>; if it does not, the addresses do not fit this build
/// and nothing else is printed.
/// </remarks>
public sealed class VphysicsDataProbe : IProbe
{
    private const long OneAddress = 0x1800ea9b8;

    /// <inheritdoc />
    public string Name => "vphysics-data";

    /// <inheritdoc />
    public string Summary =>
        "the loaded vphysics.dll's data at an image address, as doubles, floats or ints — the tables the file holds as zeros: " +
        "vphysics-data <address hex> <count> [double | float | int]";

    /// <inheritdoc />
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine(Summary);
            return;
        }

        if (!VphysicsLibrary.TryLoad(output, out nint module))
        {
            return;
        }

        long one = Marshal.ReadInt64(VphysicsLibrary.Address(module, OneAddress));
        bool control = one == BitConverter.DoubleToInt64Bits(1d);

        output.WriteLine($"control: 1800ea9b8 reads 0x{one:x16}, the double 1: {control}");

        if (!control)
        {
            return;
        }

        long address = long.Parse(arguments[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int count = int.Parse(arguments[1], CultureInfo.InvariantCulture);
        string kind = arguments.Count > 2 ? arguments[2] : "double";
        int size = kind == "double" ? 8 : 4;

        for (int index = 0; index < count; index++)
        {
            long at = address + ((long)index * size);
            nint loaded = VphysicsLibrary.Address(module, at);
            string text = kind switch
            {
                "double" => Describe(Marshal.ReadInt64(loaded)),
                "float" => Describe(Marshal.ReadInt32(loaded), asFloat: true),
                _ => Describe(Marshal.ReadInt32(loaded), asFloat: false),
            };

            output.WriteLine($"{at:x} {text}");
        }
    }

    private static string Describe(long bits) =>
        string.Create(CultureInfo.InvariantCulture, $"0x{bits:x16} {BitConverter.Int64BitsToDouble(bits):R}");

    private static string Describe(int bits, bool asFloat) =>
        asFloat
            ? string.Create(CultureInfo.InvariantCulture, $"0x{bits:x8} {BitConverter.Int32BitsToSingle(bits):R}")
            : string.Create(CultureInfo.InvariantCulture, $"0x{bits:x8} {bits}");
}
