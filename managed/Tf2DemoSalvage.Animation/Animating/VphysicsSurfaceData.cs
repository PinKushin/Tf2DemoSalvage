using System;
using System.Globalization;
using System.Text;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// vphysics' surface-properties parser — props slot 1, <c>ParseSurfaceData</c> (<c>FUN_180018740</c>), with the key parser
/// (<c>FUN_18002e6c0</c>) and the tokenizer (<c>FUN_180003640</c>) under it (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *How `ParseSurfaceData` builds an entry*):
/// <code>
/// repeat:  (name, value) = key and value;  value is exactly "{" → a block, else skipped;  until no token
/// block:   staging = the surface named name, else "default", else zeros
///          repeat:  (key, value);  "}" → close;  "base" → staging = that surface's whole entry;  a physics key → (float)atof
///          until no token — a block the text ends inside is dropped
/// close:   GetSurfaceIndex(name) ≥ 0 → overwrite that surface's parameters (through GetIVPMaterial's index rules); else append
/// once, after the first text:  append $MATERIAL_INDEX_SHADOW, copied from "default" with friction 0.8f and elasticity 0.001f
/// </code>
/// **Only the physics parameters are kept**, `+0x14..+0x27`; `base` copies audio and game fields too, which nothing here reads.
/// **Hexadecimal floats are not read**: the C runtime's `atof` accepts `0x1.8p1`, which answers zero here.
/// </remarks>
internal static class VphysicsSurfaceData
{
    /// <summary>The token length <c>FUN_18002e6c0</c> passes, <c>0x400</c>, less its terminator.</summary>
    private const int MostTokenBytes = 0x3ff;

    private const string DefaultName = "default";

    /// <summary>Props slot 14's name for <see cref="VphysicsSurfaceProps.ShadowIndex"/>, <c>DAT_1800ec618</c>.</summary>
    private const string ShadowName = "$MATERIAL_INDEX_SHADOW";

    /// <summary><c>0x3f4ccccd</c>, written at <c>1800191a5</c>.</summary>
    private const float ShadowFriction = 0.8f;

    /// <summary><c>0x3a83126f</c>, written at <c>18001919d</c>.</summary>
    private static readonly float ShadowElasticity = BitConverter.Int32BitsToSingle(0x3a83126f);

    /// <summary>Parses one surface-properties text into <paramref name="props"/>.</summary>
    /// <param name="props">The surfaces so far.</param>
    /// <param name="text">The text, as the file holds it; its end reads as the engine's terminator.</param>
    /// <exception cref="InvalidOperationException">A block closes onto a surface index that resolves to none, where the engine writes through null.</exception>
    internal static void Parse(VphysicsSurfaceProps props, ReadOnlySpan<byte> text)
    {
        int at = 0;

        do
        {
            (string name, string value) = ParseKeyValue(text, ref at);

            if (value == "{")
            {
                Block(props, text, ref at, name);
            }
        }
        while (at >= 0);

        if (props.ShadowMade)
        {
            return;
        }

        props.ShadowMade = true;

        SurfacePhysicsParams shadow = props.GetIVPMaterial(props.GetSurfaceIndex(DefaultName))?.Physics ?? default;

        props.Add(new VphysicsSurface(ShadowName, shadow with { Friction = ShadowFriction, Elasticity = ShadowElasticity }, false));
        props.ShadowSurface = props.Surfaces.Count - 1;
    }

