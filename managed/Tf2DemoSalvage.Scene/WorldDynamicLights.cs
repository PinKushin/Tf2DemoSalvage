using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>A brush entity drawn this frame, which `R_MarkDlightsOnBrushModel` (`0x1800e03a0`) walks the dlights into.</summary>
/// <param name="HeadNode">`dmodel_t::headnode`.</param>
/// <param name="Origin">Its render origin.</param>
/// <param name="Angles">Its render angles: pitch, yaw, roll in degrees.</param>
public readonly record struct LitBrush(int HeadNode, Vector3 Origin, Vector3 Angles);

/// <summary>The world's lightmaps under the dlights (B425 step 2): which faces a light marks, and each marked face rebuilt with it.</summary>
/// <remarks>
/// **Read from `engine.dll`; the account is `docs/findings/66-tf2-barely-uses-dynamic-lights.md`.** `R_PushDlights`
/// (`0x1800d48b0`) walks the world's nodes for each live light and ORs the light's bit into every face it reaches
/// (`0x1800d4520`, `0x1800d4680`, `R_TryLightMarkSurface` `0x1800d4970`). `R_RenderDynamicLightmaps` (`0x1800d09a0`)
/// rebuilds a face marked this frame or still carrying bits, which is how a light that leaves or dies is taken back
/// off: the per-face pass (`0x1800d0ba0`) drops every bit of a face nobody marked, and `R_BuildLightMap` (`0x1800cfca0`)
/// rebuilds it baked. The additions are `R_AddDynamicLights` (`0x1800ceb00`) and its bumped twin (`0x1800cf1b0`).
///
/// **Displacements** (step 3): the leaf pass marks a leaf's displacements first, by box (`0x180172c60`); `CDispInfo`
/// (vtable `0x18038d8b8`) keeps the bits at slot 0x38 (`0x1800c4540`) and adds at slot 0x30 (`0x1800c3130`), by each
/// luxel's rebuilt 3D position (`0x1800c0600`, <see cref="DecalDisplacement.Luxels"/>). **Brush entities**: each drawn one
/// walks the lights from its head node with their origins moved into its space (`0x1800e03a0`), and its faces are
/// rebuilt reading them through the same matrix (`0x180279620` in `0x1800cfca0` and `0x1800d0ba0`).
///
/// **Not built, named:** the displacement alpha lights (`flags &amp; 0xc`, `0x1800bf7d0`), which write the blend alpha
/// rather than the lightmap; a displacement's per-vertex normal (`CalcNormalFromEdges` and neighbour smoothing), for
/// which the parent plane's stands in; and `r_maxdlights` (default 32, `0x180005f70`), which cannot bind with 32 slots.
/// `r_dynamic` is "1" by default (`0x180007c90`) and is taken as always on.
/// </remarks>
public sealed class WorldDynamicLights
{
    /// <summary>How far behind its plane a face still takes a light: `R_TryLightMarkSurface`'s `-15.0`.</summary>
    public const float BehindPlane = 15f;

    /// <summary>A light with any of these adds nothing to a lightmap (`0x1800d0ba0`: `flags &amp; 0xd`).</summary>
    private const int NotOnLightmaps = 0xd;

    /// <summary>`DLIGHT_ADD_DISPLACEMENT_ALPHA | DLIGHT_SUBTRACT_DISPLACEMENT_ALPHA`: slot 0x30 sends these to the blend alpha.</summary>
    private const int DisplacementAlpha = 0xc;

    /// <summary>`OO_SQRT_2_OVER_3`, `OO_SQRT_6`, `OO_SQRT_2`, `OO_SQRT_3` (`bumpvects.h:19-23`).</summary>
    private static readonly Vector3[] LocalBumpBasis =
    [
        new(0.81649661064147949f, 0f, 0.57735025882720947f),
        new(-0.40824821591377258f, 0.70710676908493042f, 0.57735025882720947f),
        new(-0.40824821591377258f, -0.70710676908493042f, 0.57735025882720947f),
    ];

    private readonly DecalWorld _world;

    /// <summary>`m_nDLightBits`, by face.</summary>
    private readonly uint[] _bits;

    /// <summary>`m_nLastDLightFrame`, by face.</summary>
    private readonly int[] _marked;

