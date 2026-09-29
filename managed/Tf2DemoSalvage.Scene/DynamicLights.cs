using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>One `dlight_t` (`public/dlight.h:43`): a light the client allocates for a moment.</summary>
/// <remarks>Mutable, as the engine's is: an allocator gets a zeroed slot back and fills it in place.</remarks>
public sealed class DynamicLight
{
    /// <summary>`flags`: `DLIGHT_NO_WORLD_ILLUMINATION` 1, `DLIGHT_NO_MODEL_ILLUMINATION` 2, displacement alpha 4 and 8.</summary>
    public int Flags { get; set; }

    /// <summary>`origin`.</summary>
    public float X { get; set; }

    /// <summary>`origin`.</summary>
    public float Y { get; set; }

    /// <summary>`origin`.</summary>
    public float Z { get; set; }

    /// <summary>`radius`: the cutoff, and what decays.</summary>
    public float Radius { get; set; }

    /// <summary>`color.r` of the `ColorRGBExp32`.</summary>
    public byte Red { get; set; }

    /// <summary>`color.g`.</summary>
    public byte Green { get; set; }

    /// <summary>`color.b`.</summary>
    public byte Blue { get; set; }

    /// <summary>`color.exponent`: the colour is scaled by two to this power.</summary>
    public sbyte Exponent { get; set; }

    /// <summary>`die`: past this client time the light goes out.</summary>
    public float Die { get; set; }

    /// <summary>`decay`: radius lost per second.</summary>
    public float Decay { get; set; }

    /// <summary>`minlight`: the brightness at the radius; floored at 1/256 when converted.</summary>
    public float MinLight { get; set; }

    /// <summary>`key`: the entity it belongs to, which a second allocation for the same key reuses.</summary>
    public int Key { get; set; }

    /// <summary>`style`: the light style it answers to.</summary>
    public int Style { get; set; }

    /// <summary>`m_Direction`: a spotlight's axis.</summary>
    public (float X, float Y, float Z) Direction { get; set; }

    /// <summary>`m_InnerAngle`, degrees.</summary>
    public float InnerAngle { get; set; }

    /// <summary>`m_OuterAngle`, degrees: zero is a point light.</summary>
    public float OuterAngle { get; set; }

    /// <summary>What `0x18008a9c0`'s eight stores leave: every field zero, the key set.</summary>
    internal void Reset(int key)
    {
        Flags = 0;
        (X, Y, Z) = (0f, 0f, 0f);
        Radius = 0f;
        (Red, Green, Blue, Exponent) = (0, 0, 0, 0);
        (Die, Decay, MinLight) = (0f, 0f, 0f);
        Style = 0;
        Direction = default;
        (InnerAngle, OuterAngle) = (0f, 0f);
        Key = key;
    }
}

/// <summary>The engine's `cl_dlights` and `cl_elights` (B425): allocation, decay, and what a model draw takes from them.</summary>
/// <remarks>
/// **Read from `engine.dll`; the full account is `docs/findings/66-tf2-barely-uses-dynamic-lights.md`.** `IVEfx::CL_AllocDlight` →
/// `0x18008a9c0` (32 slots at `0x180533c80`), `CL_AllocElight` → `0x18008aa60` (64 at `0x180534480`), the slot picked by
/// `0x18008aac0`. `CL_DecayLights` is `0x18008b2f0`, called by `_Host_RunFrame_Render` after `SCR_UpdateScreen`, so a
/// frame is drawn with the lights as its think left them and decays them afterwards. An elight is the same record,
/// read by model lighting only; nothing in the world lightmaps sees one.
/// </remarks>
public sealed class DynamicLights
{
    /// <summary>`MAX_DLIGHTS`, `public/iefx.h:22`.</summary>
    public const int MaxDlights = 32;

    /// <summary>The elight array's length: `0x18008aa60` passes 0x40, and `CL_ClearState` clears 0x1000 bytes.</summary>
    public const int MaxElights = 64;

    /// <summary>`DLIGHT_NO_WORLD_ILLUMINATION`, `dlight.h:22`.</summary>
    public const int NoWorldIllumination = 0x1;