    private static void Block(VphysicsSurfaceProps props, ReadOnlySpan<byte> text, ref int at, string name)
    {
        int start = props.GetSurfaceIndex(name);

        if (start < 0)
        {
            start = props.GetSurfaceIndex(DefaultName);
        }

        SurfacePhysicsParams staging = props.GetIVPMaterial(start)?.Physics ?? default;
        int game = props.GetIVPMaterial(start)?.GameMaterial ?? 0;
        SurfaceSoundNames sounds = props.GetIVPMaterial(start)?.Sounds ?? default;

        do
        {
            (string key, string value) = ParseKeyValue(text, ref at);

            switch (key)
            {
                case "}":
                    Close(props, name, staging, game, sounds);
                    return;
                case "base":
                    if (props.GetIVPMaterial(props.GetSurfaceIndex(value)) is { } based)
                    {
                        staging = based.Physics;
                        game = based.GameMaterial;
                        sounds = based.Sounds;
                    }

                    break;
                case "bulletimpact":
                    sounds = sounds with { BulletImpact = value };
                    break;
                case "stepleft":
                    sounds = sounds with { StepLeft = value };
                    break;
                case "stepright":
                    sounds = sounds with { StepRight = value };
                    break;

                // `FUN_180018740`: a one-character value that is not a digit is `toupper`'d — the key parser lowercased it —
                // and anything else goes through `atoi`, stored as a short.
                case "gamematerial":
                    game = value.Length == 1 && (uint)(value[0] - '0') > 9
                        ? char.ToUpperInvariant(value[0])
                        : (short)Atoi(value);
                    break;
                case "friction":
                    staging = staging with { Friction = (float)Atof(value) };
                    break;
                case "elasticity":
                    staging = staging with { Elasticity = (float)Atof(value) };
                    break;
                case "density":
                    staging = staging with { Density = (float)Atof(value) };
                    break;
                case "thickness":
                    staging = staging with { Thickness = (float)Atof(value) };
                    break;
                case "dampening":
                    staging = staging with { Dampening = (float)Atof(value) };
                    break;
                default:
                    break;
            }
        }
        while (at >= 0);
    }

    private static void Close(VphysicsSurfaceProps props, string name, SurfacePhysicsParams staging, int game, SurfaceSoundNames sounds)
    {
        int index = props.GetSurfaceIndex(name);

        if (index < 0)
        {
            props.Add(new VphysicsSurface(name, staging, false) { GameMaterial = game, Sounds = sounds });
            return;
        }

        VphysicsSurface target = props.GetIVPMaterial(index) ??
            throw new InvalidOperationException($"The surface '{name}' closes onto index {index}, which names no surface.");

        props.Replace(
            target,
            new VphysicsSurface(target.Name, staging, target.HasSecondFriction) { GameMaterial = game, Sounds = sounds });
    }

    /// <summary>The C runtime's `atoi`: leading whitespace, a sign, digits, stopping at the first other character.</summary>
    private static int Atoi(string value)
    {
        int at = 0;

        while (at < value.Length && char.IsWhiteSpace(value[at]))
        {
            at++;
        }

        bool negative = at < value.Length && value[at] == '-';

        if (at < value.Length && (value[at] == '-' || value[at] == '+'))
        {
            at++;
        }

        long result = 0;

        while (at < value.Length && (uint)(value[at] - '0') <= 9 && result <= int.MaxValue)
        {
            result = (result * 10) + (value[at] - '0');
            at++;
        }

        return (int)(negative ? -result : result);
    }

    /// <summary>A key and its value, lowercased — <c>FUN_18002e6c0</c>. A key of exactly <c>}</c> reads no value.</summary>
    private static (string Key, string Value) ParseKeyValue(ReadOnlySpan<byte> text, ref int at)
    {
        string key = ParseFile(text, ref at);

        if (key == "}")
        {
            return (key, string.Empty);
        }

        string value = ParseFile(text, ref at);

        return (Lower(key), Lower(value));
    }