    /// <summary>The faces rebuilt last frame that still carry bits, and this frame's marks: this frame's rebuild list.</summary>
    private readonly HashSet<int> _pending = [];

    private readonly List<int> _carrying = [];

    private float[] _added = [];

    private int _frame;

    /// <summary>Which of this frame's brushes marked each face; −1 for the world. Read only for a face marked this frame.</summary>
    private readonly int[] _space;

    /// <summary>Each leaf's displacements, as indices into the world's displacements.</summary>
    private readonly List<int>[] _leafDisplacements;

    /// <summary>Each displacement by its parent face.</summary>
    private readonly Dictionary<int, DecalDisplacement> _displacements = [];

    private IReadOnlyList<LitBrush> _brushes = [];

    /// <summary>A map's dlight state.</summary>
    /// <param name="world">The nodes, leaves and faces the walk visits.</param>
    /// <remarks>
    /// **The leaves' displacement lists are interpolated**, as <see cref="DecalWorld.LeafDisplacements"/> is: each
    /// displacement's box pushed down the tree, since the engine's load-time builder was not found.
    /// </remarks>
    public WorldDynamicLights(DecalWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        _world = world;
        _bits = new uint[world.Faces.Count];
        _marked = new int[world.Faces.Count];
        _space = new int[world.Faces.Count];

        List<(Vector3, Vector3)> boxes = [];

        foreach (DecalDisplacement displacement in world.Displacements)
        {
            _displacements[displacement.Face] = displacement;
            boxes.Add((displacement.Mins, displacement.Maxs));
        }

        _leafDisplacements = DecalWorld.Link(world.Nodes, world.LeafFaces.Count, boxes);
    }

    /// <summary>The brush entities among a frame's drawn props: `*N` models, at their render origin and angles.</summary>
    /// <param name="drawn">What the frame drew.</param>
    /// <param name="models">The map's models.</param>
    /// <returns>One per drawn submodel.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<LitBrush> BrushesOf(IReadOnlyList<SceneProp> drawn, IReadOnlyList<BspModel> models)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(models);

        List<LitBrush> brushes = [];

        foreach (SceneProp prop in drawn)
        {
            if (prop.ModelPath.Length > 1 && prop.ModelPath[0] == BrushModels.SubmodelPrefix &&
                int.TryParse(prop.ModelPath.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int model) &&
                model > 0 && model < models.Count)
            {
                ScenePose pose = prop.Pose;

                brushes.Add(new LitBrush(
                    models[model].HeadNode, new Vector3(pose.X, pose.Y, pose.Z), new Vector3(pose.Pitch, pose.Yaw, pose.Roll)));
            }
        }

