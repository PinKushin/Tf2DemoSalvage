using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>One triangle corner of a placed prop, already in world space.</summary>
/// <param name="X">Where it stands in the map.</param>
/// <param name="Y">Where it stands in the map.</param>
/// <param name="Z">Where it stands in the map.</param>
/// <param name="U">Texture coordinate.</param>
/// <param name="V">Texture coordinate.</param>
/// <param name="MaterialIndex">Which material paints it, in the map's combined table.</param>
/// <param name="OriginX">Where the placement stands, east-west.</param>
/// <param name="OriginY">Where the placement stands, north-south.</param>
/// <param name="Red">Baked lighting, one where the placement has none.</param>
/// <param name="Green">Baked lighting, one where the placement has none.</param>
/// <param name="NormalX">Surface normal, east-west, in the model own space.</param>
/// <param name="NormalY">Surface normal, north-south.</param>
/// <param name="NormalZ">Surface normal, vertically.</param>
/// <param name="Blue">Baked lighting, one where the placement has none.</param>
/// <param name="Bones">Which bones move this vertex, for a model skinned on the GPU.</param>
/// <param name="Weights">How much each of those bones moves it.</param>
/// <param name="BodyPart">Which body part it belongs to, for a model with alternatives.</param>
/// <param name="BodyModel">Which of that part's alternatives, chosen per entity at draw time.</param>
/// <param name="LightU">Lightmap atlas coordinate across; zero for anything but brushwork.</param>
/// <param name="LightV">Lightmap atlas coordinate down; zero for anything but brushwork.</param>
/// <param name="LightStep">How far along the atlas each directional set sits, or zero.</param>
/// <param name="MaterialSlot">The mesh's own skinref, which a skin family is looked up by.</param>
public readonly record struct PropVertex(
    float X, float Y, float Z, float U, float V, int MaterialIndex,
    float OriginX = 0f, float OriginY = 0f,
    float Red = 1f, float Green = 1f, float Blue = 1f,
    float NormalX = 0f, float NormalY = 0f, float NormalZ = 1f,
    (byte First, byte Second, byte Third) Bones = default,
    (float First, float Second, float Third) Weights = default,
    int BodyPart = 0,
    int BodyModel = 0,

    // **Brushwork only, and zero is the right default for everything else (B131).** A studio model
    // has no lightmap — the same model stands in many places under different light — so it keeps
    // (0, 0), which is the atlas's reserved white texel and multiplies to no change. A brush
    // entity is the other case entirely: vrad lights every model's faces, not just the world's
    // (vrad.cpp:703), so a door's faces have baked samples sitting in the same lighting lump as
    // the wall's. Dropping them here is what made an opening door a flat panel.
    float LightU = 0f, float LightV = 0f, float LightStep = 0f,

    // **The mesh's own `mstudiomesh_t::material`, which is a SKINREF and not a texture index**
    // (B229). `g_skinref[skin][skinref]` turns it into one
    // (`utils/motionmapper/motionmapper.h:134`), so this is the key a skin family is looked up by
    // — `MaterialIndex` above is already the ANSWER for one particular family, and asking "what
    // does that answer become in another family" is a question the engine never asks and that has
    // two answers whenever two meshes share a material.
    //
    // −1 for anything with no skin table to index: brush entities, and any geometry not built
    // from a `.mdl`. It can then never match a swap entry, which is what those want.
    int MaterialSlot = -1);

/// <summary>
/// The models a map places, loaded and put where the map says.
/// </summary>
/// <remarks>
/// **This is what fills the map's remaining holes.** A displacement painted with
/// <c>tools/toolsinvisibledisplacement</c> is collision-only terrain the engine never draws, and
/// what a player sees standing there is a prop on top of it — the rock at cp_process mid being the
/// case that named the problem. Reading the placements was not enough; they have to be drawn.
///
/// **Each model is loaded once and placed many times.** A map names a few hundred distinct models
/// and places several thousand instances of them, so the three files behind each one are read once
/// and the vertices are then transformed per placement. On a real map that is the difference
/// between hundreds of archive reads and tens of thousands.
///
/// **Materials join the map's own table rather than living beside it.** A prop's material index
/// continues where the BSP's texture table ends, so the renderer's existing per-material batching
/// and texture array serve both without knowing which is which — one binding path, not two.
///
/// **A prop that cannot be loaded costs itself and nothing else.** A missing model, an unreadable
/// one, a mismatched checksum: each is logged and skipped, because a map is largely drawable
/// without any given rock and not drawable at all if one bad file stops the load.
/// </remarks>
public static class PropModels
{
    /// <summary>Most animation frames to bake for one model.</summary>
    /// <remarks>
    /// **A budget, not a format limit.** Baking trades memory for playback cost, and the trade is
    /// only good while the frame count is small: TF2's animated props are tens of frames over a
    /// few thousand vertices. A model claiming hundreds is drawn from what fits rather than
    /// allowed to allocate without bound, and the owner's stated ceiling is an eight-gigabyte
    /// machine.
    /// </remarks>
    private const int MaximumBakedFrames = 64;

    /// <summary>Most baked corners to hold for one model, across every animation it has.</summary>
    /// <remarks>
    /// **Frames alone do not bound the cost; frames times corners do.** sentry3_heavy has 51,492
    /// corners and six animations totalling 113 frames, which a frames-only cap happily allowed -
    /// about 490 megabytes of vertex data for a single model, measured. At twenty-one floats a
    /// corner this budget is roughly 170 megabytes for the worst case and far less for anything
    /// ordinary, since a health pack is 1,608 corners and thirty frames.
    ///
    /// A model too large to bake draws from however many frames fit, which for a big one is its
    /// first. Standing still is a worse animation than moving and a much better one than a machine
    /// swapping.
    /// </remarks>
    private const int MaximumBakedCorners = 2_000_000;

    /// <summary>The most placements to draw from one map.</summary>
    /// <remarks>
    /// A map is untrusted input (D32). Real maps place a few thousand props; the ceiling is well
    /// clear of that and still refuses a file claiming millions.
    /// </remarks>
    private const int MaximumPlacements = 100_000;

    /// <summary>What the engine multiplies static prop vertex lighting by, from its own shader.</summary>
    private const float Overbright = 2f;

    /// <summary>Loads a map's props and places them.</summary>
    /// <param name="map">The map's bytes.</param>
    /// <param name="pak">The map's own embedded content, searched before the game's.</param>
    /// <param name="archives">The game's archives and folders.</param>
    /// <param name="materialTable">The map's material table, extended in place with the props'. Every list it holds grows together.</param>
    /// <param name="load">Resolves a material to its textures, or returns null.</param>
    /// <param name="refusedLighting">
    /// Collects the placements whose baked lighting existed and was refused, when supplied. Passed
    /// in rather than returned through a static: a static written by every map load is meaningless
    /// once two loads overlap, which is exactly what the parallel test suite does.
    /// </param>
    /// <returns>Every placed triangle corner, three per triangle.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <param name="lightAt">
    /// The light reaching a point, used for props whose baked vertex lighting is absent or refused.
    /// </param>
    /// <param name="props">
    /// Where loading reports what it refused, and what it could not paint. <b>Required, and first,
    /// so that a caller cannot omit it</b> — see the remarks.
    /// </param>
    /// <remarks>
    /// **A prop without baked lighting is lit from the light cache, not left white** (B123). The
    /// engine carries both in one structure and chooses between them —
    /// <c>istudiorender.h</c>'s <c>DrawModelInfo_t</c> holds <c>m_pColorMeshes</c> and
    /// <c>m_bStaticLighting</c> beside <c>m_vecAmbientCube[6]</c> and <c>m_LocalLightDescs[4]</c> —
    /// so when there are no colour meshes the model is lit exactly as a dynamic one is.
    /// <c>c_physicsprop.cpp:85</c> shows the same model switching between the two: it asks for
    /// <c>STUDIO_STATIC_LIGHTING</c> only while asleep, and is cube-lit while awake.
    ///
    /// Refusals are the common case rather than an edge — 44 on <c>cp_badlands</c> — because TF2 has
    /// updated models since these maps were compiled and their checksums no longer match. Leaving
    /// those props white meant no lighting at all: flat albedo, which reads washed out on a pale
    /// model and as a dark disc on the capture point's dark metal, which is how this was noticed.
    ///
    /// **The logger is required, and that is a fix rather than a style choice (B229).** It used to
    /// be <c>ILogger? props = null</c>, defaulted to <c>NullLogger.Instance</c>, with a comment
    /// saying "most callers of this are tests that want geometry, not commentary". There was
    /// exactly ONE caller in the repository — `MapAssets` — and it passed nothing. So every finding
    /// this method produces was discarded, including the two warnings in <c>Register</c> that name
    /// the model whose mesh will draw in the missing-material chequer.
    ///
    /// That cost four hypotheses on B229: the viewer log was read for those warnings, they were
    /// absent, and their absence was taken as evidence about the geometry rather than about the
    /// sink. The same log held 125 `pairing` lines, which is exactly the number of ENTITY models —
    /// those arrive through <see cref="LoadFrames"/>, which was handed a real logger four lines
    /// away in the same method.
    ///
    /// A null-object default is right where several callers genuinely differ. With one caller it
    /// is a hole with a comment over it, and this is the second time that shape has cost a day
    /// (`docs/memory/a-null-object-default-hides-a-missed-wiring.md`). Required means the compiler
    /// asks the question instead of a reviewer having to.
    /// </remarks>
    public static IReadOnlyList<PropVertex> Load(
        ILogger props,
        ReadOnlyMemory<byte> map,
        PakFile pak,
        GameArchives archives,
        MaterialTable materialTable,
        Func<string, ResolvedMaterial?> load,
        ICollection<string>? refusedLighting = null,
        Func<float, float, float, PointLighting>? lightAt = null)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(pak);
        ArgumentNullException.ThrowIfNull(archives);
        ArgumentNullException.ThrowIfNull(materialTable);
        ArgumentNullException.ThrowIfNull(load);

        IReadOnlyList<BspStaticProp> placements;

        try
        {
            placements = BspStaticProps.Read(map);
        }
        catch (InvalidDataException failure)
        {
            props.LogWarning(failure, "reading the map's static props");
            return [];
        }

        if (placements.Count == 0)
        {
            return [];
        }

        int brushMaterialCount = materialTable.Count;
        // **Sequential, and not because nobody has looked.** Loading props is the largest stage of
        // a map load - 2.97s against 0.65s for materials and 1.17s for lightmaps on
        // cp_process_f12 - and the decode itself would parallelise, since each model's .mdl, .vvd
        // and .vtx are independent.
        //
        // What does not parallelise is what the loop does alongside the decode: it appends each
        // model's materials to the shared table and hands out their indices. Those indices are
        // referenced by every prop vertex, so producing them out of order repaints the props with
        // each other's textures, differently on each run. Separating the decode from the index
        // assignment is a real refactor rather than a wrapper, so it is left as one.
        Dictionary<string, LoadedModel?> loaded = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> materialIndices = new(StringComparer.OrdinalIgnoreCase);
        List<PropVertex> world = [];

        int placed = 0;
        int skipped = 0;
        int unlit = 0;

        // Every placement whose baked lighting existed and was refused, named. Empty is the only
        // acceptable state on a map this project claims to read; see RejectedPropLighting.
        List<string> refused = [];

