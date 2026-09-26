using System;
using System.Globalization;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>What a `KeyValues` holding one value is — the message `RequestInfo` and `SetInfo` pass.</summary>
public enum VguiKeyValueType
{
    /// <summary>`TYPE_STRING`.</summary>
    Text,

    /// <summary>`TYPE_INT`.</summary>
    Whole,

    /// <summary>`TYPE_FLOAT`.</summary>
    Real,

    /// <summary>`TYPE_COLOR`.</summary>
    Color,
}

/// <summary>A one-key `KeyValues`, with the type conversions tier1/KeyValues.cpp applies when it is read as another type.</summary>
/// <param name="Type">What was set.</param>
/// <param name="Text">A string's value.</param>
/// <param name="Whole">An int's value.</param>
/// <param name="Real">A float's value.</param>
/// <param name="Colour">A colour's value.</param>
public readonly record struct VguiKeyValue(
    VguiKeyValueType Type, string Text = "", int Whole = 0, float Real = 0f, (byte Red, byte Green, byte Blue, byte Alpha) Colour = default)
{
    /// <summary>`SetFloat`.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The key.</returns>
    public static VguiKeyValue FromReal(float value) => new(VguiKeyValueType.Real, Real: value);

    /// <summary>`SetColor`.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The key.</returns>
    public static VguiKeyValue FromColour((byte, byte, byte, byte) value) => new(VguiKeyValueType.Color, Colour: value);

    /// <summary>`GetFloat`: a string through `atof`, an int widened; a colour answers 0.</summary>
    public float AsReal => Type switch
    {
        VguiKeyValueType.Real => Real,
        VguiKeyValueType.Whole => Whole,
        VguiKeyValueType.Text => PanelLayout.Atof(Text),
        _ => 0f,
    };

    /// <summary>`GetInt`: a float truncated, a string through `atoi`; a colour answers 0.</summary>
    public int AsWhole => Type switch
    {
        VguiKeyValueType.Whole => Whole,
        VguiKeyValueType.Real => (int)Real,
        VguiKeyValueType.Text => PanelLayout.Atoi(Text),
        _ => 0,
    };

    /// <summary>`GetString( name, "" )`: a float as `%f`, an int as `%d`; a colour is not converted and answers "".</summary>
    public string AsText => Type switch
    {
        VguiKeyValueType.Text => Text,
        VguiKeyValueType.Real => Real.ToString("F6", CultureInfo.InvariantCulture),
        VguiKeyValueType.Whole => Whole.ToString(CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    /// <summary>`GetColor`: a float or int fills red alone; a string is `sscanf( "%f %f %f %f" )` into zeroes, each truncated.</summary>
    public (byte Red, byte Green, byte Blue, byte Alpha) AsColour
    {
        get
        {
            switch (Type)
            {
                case VguiKeyValueType.Color:
                    return Colour;
                case VguiKeyValueType.Real:
                    return ((byte)Real, 0, 0, 0);
                case VguiKeyValueType.Whole:
                    return ((byte)Whole, 0, 0, 0);
                default:
                    Span<float> channels = stackalloc float[4];
                    PanelLayout.ScanFloats(Text, channels);

                    return ((byte)channels[0], (byte)channels[1], (byte)channels[2], (byte)channels[3]);
            }
        }
    }
}