    /// <summary>`DLIGHT_NO_MODEL_ILLUMINATION`, `dlight.h:23`.</summary>
    public const int NoModelIllumination = 0x2;

    /// <summary>Model lighting skips a light with any of these (`0x1801b7a10`: `flags &amp; 0xe`).</summary>
    private const int NotOnModels = 0xe;

    /// <summary>
    /// The `minlight` floor, `DAT_18047be1c`: `FUN_1801b8d30` stores 1/256 for a BSP version above 19 and 20/256
    /// otherwise. Every TF2 map is version 20. ponytail: v19 maps take 20/256; carry the version if one ever loads.
    /// </summary>
    public const float MinimumLightingValue = 1f / 256f;

    private readonly DynamicLight[] _dlights = CreateSlots(MaxDlights);
    private readonly DynamicLight[] _elights = CreateSlots(MaxElights);

    /// <summary>`r_dlightactive`, `0x1806996c4`: a bit per dlight, set on allocation and rebuilt by each decay.</summary>
    private uint _active;

    /// <summary>`cl.GetTime()`: the client time allocators stamp `die` from and decay compares it with.</summary>
    public float Time { get; set; }

    /// <summary>The dlight slots, in order.</summary>
    public IReadOnlyList<DynamicLight> Dlights => _dlights;

    /// <summary>The elight slots, in order.</summary>
    public IReadOnlyList<DynamicLight> Elights => _elights;

    private static DynamicLight[] CreateSlots(int count)
    {
        DynamicLight[] slots = new DynamicLight[count];

        for (int index = 0; index < count; index++)
        {
            slots[index] = new DynamicLight();
        }

        return slots;
    }

    /// <summary>`CL_AllocDlight` (`0x18008a9c0`).</summary>
    /// <param name="key">The owning entity, or 0 for a light that shares with nothing.</param>
    /// <returns>The slot, zeroed and keyed.</returns>
    public DynamicLight AllocDlight(int key)
    {
        int slot = Slot(_dlights, key);
        _active |= 1u << slot;
        _dlights[slot].Reset(key);

        return _dlights[slot];
    }

    /// <summary>`CL_AllocElight` (`0x18008aa60`).</summary>
    /// <param name="key">The owning entity, or 0.</param>
    /// <returns>The slot, zeroed and keyed.</returns>
    public DynamicLight AllocElight(int key)
    {
        DynamicLight light = _elights[Slot(_elights, key)];
        light.Reset(key);

        return light;
    }

    /// <summary>`0x18008aac0`: the slot with this nonzero key, else the first whose `die` is past, else slot 0.</summary>
    private int Slot(DynamicLight[] slots, int key)
    {
        if (key != 0)
        {
            for (int index = 0; index < slots.Length; index++)
            {
                if (slots[index].Key == key)
                {
                    return index;
                }
            }
        }

        for (int index = 0; index < slots.Length; index++)
        {
            if (slots[index].Die < Time)
            {
                return index;
            }
        }

        return 0;
    }

    /// <summary>`CL_DecayLights` (`0x18008b2f0`).</summary>
    /// <param name="frameTime">`cl.GetFrameTime()`: nothing happens unless it is positive.</param>
    public void Decay(float frameTime)
    {
        if (!(frameTime > 0f))
        {
            return;
        }

        _active = 0;

        for (int index = 0; index < _dlights.Length; index++)
        {
            DynamicLight light = _dlights[index];

            if (!(light.Radius > 0f))
            {
                continue;
            }

            if (Time <= light.Die)
            {
                if (light.Decay != 0f)
                {
                    light.Radius = Math.Max(light.Radius - (light.Decay * frameTime), 0f);
                }
            }
            else
            {
                light.Radius = 0f;
            }

            if (light.Radius > 0f)
            {
                _active |= 1u << index;
            }
        }

        foreach (DynamicLight light in _elights)
        {
            if (!(light.Radius > 0f))
            {
                continue;
            }

            light.Radius = Time <= light.Die ? Math.Max(light.Radius - (frameTime * light.Decay), 0f) : 0f;
        }
    }