    /// <summary>One token — <c>FUN_180003640</c> with the break set <c>{}()':</c>; <paramref name="at"/> becomes −1 when none is found.</summary>
    /// <remarks>
    /// **Bytes are compared signed**, so every byte from <c>0x80</c> up is whitespace outside quotes. <c>//</c> runs to the line's
    /// end and <c>/* */</c> to its close; a quoted token runs to its quote or the text's end; a break character is a token alone.
    /// </remarks>
    private static string ParseFile(ReadOnlySpan<byte> text, ref int at)
    {
        if (at < 0)
        {
            return string.Empty;
        }

        int c;

        // Stryker disable all : the Boolean Literal mutator flips this loop's 'true' to 'false',
        // so the loop — which holds the only assignment to 'c' — never runs and the reads of 'c'
        // below are unassigned (CS0165), and Safe Mode then drops every mutation in this method —
        // B410.
        while (true)
        {
            c = Signed(text, at);

            while (c <= 0x20)
            {
                if (c == 0)
                {
                    at = -1;
                    return string.Empty;
                }

                c = Signed(text, ++at);
            }

            if (c == '/' && Signed(text, at + 1) == '/')
            {
                while (Signed(text, at) is not (0 or '\n'))
                {
                    at++;
                }

                continue;
            }

            if (c == '/' && Signed(text, at + 1) == '*')
            {
                at += 2;

                while (Signed(text, at) != 0 && !(Signed(text, at) == '*' && Signed(text, at + 1) == '/'))
                {
                    at++;
                }

                if (Signed(text, at) != 0)
                {
                    at += 2;
                }

                continue;
            }

            break;
        }

        // Stryker restore all

        StringBuilder token = new();

        if (c == '"')
        {
            at++;

            while (true)
            {
                c = Signed(text, at++);

                if (c is '"' or 0)
                {
                    return token.ToString();
                }

                token.Append((char)(byte)c);

                if (token.Length == MostTokenBytes)
                {
                    return token.ToString();
                }
            }
        }

        if (IsBreak(c))
        {
            at++;
            return ((char)c).ToString();
        }

        do
        {
            token.Append((char)(byte)c);
            c = Signed(text, ++at);
        }
        while (token.Length < MostTokenBytes && !IsBreak(c) && c > 0x20);

        return token.ToString();
    }

    /// <summary>The C runtime's <c>atof</c> for decimal text: leading space, a sign, digits with a point, an exponent, <c>inf</c> and <c>nan</c>.</summary>
    private static double Atof(string value)
    {
        int at = 0;

        while (at < value.Length && value[at] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r')
        {
            at++;
        }

        int start = at;

        if (at < value.Length && value[at] is '+' or '-')
        {
            at++;
        }

        if (value.AsSpan(at).StartsWith("inf", StringComparison.OrdinalIgnoreCase) ||
            value.AsSpan(at).StartsWith("nan", StringComparison.OrdinalIgnoreCase))
        {
            // The C runtime's NaN is positive with every payload bit set: vphysics narrows it to 0x7fffffff.
            double special = value.AsSpan(at).StartsWith("inf", StringComparison.OrdinalIgnoreCase)
                ? double.PositiveInfinity
                : BitConverter.Int64BitsToDouble(long.MaxValue);
            return value[start] == '-' ? -special : special;
        }

        int digits = 0;

        while (at < value.Length && char.IsAsciiDigit(value[at]))
        {
            at++;
            digits++;
        }

        if (at < value.Length && value[at] == '.')
        {
            at++;

            while (at < value.Length && char.IsAsciiDigit(value[at]))
            {
                at++;
                digits++;
            }
        }

        if (digits == 0)
        {
            return 0d;
        }

        if (at < value.Length && value[at] is 'e' or 'E')
        {
            int exponent = at + 1;

            if (exponent < value.Length && value[exponent] is '+' or '-')
            {
                exponent++;
            }

            if (exponent < value.Length && char.IsAsciiDigit(value[exponent]))
            {
                at = exponent;

                while (at < value.Length && char.IsAsciiDigit(value[at]))
                {
                    at++;
                }
            }
        }

        return double.Parse(value.AsSpan(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary><c>FUN_1800b9a10</c>: ASCII capitals lowered; the C locale leaves every other byte.</summary>
    private static string Lower(string token)
    {
        char[] lowered = token.ToCharArray();

        for (int index = 0; index < lowered.Length; index++)
        {
            if (lowered[index] is >= 'A' and <= 'Z')
            {
                lowered[index] = (char)(lowered[index] + 0x20);
            }
        }

        return new string(lowered);
    }

    private static bool IsBreak(int c) => c is '{' or '}' or '(' or ')' or '\'' or ':';

    private static int Signed(ReadOnlySpan<byte> text, int at) => at < text.Length ? (sbyte)text[at] : 0;
}