        for (int index = 0; index < placements.Count; index++)
        {
            BspStaticProp placement = placements[index];

            if (placed >= MaximumPlacements)
            {
                props.LogWarning(
                    "stopping at {Maximum} placements; the map declares more", MaximumPlacements);
                break;
            }

            if (!loaded.TryGetValue(placement.Model, out LoadedModel? model))
            {
                model = Read(
                    props,
                    placement.Model, pak, archives, materialTable, materialIndices, load);
                loaded[placement.Model] = model;
            }

            if (model is null)
            {
                skipped++;
                continue;
            }

            PropTransform transform = new(placement);

            PropLighting lighting = Lighting(props, pak, placed: index, model.Checksum);

            if (lighting.Colours is null)
            {
                unlit++;
            }

            if (lighting.Rejected is { } reason)
            {
                // **Counted apart from "has none", because they are not the same event.** A prop
                // with no baked lighting is ordinary; a prop whose baked lighting was READ and
                // refused is this project failing on data the game uses. Folding them together is
                // what let four refusals sit inside a plausible "without baked lighting" total.
                refused.Add($"prop {index} ({placement.Model}): {reason}");
            }

            // **The placement's skin family, which every prop used to ignore.**
            // StaticPropLump_t.m_Skin says which family a placed model draws with, and reading it
            // as zero for everything drew the FIRST variant of every skinned prop — not an error,
            // and it reads as the map's own art rather than as a defect. Measured on
            // cp_process_final: 267 of 1631 placements ask for a family other than zero.
            //
            // The lookup is done here rather than at load time because a model is loaded ONCE and
            // placed many times, with different families at different placements. Vertices are
            // already copied per placement, so resolving the material on the way past costs a
            // dictionary lookup and no extra geometry.
            //
            // **Indexed BY family rather than by family-minus-one, and family zero is not special**
            // (B229). The table used to hold families 1..N and to be keyed on family zero's
            // resolved material, which made a model whose family-zero texture the map does not
            // pack undrawable in every family. An out-of-range skin falls back to family zero,
            // which is `props_shared.cpp:1079`'s answer for the same input.
            IReadOnlyDictionary<int, int>? family = null;

            if (model.Frames.SkinSwaps is { Count: > 0 } families)
            {
                int chosen = placement.Skin >= 0 && placement.Skin < families.Count
                    ? placement.Skin
                    : 0;

                family = families[chosen];
            }

            // **Sampled once per placement, not per vertex.** The engine gives a whole model one
            // ambient cube — `DrawModelInfo_t.m_vecAmbientCube` is a single set of six — and it is
            // the vertex NORMAL that varies across the mesh, which is applied below. Sampling per
            // vertex would also be a lookup through the BSP tree for every corner of every prop.
            //
            // Only when there is nothing baked to use. A prop with valid vertex lighting keeps it:
            // that is higher quality than a cube, which is why the compiler wrote it.
            // **The cube alone here, and the local lights are dropped deliberately.** This bakes a
            // static prop's light into its VERTEX COLOURS, one sample per prop — there is no normal
            // to shade a local light against at this point and no per-draw constant to carry one in.
            // A prop lit this way therefore keeps the flat-cube behaviour it has always had; the
            // per-light path is for the models drawn through the shader.
            AmbientCube? cube = lighting.Colours is null
                ? lightAt?.Invoke(placement.X, placement.Y, placement.Z).Cube
                : null;

            for (int at = 0; at < model.Corners.Count; at++)
            {
                PropVertex corner = model.Corners[at];

                if (family is not null && family.TryGetValue(corner.MaterialSlot, out int painted))
                {
                    corner = corner with { MaterialIndex = painted };
                }

                (float x, float y, float z) = transform.Apply(corner.X, corner.Y, corner.Z);

                (float red, float green, float blue) = Colour(
                    lighting.Colours, model.Meshes[at], model.Vertices[at], cube, corner);

                // **The placement's own origin rides along**, so a prop can be kept or dropped as
                // one thing. Judging its triangles individually cannot tell a 3D skybox prop from
                // a real one: both are made of ordinary triangles, and the skybox's are a valid
                // shape at a valid position - just a position that is nowhere near where the
                // player sees it.
                world.Add(corner with
                {
                    X = x,
                    Y = y,
                    Z = z,
                    OriginX = placement.X,
                    OriginY = placement.Y,
                    Red = red,
                    Green = green,
                    Blue = blue,
                });
            }

            placed++;
        }

        int transparent = 0;

        for (int index = brushMaterialCount; index < materialTable.Count; index++)
        {
            if (materialTable.Textures[index] is { IsTransparent: true })
            {
                transparent++;
            }
        }

        if (refusedLighting is not null)
        {
            // A loop rather than AddRange: the parameter is ICollection<string> so any caller's
            // collection type works, and ICollection has no AddRange. The list is a handful of
            // material names, so the difference costs nothing measurable.
            foreach (string name in refused)
            {
                refusedLighting.Add(name);
            }
        }

        // **Four categories, not one.** A log that reports only failures reads clean while
        // everything quietly falls back, which is how four refused lighting files sat inside an
        // ordinary-looking total. What is asked for, what was found, what was produced and what is
        // still missing are different questions, and only the last of them used to be written down.
        props.LogInformation(
            "{Message}",
            $"ASKED FOR {placed} placements across {loaded.Count} models; " +
            $"HAVE baked lighting for {placed - unlit}; " +
            $"PRODUCED {world.Count / 3} triangles, {transparent} of " +
            $"{materialTable.Count - brushMaterialCount} prop materials alpha tested; " +
            $"MISSING {skipped} models that would not load, {unlit - refused.Count} placements the " +
            $"compiler never lit, {refused.Count} whose baked lighting exists and was REFUSED");

        foreach (string rejection in refused)
        {
            // Named individually, because a count tells you something is wrong and a name tells you
            // which prop to go and look at. Four of these hid inside an aggregate for weeks.
            props.LogInformation("refused baked lighting: {Rejection}", rejection);
        }

