using System;
using System.Text;

using Tf2DemoSalvage.Core.Primitives;

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
/// **Numbers go through <see cref="CStdlib"/>**, because the binary calls the C runtime's own <c>atof</c> and <c>atoi</c>;
/// the private copies this held were moved there, inf, nan and all. Hexadecimal floats are still not read.
/// </remarks>
internal static class VphysicsSurfaceData
{
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

        Staged staged = Staged.Of(props.GetIVPMaterial(start));

        do
        {
            (string key, string value) = ParseKeyValue(text, ref at);

            if (key == "}")
            {
                Close(props, name, staged);
                return;
            }

            staged = Apply(props, staged, key, value);
        }
        while (at >= 0);
    }

    /// <summary>One key of a block applied to the surface being parsed.</summary>
    private static Staged Apply(VphysicsSurfaceProps props, Staged staged, string key, string value)
    {
        SurfaceSoundNames sounds = staged.Sounds;
        SurfaceAudio audio = staged.Audio;
        SurfacePhysicsParams physics = staged.Physics;

        switch (key)
        {
            case "base":
                return props.GetIVPMaterial(props.GetSurfaceIndex(value)) is { } based ? Staged.Of(based) : staged;
            case "bulletimpact":
                sounds = sounds with { BulletImpact = value };
                break;
            case "stepleft":
                sounds = sounds with { StepLeft = value };
                break;
            case "stepright":
                sounds = sounds with { StepRight = value };
                break;
            case "impacthard":
                sounds = sounds with { ImpactHard = value };
                break;
            case "impactsoft":
                sounds = sounds with { ImpactSoft = value };
                break;
            case "audiohardnessfactor":
                audio = audio with { HardnessFactor = (float)CStdlib.Atof(value) };
                break;
            case "impacthardthreshold":
                audio = audio with { HardThreshold = (float)CStdlib.Atof(value) };
                break;
            case "audiohardminvelocity":
                audio = audio with { HardVelocityThreshold = (float)CStdlib.Atof(value) };
                break;
            case "scraperough":
                sounds = sounds with { ScrapeRough = value };
                break;
            case "scrapesmooth":
                sounds = sounds with { ScrapeSmooth = value };
                break;
            case "audioroughnessfactor":
                audio = audio with { RoughnessFactor = (float)CStdlib.Atof(value) };
                break;
            case "scraperoughthreshold":
                audio = audio with { RoughThreshold = (float)CStdlib.Atof(value) };
                break;

            // `FUN_180018740`: a one-character value that is not a digit is `toupper`'d — the key parser lowercased it —
            // and anything else goes through `atoi`, stored as a short.
            case "gamematerial":
                return staged with
                {
                    Game = value.Length == 1 && (uint)(value[0] - '0') > 9 ? char.ToUpperInvariant(value[0]) : (short)CStdlib.Atoi(value),
                };
            // `surfacegameprops_t`'s movement half (`vphysics_interface.h:942-946`); key names as the shipped
            // `scripts/surfaceproperties.txt` spells them. *Published header and shipped data; the parser's branch unread.*
            case "jumpfactor":
                return staged with { JumpFactor = (float)CStdlib.Atof(value) };
            case "maxspeedfactor":
                return staged with { MaxSpeedFactor = (float)CStdlib.Atof(value) };
            case "friction":
                physics = physics with { Friction = (float)CStdlib.Atof(value) };
                break;
            case "elasticity":
                physics = physics with { Elasticity = (float)CStdlib.Atof(value) };
                break;
            case "density":
                physics = physics with { Density = (float)CStdlib.Atof(value) };
                break;
            case "thickness":
                physics = physics with { Thickness = (float)CStdlib.Atof(value) };
                break;
            case "dampening":
                physics = physics with { Dampening = (float)CStdlib.Atof(value) };
                break;
            default:
                break;
        }

        return staged with { Sounds = sounds, Audio = audio, Physics = physics };
    }

    /// <summary>A block's surface as it is being parsed: what `base` copies and a key replaces.</summary>
    private readonly record struct Staged(SurfacePhysicsParams Physics, int Game, SurfaceSoundNames Sounds, SurfaceAudio Audio)
    {
        public float JumpFactor { get; init; }

        public float MaxSpeedFactor { get; init; }

        public static Staged Of(VphysicsSurface? surface) =>
            surface is null
                ? default
                : new(surface.Physics, surface.GameMaterial, surface.Sounds, surface.Audio)
                {
                    JumpFactor = surface.JumpFactor,
                    MaxSpeedFactor = surface.MaxSpeedFactor,
                };

        public VphysicsSurface Build(string name, bool hasSecondFriction) => new(name, Physics, hasSecondFriction)
        {
            GameMaterial = Game,
            Sounds = Sounds,
            Audio = Audio,
            JumpFactor = JumpFactor,
            MaxSpeedFactor = MaxSpeedFactor,
        };
    }

    private static void Close(VphysicsSurfaceProps props, string name, Staged staged)
    {
        int index = props.GetSurfaceIndex(name);

        if (index < 0)
        {
            props.Add(staged.Build(name, false));
            return;
        }

        VphysicsSurface target = props.GetIVPMaterial(index) ??
            throw new InvalidOperationException($"The surface '{name}' closes onto index {index}, which names no surface.");

        props.Replace(target, staged.Build(target.Name, target.HasSecondFriction));
    }

    /// <summary>A key and its value, lowercased — <c>FUN_18002e6c0</c>. A key of exactly <c>}</c> reads no value.</summary>
    private static (string Key, string Value) ParseKeyValue(ReadOnlySpan<byte> text, ref int at) =>
        Content.Assets.VphysicsTokens.ParseKeyValue(text, ref at);
}
