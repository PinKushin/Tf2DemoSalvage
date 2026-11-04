using System;
using System.Collections;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// `r_lightaverage` and `r_lightinterp`: what a moving model draws is its lighting state blended with what it drew last, by instance, with
/// a time constant of `1 / r_lightinterp` seconds (B676).
/// </summary>
/// <remarks>
/// *Read in decompile and disassembly*, `engine.dll` build 11191711: `FUN_1800f4310`, from `FUN_1800f2dd0` when `*param_7` and `param_4`
/// are both 0, so never a static prop's, which has a handle. `FUN_1800f2570` is the first draw and `FUN_1800f4090` the cube.
///
/// **The history lives on the model instance**, created with `+0x128 = -999999` (`FUN_1800eb5d0`, the only such write) and stamped
/// only by `FUN_1800f2570` and `FUN_1800f4310`: nothing resets it on a teleport or a respawn. *Not read for this:* that a model change
/// destroys the instance (`C_BaseEntity::SetModelPointer`, from memory of the SDK), so it starts again here on that.
///
/// **Identity is the engine's pointer.** A world light is one lump entry, which this compares by value, since two entries alike in every
/// field draw alike. A dlight is a slot the engine overwrites each frame (`FUN_1801b9350`, `DAT_180775730`), so it is its slot here. The
/// sun is one light in a slot (`FUN_1800f2010` case 3) and travels apart in this project, so here it is stored and counted among the slots.
///
/// **The blend is of the raw intensity.** The style comes after, in `FUN_1800f2010`, so a flickering lamp flickers at once.
/// </remarks>
public sealed class ModelLightAverage
{
    /// <summary>`LightDesc_t m_LocalLightDescs[4]`: a draw has four slots.</summary>
    private const int Slots = LocalLights.MaximumLocalLights;

    /// <summary>What can be stored: four slots, and this project's sun, which its own ranking does not count among them.</summary>
    private const int Stored = Slots + 1;

    /// <summary>
    /// A clock further behind than this is a seek, as <see cref="EntityModelSet"/>'s client cycles judge one: the engine cannot seek backward,
    /// since a skip back reloads the demo and makes every instance new. Nearer than this the engine's rule holds the light.
    /// </summary>
    private const double LongestBackstep = 1d;

    /// <summary>`1.0 &lt;= r² + g² + b²`: a light that left stays while its stored colour is this long.</summary>
    private const float KeptLengthSquared = 1f;

    /// <summary>`FUN_1800f4090`'s clamp on each cube channel.</summary>
    private const float BrightestFace = 8f;

    private readonly Dictionary<int, History> _histories = [];

    /// <summary>`r_lightaverage`, default 1.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>`r_lightinterp`, default 5.</summary>
    public float Rate { get; set; } = 5f;

    /// <summary>
    /// `min( r_worldlights, MaxNumLights )` (`DAT_18075cdc8`, named by `FUN_180010150`). Four: `r_worldlights` is not ported, nor does this
    /// project's state builder take it, so only a test sets this lower.
    /// </summary>
    public int Limit { get; init; } = Slots;

    /// <summary>
    /// Whether the fold's light reaches a point: `FUN_1801baeb0`, flags 0, traces. Null sees everything. Asked only past <see cref="Limit"/>.
    /// </summary>
    public Func<BspWorldLight, float, float, float, bool>? Seen { get; set; }

    /// <summary>What a model draws.</summary>
    /// <param name="Cube">The cube.</param>
    /// <param name="Sun">The sun, or null.</param>
    /// <param name="Locals">The local lights.</param>
    public readonly record struct Averaged(AmbientCube Cube, SunLight? Sun, IReadOnlyList<LocalLight> Locals);

    /// <summary>How many instances have drawn through here since the level loaded, counted where they are stored.</summary>
    public int Instances => _histories.Count;

    /// <summary>Forgets every instance: a level shutdown destroys them.</summary>
    public void Clear() => _histories.Clear();