        return world;
    }

    /// <summary>What reading one placement's baked lighting produced.</summary>
    /// <param name="Colours">The baked colours, or null when there are none to apply.</param>
    /// <param name="Rejected">
    /// Why lighting that DID exist was refused, or null when nothing was refused.
    /// </param>
    /// <remarks>
    /// **This type exists because one <c>null</c> used to mean two unrelated things.** A prop with
    /// no baked lighting is ordinary — a map compiled without static prop lighting has none for any
    /// of them — while a prop whose lighting was found, read, and then refused on a checksum is this
    /// project failing on data the game uses happily. Both returned null, both drew the prop with
    /// white vertex colours, and only a warning line separated them.
    ///
    /// The cost was concrete: B55 recorded "four vertex-lighting checksum mismatches" in passing,
    /// inside a total that read as ordinary, and B83 spent four hypotheses on capture points that
    /// draw wrong. Absence and refusal are now different values, counted separately, and the refusal
    /// count is asserted by a test rather than logged.
    /// </remarks>
    private readonly record struct PropLighting(
        IReadOnlyList<IReadOnlyList<(byte Red, byte Green, byte Blue)>>? Colours,
        string? Rejected)
    {
        /// <summary>No baked lighting exists for this placement, which is ordinary.</summary>
        public static PropLighting None => new(null, null);

        /// <summary>Baked lighting exists and this project would not use it.</summary>
        public static PropLighting Refused(string reason) => new(null, reason);
    }

    /// <summary>Reads one placement's baked lighting, or nothing when it has none.</summary>
    /// <remarks>
    /// **Named by the placement's index in the static prop lump**, which is how the compiler wrote
    /// it. A prop with no lighting is normal rather than an error - a map compiled without static
    /// prop lighting has none for any of them - so this reports absence quietly and the caller
    /// counts it.
    /// </remarks>
    // The logger is a parameter because this is static (D83).
    private static PropLighting Lighting(ILogger props, PakFile pak, int placed, int checksum)
    {
        foreach (string path in StudioVertexLighting.PathsFor(placed))
        {
            byte[]? file;

            try
            {
                file = pak.ReadFile(path);
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException)
            {
                // The pakfile itself would not give up the entry. That is a refusal, not an
                // absence: the map says the lighting is there and this could not read it.
                return PropLighting.Refused($"{path} could not be read: {failure.Message}");
            }

            if (file is null)
            {
                continue;
            }

            try
            {
                return new PropLighting(StudioVertexLighting.Read(file, checksum), null);
            }
            catch (InvalidDataException failure)
            {
                // Includes the checksum guard: lighting baked against a different build of the
                // model would light the wrong parts of it, so refusing to apply it is right.
                // Refusing SILENTLY was not - the prop then draws with white vertex colours and
                // nothing distinguishes it from one the compiler never lit.
                props.LogWarning(failure, "reading {Path}", path);
                return PropLighting.Refused($"{path}: {failure.Message}");
            }
        }

        return PropLighting.None;
    }

    /// <summary>One vertex's baked colour, or white where there is none to apply.</summary>
    /// <remarks>
    /// **Applied only where the counts agree**, which is the check the engine makes before
    /// uploading vertex colours. vrad counts a mesh's colours from its strip group, which may
    /// duplicate vertices past the model mesh's own count; measured at one model in two hundred on
    /// cp_process_final. Applying a short run anyway would shift every colour after it onto the
    /// wrong vertex, which lights the prop convincingly and wrongly.
    /// </remarks>
    private static (float Red, float Green, float Blue) Colour(
        IReadOnlyList<IReadOnlyList<(byte Red, byte Green, byte Blue)>>? lighting,
        int mesh,
        int vertex,
        AmbientCube? cube,
        PropVertex corner)
    {
        // **The engine's fallback, which is the light cache rather than white** (B123). A cube is
        // supplied only when nothing was baked, so this cannot override valid vertex lighting; see
        // the remarks on Load.
        //
        // Evaluated with the vertex's own normal, which is what makes a cube light a shape rather
        // than tint it flat — the same `VertexShaderAmbientLight` arithmetic the world uses.
        if (cube is { } sampled)
        {
            return sampled.Light(corner.NormalX, corner.NormalY, corner.NormalZ);
        }

        if (lighting is null || mesh < 0 || mesh >= lighting.Count)
        {
            return (1f, 1f, 1f);
        }

        IReadOnlyList<(byte Red, byte Green, byte Blue)> colours = lighting[mesh];

        if (vertex < 0 || vertex >= colours.Count)
        {
            return (1f, 1f, 1f);
        }

        (byte red, byte green, byte blue) = colours[vertex];

        // **Doubled, because the engine doubles it.** vrad builds its vertex-light table as
        // pow(linear, 1/gamma) * overbrightFactor, storing HALF the light when overbright is 2, and
        // the vertex-lit shader multiplies it back:
        //
        // its vertex-lit shader defines an overbright of two and multiplies the stored colour by
        // it before converting to linear.
        //
        // Without it every prop draws at half brightness - dark rocks and near-black foliage on a
        // sunlit map, which is what the owner kept reporting as blobs.
        //
        // The measurement had said so before the source did: prop colours averaged 0.2309 against
        // the world's lightmaps at 0.4704, a ratio of 2.04. That was first explained as a missing
        // gamma step, because 0.23 ^ (1/2.2) is 0.495 and also lands near 0.47 - two different
        // curves passing through one point, and the wrong one was picked. Only the shader settles
        // which.
        //
        // Clamped rather than carried, since this renderer works in display space and has no tone
        // map to give over-range light anywhere to go.
        return (
            Math.Min(1f, red / 255f * Overbright),
            Math.Min(1f, green / 255f * Overbright),
            Math.Min(1f, blue / 255f * Overbright));
    }

    /// <summary>Reads one model's three files and turns them into triangles.</summary>
    /// <summary>Reads one model for an entity to wear, in the model's own coordinates.</summary>
    /// <param name="path">The model path, as modelprecache named it.</param>
    /// <param name="pak">The map's embedded files, which override the game's.</param>
    /// <param name="archives">The game's own archives.</param>
    /// <param name="materialTable">The map's material table, extended in place with the props'. Every list it holds grows together.</param>
    /// <param name="load">Resolves a material path to a texture.</param>
    /// <returns>The triangles, or <c>null</c> when the model could not be read.</returns>
    /// <remarks>
    /// **Its materials join the map's table**, so the renderer binds them the same way it binds a
    /// brush face's and one texture upload covers everything. That is why entity models are
    /// loaded with the map rather than during playback: growing the table afterwards would mean
    /// re-uploading the textures mid-match.
    ///
    /// Its own material index cache, because this is called once per model rather than in the loop
    /// static props use — and sharing one across calls would keep a dictionary alive for the life
    /// of the map to save a lookup per model.
    /// </remarks>
    /// <param name="props">Where a refusal is reported; a parameter because this is static (D83).</param>
    internal static IReadOnlyList<PropVertex>? LoadOne(
        ILogger props,
        string path,
        PakFile pak,
        GameArchives archives,
        MaterialTable materialTable,
        Func<string, ResolvedMaterial?> load) =>
        Read(props, path, pak, archives, materialTable, [], load)?.Corners;

    /// <summary>Reads one model once per frame of its animation.</summary>
    /// <param name="path">The model path, as modelprecache named it.</param>
    /// <param name="pak">The map's embedded files, which override the game's.</param>
    /// <param name="archives">The game's own archives.</param>
    /// <param name="materialTable">The map's material table, extended in place with the props'. Every list it holds grows together.</param>
    /// <param name="load">Resolves a material path to a texture.</param>
    /// <returns>One geometry per frame, or <c>null</c> when the model could not be read.</returns>
    /// <remarks>
    /// **Every frame is skinned once, at load, rather than per frame of playback.** A health pack's
    /// animation is thirty frames and its geometry is small, so baking all of them costs a few
    /// megabytes and makes playback free: the renderer picks a vertex range and draws it, with no
    /// per-frame work on either processor.
    ///
    /// **This does not generalise to players and is not meant to.** A player has hundreds of
    /// frames over ninety bones, and baking those would be gigabytes. Those need the bone matrices
    /// in a constant buffer and the transform in the vertex shader, which is what the engine does
    /// and what this project will do for them - the two strategies coexist because the models
    /// differ by two orders of magnitude, not because one is a stopgap.
    /// </remarks>
    /// <param name="mustSkin">Whether the model is bone-merged and so cannot be baked.</param>
    /// <param name="props">Where a refusal is reported; a parameter because this is static (D83).</param>
    internal static ModelFrames? LoadFrames(
        ILogger props,
        string path,
        PakFile pak,
        GameArchives archives,
        MaterialTable materialTable,
        Func<string, ResolvedMaterial?> load,
        bool mustSkin = false) =>
        Read(props, path, pak, archives, materialTable, [], load, mustSkin)?.Frames;

    /// <summary>Reads one model's geometry, in the model's own coordinates.</summary>
    /// <remarks>
    /// Internal rather than private because networked entities need the same thing: a model loaded
    /// once, in model space, to be posed per instance. A static prop is posed by the map and an
    /// entity is posed by the demo, and only the transform differs.
    /// </remarks>
    private static long BakeTicks;

    /// <summary>How long posing and baking model frames has taken this run.</summary>
    /// <remarks>
    /// **Baking is this project's one deliberate deviation from Valve**: small props and model
    /// animations are posed once at load rather than driven per frame. That trade is paid here, so
    /// this is where it can be judged — and the owner is weighing it against a target of a five to
    /// ten second map load.
    /// </remarks>
    public static double BakeSeconds => BakeTicks / (double)System.Diagnostics.Stopwatch.Frequency;

    internal static LoadedModel? Read(
        ILogger props,
        string path,
        PakFile pak,
        GameArchives archives,
        MaterialTable materialTable,
        Dictionary<string, int> materialIndices,
        Func<string, ResolvedMaterial?> load,
        bool mustSkin = false)
    {
        byte[]? Find(string file)
        {
            try
            {
                return pak.ReadFile(file) ?? archives.Read(file);
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException)
            {
                return null;
            }
        }

        // The index file is named by replacing the extension, and the game writes several - dx80,
        // dx90, sw. dx90 is the one every machine that can run this viewer has.
        string stem = path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)
            ? path[..^4]
            : path;

        if (Find(path) is not { } modelFile ||
            Find(stem + ".vvd") is not { } vertexFile ||
            Find(stem + ".dx90.vtx") is not { } indexFile)
        {
            props.LogWarning("{Path}: one of its three files is missing", path);
            return null;
        }

        try
        {
            StudioModelInfo model = StudioModel.Read(modelFile);
            IReadOnlyList<StudioVertex> vertices = StudioVertices.Read(vertexFile);
            IReadOnlyList<IReadOnlyList<StudioCorner>> meshes = StudioTriangles.Read(indexFile, model);

            // **The skeleton, which decides whether the model stands up.** A model compiled with
            // $staticprop has its bone transform baked into the vertices and names no bone
            // weights, so skinning it is an identity and costs a multiply. An animated model does
            // not, and drawing its vertices raw lays it on its side - measured as a player 84
            // units long on Y with only 25 on Z, where a TF2 player is 83 units TALL.
            IReadOnlyList<StudioBone> bones = StudioBones.Read(modelFile);

            // **Which animations this model can actually play.** A sequence names an animation
            // and the animation says how long it is. Every distinct animation any sequence
            // references gets baked, rather than only sequence zero's - a door has an opening
            // sequence and a closing one, and baking the first would draw it shut while the demo
            // says otherwise.
            IReadOnlyList<StudioSequence> sequences = StudioSequences.Read(modelFile);

            // **A player model holds almost none of its own animation.** scout.mdl declares 306
            // sequences and two local animations of one frame each - the reference pose. The 1,012
            // animations it plays are in the models it INCLUDES, so a budget computed from the
            // local ones alone says a player is cheap to bake, which is how this first measured.
            List<byte[]> groupModels = [modelFile];
            List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups =
                [(0, sequences)];

            foreach (string included in StudioModelGroups.Read(modelFile))
            {
                if (Find(included) is not { } animations)
                {
                    // Reported rather than skipped: a missing animation model is why a class would
                    // stand still, and it looks exactly like an animation state that never ran.
                    props.LogWarning(
                        "{Path} includes {Included}, which was not found", path, included);
                    continue;
                }

                groups.Add((groupModels.Count, StudioSequences.Read(animations)));
                groupModels.Add(animations);
            }

            StudioSequenceTable table = StudioSequenceTable.Merge(groups);

            // **The merged table's first entries, by NAME.** A demo says `m_nSequence`, and a
            // number can only be compared against another number — which is how a viewmodel came to
            // play a sequence that left its root bone at identity and the model on its side. The
            // name says what the engine would call it, so the two tables can actually be compared.
            //
            // Capped and logged once per model: the interesting part is the front of the list,
            // where a merge order that disagrees with the engine's shows up first.
            if (table.Count > 0)
            {
                props.LogInformation(
                    "{Message}",
                    $"sequences {path}: {table.Count} merged, " +
                    string.Join(
                        ", ",
                        Enumerable.Range(0, Math.Min(8, table.Count))
                            .Select(index =>
                            {
                                if (table.At(index) is not { } at ||
                                    at.Group >= groups.Count ||
                                    at.Local >= groups[at.Group].Sequences.Count)
                                {
                                    return $"[{index}] unresolved";
                                }

                                StudioSequence entry = groups[at.Group].Sequences[at.Local];

                                return $"[{index}] g{at.Group} '{entry.Label}'" +
                                    (entry.Activity.Length > 0 ? $" act {entry.Activity}" : string.Empty);
                            })));
            }

            // **One pose parameter list across the base model and everything it includes.** A
            // player model declares two of its own; move_x and move_y arrive with the animation
            // model, and a sequence's paramindex is local to whichever group owns it. Built here
            // from groupModels because that list is already in group order with the base first,
            // which is the order the engine merges in.
            (IReadOnlyList<StudioPoseParameter> sharedPose, IReadOnlyList<IReadOnlyList<int>> masterPose) =
                StudioPoseParameterMerge.Merge(
                    [.. groupModels.Select(file => StudioSequences.PoseParameters(file))]);

            List<int> sequenceAnimation = [.. sequences.Select(sequence => sequence.Animation)];
            List<bool> sequenceLoops = [.. sequences.Select(sequence => sequence.Loops)];
            // **Looping animations get the budget first.** A loop is the one that plays
            // continuously, so starving it is the most visible way to spend a limited bake - and a
            // greedy pass did exactly that on sentry3_heavy, giving 38 frames to a one-shot and a
            // single frame to the idle it actually shows.
            HashSet<int> looping =
            [
                .. sequences.Where(entry => entry.Loops && entry.Animation >= 0)
                    .Select(entry => entry.Animation),
            ];

            List<int> wanted =
            [
                .. sequenceAnimation
                    .Distinct()
                    .Where(index => index >= 0)
                    .OrderByDescending(looping.Contains),
            ];

            if (wanted.Count == 0)
            {
                wanted.Add(0);
            }

            Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> layout = [];
            List<StudioSkeleton> skeletons = [];

            // **The budget is per MODEL, not per animation, and that distinction is measured.**
            // A per-animation cap let sentry3_heavy bake 113 frames across six animations of
            // 51,492 corners - roughly 490 megabytes for one model, on a project whose stated
            // ceiling is an eight gigabyte machine. The cost is frames TIMES corners, so a cap
            // counting only frames does not bound it.
            int cornersPerFrame = 0;

            for (int index = 0; index < meshes.Count && index < model.Meshes.Count; index++)
            {
                cornersPerFrame += meshes[index].Count;
            }

            int affordable = Math.Clamp(
                MaximumBakedCorners / Math.Max(1, cornersPerFrame), 1, MaximumBakedFrames);

            // **What baking this model completely would cost, against what it may spend.** A model
            // that cannot afford all of its frames is skinned on the GPU rather than baked short:
            // truncating leaves it drawing a fraction of what it can do, silently.
            // Counted across every sequence the merged table can reach, not the base model's own
            // animations - which is the whole point of merging it.
            long wantedFrames = 0;

            for (int sequence = 0; sequence < table.Count; sequence++)
            {
                if (table.At(sequence) is not { } where)
                {
                    continue;
                }

                wantedFrames += Math.Max(
                    1,
                    StudioAnimation.Frames(
                        groupModels[where.Group],
                        groups[where.Group].Sequences[where.Local].Animation));
            }

            // **A worn model is skinned however cheap it is, and this is not an optimisation
            // choice.** Baking pre-transforms the vertices by one pose and discards the bone
            // indices, which is fine for a model drawn at its own transform and useless for one
            // that is bone-merged: a merged item's entire position comes from its wearer's
            // skeleton, so with no bones to pose it there is nothing to attach it by, and it draws
            // at the wearer's ORIGIN - which on a player is their feet.
            //
            // Measured: every cosmetic in cp_process is a few thousand corners and one sequence,
            // so all of them were baked, and the log said "1 baked frames" for each while the
            // merge quietly did nothing. The hats sat at ankle height and the whole mechanism read
            // as broken.
            // **`bones.Count > 1` is a budget rule and must not reach `mustSkin`, which is a
            // correctness one.** Written as one conjunction it did, and then a worn model with a
            // single bone could never be skinned however loudly the caller asked — which is the
            // paragraph above being contradicted by the line that implements it.
            //
            // One bone is not a degenerate case here, it is the NORMAL shape for a weapon that
            // hangs off its wearer: the Original declares exactly one, `weapon_bone`, and the
            // soldier's arms provide it (ViewmodelBoneMergeTests). So it could merge perfectly and
            // never merged at all, and was drawn at the wearer's origin — for a viewmodel, the
            // camera. That is the owner's "the original being way too high and taking up all the
            // screen", on every demo since that weapon shipped in June 2012.
            //
            // It hid because the stock launcher has four bones, clears the guard, and works. One
            // weapon in the game was wrong, for a reason that had nothing to do with that weapon.
            //
            // The guard is still right for the budget path: skinning a single-bone model to save
            // frames buys nothing, because one bone cannot deform anything.
            bool skin = bones.Count > 0 &&
                (mustSkin || (wantedFrames > affordable && bones.Count > 1));

            long bakingFrom = System.Diagnostics.Stopwatch.GetTimestamp();

            foreach (int index in wanted)
            {
                int frames = Math.Clamp(
                    StudioAnimation.Frames(modelFile, index),
                    1,
                    Math.Max(1, affordable - skeletons.Count));

                layout[index] = (
                    skeletons.Count, frames, StudioAnimation.CyclesPerSecond(modelFile, index));

                for (int frame = 0; frame < frames; frame++)
                {
                    skeletons.Add(StudioBones.Posed(
                        bones, StudioAnimation.Pose(modelFile, bones, index, frame)));
                }
            }

            List<int> cornerMeshes = [];
            List<int> cornerVertices = [];
            List<IReadOnlyList<PropVertex>> baked = new(skeletons.Count);

            // The material indices are resolved once and reused for every frame. Registering them
            // inside the bake would append the same materials to the shared table once per frame.
            int[] materialByMesh = new int[Math.Min(meshes.Count, model.Meshes.Count)];

            short[] skinTable = StudioSkins.Read(modelFile);
            int families = StudioSkins.Families(modelFile);
            int references = StudioSkins.References(modelFile);

            for (int index = 0; index < materialByMesh.Length; index++)
            {
                // **Family ZERO's texture, said explicitly rather than assumed** (B229). A mesh's
                // `material` is a skinref, and family zero's row is what turns it into a texture
                // index — usually the identity, which is why reading the reference directly agreed
                // with the engine on almost every model this project has ever loaded. On a model
                // whose first row is not the identity it does not, and the mesh silently takes
                // another mesh's texture.
                materialByMesh[index] = Register(
                    props,
                    model,
                    StudioSkins.TextureFor(
                        skinTable, references, families, 0, model.Meshes[index].MaterialIndex),
                    materialTable,
                    materialIndices,
                    load);
            }

            // **Team colours are a SKIN FAMILY, not a tint.** A TF2 player model carries two: skin
            // 0 is RED and skin 1 is BLU, which is the convention the game itself uses -
            // `m_nSkin = ( team == TF_TEAM_RED ) ? 0 : 1` at tf_player_shared.cpp:4849. Using
            // family zero for everyone draws both teams in red, which is what happened.
            //
            // Every family's materials are registered here so all of them upload with the map, and
            // the batches below are emitted once per family over the SAME vertices - a family
            // differs only in which material paints a mesh, so it costs batch metadata rather than
            // geometry.
            //
            // **Keyed by the mesh's SKINREF, and built for every family including zero (B229).**
            // It used to start at family 1 and key each entry on family zero's RESOLVED material
            // index, which made family zero load-bearing for every other family: a model whose
            // family-zero texture the map does not pack resolved to −1, `−1` was refused as a key,
            // and every placement of that model drew in the missing-material chequer however well
            // its own family resolved. `cp_fulgur` places `props_aquatic/pipe_256.mdl` at skins 1
            // and 12 of 15 and packs exactly those two textures.
            //
            // Family zero is included so the lookup below has no special case, and entries are
            // recorded even when they resolve to −1: a family whose texture is genuinely missing
            // draws the chequer, which is what the engine does, rather than quietly falling back to
            // another family's art.
            List<Dictionary<int, int>> byFamily = [];

            for (int family = 0; family < families; family++)
            {
                Dictionary<int, int> painted = [];

                for (int index = 0; index < materialByMesh.Length; index++)
                {
                    int reference = model.Meshes[index].MaterialIndex;

                    if (reference < 0 || painted.ContainsKey(reference))
                    {
                        continue;
                    }

                    painted[reference] = family == 0
                        ? materialByMesh[index]
                        : Register(
                            props,
                            model,
                            StudioSkins.TextureFor(
                                skinTable, references, families, family, reference),
                            materialTable,
                            materialIndices,
                            load);
                }

                byFamily.Add(painted);
            }

            if (families > 1)
            {
                props.LogInformation(
                    "{Message}",
                    $"skins {path}: {families} families over {references} references, " +
                    string.Join(
                        ", ",
                        byFamily.Select((family, at) =>
                            $"[{at}] {family.Count} refs, "
                            + $"{family.Values.Count(material => material < 0)} unpainted")));
            }

            // **A skinned model keeps ONE copy of its geometry, and unposed.** The shader applies
            // the bone matrices, so skinning here as well would transform every vertex twice - by
            // the rest pose on the processor and by the real pose on the card.
            int slots = skin ? 1 : skeletons.Count;

            for (int slot = 0; slot < slots; slot++)
            {
                StudioSkeleton posed = skeletons[slot];
                List<PropVertex> frame = [];

                // **The two files paired mesh by mesh, said once per model.** `meshes` comes from
                // the .vtx and `model.Meshes` from the .mdl, and they are matched by POSITION — so
                // if the two walks ever disagree about a model, every mesh after it takes another
                // mesh's corners against its own vertex range. The result is a batch that reports
                // the right material and alternative while drawing someone else's triangles, which
                // is precisely what a capture point showing the neutral sign inside the blue one
                // looks like. Both counts are equal by construction, so only a per-mesh comparison
                // can show it.
                if (slot == 0)
                {
                    props.LogInformation(
                        "{Message}",
                        $"pairing {path}: " + string.Join(
                            ", ",
                            model.Meshes.Select((mesh, at) =>
                                $"[{at}] part {mesh.BodyPart} alt {mesh.BodyModel} " +
                                $"mdl {mesh.VertexCount}v vtx {(at < meshes.Count ? meshes[at].Count : -1)}c " +
                        $"mat {(at < materialByMesh.Length ? materialByMesh[at] : -1)}" +
                        $" '{(at < materialByMesh.Length && materialByMesh[at] >= 0 && materialByMesh[at] < materialTable.Count ? materialTable.Materials[materialByMesh[at]].Name : "?")}'")));
                }

                for (int index = 0; index < materialByMesh.Length; index++)
                {
                    StudioMesh mesh = model.Meshes[index];

                    // **Names the MODEL whose mesh will draw chequered** (B62 follow-on). Two
                    // instruments already report this and neither can be acted on: the material
                    // inventory says every material resolved, and `MapWorld` says 19,274 triangles
                    // name material -1 at a position. Both true, neither says which model — and
                    // `Register`'s own two warnings fired zero times, so the -1 is arriving by a
                    // route nobody has yet named.
                    if (materialByMesh[index] < 0 && props.IsEnabled(LogLevel.Debug))
                    {
                        props.LogDebug(
                            "{Message}",
                            $"{model.Name}: mesh {index} carries material {materialByMesh[index]}, "
                            + $"from slot {mesh.MaterialIndex} of {model.Materials.Count}");
                    }

                    if (mesh.FirstVertex + mesh.VertexCount > vertices.Count)
                    {
                        continue;
                    }

                    foreach (StudioCorner corner in meshes[index])
                    {
                        StudioVertex vertex = vertices[mesh.FirstVertex + corner.Vertex];

                        (float x, float y, float z) = skin
                            ? (vertex.X, vertex.Y, vertex.Z)
                            : posed.Skin(
                                vertex.Bones, vertex.Weights, vertex.X, vertex.Y, vertex.Z);

                        // **The normal comes along now.** A static prop is lit by baked vertex
                        // colours and never needed it; an entity is lit from its leaf's ambient
                        // cube, which is evaluated against the surface normal.
                        frame.Add(new PropVertex(
                            x, y, z, vertex.U, vertex.V, materialByMesh[index],

                            // **The skinref this corner's mesh names**, which is what a family is
                            // looked up by (B229). `MaterialIndex` beside it is family zero's
                            // answer, kept because it is the default for a placement that names no
                            // other family and because everything downstream already batches on it.
                            MaterialSlot: mesh.MaterialIndex,
                            NormalX: vertex.NormalX,
                            NormalY: vertex.NormalY,
                            NormalZ: vertex.NormalZ,

                            // **Carried whether or not this model is skinned.** A baked model
                            // ignores them; a skinned one is moved by them in the shader, and the
                            // decision between the two is made after the vertices are built.
                            Bones: vertex.Bones,
                            Weights: vertex.Weights,

                            // **Which alternative of which body part this corner belongs to.** The
                            // choice between a part's alternatives is per ENTITY - three capture
                            // points share one model and show three different signs - so it cannot
                            // be made here. Carried through to the batching, which keeps each
                            // alternative in its own run so one can be skipped whole at draw time.
                            BodyPart: mesh.BodyPart,
                            BodyModel: mesh.BodyModel));

                        // **Position by mesh vertex, colour by strip group vertex.** They are
                        // different orderings of the same surface, and using one for both speckles
                        // the prop. Only the first frame fills these: every frame has the same
                        // corners in the same order, and only their positions differ.
                        if (slot == 0)
                        {
                            cornerMeshes.Add(corner.LightingGroup);
                            cornerVertices.Add(corner.LightingVertex);
                        }
                    }
                }

                baked.Add(frame);
            }

            System.Threading.Interlocked.Add(
                ref BakeTicks, System.Diagnostics.Stopwatch.GetTimestamp() - bakingFrom);

            // **Is the last frame really a duplicate of the first?** STUDIO_LOOPING says it
            // "should be", and dropping it is what removes a one frame stall at the loop seam.
            // But if an artist authored the frames as distinct steps covering the whole turn,
            // dropping one skips real motion - which reads as a hitch just the same, from the
            // opposite cause. Measured rather than assumed either way.
            foreach ((int animation, (int Start, int Frames, float CyclesPerSecond) where) in layout)
            {
                // **A skinned model has one slot of geometry and a layout describing thousands of
                // frames, so this probe cannot index it.** It crashed the viewer outright doing
                // exactly that - a diagnostic taking down the thing it was meant to explain, which
                // is the sharp end of measuring the wrong quantity.
                if (skin || where.Frames < 2 || where.Start + where.Frames > baked.Count)
                {
                    continue;
                }

                IReadOnlyList<PropVertex> opening = baked[where.Start];
                IReadOnlyList<PropVertex> closing = baked[where.Start + where.Frames - 1];

                float apart = 0f;

                for (int corner = 0; corner < opening.Count && corner < closing.Count; corner++)
                {
                    apart = MathF.Max(
                        apart,
                        MathF.Abs(opening[corner].X - closing[corner].X) +
                        MathF.Abs(opening[corner].Y - closing[corner].Y) +
                        MathF.Abs(opening[corner].Z - closing[corner].Z));
                }

                props.LogInformation(
                    "{Message}",
                    $"seam {path} anim {animation}: first and last frame differ by {apart:0.####} " +
                    $"units at most ({(apart < 0.01f ? "DUPLICATE, drop it" : "DISTINCT, keep it")})");
            }

            if (baked.Count > 1)
            {
                props.LogInformation(
                    "{Message}",
                    $"baked {path}: {baked.Count} frames across {wanted.Count} animations, " +
                    $"sequences [{string.Join(", ", sequences.Select(q => $"anim {q.Animation} flags 0x{q.Flags:X}{(q.Loops ? " LOOP" : string.Empty)}"))}], " +
                    $"{baked[0].Count} vertices each, " +
                    string.Join(
                        " ",
                        layout.Select(entry =>
                            $"[anim {entry.Key}: {entry.Value.Frames}f @ " +
                            $"{entry.Value.CyclesPerSecond:0.####} cyc/s, period " +
                            $"{(entry.Value.CyclesPerSecond > 0f ? 1f / entry.Value.CyclesPerSecond : 0f):0.###}s]")));
            }

            if (skin)
            {
                props.LogInformation(
                    "{Message}",
                    $"skinning {path}: {wantedFrames} frames over {table.Count} merged sequences " +
                    $"from {groupModels.Count} models would " +
                    $"cost {wantedFrames * cornersPerFrame:N0} corners against a budget of " +
                    $"{(long)affordable * cornersPerFrame:N0}, so it is posed on the GPU instead");
            }

            return new LoadedModel(
                baked[0],
                cornerMeshes,
                cornerVertices,
                model.Checksum,
                new ModelFrames(
                    baked,
                    layout,
                    sequenceAnimation,
                    sequenceLoops,
                    skin
                        ? new SkinnedModel(
                            bones,
                            groupModels,
                            table,
                            groups,
                            sharedPose,
                            masterPose)
                        {
                            Props = props,
                        }
                        : null,
                    IlluminationOf(modelFile),
                    byFamily,
                    model.BodyParts,

                    // Read for every model rather than only for wearers: which models get worn is
                    // not known here, and a table of a few dozen entries costs nothing next to the
                    // geometry beside it.
                    StudioAttachment.Read(modelFile),

                    // **Valve's own render bounds, per sequence** — see ModelFrames. One pass over
                    // the sequence list while the file is open, rather than the vertex extent that
                    // was nearly substituted for it.
                    ReadBoundsBySequence(modelFile, sequenceAnimation.Count),
                    StudioBounds.RenderBounds(modelFile, sequence: -1),

                    // **The one thing that entitles a model to be drawn twice.** Without it a model
                    // with any translucent material belongs wholly to the translucent pass — see
                    // RenderGroups, which is where the consequence is spelled out.
                    model.IsTranslucentTwoPass));
        }
        catch (InvalidDataException failure)
        {
            // Includes the checksum mismatch, which is the engine's own guard against a model
            // whose three files do not belong together.
            props.LogWarning(failure, "reading {Path}", path);
            return null;
        }
    }

    /// <summary>Every sequence's render bounds, computed once while the file is open.</summary>
    /// <remarks>
    /// **The union is per sequence and cannot be collapsed to one box**, because that is what makes
    /// a running player bounded differently from a crouched one — `GetRenderBounds` folds
    /// `seqdesc.bbmin`/`bbmax` into the header's box for whichever sequence is playing.
    /// </remarks>
    private static StudioBox[] ReadBoundsBySequence(
        ReadOnlyMemory<byte> file, int sequences)
    {
        if (sequences <= 0)
        {
            return [];
        }

        StudioBox[] boxes = new StudioBox[sequences];

        for (int sequence = 0; sequence < sequences; sequence++)
        {
            boxes[sequence] = StudioBounds.RenderBounds(file, sequence);
        }

        return boxes;
    }

    /// <summary>Where a model wants its light sampled, in its own space.</summary>
    /// <remarks>
    /// **<c>studiohdr_t.illumposition</c>, which studio.h calls the "illumination center".** A
    /// model's ORIGIN is not where it should be lit: a player's origin is at its feet, and a point
    /// resting exactly on a floor plane lands in the solid leaf beneath it, which carries no light
    /// at all. The model then draws black - measured on a medic, a soldier, a scout and a resupply
    /// locker, each at a real position inside the map.
    ///
    /// Offset 92, after <c>eyeposition</c> at 80 and before <c>hull_min</c> at 104. Pinned by the
    /// same field chain that puts <c>numbones</c> at 156, which this project already verified
    /// against real files.
    ///
    /// This is the engine's own answer to "where is this model lit", rather than a nudge upwards
    /// chosen to make the symptom go away.
    /// </remarks>
    private static (float X, float Y, float Z) IlluminationOf(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        const int illuminationOffset = 92;

        if (bytes.Length < illuminationOffset + 12)
        {
            return default;
        }

        return (
            BinaryPrimitives.ReadSingleLittleEndian(bytes[illuminationOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[(illuminationOffset + 4)..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[(illuminationOffset + 8)..]));
    }

    /// <summary>Finds or creates the combined table's entry for one of a model's materials.</summary>
    /// <remarks>
    /// Keyed by the resolved path rather than by the model, because props share materials heavily —
    /// a dozen rocks off one texture — and a per-model entry would decode it a dozen times.
    /// </remarks>
    // The logger is a parameter because this is static (D83).
    private static int Register(
        ILogger props,
        StudioModelInfo model,
        int materialIndex,
        MaterialTable materialTable,
        Dictionary<string, int> indices,
        Func<string, ResolvedMaterial?> load)
    {
        if (materialIndex < 0 || materialIndex >= model.Materials.Count)
        {
            // **This was silent, and the silence is why a magenta pipe took an evening.** A mesh
            // naming a slot outside its model's material list returns −1, the batch carries −1, and
            // the renderer binds the missing-material chequer for it — while the material inventory
            // reports every material resolved, because none FAILED. Two instruments both telling
            // the truth and neither able to see the fault between them.
            //
            // The owner found it by looking: pipe elbows and flat panels in the 3D skybox drawn in
            // Valve's chequer on `cp_fulgur`, a map the real game renders without one.
            props.LogWarning(
                "{Message}",
                $"{model.Name}: a mesh names material slot {materialIndex} of "
                + $"{model.Materials.Count}, which is outside the model's own list; that mesh will "
                + "draw as the missing-material chequer");

            return -1;
        }

        List<string> tried = [];

        foreach (string candidate in model.MaterialPaths(materialIndex))
        {
            tried.Add(candidate);

            if (indices.TryGetValue(candidate, out int existing))
            {
                return existing;
            }

            if (load(candidate) is not { Texture: { } painted } texture)
            {
                continue;
            }

            // **Seven lists, and the count is why this is one call.** Every list the renderer
            // indexes by material number has to grow together, and this appended to three of them
            // — texture, second texture and the table entry — while detail, bump, cubemap and
            // proxies were padded with nulls afterwards by the caller.
            //
            // So every model material silently lost all four. That is not visible as an error; it
            // is a prop that is slightly flat, which is indistinguishable from art direction, and
            // it is why a capture point's Sine proxy never ran: `cappoint_logo_blue` is an entity
            // model, and its proxies were being thrown away here.
            //
            // The history is that this comment used to read "**Three lists, not two**", added when
            // the second texture went missing the same way and a capture point beam kept its
            // stripes only for BLU. Adding one more `Add` beside the others fixes it until the next
            // list appears. MaterialTable.Add appends all seven, so there is no longer a way to
            // append one and forget the rest.
            int index = materialTable.Add(
                new BspMaterial(candidate, (0.5f, 0.5f, 0.5f), painted.Width, painted.Height),
                texture);

            indices[candidate] = index;

            return index;
        }

        // **This says what it knows, which is less than it used to claim.** It knew only that no
        // candidate yielded a usable TEXTURE, and reported "material not found" - which is a
        // different failure with different causes. Read literally it sent an investigation into
        // path joining and archive mounting while the truth was that the VMT resolved perfectly
        // and its texture existed only as a dxlevel-specific variant (B62).
        //
        // The distinction is already in the log a few lines above, from Resolve: one message for a
        // VMT that is missing and another for a texture that is. This points at them rather than
        // overwriting them with a guess.
        props.LogWarning(
            "{Message}",
            $"{model.Name}: material \"{model.Materials[materialIndex]}\" produced no texture; " +
            $"tried {string.Join(", ", tried)}. The lines above say whether the VMT was missing or " +
            $"whether it resolved and its texture was.");

        return -1;
    }

    /// <summary>
    /// A model's triangles in its own space, ready to be placed.
    /// </summary>
    /// <param name="Corners">The triangle corners, three per triangle.</param>
    /// <param name="Meshes">Which strip group each corner came from, in .vhv header order.</param>
    /// <param name="Vertices">Which vertex of that strip group each corner is.</param>
    /// <param name="Checksum">The model's checksum, which its lighting must match.</param>
    /// <remarks>
    /// **The mesh and vertex are kept because the model is shared and the lighting is not.** One
    /// model stands in fifty places under fifty different bakes, so the geometry is cached once and
    /// the colours are looked up per placement — which needs to know, for each corner, which mesh
    /// and which vertex of it produced that corner.
    /// </remarks>
    /// <param name="Frames">Every baked animation frame, and how to choose between them.</param>
    internal sealed record LoadedModel(
        IReadOnlyList<PropVertex> Corners,
        IReadOnlyList<int> Meshes,
        IReadOnlyList<int> Vertices,
        int Checksum,
        ModelFrames Frames);

    /// <summary>A model posed by its bones at draw time instead of having its frames baked.</summary>
    /// <param name="Bones">Its skeleton, which the pose is computed against.</param>
    /// <param name="Models">The base model and every animation model it includes, in group order.</param>
    /// <param name="Sequences">The merged sequence table a networked sequence number indexes.</param>
    /// <param name="Groups">Each group's own sequences, for resolving a merged number back.</param>
    /// <param name="PoseParameters">
    /// The SHARED pose parameters — the base model's merged with every included model's, as a
    /// virtual model builds them. A player model declares only <c>body_pitch</c> and
    /// <c>body_yaw</c>; <c>move_x</c> and <c>move_y</c> arrive with the animation model.
    /// </param>
    /// <param name="MasterPose">
    /// Per group, the map from that group's own pose parameter indices into
    /// <paramref name="PoseParameters"/>. A sequence's <c>paramindex</c> is local to its group, so
    /// this is what makes it mean anything against the shared list.
    /// </param>
    /// <remarks>
    /// **One copy of the geometry and a pose per draw.** Baking trades memory for draw cost and
    /// only pays while the frame count is small: a health pack is one animation of thirty frames,
    /// a scout is 1,012 animations over 23,442 corners. So a player's vertices are uploaded once
    /// with their bone indices and weights and the matrices arrive per draw, which is what
    /// IMaterialSystem::LoadBoneMatrix does in the engine.
    ///
    /// **Where this follows Valve and where it does not.** The include and merge mechanism is
    /// theirs, ported: <c>virtualmodel_t</c> merges sequences by label with forward declarations
    /// overridden. Skinning by bone matrices held as shader constants is theirs too.
    ///
    /// **Baking is not.** The engine has one path and skins everything, props included; this
    /// project bakes what is cheap to bake and skins what is not, which is an optimisation the
    /// owner chose knowingly. The cost of the divergence is two paths that can drift apart, and
    /// the mitigation is that the choice between them is made by measurement in one place rather
    /// than by classifying models.
    /// </remarks>
    public sealed record SkinnedModel(
        IReadOnlyList<StudioBone> Bones,
        IReadOnlyList<byte[]> Models,
        StudioSequenceTable Sequences,
        IReadOnlyList<(int Group, IReadOnlyList<StudioSequence> Sequences)> Groups,
        IReadOnlyList<StudioPoseParameter> PoseParameters,
        IReadOnlyList<IReadOnlyList<int>> MasterPose)
    {
        /// <summary>Where bone remapping reports how well two skeletons matched.</summary>
        /// <remarks>
        /// **An init property rather than a positional parameter (D83)**, so adding it did not
        /// change this record's public shape or its construction sites. It is set where the model
        /// is read, which is the only place that has a logger to give it.
        ///
        /// Defaults to the null logger, so a record built in a test says nothing rather than
        /// needing one.
        /// </remarks>
        public ILogger Props { get; init; } = NullLogger.Instance;

        /// <summary>The bone matrices for one sequence at one frame.</summary>
        /// <param name="sequence">The merged sequence number, as a demo would network it.</param>
        /// <param name="frame">Which frame of the animation it names.</param>
        /// <returns>One row-major three-by-four matrix per bone, or the rest pose.</returns>
        /// <remarks>
        /// **The skeleton is the BASE model's and the animation is the included model's.** An
        /// animation model carries the same bones by design - that is what makes it shareable - so
        /// a pose computed from one applies to the other. Taking the skeleton from the animation
        /// model instead would work until a model shipped a skeleton that differed.
        ///
        /// Computed per draw rather than stored: a scout's animation data is five megabytes, and
        /// recomputing one pose costs far less than keeping every pose it could take.
        /// </remarks>
        public IReadOnlyList<float[]> Pose(int sequence, int frame) =>
            Skeleton(sequence, frame).Matrices;

        /// <summary>The whole skeleton for one sequence at one frame, bone positions included.</summary>
        /// <param name="sequence">The merged sequence number, as a demo would network it.</param>
        /// <param name="frame">Which frame of the animation it names.</param>
        /// <returns>The posed skeleton.</returns>
        /// <remarks>
        /// Bone merging needs <see cref="StudioSkeleton.BoneToWorld"/> and skinning matrices cannot
        /// supply it, since they already have the wearer's bind pose folded in.
        /// </remarks>
        public StudioSkeleton Skeleton(int sequence, int frame) =>
            Skeleton(sequence, frame, []);

        /// <summary>The whole skeleton for one sequence at one frame, blend resolved.</summary>
        /// <param name="sequence">The merged sequence number, as a demo would network it.</param>
        /// <param name="frame">Which frame of the animation it names.</param>
        /// <param name="poseValues">
        /// A value for each of the model's pose parameters, in <see cref="PoseParameters"/> order.
        /// </param>
        /// <returns>The posed skeleton.</returns>
        /// <remarks>
        /// **A sequence names a grid of animations, not one animation.** Taking the corner is
        /// right for a prop and wrong for a player: a nine-way movement blend's corner is one
        /// extreme direction, so the legs run that way whatever the body is doing.
        ///
        /// The engine locates a point in the grid from two pose parameters
        /// (<c>Studio_LocalPoseParameter</c>), splits the surrounding square along a diagonal and
        /// blends the three corners of the triangle the point falls in
        /// (<c>Calc3WayBlendIndices</c>, reached because <c>anim_3wayblend</c> defaults to on).
        /// This does the same, in <c>CalcPoseSingle</c>'s own order — the pairwise blend first at
        /// <c>weight[1] / (weight[0] + weight[1])</c>, then the third corner at <c>weight[2]</c>.
        /// </remarks>
        public StudioSkeleton Skeleton(int sequence, int frame, IReadOnlyList<float> poseValues) =>
            Locals(sequence, frame, poseValues) is { Count: > 0 } pose
                ? StudioBones.Posed(Bones, pose)
                : StudioBones.RestPose(Bones);

        /// <summary>The LOCAL transforms an animation gives each bone, before any concatenation.</summary>
        /// <param name="sequence">A merged sequence number.</param>
        /// <param name="frame">Which frame of it.</param>
        /// <param name="poseValues">Every pose parameter's value, normalised, in the model's order.</param>
        /// <returns>The bones the animation moves; ones it omits keep their rest values.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="poseValues"/> is null.</exception>
        /// <remarks>
        /// **Extracted from <see cref="Skeleton(int, int, IReadOnlyList{float})"/> on 2026-08-24,
        /// which now calls it** (D88). The bone pipeline needs locals rather than finished matrices:
        /// <c>StandardBlendingRules</c> produces <c>pos</c> and <c>q</c> arrays and
        /// <c>BuildTransformations</c> turns them into bone-to-world afterwards, with the merge and
        /// the IK solve in between. Handing out concatenated matrices means those stages have
        /// nowhere to run.
        ///
        /// A pure extraction — every path through the old method already ended in
        /// <c>StudioBones.Posed(Bones, pose)</c>, so this is the same code with the last line moved
        /// to the caller.
        /// </remarks>
        public IReadOnlyList<StudioBonePose> Locals(
            int sequence, int frame, IReadOnlyList<float> poseValues)
        {
            ArgumentNullException.ThrowIfNull(poseValues);

            if (Sequences.At(sequence) is not { } where ||
                where.Group >= Models.Count ||
                where.Local >= Groups[where.Group].Sequences.Count)
            {
                return [];
            }

            StudioSequence chosen = Groups[where.Group].Sequences[where.Local];

            if (chosen.Blend is not { Blends: true } grid || poseValues.Count == 0)
            {
                return PoseOf(where.Group, chosen.Animation, frame);
            }

            // The owning group's map, because paramindex is local to it. An unknown group gets an
            // empty map rather than the base model's, which would silently read the wrong parameter
            // instead of reading none.
            IReadOnlyList<int> map = where.Group >= 0 && where.Group < MasterPose.Count
                ? MasterPose[where.Group]
                : [];

            (int x, float settingX) = grid.Locate(0, PoseParameters, poseValues, map);
            (int y, float settingY) = grid.Locate(1, PoseParameters, poseValues, map);

            (int[] animations, float[] weights) = grid.ThreeWay(x, y, settingX, settingY);

            IReadOnlyList<StudioBonePose> pose = PoseOf(where.Group, animations[0], frame);

            // **On the diagonal the middle corner drops out**, and the remaining two are blended
            // by their share of what is left rather than by weight[2] outright.
            if (weights[1] < 0.001f)
            {
                float share = weights[0] + weights[2];

                return share <= 0f
                    ? pose
                    : StudioPoseBlend.Blend(
                        Bones, pose, PoseOf(where.Group, animations[2], frame),
                        weights[2] / share);
            }

            float pair = weights[0] + weights[1];

            if (pair > 0f)
            {
                pose = StudioPoseBlend.Blend(
                    Bones, pose, PoseOf(where.Group, animations[1], frame), weights[1] / pair);
            }

            return StudioPoseBlend.Blend(
                Bones, pose, PoseOf(where.Group, animations[2], frame), weights[2]);
        }

        /// <summary>How fast a sequence was authored to travel, in units a second.</summary>
        /// <param name="sequence">The merged sequence number.</param>
        /// <param name="poseValues">
        /// A value for each pose parameter, in <see cref="PoseParameters"/> order and normalised,
        /// because they choose which cells of the grid are blended and therefore which animations'
        /// travel is being asked about.
        /// </param>
        /// <returns>The ground speed, or zero when nothing in the blend moves.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="poseValues"/> is null.</exception>
        /// <remarks>
        /// **<c>GetSequenceGroundSpeed</c>** (<c>baseanimating.cpp:1096</c>), which
        /// <c>ComputePoseParam_MoveYaw</c> needs to scale a slow player's blend back towards the
        /// middle of the grid. The blend is resolved the same way <see cref="Skeleton(int, int, IReadOnlyList{float})"/> resolves
        /// it, so the speed reported is that of the animations actually being played rather than of
        /// the sequence's first cell.
        /// </remarks>
        public float GroundSpeed(int sequence, IReadOnlyList<float> poseValues)
        {
            ArgumentNullException.ThrowIfNull(poseValues);

            if (Sequences.At(sequence) is not { } where ||
                where.Group >= Models.Count ||
                where.Group >= Groups.Count ||
                where.Local >= Groups[where.Group].Sequences.Count)
            {
                return 0f;
            }

            StudioSequence chosen = Groups[where.Group].Sequences[where.Local];

            if (chosen.Blend is not { Blends: true } grid || poseValues.Count == 0)
            {
                return StudioMotion.GroundSpeed(Models[where.Group], [(chosen.Animation, 1f)]);
            }

            IReadOnlyList<int> map = where.Group >= 0 && where.Group < MasterPose.Count
                ? MasterPose[where.Group]
                : [];

            (int x, float settingX) = grid.Locate(0, PoseParameters, poseValues, map);
            (int y, float settingY) = grid.Locate(1, PoseParameters, poseValues, map);

            (int[] animations, float[] weights) = grid.ThreeWay(x, y, settingX, settingY);

            List<(int Animation, float Weight)> blend = new(animations.Length);

            for (int corner = 0; corner < animations.Length; corner++)
            {
                blend.Add((animations[corner], weights[corner]));
            }

            return StudioMotion.GroundSpeed(Models[where.Group], blend);
        }

        /// <summary>One animation's pose, renumbered onto the base model's bones.</summary>
        /// <remarks>
        /// **The animation's bones are ITS model's, and must be renumbered.** An animation model
        /// has its own bone list and its own ordering; applying those indices to the base skeleton
        /// moves the wrong joints by the right amounts. Valve remap every animation through
        /// <c>masterBone</c> for exactly this — <c>bone_setup.cpp:966</c>.
        /// </remarks>
        private IReadOnlyList<StudioBonePose> PoseOf(int group, int animation, int frame)
        {
            IReadOnlyList<StudioBone> owner = BonesOf(group);

            IReadOnlyList<StudioBonePose> pose =
                StudioAnimation.Pose(Models[group], owner, animation, frame);

            if (group == 0 || Remaps(group) is not { } remap)
            {
                return pose;
            }

            List<StudioBonePose> renumbered = new(pose.Count);

            foreach (StudioBonePose moved in pose)
            {
                int bone = moved.Bone >= 0 && moved.Bone < remap.Length ? remap[moved.Bone] : -1;

                if (bone >= 0)
                {
                    renumbered.Add(moved with { Bone = bone });
                }
            }

            return renumbered;
        }

        private readonly Dictionary<int, IReadOnlyList<StudioBone>> _bonesByGroup = [];
        private readonly Dictionary<int, int[]> _remapByGroup = [];

        /// <summary>The bones an animation model numbers its own animations against.</summary>
        private IReadOnlyList<StudioBone> BonesOf(int group)
        {
            if (_bonesByGroup.TryGetValue(group, out IReadOnlyList<StudioBone>? cached))
            {
                return cached;
            }

            IReadOnlyList<StudioBone> read = group == 0 || group >= Models.Count
                ? Bones
                : StudioBones.Read(Models[group]);

            _bonesByGroup[group] = read;
            return read;
        }

        /// <summary>How a group's bone numbering maps onto the base model's.</summary>
        private int[]? Remaps(int group)
        {
            if (_remapByGroup.TryGetValue(group, out int[]? cached))
            {
                return cached;
            }

            IReadOnlyList<StudioBone> owner = BonesOf(group);

            if (owner.Count == 0)
            {
                return null;
            }

            int[] built = StudioBones.Remap(owner, Bones);

            // **How much of the skeleton actually matched.** A bone that does not map is dropped,
            // and a dropped bone keeps its REST transform - which for a player is part of the
            // lying-down modelling pose. So a partial remap is a partially standing player, and
            // the count is the difference between "the pose is wrong" and "the pose is missing".
            int matched = 0;

            foreach (int bone in built)
            {
                matched += bone >= 0 ? 1 : 0;
            }

            Props.LogInformation(
                "remap group {Group}: {Matched} of {Built} bones matched the base skeleton's {Bones}",
                group,
                matched,
                built.Length,
                Bones.Count);

            _remapByGroup[group] = built;
            return built;
        }

        /// <summary>The merged sequence number whose label contains a name.</summary>
        /// <param name="name">Part of a sequence label, matched case-insensitively.</param>
        /// <returns>The sequence number, or −1 when this model has no such sequence.</returns>
        /// <remarks>
        /// **By name because that is what a sequence IS to anyone reading a model.** TF2's own
        /// naming is descriptive - run_PRIMARY, AttackStand_PRIMARY, PRIMARY_airwalk_reload_start -
        /// and the numbers differ per class, so a number hardcoded for the scout means something
        /// else on the heavy.
        ///
        /// Answers −1 rather than a fallback: a class that genuinely lacks a sequence should be
        /// visibly missing it, not quietly playing a different one.
        /// </remarks>
        public int Find(string name)
        {
            for (int sequence = 0; sequence < Sequences.Count; sequence++)
            {
                if (Sequences.At(sequence) is not { } where ||
                    where.Group >= Groups.Count ||
                    where.Local >= Groups[where.Group].Sequences.Count)
                {
                    continue;
                }

                // **Exact, which is what the engine does and what this got wrong.**
                // Studio_LookupSequence compares labels with stricmp. Matching on Contains instead
                // takes the first LONGER label that happens to embed the wanted one, and TF2 has
                // several: asking a scout for "Stand_PRIMARY" returned sequence 9,
                // "AttackStand_PRIMARY", while the real "stand_PRIMARY" sits at 175 and was never
                // reached.
                //
                // The consequence was not a slightly wrong animation. An attack sequence is an
                // upper-body layer meant to be added onto a base pose, so playing one on its own as
                // an absolute pose leaves the skeleton near its reference — which for a TF2 player
                // is lying on its back. Every player in the viewer was posed that way: the shape
                // differed from rest, so the animation was demonstrably being applied, and the
                // model still never stood up. Measured, scout: this sequence poses to a Z span of
                // 23 where "stand_PRIMARY" gives 59 and "run_PRIMARY" gives 68.
                if (string.Equals(
                    Groups[where.Group].Sequences[where.Local].Label,
                    name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return sequence;
                }
            }

            return -1;
        }

        /// <summary>The sequence an activity selects, or −1 when the model claims none.</summary>
        /// <param name="activity">The activity's name, as <c>ACT_MP_RUN</c>.</param>
        /// <returns>A merged sequence number, or −1.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="activity"/> is null.</exception>
        /// <remarks>
        /// **This is how the engine finds an animation, and matching sequence LABELS was not.**
        /// `mstudioseqdesc_t` carries both a label and an activity name, and
        /// <c>SelectWeightedSequence</c> works from the activity: the label is a human name for one
        /// sequence, while the activity is the question the game asks. Selecting by label meant
        /// guessing that a class named its running animation <c>run_PRIMARY</c>, which is true for
        /// TF2's player models by convention and is not what makes the engine find it.
        ///
        /// **Weighted, because several sequences answer to one activity.** Valve picks among them in
        /// proportion to <c>actweight</c>, so a model with three idle variants does not always show
        /// the first. This takes the HIGHEST weight rather than sampling at random: a demo has to
        /// replay the same way twice, and a random pick would make a screenshot unreproducible and a
        /// test flaky by design. Recorded as a deliberate divergence rather than parity.
        ///
        /// A weight of zero is never chosen even when the activity name matches, which is Valve's
        /// rule and the reason the comparison is strictly greater than zero.
        /// </remarks>
        public int ForActivity(string activity)
        {
            ArgumentNullException.ThrowIfNull(activity);

            int best = -1;
            int bestWeight = 0;

            for (int sequence = 0; sequence < Sequences.Count; sequence++)
            {
                if (Sequences.At(sequence) is not { } where ||
                    where.Group >= Groups.Count ||
                    where.Local >= Groups[where.Group].Sequences.Count)
                {
                    continue;
                }

                StudioSequence candidate = Groups[where.Group].Sequences[where.Local];

                if (candidate.ActivityWeight <= 0 ||
                    !string.Equals(candidate.Activity, activity, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (candidate.ActivityWeight > bestWeight)
                {
                    bestWeight = candidate.ActivityWeight;
                    best = sequence;
                }
            }

            return best;
        }

        /// <summary>How fast the animation behind a sequence advances, in cycles a second.</summary>
        /// <param name="sequence">The merged sequence number.</param>
        /// <returns>Cycles a second, or zero when it does not animate.</returns>
        /// <remarks>
        /// **A player's cycle is not networked either**, so it is advanced from elapsed time the
        /// same way a health pack's is - the client does it in FrameAdvance and treats a sent
        /// cycle as a correction. Without this every player holds one frame of a real animation,
        /// which looks like a very convincing statue.
        /// </remarks>
        public float CyclesPerSecond(int sequence)
        {
            if (Sequences.At(sequence) is not { } where ||
                where.Group >= Models.Count ||
                where.Local >= Groups[where.Group].Sequences.Count)
            {
                return 0f;
            }

            return StudioAnimation.CyclesPerSecond(
                Models[where.Group], Groups[where.Group].Sequences[where.Local].Animation);
        }

        /// <summary>Whether the sequence loops.</summary>
        /// <param name="sequence">The merged sequence number.</param>
        /// <returns><c>true</c> when it carries <c>STUDIO_LOOPING</c>.</returns>
        public bool Loops(int sequence) =>
            Sequences.At(sequence) is { } where &&
            where.Group < Groups.Count &&
            where.Local < Groups[where.Group].Sequences.Count &&
            Groups[where.Group].Sequences[where.Local].Loops;

        /// <summary>The first merged sequence whose activity contains a fragment.</summary>
        /// <param name="fragment">Part of an activity name, such as <c>VM_IDLE</c>.</param>
        /// <returns>The merged sequence number, or −1 when no sequence claims it.</returns>
        /// <remarks>
        /// **Activities are how the engine asks for an animation, and names are how two sequence
        /// tables can be compared at all.** A demo carries `m_nSequence`, a number, and a number can
        /// only be checked against another number — which is how the viewmodel came to play
        /// `r_handposes`, a one-frame pose holder sitting at merged index 1, while the actual
        /// viewmodel animations start at 2 and carry `ACT_PRIMARY_VM_IDLE` and friends.
        /// </remarks>
        public int SequenceByActivity(string fragment)
        {
            ArgumentNullException.ThrowIfNull(fragment);

            for (int index = 0; index < Sequences.Count; index++)
            {
                if (Sequences.At(index) is not { } at ||
                    at.Group >= Groups.Count ||
                    at.Local >= Groups[at.Group].Sequences.Count)
                {
                    continue;
                }

                if (Groups[at.Group].Sequences[at.Local].Activity
                    .Contains(fragment, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>What the animation behind a sequence uses that this reader does not implement.</summary>
        /// <param name="sequence">The merged sequence number.</param>
        /// <returns>A short note for the log, or empty when it uses neither mechanism.</returns>
        /// <remarks>
        /// Reported rather than assumed. Zero-frame data and local hierarchy both sit between an
        /// animation's bone tracks and the final pose, and a viewer that implements neither is only
        /// wrong for animations that actually carry them — which is a measurement, not a guess.
        /// </remarks>
        public string UnimplementedFor(int sequence)
        {
            if (Sequences.At(sequence) is not { } where ||
                where.Group >= Models.Count ||
                where.Local >= Groups[where.Group].Sequences.Count)
            {
                return string.Empty;
            }

            (int hierarchy, int zeroFrames) = StudioAnimation.Unimplemented(
                Models[where.Group], Groups[where.Group].Sequences[where.Local].Animation);

            return (hierarchy, zeroFrames) switch
            {
                (0, 0) => string.Empty,
                (0, _) => $" ZEROFRAME x{zeroFrames}",
                (_, 0) => $" LOCALHIERARCHY x{hierarchy}",
                _ => $" LOCALHIERARCHY x{hierarchy} ZEROFRAME x{zeroFrames}",
            };
        }

        /// <summary>Whether a merged sequence is a DELTA, meant to be layered rather than played.</summary>
        /// <param name="sequence">The merged sequence number.</param>
        /// <returns>Whether it carries <c>STUDIO_DELTA</c>.</returns>
        /// <remarks>
        /// Reported rather than acted on for now: a delta posed on its own builds a skeleton from
        /// differences with nothing underneath, and the tell is a bone sitting at identity where its
        /// rest rotation carried a real orientation. Knowing whether the viewmodel's sequence is one
        /// separates "we are playing the wrong sequence" from "we are playing it the wrong way".
        /// </remarks>
        public bool IsDelta(int sequence) =>
            Sequences.At(sequence) is { } where &&
            where.Group < Models.Count &&
            where.Local < Groups[where.Group].Sequences.Count &&
            Groups[where.Group].Sequences[where.Local].IsDelta;

        /// <summary>How many frames the animation behind a sequence has.</summary>
        /// <param name="sequence">The merged sequence number.</param>
        /// <returns>The frame count, or one when the sequence does not resolve.</returns>
        public int Frames(int sequence) =>
            Sequences.At(sequence) is { } where &&
            where.Group < Models.Count &&
            where.Local < Groups[where.Group].Sequences.Count
                ? Math.Max(
                    1,
                    StudioAnimation.Frames(
                        Models[where.Group], Groups[where.Group].Sequences[where.Local].Animation))
                : 1;
    }

    /// <summary>A model's baked animation frames, and how to choose between them.</summary>
    /// <param name="Geometry">Every baked frame, animations laid end to end.</param>
    /// <param name="Layout">Where each animation starts in that list, and how long it is.</param>
    /// <param name="SequenceAnimation">Which animation each sequence plays.</param>
    /// <param name="SequenceLoops">Whether each sequence loops, from <c>STUDIO_LOOPING</c>.</param>
    /// <param name="Skinned">Set when this model is posed on the GPU instead of having frames baked.</param>
    /// <param name="Illumination">Where the model wants its light sampled, in model space.</param>
    /// <param name="SkinSwaps">Per extra skin family, how each material of family zero is replaced.</param>
    /// <param name="BodyParts">Each body part's place value and alternative count, for m_nBody.</param>
    /// <param name="Attachments">
    /// The named points other entities hang from, in the model's own order. Indexed ONE-based by
    /// <c>m_iParentAttachment</c>, because the engine stores them that way.
    /// </param>
    /// <param name="BoundsBySequence">
    /// The engine's render bounds per sequence, model space — the authored box unioned with each
    /// sequence's own, as <c>GetRenderBounds</c> builds it.
    /// </param>
    /// <param name="HeaderBounds">
    /// The header's box with no sequence unioned in, for a model with none and for an index
    /// nothing knows about.
    /// </param>
    /// <param name="TwoPass">
    /// Whether the model declares <c>$mostlyopaque</c> — <c>STUDIOHDR_FLAGS_TRANSLUCENT_TWOPASS</c>
    /// — which is the only thing that entitles it to be drawn in both passes.
    /// </param>
    /// <remarks>
    /// **The indirection is the point.** A demo networks a SEQUENCE and a CYCLE; the geometry is
    /// per ANIMATION and per FRAME. Collapsing the two would draw whatever animation happened to
    /// sit at the sequence's number, which is motion that looks deliberate and is wrong.
    /// </remarks>
    public sealed record ModelFrames(
        IReadOnlyList<IReadOnlyList<PropVertex>> Geometry,
        IReadOnlyDictionary<int, (int Start, int Frames, float CyclesPerSecond)> Layout,
        IReadOnlyList<int> SequenceAnimation,
        IReadOnlyList<bool> SequenceLoops,
        SkinnedModel? Skinned = null,
        (float X, float Y, float Z) Illumination = default,
        IReadOnlyList<IReadOnlyDictionary<int, int>>? SkinSwaps = null,
        IReadOnlyList<(int Base, int Count)>? BodyParts = null,

        // **The named points other entities hang from.** A hat merges bones by name; a halo, a
        // canteen, a spellbook and a spy's sapper share no bone name with their wearer and hang
        // from one of these instead. Kept on the WEARER's model, because m_iParentAttachment indexes
        // the parent's table — the spellbook itself declares none at all, while a scout declares 29.
        IReadOnlyList<StudioAttachment>? Attachments = null,

        // **The box the engine would draw this model by, per sequence** — `GetRenderBounds`, which
        // takes the authored clipping box or the movement hull and unions the playing sequence's
        // own. Precomputed here because the `.mdl` bytes are in hand exactly once, at load, and
        // keeping them alive per model to ask again later would cost far more than a box each.
        //
        // Indexed by sequence; <see cref="HeaderBounds"/> is what a sequence outside the list gets.
        IReadOnlyList<StudioBox>? BoundsBySequence = null,

        // The header's own box, with no sequence unioned in — the answer for a model with no
        // sequences and the fallback for a sequence index nothing knows about.
        StudioBox HeaderBounds = default,

        // **Whether the model asked to be drawn twice** — STUDIOHDR_FLAGS_TRANSLUCENT_TWOPASS,
        // authored as `$mostlyopaque`. Carried on the model rather than looked up per frame because
        // it is a fact about the file, and the file is open exactly once.
        //
        // `false` here means "one pass", which is the engine's answer for the overwhelming majority
        // of models — so a model whose flag went unread draws its translucent parts unsorted with
        // its solid ones rather than drawing nothing. Visible, not silent, which is why the default
        // is tolerable; `TwoPassWiringTests` is what proves production sets it.
        bool TwoPass = false)
    {
        /// <summary>The render bounds for one sequence, in model space.</summary>
        /// <param name="sequence">Which sequence is playing.</param>
        /// <returns>The box, falling back to <see cref="HeaderBounds"/>.</returns>
        /// <remarks>
        /// **The fallback is the header's box rather than an empty one.** An empty box would union
        /// to nothing and bucket the model as the smallest, drawing a building last; the header's
        /// box is what the engine uses when a model has no sequence to add.
        /// </remarks>
        public StudioBox RenderBoundsFor(int sequence) =>
            BoundsBySequence is { } boxes && sequence >= 0 && sequence < boxes.Count
                ? boxes[sequence]
                : HeaderBounds;

        /// <summary>Whether this model is posed on the GPU rather than having its frames baked.</summary>
        /// <remarks>
        /// **The bake budget decides this, not a list of paths.** A model whose animations fit the
        /// corner budget is baked, which is cheaper to draw; one whose animations do not is skinned
        /// instead, which is the only thing that works at a player's scale. Deciding by measurement
        /// means a model nobody has classified still takes the right path.
        ///
        /// **A large PROP gains from this rather than losing.** Before, a model over budget kept
        /// however many frames fitted and silently lost the rest — sentry3_heavy has 113 frames
        /// across six animations and was truncated to 43, so most of what it can do never played.
        /// Skinned, it has no frame limit at all. Nothing that used to be baked stops being drawn;
        /// the ones that change are the ones that were being drawn incompletely.
        ///
        /// Static props are untouched either way. They have a single frame, are never over budget,
        /// and name no bone weights to be skinned by.
        /// </remarks>
        public bool IsSkinned => Skinned is not null;

        /// <summary>The geometry one frame after a given slot, wrapping inside its animation.</summary>
        /// <param name="slot">A frame's index in <see cref="Geometry"/>.</param>
        /// <returns>The next frame's geometry, or the same one when it does not animate.</returns>
        /// <remarks>
        /// **Wrapped inside the animation that owns the slot, not across the whole list.** The
        /// frames of several animations lie end to end, so stepping off the end of one would blend
        /// a door's last open frame into a completely different animation's first.
        /// </remarks>
        public IReadOnlyList<PropVertex> NextOf(int slot)
        {
            foreach ((int Start, int Frames, float CyclesPerSecond) where in Layout.Values)
            {
                if (slot < where.Start || slot >= where.Start + where.Frames)
                {
                    continue;
                }

                int intervals = Math.Max(1, where.Frames - 1);
                int offset = slot - where.Start;

                return Geometry[Math.Clamp(
                    where.Start + ((offset + 1) % intervals), 0, Geometry.Count - 1)];
            }

            return Geometry[Math.Clamp(slot, 0, Geometry.Count - 1)];
        }

        /// <summary>Whether this model has anything to animate.</summary>
        public bool IsStill => Geometry.Count <= 1;

        /// <summary>Which baked frame a sequence and cycle land on.</summary>
        /// <param name="sequence">The networked sequence; zero when the demo has not said.</param>
        /// <param name="cycle">How far through it, where one is the end.</param>
        /// <param name="seconds">Demo time, for advancing the cycle as the client does.</param>
        /// <param name="playbackRate">
        /// The entity's <c>m_flPlaybackRate</c> — the third factor in Valve's advance
        /// (<c>c_baseanimating.cpp:5493</c>). One is normal speed.
        /// </param>
        /// <returns>An index into <see cref="Geometry"/>, always in range.</returns>
        /// <remarks>
        /// **An unknown sequence draws the first frame rather than nothing.** A demo can name a
        /// sequence this model does not have - a wrong model resolved for an entity, or a sequence
        /// added in a later game version than the recording - and a prop that vanishes is a worse
        /// answer than one that stands still.
        /// </remarks>
        public int Frame(int sequence, float cycle, double seconds, float playbackRate = 1f) =>
            Select(sequence, cycle, seconds, playbackRate).Frame;

        /// <summary>Which baked frames a sequence and cycle fall between, and how far.</summary>
        /// <param name="sequence">The networked sequence; zero when the demo has not said.</param>
        /// <param name="cycle">How far through it, where one is the end.</param>
        /// <param name="seconds">Demo time, for advancing the cycle as the client does.</param>
        /// <param name="playbackRate">
        /// The entity's <c>m_flPlaybackRate</c> — the third factor in Valve's advance
        /// (<c>c_baseanimating.cpp:5493</c>). One is normal speed.
        /// </param>
        /// <returns>The frame to draw, the one after it, and the blend between them.</returns>
        /// <remarks>
        /// **The fraction is the whole point.** Rounding a cycle to the nearest baked frame steps
        /// the model at the animation's authored rate — ten times a second for a pickup, against a
        /// display running at sixty — which reads as a stutter. Carrying the remainder lets the
        /// shader blend, and the two frames are adjacent ranges of one buffer.
        ///
        /// <c>Next</c> wraps for a looping sequence and holds for a one-shot, matching what
        /// <see cref="StudioSequences.FrameFor(float, int, bool)"/> does with the frame itself.
        /// </remarks>
        public (int Frame, int Next, float Blend) Select(
            int sequence, float cycle, double seconds, float playbackRate)
        {
            if (Geometry.Count == 0)
            {
                return (0, 0, 0f);
            }

            // **A sequence the demo never mentioned is sequence zero, not an error.** A property
            // that never changes from its default is never sent, so an absent m_nSequence means
            // the entity is still on its first sequence - which is why every health pack in the
            // corpus reports -1 and every one of them is animating in game.
            int wanted = sequence < 0 ? 0 : sequence;

            int animation = wanted < SequenceAnimation.Count ? SequenceAnimation[wanted] : -1;

            if (animation < 0 ||
                !Layout.TryGetValue(
                    animation, out (int Start, int Frames, float CyclesPerSecond) where))
            {
                return (0, 0, 0f);
            }

            // **The cycle is advanced here, because the server does not advance it.** The client
            // does it every frame in C_BaseAnimating::FrameAdvance and treats a networked cycle as
            // an occasional correction; replaying only what was sent leaves every prop frozen on
            // frame zero, which is what a health pack looked like.
            // **Valve's formula has three factors and this had two.** c_baseanimating.cpp:5493 is
            // `addcycle = flInterval * cyclerate * m_flPlaybackRate`, and the playback rate was
            // absent here - decoded, retained, and read by nothing - so anything not playing at
            // rate 1 advanced at the wrong speed.
            double advanced = cycle + (seconds * where.CyclesPerSecond * playbackRate);

            bool loops = wanted < SequenceLoops.Count && SequenceLoops[wanted];

            float phase = (float)(advanced - Math.Floor(advanced));

            int frame = StudioSequences.FrameFor(phase, where.Frames, loops);

            // How far past that frame the cycle actually is. The frame count spans one fewer
            // interval than it has frames, which is the same arithmetic the cycle rate uses.
            int intervals = Math.Max(1, where.Frames - 1);
            float exact = phase * intervals;
            float blend = exact - MathF.Floor(exact);

            // **The next frame wraps for a loop and holds for a one-shot.** A door that has
            // finished opening must blend toward the pose it is already in, not back to shut.
            int next = loops
                ? (frame + 1) % intervals
                : Math.Min(frame + 1, where.Frames - 1);

            return (
                Math.Clamp(where.Start + frame, 0, Geometry.Count - 1),
                Math.Clamp(where.Start + next, 0, Geometry.Count - 1),
                Math.Clamp(blend, 0f, 1f));
        }
    }
}