        return brushes;
    }

    /// <summary>A face's dlight bits after the last frame's rebuild.</summary>
    /// <param name="face">The face's index.</param>
    /// <returns>A bit per dlight slot; zero for a face no light reaches.</returns>
    public uint Bits(int face) => face >= 0 && face < _bits.Length ? _bits[face] : 0u;

    /// <summary>Whether `R_PushDlights` walks a dlight into the world.</summary>
    /// <param name="light">The light.</param>
    /// <param name="time">The client time.</param>
    /// <returns>True when `time &lt;= die`, the radius is positive and `DLIGHT_NO_WORLD_ILLUMINATION` is clear.</returns>
    public static bool Pushes(DynamicLight light, float time)
    {
        ArgumentNullException.ThrowIfNull(light);

        return time <= light.Die && light.Radius > 0f && (light.Flags & DynamicLights.NoWorldIllumination) == 0;
    }

    /// <summary>`R_AddDynamicLights`' scale at one luxel.</summary>
    /// <param name="distanceSquared">The luxel's squared distance: its two lightmap axes in world units, plus the plane's.</param>
    /// <param name="radiusSquared">The light's radius, squared.</param>
    /// <param name="minLight">`minlight`, floored at <see cref="DynamicLights.MinimumLightingValue"/>.</param>
    /// <returns>`min( (d² == 0 ? 1 : minlight · r² / d²) · (1 − d² / r²), 2 )`, or zero at or past the radius.</returns>
    public static float Falloff(float distanceSquared, float radiusSquared, float minLight)
    {
        if (!(distanceSquared < radiusSquared))
        {
            return 0f;
        }

        float minimum = minLight <= DynamicLights.MinimumLightingValue ? DynamicLights.MinimumLightingValue : minLight;
        float scale = distanceSquared == 0f ? 1f : minimum * radiusSquared / distanceSquared;

        return MathF.Min((1f - (distanceSquared * (1f / radiusSquared))) * scale, 2f);
    }

    /// <summary>One frame: mark from the lights, then rebuild every face marked or carrying bits.</summary>
    /// <param name="lights">The dlights as the frame drew them.</param>
    /// <param name="atlas">The world's lightmaps, rebuilt in place.</param>
    /// <param name="scale">Each light style's value over 264.</param>
    /// <param name="dirty">Each rebuilt face's region, added to.</param>
    /// <param name="brushes">The brush entities drawn this frame; none when null.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void Frame(
        DynamicLights lights,
        LightmapAtlas atlas,
        Func<int, float> scale,
        ICollection<AtlasRegion> dirty,
        IReadOnlyList<LitBrush>? brushes = null)
    {
        ArgumentNullException.ThrowIfNull(lights);
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(scale);
        ArgumentNullException.ThrowIfNull(dirty);

        _frame++;
        _pending.Clear();
        _pending.UnionWith(_carrying);
        _carrying.Clear();
        _brushes = brushes ?? [];

        for (int slot = 0; slot < DynamicLights.MaxDlights && _world.Nodes.Count > 0; slot++)
        {
            if (Pushes(lights.Dlights[slot], lights.Time))
            {
                Walk(0, lights.Dlights[slot], Origin(lights.Dlights[slot]), 1u << slot, -1);
            }
        }

        for (int brush = 0; brush < _brushes.Count; brush++)
        {
            MarkBrush(brush, lights);
        }

        foreach (int face in _pending)
        {
            int sets = atlas.SetsOf(face);

            // `SURFDRAW_NOLIGHT`: a face with no lightmap is never rebuilt.
            if (_world.Faces[face] is not { } surface || sets == 0)
            {
                continue;
            }

            uint lit = Keep(face, surface, lights);

            if (_bits[face] != 0)
            {
                _carrying.Add(face);
            }

            Build(face, surface, lights, lit, sets, scale);
            atlas.Rebuild(face, scale, lit == 0 ? [] : _added, dirty);
        }
    }

    /// <summary>`0x1800d0ba0`: the bits a face keeps this frame, and the lights its rebuild adds.</summary>
    private uint Keep(int face, DecalFace surface, DynamicLights lights)
    {
        uint bits = _bits[face];

        for (int slot = 0; slot < DynamicLights.MaxDlights; slot++)
        {
            if (!lights.IsActive(slot))
            {
                bits &= ~(1u << slot);
            }
        }

        uint lit = 0;

        if (_marked[face] == _frame && surface.Displacement)
        {
            // Slot 0x38 (`0x1800c4540`): only `DLIGHT_NO_WORLD_ILLUMINATION` refuses, and no bit is dropped one by one.
            for (int slot = 0; slot < DynamicLights.MaxDlights; slot++)
            {
                if ((bits & (1u << slot)) != 0 && (lights.Dlights[slot].Flags & DynamicLights.NoWorldIllumination) == 0)
                {
                    lit |= 1u << slot;
                }
            }
        }
        else if (_marked[face] == _frame)
        {
            for (int slot = 0; slot < DynamicLights.MaxDlights; slot++)
            {
                uint bit = 1u << slot;
                DynamicLight light = lights.Dlights[slot];

                if ((bits & bit) == 0 || (light.Flags & NotOnLightmaps) != 0)
                {
                    continue;
                }

                float distance = PlaneDistance(surface, LightOrigin(light, face));

                if (distance >= -BehindPlane && distance * distance < light.Radius * light.Radius)
                {
                    lit |= bit;
                }
                else
                {
                    bits &= ~bit;
                }
            }
        }

        _bits[face] = lit != 0 && bits != 0 ? bits : 0u;

        return _bits[face] == 0 ? 0u : lit;
    }

    /// <summary>`0x1800e03a0`: a drawn brush entity's walk, the light in the model's space, from its head node when its box is reached.</summary>
    private void MarkBrush(int brush, DynamicLights lights)
    {
        LitBrush entity = _brushes[brush];

        if (entity.HeadNode < 0 || entity.HeadNode >= _world.Nodes.Count)
        {
            return;
        }

        DecalNode head = _world.Nodes[entity.HeadNode];
        Vector3 centre = (head.Mins + head.Maxs) * 0.5f;
        Vector3 half = head.Maxs - centre;

        for (int slot = 0; slot < DynamicLights.MaxDlights; slot++)
        {
            DynamicLight light = lights.Dlights[slot];

            // No flag test here, unlike `R_PushDlights`.
            if (!(lights.Time <= light.Die) || !(light.Radius > 0f))
            {
                continue;
            }

            Vector3 origin = Local(entity, Origin(light));

            // `IsBoxIntersectingSphereExtents` (`0x180172d10`).
            Vector3 gap = Vector3.Max(Vector3.Abs(origin - centre) - half, Vector3.Zero);

            if (gap.LengthSquared() < light.Radius * light.Radius)
            {
                Walk(entity.HeadNode, light, origin, 1u << slot, brush);
            }
        }
    }

    /// <summary>`0x180279620`: a world point in a brush entity's space, `Rᵀ(p − origin)` with `R` its `AngleMatrix`.</summary>
    private static Vector3 Local(LitBrush brush, Vector3 point)
    {
        (Vector3 forward, Vector3 left, Vector3 up) = StaticPropCollision.AngleMatrix(brush.Angles.X, brush.Angles.Y, brush.Angles.Z);
        Vector3 delta = point - brush.Origin;

        return new Vector3(Vector3.Dot(forward, delta), Vector3.Dot(left, delta), Vector3.Dot(up, delta));
    }

    /// <summary>The light's origin in the space of whatever marked the face this frame.</summary>
    private Vector3 LightOrigin(DynamicLight light, int face) =>
        _space[face] >= 0 && _space[face] < _brushes.Count ? Local(_brushes[_space[face]], Origin(light)) : Origin(light);

    /// <summary>`0x1800d4520`: down the nodes the light's sphere straddles, trying each node's faces, then the leaves.</summary>
    private void Walk(int node, DynamicLight light, Vector3 origin, uint bit, int space)
    {
        while (node >= 0)
        {
            if (node >= _world.Nodes.Count)
            {
                return;
            }

            DecalNode plane = _world.Nodes[node];
            float distance = Vector3.Dot(origin, plane.Normal) - plane.Distance;

            if (distance > light.Radius)
            {
                node = plane.Front;
            }
            else if (distance < -light.Radius)
            {
                node = plane.Back;
            }
            else
            {
                for (int face = plane.FirstFace; face < plane.FirstFace + plane.FaceCount; face++)
                {
                    TryMark(face, light, origin, bit, leaf: false, space);
                }

                Walk(plane.Front, light, origin, bit, space);
                Walk(plane.Back, light, origin, bit, space);

                return;
            }
        }

        int leaf = -(node + 1);

        // `0x1800d4680`: the leaf's displacements first, by box, then its faces.
        if (leaf < _leafDisplacements.Length)
        {
            foreach (int displacement in _leafDisplacements[leaf])
            {
                MarkDisplacement(_world.Displacements[displacement], origin, light.Radius, bit);
            }
        }

        if (leaf < _world.LeafFaces.Count)
        {
            foreach (int face in _world.LeafFaces[leaf])
            {
                TryMark(face, light, origin, bit, leaf: true, space);
            }
        }
    }

    /// <summary>`0x1800d4680`'s displacement loop: a parent face not yet carrying the bit this frame, whose box the sphere meets.</summary>
    private void MarkDisplacement(DecalDisplacement displacement, Vector3 origin, float radius, uint bit)
    {
        int index = displacement.Face;

        if (index < 0 || index >= _world.Faces.Count || (_marked[index] == _frame && (_bits[index] & bit) != 0))
        {
            return;
        }

        // `IsBoxIntersectingSphere` (`0x180172c60`).
        Vector3 gap = Vector3.Max(displacement.Mins - origin, Vector3.Zero) + Vector3.Max(origin - displacement.Maxs, Vector3.Zero);

        if (gap.LengthSquared() < radius * radius)
        {
            _marked[index] = _frame;
            _bits[index] |= bit;
            _space[index] = -1;
            _pending.Add(index);
        }
    }

    /// <summary>`R_TryLightMarkSurface` (`0x1800d4970`), with the leaf pass's own guards (`0x1800d4680`).</summary>
    private void TryMark(int index, DynamicLight light, Vector3 origin, uint bit, bool leaf, int space)
    {
        if (index < 0 || index >= _world.Faces.Count || _world.Faces[index] is not { Displacement: false } face ||
            (_marked[index] == _frame && (_bits[index] & bit) != 0))
        {
            return;
        }

        float distance = PlaneDistance(face, origin);

        if (leaf && (face.OnNode || distance > light.Radius || distance < -light.Radius))
        {
            return;
        }

        float remaining = (light.Radius * light.Radius) - (distance * distance);

        if (distance < -BehindPlane || !(remaining > 0f))
        {
            return;
        }

        LuxelMapping map = face.Lighting;
        float perUnit = new Vector3(map.Across.X, map.Across.Y, map.Across.Z).Length();
        float s = Row(map.Across, origin);
        float t = Row(map.Down, origin);
        float reach = MathF.Sqrt(perUnit * remaining * perUnit);

        // `0x1801735f0`: the squared distance from (s, t) to the luxel rectangle, against the circle's.
        float across = Outside(s, map.MinU, map.MinU + map.Width - 1);
        float down = Outside(t, map.MinV, map.MinV + map.Height - 1);

        if (!((across * across) + (down * down) < reach * reach))
        {
            return;
        }

        // ORed, not reset: a bit from an earlier frame stays until the per-face pass drops it.
        _marked[index] = _frame;
        _bits[index] |= bit;
        _space[index] = space;
        _pending.Add(index);

        static float Outside(float value, float low, float high)
        {
            if (value < low)
            {
                return value - low;
            }

            return value > high ? high - value : 0f;
        }
    }

    /// <summary>`R_AddDynamicLights` for every lit light, into <see cref="_added"/>.</summary>
    private void Build(int index, DecalFace face, DynamicLights lights, uint lit, int sets, Func<int, float> scale)
    {
        int luxels = face.Lighting.Width * face.Lighting.Height;
        int length = sets * luxels * 3;

        if (_added.Length < length)
        {
            _added = new float[length];
        }

        Array.Clear(_added, 0, length);

        for (int slot = 0; slot < DynamicLights.MaxDlights && lit != 0; slot++)
        {
            if ((lit & (1u << slot)) == 0)
            {
                continue;
            }

            DynamicLight light = lights.Dlights[slot];
            Vector3 origin = LightOrigin(light, index);
            float radiusSquared = light.Radius * light.Radius;

            if (face.Displacement)
            {
                // Slot 0x30 (`0x1800c3130`): `flags & 0xc` goes to the alpha path (`0x1800bf7d0`), not the lightmap.
                if ((light.Flags & DisplacementAlpha) == 0 && _displacements.TryGetValue(index, out DecalDisplacement? terrain))
                {
                    AddDisplacement(face, terrain.Luxels, light, origin, radiusSquared, scale, luxels, sets > 1);
                }

                continue;
            }

            float distance = PlaneDistance(face, origin);

            if (sets > 1)
            {
                AddBumped(face, light, origin, distance * distance, radiusSquared, scale, luxels);
            }
            else
            {
                AddFlat(face, light, origin, distance * distance, radiusSquared, scale);
            }
        }
    }

    /// <summary>`0x1800bf8d0`/`0x1800bf9d0` through `0x1800c0600`: each luxel by its own 3D position.</summary>
    /// <remarks>
    /// Flat (`0x1800c0ad0`): `d² = |o − p|²`, the falloff as the face's. Bumped (`0x1800c0c40`): `dir = (o − p)/√(d² + 1e-10)`
    /// on the luxel's normal N and tangents S, T; page 0 takes `max(n, 0)·f`, pages 1–3 `max(basis·(s, t, n), 0)·f` on
    /// `g_localBumpBasis` — no division by the normal's share, unlike a face. The frame is
    /// `GenerateDispSurfTangentSpaces` (`builddisp.cpp:1692-1727`) with the parent plane's normal for the vertex normal
    /// (named, not the engine's smoothed one). *Interpolated:* `+0x108` as `m_TangentS` and `+0x110` as `m_TangentT`, from
    /// which page each dot feeds. The engine normalises with `rsqrtss` and one Newton step; this with a true square root.
    /// </remarks>
    private void AddDisplacement(
        DecalFace face, IReadOnlyList<Vector3> positions, DynamicLight light, Vector3 origin, float radiusSquared,
        Func<int, float> scale, int luxels, bool bumped)
    {
        Vector3 colour = Colour(light, scale);
        Vector3 normal = face.PlaneNormal;
        Vector3 sAxis = new(face.TextureS.X, face.TextureS.Y, face.TextureS.Z);
        Vector3 tAxis = new(face.TextureT.X, face.TextureT.Y, face.TextureT.Z);
        Vector3 tangentS = Vector3.Normalize(Vector3.Cross(normal, Vector3.Normalize(tAxis)));
        Vector3 tangentT = Vector3.Normalize(Vector3.Cross(tangentS, normal));

        if (Vector3.Dot(normal, Vector3.Cross(sAxis, tAxis)) > 0f)
        {
            tangentS = -tangentS;
        }

        for (int luxel = 0; luxel < positions.Count && luxel < luxels; luxel++)
        {
            Vector3 delta = origin - positions[luxel];
            float distanceSquared = delta.LengthSquared();
            float falloff = Falloff(distanceSquared, radiusSquared, light.MinLight);

            if (!bumped)
            {
                Add(0, luxel, colour * falloff);

                continue;
            }

            if (falloff == 0f)
            {
                continue;
            }

            Vector3 direction = delta / MathF.Sqrt(distanceSquared + 1e-10f);
            float n = Vector3.Dot(direction, normal);
            float s = Vector3.Dot(direction, tangentS);
            float t = Vector3.Dot(direction, tangentT);
            float up = n * 0.57735026f;
            float side = s * -0.40824822f;

            Add(0, luxel, colour * (MathF.Max(n, 0f) * falloff));
            Add(luxels, luxel, colour * (MathF.Max((s * 0.8164966f) + up, 0f) * falloff));
            Add(2 * luxels, luxel, colour * (MathF.Max((t * 0.70710677f) + side + up, 0f) * falloff));
            Add(3 * luxels, luxel, colour * (MathF.Max(side - (t * 0.70710677f) + up, 0f) * falloff));
        }
    }

    /// <summary>`0x1800ceb00`.</summary>
    private void AddFlat(
        DecalFace face, DynamicLight light, Vector3 origin, float planeSquared, float radiusSquared, Func<int, float> scale)
    {
        // A spotlight aimed away from the face's side adds nothing.
        if (light.OuterAngle != 0f && light.OuterAngle < 180f &&
            Vector3.Dot(new Vector3(light.Direction.X, light.Direction.Y, light.Direction.Z), face.PlaneNormal) >= 0f)
        {
            return;
        }

        LuxelMapping map = face.Lighting;
        float unitsPerLuxel = 1f / new Vector3(map.Across.X, map.Across.Y, map.Across.Z).Length();
        float s = Row(map.Across, origin) - map.MinU;
        float t = Row(map.Down, origin) - map.MinV;
        Vector3 colour = Colour(light, scale);

        for (int row = 0; row < map.Height; row++)
        {
            float down = (t - row) * unitsPerLuxel;

            for (int column = 0; column < map.Width; column++)
            {
                float across = (s - column) * unitsPerLuxel;
                float falloff = Falloff((across * across) + (down * down) + planeSquared, radiusSquared, light.MinLight);

                Add(0, (row * map.Width) + column, colour * falloff);
            }
        }
    }

    /// <summary>`0x1800cf1b0`: the flat page as <see cref="AddFlat"/> without its spot test, and a share on each basis vector.</summary>
    /// <remarks>
    /// **The direction is taken from the luxel's ROW only**: `origin + t_vec · row · wupl²`, with no column term. Read
    /// in the disassembly (`0x1800cf460` loads the row's position once, and the column loop at `0x1800cf4d5` never
    /// moves it), so a luxel's direction is the one at the start of its row. Reproduced, not corrected.
    /// </remarks>
    private void AddBumped(
        DecalFace face, DynamicLight light, Vector3 origin, float planeSquared, float radiusSquared, Func<int, float> scale,
        int luxels)
    {
        LuxelMapping map = face.Lighting;
        Vector3 sVector = new(map.Across.X, map.Across.Y, map.Across.Z);
        Vector3 tVector = new(map.Down.X, map.Down.Y, map.Down.Z);
        float unitsPerLuxel = 1f / sVector.Length();
        float squared = unitsPerLuxel * unitsPerLuxel;
        float s = Row(map.Across, origin) - map.MinU;
        float t = Row(map.Down, origin) - map.MinV;
        Vector3 colour = Colour(light, scale);
        Vector3 normal = face.PlaneNormal;
        Vector3[] bump = BumpNormals(Vector3.Normalize(sVector), Vector3.Normalize(tVector), normal);

        // `0x1800d0340`: the world position of luxel (0, 0) as the engine reckons it.
        Vector3 start = ((map.MinU - map.Across.Offset) * squared * sVector) +
                        ((map.MinV - map.Down.Offset) * squared * tVector) +
                        (face.PlaneDistance * normal);

        Vector3 spot = new(light.Direction.X, light.Direction.Y, light.Direction.Z);
        bool fixedDirection = light.OuterAngle != 0f && MathF.Abs(spot.LengthSquared() - 1f) < 0.001f;

        for (int row = 0; row < map.Height; row++)
        {
            float down = (t - row) * unitsPerLuxel;
            Vector3 position = (tVector * (row * squared)) + start;

            for (int column = 0; column < map.Width; column++)
            {
                float across = (s - column) * unitsPerLuxel;
                float falloff = Falloff((across * across) + (down * down) + planeSquared, radiusSquared, light.MinLight);

                if (falloff == 0f)
                {
                    continue;
                }

                int luxel = (row * map.Width) + column;

                Add(0, luxel, colour * falloff);

                Vector3 direction = fixedDirection ? -spot : Direction(origin - position);
                float facing = Vector3.Dot(direction, normal);
                float share = falloff / (facing < 0.001f ? 0.001f : facing);

                for (int basis = 0; basis < 3; basis++)
                {
                    float dot = Vector3.Dot(direction, bump[basis]);

                    if (dot > 0f)
                    {
                        Add((basis + 1) * luxels, luxel, colour * (dot * share));
                    }
                }
            }
        }

        static Vector3 Direction(Vector3 delta) => delta / MathF.Sqrt(delta.LengthSquared() + 1e-10f);
    }

    /// <summary>`GetBumpNormals` (`bumpvects.cpp`), with the flat normal as the phong one, as `0x1800d0340` calls it.</summary>
    private static Vector3[] BumpNormals(Vector3 s, Vector3 t, Vector3 normal)
    {
        bool leftHanded = Vector3.Dot(normal, Vector3.Cross(s, t)) < 0f;
        Vector3 across = Vector3.Normalize(Vector3.Cross(normal, s));
        Vector3 forward = Vector3.Normalize(Vector3.Cross(across, normal));

        if (leftHanded)
        {
            across = -across;
        }

        Vector3[] bump = new Vector3[3];

        for (int basis = 0; basis < 3; basis++)
        {
            Vector3 local = LocalBumpBasis[basis];

            bump[basis] = (local.X * forward) + (local.Y * across) + (local.Z * normal);
        }

        return bump;
    }

    /// <summary>The light's colour in the samples' units: `colour · 2^e · style / 264`, where the engine's is that over 255.</summary>
    private static Vector3 Colour(DynamicLight light, Func<int, float> scale)
    {
        float factor = MathF.Pow(2f, light.Exponent) * scale(light.Style);

        return new Vector3(light.Red, light.Green, light.Blue) * factor;
    }

    private void Add(int page, int luxel, Vector3 light)
    {
        int at = (page + luxel) * 3;

        _added[at] += light.X;
        _added[at + 1] += light.Y;
        _added[at + 2] += light.Z;
    }

    private static Vector3 Origin(DynamicLight light) => new(light.X, light.Y, light.Z);

    private static float PlaneDistance(DecalFace face, Vector3 origin) =>
        Vector3.Dot(origin, face.PlaneNormal) - face.PlaneDistance;

    private static float Row((float X, float Y, float Z, float Offset) vector, Vector3 at) =>
        (vector.X * at.X) + (vector.Y * at.Y) + (vector.Z * at.Z) + vector.Offset;
}