    /// <summary>Every light a model draw ranks: `0x1801b7a10`'s two loops, dlights first.</summary>
    /// <param name="into">Cleared, then filled.</param>
    /// <remarks>An active dlight (its bit in `r_dlightactive`), an elight of positive radius, and neither with a bit in `0xe`.</remarks>
    public void ModelLights(ICollection<DynamicLight> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();

        for (int index = 0; index < _dlights.Length; index++)
        {
            if ((_active & (1u << index)) != 0 && (_dlights[index].Flags & NotOnModels) == 0)
            {
                into.Add(_dlights[index]);
            }
        }

        foreach (DynamicLight light in _elights)
        {
            if (light.Radius > 0f && (light.Flags & NotOnModels) == 0)
            {
                into.Add(light);
            }
        }
    }

    /// <summary>`CL_ClearState` (`0x18008b030`): both arrays zeroed, as at a level change.</summary>
    public void Clear()
    {
        foreach (DynamicLight light in _dlights)
        {
            light.Reset(0);
        }

        foreach (DynamicLight light in _elights)
        {
            light.Reset(0);
        }

        _active = 0;
    }

    /// <summary>The `dworldlight_t` a model draw lights with: `engine.dll` `0x1801bb940`.</summary>
    /// <param name="light">The dlight or elight.</param>
    /// <param name="cluster">The PVS cluster of its origin.</param>
    /// <returns>A point light, or a spotlight when the outer angle is positive.</returns>
    /// <remarks>
    /// Intensity is `color · table[exponent]`, the table at `0x18047e280` holding 2^e / 255. The radius is floored at
    /// 0.1, and the quadratic term is `1 / (r · max(minlight, 1/256) · r)` with no constant or linear term, so the
    /// light falls to `minlight` exactly at its radius. The exponent is never written and stays zero.
    /// ponytail: `LocalLights` applies vrad's all-terms-below-0.001 rule to every light, so a dlight with
    /// `r² · minlight &gt; 1000` (radius past ~505 at the 1/256 floor) would be read as constant; TF2's allocators stay under it.
    /// </remarks>
    public static BspWorldLight ToWorldLight(DynamicLight light, int cluster)
    {
        ArgumentNullException.ThrowIfNull(light);

        float scale = MathF.Pow(2f, light.Exponent) / 255f;
        float radius = light.Radius <= 0.1f ? 0.1f : light.Radius;
        float minimum = light.MinLight <= MinimumLightingValue ? MinimumLightingValue : light.MinLight;
        bool spot = light.OuterAngle > 0f;

        return new BspWorldLight(
            (light.X, light.Y, light.Z),
            (light.Red * scale, light.Green * scale, light.Blue * scale),
            spot ? light.Direction : default,
            spot ? WorldLightKind.Spotlight : WorldLightKind.Point,
            QuadraticAttenuation: 1f / (radius * minimum * radius),
            Radius: radius,
            StopDot: spot ? (float)Math.Cos(light.InnerAngle * 0.017453292519943295) : 0f,
            StopDot2: spot ? (float)Math.Cos(light.OuterAngle * 0.017453292519943295) : 0f,
            Style: light.Style,
            Cluster: cluster);
    }

    /// <summary>`CTFProjectile_BallOfFire::ClientThink` for the local player's own fireball (`tf_projectile_dragons_fury.cpp:509-528`).</summary>
    /// <param name="lights">The list.</param>
    /// <param name="entity">The fireball's index, which keys the light.</param>
    /// <param name="origin">`GetAbsOrigin()`.</param>
    public static void DragonsFury(DynamicLights lights, int entity, (float X, float Y, float Z) origin)
    {
        ArgumentNullException.ThrowIfNull(lights);

        DynamicLight light = lights.AllocDlight(entity);
        light.Radius = 100f;
        (light.X, light.Y, light.Z) = origin;
        light.Die = lights.Time + 0.05f;
        (light.Red, light.Green, light.Blue, light.Exponent) = (255, 100, 30, 8);
    }
}