    /// <summary>`FUN_1800f4310`, for one model's draw.</summary>
    /// <param name="instance">The model's instance, its entity.</param>
    /// <param name="model">The model the instance was made for.</param>
    /// <param name="state">The state, or null where the caller has no slots to give, as a map with no lights.</param>
    /// <param name="cube">The state's cube.</param>
    /// <param name="sun">The sun the state sees.</param>
    /// <param name="x">The model's illumination point, for the fold.</param>
    /// <param name="y">The model's illumination point.</param>
    /// <param name="z">The model's illumination point.</param>
    /// <param name="seconds">The client's time, `FUN_1800a4320`.</param>
    /// <param name="half">The pose half this frame's lists belong to (B581).</param>
    /// <param name="style">Each light style's value over 264; one for every style when null.</param>
    /// <returns>What it draws.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    public Averaged Draw(
        int instance,
        string model,
        LightingState? state,
        AmbientCube cube,
        SunLight? sun,
        float x,
        float y,
        float z,
        double seconds,
        int half,
        Func<int, float>? style)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!Enabled || Rate == 0f)
        {
            return new Averaged(cube, sun, state?.Lighting(style).Locals ?? []);
        }

        Span<BspWorldLight> now = stackalloc BspWorldLight[Stored];
        int count = Current(state, sun, now);

        if (!_histories.TryGetValue(instance, out History? history) || !string.Equals(history.Model, model, StringComparison.Ordinal) ||
            seconds < history.Time - LongestBackstep)
        {
            history = new History(model);
            _histories[instance] = history;
            history.Store(cube, now, count, seconds);

            return Output(cube, now, history.Colours, count, half, style, history.Lists);
        }

        double elapsed = seconds - history.Time;

        if (elapsed > 0d)
        {
            history.Time = seconds;
        }
        else
        {
            elapsed = 0d;
        }

        float f = MathF.Exp(-((float)elapsed * Rate));
        AmbientCube drawn = history.Cube = Blend(cube, history.Cube, f);

        return Slotted(history, drawn, now, count, f, x, y, z, half, style);
    }

    /// <summary>The state's lights and the sun as one list of slots, which is the engine's one list.</summary>
    private static int Current(LightingState? state, SunLight? sun, Span<BspWorldLight> into)
    {
        int count = 0;

        for (int slot = 0; state is not null && slot < state.Count; slot++)
        {
            into[count++] = state.SlotAt(slot);
        }

        if (sun is { } reaching)
        {
            into[count++] = new BspWorldLight(
                default,
                (reaching.Red, reaching.Green, reaching.Blue),
                (reaching.DirectionX, reaching.DirectionY, reaching.DirectionZ),
                WorldLightKind.SkyLight);
        }

        return count;
    }

    /// <summary>`FUN_1800f4310` from the matching of lights on.</summary>
    private Averaged Slotted(
        History history,
        AmbientCube cube,
        Span<BspWorldLight> now,
        int count,
        float f,
        float x,
        float y,
        float z,
        int half,
        Func<int, float>? style)
    {
        Span<bool> kept = stackalloc bool[Stored];
        Span<BspWorldLight> lights = stackalloc BspWorldLight[Stored];
        Span<(float Red, float Green, float Blue)> colours = stackalloc (float, float, float)[Stored];

        for (int index = 0; index < count; index++)
        {
            (float Red, float Green, float Blue) old = default;

            for (int stored = 0; stored < history.Count; stored++)
            {
                if (SameLight(now[index], history.Lights[stored]))
                {
                    kept[stored] = true;
                    old = history.Colours[stored];
                    break;
                }
            }

            (float red, float green, float blue) = now[index].Intensity;

            lights[index] = now[index];
            colours[index] = (red - ((red - old.Red) * f), green - ((green - old.Green) * f), blue - ((blue - old.Blue) * f));
        }

        int total = count;
        AmbientCube folded = cube;

        for (int stored = 0; stored < history.Count; stored++)
        {
            (float red, float green, float blue) = history.Colours[stored];

            if (kept[stored] || !((red * red) + (green * green) + (blue * blue) >= KeptLengthSquared))
            {
                continue;
            }

            if (total >= Slots)
            {
                break;
            }

            lights[total] = history.Lights[stored];
            colours[total] = (f * red, f * green, f * blue);

            if (total >= Limit)
            {
                BspWorldLight leaving = lights[total] with { Intensity = colours[total] };

                if (Seen?.Invoke(leaving, x, y, z) ?? true)
                {
                    folded = LocalLights.Fold(folded, leaving, x, y, z, style);
                }
            }

            total++;
        }

        history.Count = total;

        for (int index = 0; index < total; index++)
        {
            history.Lights[index] = lights[index];
            history.Colours[index] = colours[index];
        }

        // Four lamps and this project's sun are five, and the limit is the engine's four slots, so at its default it cuts nothing.
        return Output(folded, lights, history.Colours, Limit >= Slots ? total : Math.Min(total, Limit), half, style, history.Lists);
    }

    /// <summary>What the draw takes: the first `drawn` of the lights, each at its colour and then its style.</summary>
    private static Averaged Output(
        AmbientCube cube,
        ReadOnlySpan<BspWorldLight> lights,
        ReadOnlySpan<(float Red, float Green, float Blue)> colours,
        int drawn,
        int half,
        Func<int, float>? style,
        LocalsList[] lists)
    {
        LocalsList list = lists[half];
        SunLight? sun = null;

        list.Count = 0;

        for (int index = 0; index < drawn; index++)
        {
            BspWorldLight light = lights[index] with { Intensity = colours[index] };

            if (light.Kind == WorldLightKind.SkyLight)
            {
                sun = new SunLight(
                    light.Intensity.Red,
                    light.Intensity.Green,
                    light.Intensity.Blue,
                    light.Normal.X,
                    light.Normal.Y,
                    light.Normal.Z);
            }
            else if (light.Kind != WorldLightKind.QuakeLight)
            {
                list.Add(LocalLights.Local(light, style));
            }
        }

        return new Averaged(cube, sun, list);
    }

    /// <summary>`FUN_1800f4090`: f clamped to 0..1, each channel `new - (new - old) * f` clamped to 0..8.</summary>
    private static AmbientCube Blend(AmbientCube now, AmbientCube old, float f)
    {
        f = Math.Clamp(f, 0f, 1f);

        return new AmbientCube(
            Blend(now.PositiveX, old.PositiveX, f),
            Blend(now.NegativeX, old.NegativeX, f),
            Blend(now.PositiveY, old.PositiveY, f),
            Blend(now.NegativeY, old.NegativeY, f),
            Blend(now.PositiveZ, old.PositiveZ, f),
            Blend(now.NegativeZ, old.NegativeZ, f));
    }

    private static (float Red, float Green, float Blue) Blend(
        (float Red, float Green, float Blue) now, (float Red, float Green, float Blue) old, float f) => (
        Clamped(now.Red - ((now.Red - old.Red) * f)),
        Clamped(now.Green - ((now.Green - old.Green) * f)),
        Clamped(now.Blue - ((now.Blue - old.Blue) * f)));

    /// <summary>The `COMISS` pair's order: at or below 0 is 0, then at or above 8 is 8. A NaN falls through both, as it does there.</summary>
    private static float Clamped(float value)
    {
        if (value <= 0f)
        {
            value = 0f;
        }

        return value >= BrightestFace ? BrightestFace : value;
    }

    /// <summary>Whether two slots hold one light: see the class's remarks.</summary>
    private static bool SameLight(in BspWorldLight a, in BspWorldLight b)
    {
        if (a.DynamicSlot >= 0 || b.DynamicSlot >= 0)
        {
            return a.DynamicSlot == b.DynamicSlot;
        }

        if (a.Kind == WorldLightKind.SkyLight || b.Kind == WorldLightKind.SkyLight)
        {
            return a.Kind == b.Kind;
        }

        return a.Origin == b.Origin && a.Intensity == b.Intensity && a.Normal == b.Normal && a.Kind == b.Kind && a.Style == b.Style;
    }

    /// <summary>One instance's `+0x18..+0x128`, and the lists it hands each pose half.</summary>
    private sealed class History(string model)
    {
        public string Model { get; } = model;

        public double Time { get; set; }

        public AmbientCube Cube { get; set; }

        public int Count { get; set; }

        public BspWorldLight[] Lights { get; } = new BspWorldLight[Stored];

        public (float Red, float Green, float Blue)[] Colours { get; } = new (float, float, float)[Stored];

        public LocalsList[] Lists { get; } = [new(), new()];

        /// <summary>`FUN_1800f2570`: the state, each slot's colour, and the time.</summary>
        public void Store(AmbientCube cube, ReadOnlySpan<BspWorldLight> lights, int count, double seconds)
        {
            Cube = cube;
            Count = count;
            Time = seconds;

            for (int index = 0; index < count; index++)
            {
                Lights[index] = lights[index];
                Colours[index] = lights[index].Intensity;
            }
        }
    }

    /// <summary>A list a draw reads by reference, rewritten when its pose half comes round again (B581): no allocation per model per frame.</summary>
    private sealed class LocalsList : IReadOnlyList<LocalLight>
    {
        private readonly LocalLight[] _lights = new LocalLight[Slots];

        public int Count { get; set; }

        public LocalLight this[int index] => index < Count ? _lights[index] : throw new ArgumentOutOfRangeException(nameof(index));

        public void Add(in LocalLight light) => _lights[Count++] = light;

        public IEnumerator<LocalLight> GetEnumerator()
        {
            for (int index = 0; index < Count; index++)
            {
                yield return _lights[index];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
