using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Viewer3D;

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
internal readonly record struct PropVertex(
    float X, float Y, float Z, float U, float V, int MaterialIndex,
    float OriginX = 0f, float OriginY = 0f,
    float Red = 1f, float Green = 1f, float Blue = 1f,
    float NormalX = 0f, float NormalY = 0f, float NormalZ = 1f,
    (byte First, byte Second, byte Third) Bones = default,
    (float First, float Second, float Third) Weights = default,
    int BodyPart = 0,
    int BodyModel = 0);

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
internal static class PropModels
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
    /// <param name="materials">The map's material table, extended in place with the props'.</param>
    /// <param name="textures">The decoded textures, extended in step with the table.</param>
/// <param name="blendTextures">
/// Each material's SECOND texture, kept in step with <paramref name="textures"/>. A material with
/// none contributes null, because the renderer indexes both lists by one number.
/// </param>
    /// <param name="load">Resolves a material to its textures, or returns null.</param>
    /// <returns>Every placed triangle corner, three per triangle.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<PropVertex> Load(
        ReadOnlyMemory<byte> map,
        PakFile pak,
        GameArchives archives,
        List<BspMaterial> materials,
        List<MapTexture?> textures,
        List<MapTexture?> blendTextures,
        Func<string, ResolvedMaterial?> load)
    {
        ArgumentNullException.ThrowIfNull(pak);
        ArgumentNullException.ThrowIfNull(archives);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(load);

        IReadOnlyList<BspStaticProp> placements;

        try
        {
            placements = BspStaticProps.Read(map);
        }
        catch (InvalidDataException failure)
        {
            ViewerLog.Warn("props", "reading the map's static props", failure);
            return [];
        }

        if (placements.Count == 0)
        {
            return [];
        }

        int brushMaterialCount = textures.Count;
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

        for (int index = 0; index < placements.Count; index++)
        {
            BspStaticProp placement = placements[index];

            if (placed >= MaximumPlacements)
            {
                ViewerLog.Warn(
                    "props", $"stopping at {MaximumPlacements} placements; the map declares more");
                break;
            }

            if (!loaded.TryGetValue(placement.Model, out LoadedModel? model))
            {
                model = Read(
                    placement.Model, pak, archives, materials, textures, blendTextures, materialIndices, load);
                loaded[placement.Model] = model;
            }

            if (model is null)
            {
                skipped++;
                continue;
            }

            PropTransform transform = new(placement);

            IReadOnlyList<IReadOnlyList<(byte Red, byte Green, byte Blue)>>? lighting =
                Lighting(pak, placed: index, model.Checksum);

            if (lighting is null)
            {
                unlit++;
            }

            for (int at = 0; at < model.Corners.Count; at++)
            {
                PropVertex corner = model.Corners[at];

                (float x, float y, float z) = transform.Apply(corner.X, corner.Y, corner.Z);

                (float red, float green, float blue) = Colour(
                    lighting, model.Meshes[at], model.Vertices[at]);

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

        for (int index = brushMaterialCount; index < textures.Count; index++)
        {
            if (textures[index] is { IsTransparent: true })
            {
                transparent++;
            }
        }

        ViewerLog.Write(
            "props",
            $"{placed} props placed from {loaded.Count} models ({skipped} skipped, " +
            $"{unlit} without baked lighting), {world.Count / 3} triangles, " +
            $"{transparent} of {textures.Count - brushMaterialCount} prop materials alpha tested");

        return world;
    }

    /// <summary>Reads one placement's baked lighting, or nothing when it has none.</summary>
    /// <remarks>
    /// **Named by the placement's index in the static prop lump**, which is how the compiler wrote
    /// it. A prop with no lighting is normal rather than an error - a map compiled without static
    /// prop lighting has none for any of them - so this reports absence quietly and the caller
    /// counts it.
    /// </remarks>
    private static IReadOnlyList<IReadOnlyList<(byte Red, byte Green, byte Blue)>>? Lighting(
        PakFile pak, int placed, int checksum)
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
                return null;
            }

            if (file is null)
            {
                continue;
            }

            try
            {
                return StudioVertexLighting.Read(file, checksum);
            }
            catch (InvalidDataException failure)
            {
                // Includes the checksum guard: lighting baked against a different build of the
                // model would light the wrong parts of it. Unlit is the honest fallback.
                ViewerLog.Warn("props", $"reading {path}", failure);
                return null;
            }
        }

        return null;
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
        int vertex)
    {
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
    /// <param name="materials">Material table to register this model's materials in.</param>
    /// <param name="textures">Texture list, kept in step with the materials.</param>
/// <param name="blendTextures">
/// Each material's SECOND texture, kept in step with <paramref name="textures"/>. A material with
/// none contributes null, because the renderer indexes both lists by one number.
/// </param>
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
    internal static IReadOnlyList<PropVertex>? LoadOne(
        string path,
        PakFile pak,
        GameArchives archives,
        List<BspMaterial> materials,
        List<MapTexture?> textures,
        List<MapTexture?> blendTextures,
        Func<string, ResolvedMaterial?> load) =>
        Read(path, pak, archives, materials, textures, blendTextures, [], load)?.Corners;

    /// <summary>Reads one model once per frame of its animation.</summary>
    /// <param name="path">The model path, as modelprecache named it.</param>
    /// <param name="pak">The map's embedded files, which override the game's.</param>
    /// <param name="archives">The game's own archives.</param>
    /// <param name="materials">Material table to register this model's materials in.</param>
    /// <param name="textures">Texture list, kept in step with the materials.</param>
/// <param name="blendTextures">
/// Each material's SECOND texture, kept in step with <paramref name="textures"/>. A material with
/// none contributes null, because the renderer indexes both lists by one number.
/// </param>
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
    internal static ModelFrames? LoadFrames(
        string path,
        PakFile pak,
        GameArchives archives,
        List<BspMaterial> materials,
        List<MapTexture?> textures,
        List<MapTexture?> blendTextures,
        Func<string, ResolvedMaterial?> load,
        bool mustSkin = false) =>
        Read(path, pak, archives, materials, textures, blendTextures, [], load, mustSkin)?.Frames;

    /// <summary>Reads one model's geometry, in the model's own coordinates.</summary>
    /// <remarks>
    /// Internal rather than private because networked entities need the same thing: a model loaded
    /// once, in model space, to be posed per instance. A static prop is posed by the map and an
    /// entity is posed by the demo, and only the transform differs.
    /// </remarks>
    internal static LoadedModel? Read(
        string path,
        PakFile pak,
        GameArchives archives,
        List<BspMaterial> materials,
        List<MapTexture?> textures,
        List<MapTexture?> blendTextures,
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
            ViewerLog.Warn("props", $"{path}: one of its three files is missing");
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
                    ViewerLog.Warn("props", $"{path} includes {included}, which was not found");
                    continue;
                }

                groups.Add((groupModels.Count, StudioSequences.Read(animations)));
                groupModels.Add(animations);
            }

            StudioSequenceTable table = StudioSequenceTable.Merge(groups);

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
            bool skin = (mustSkin || wantedFrames > affordable) && bones.Count > 1;

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

            for (int index = 0; index < materialByMesh.Length; index++)
            {
                materialByMesh[index] = Register(
                    model,
                    model.Meshes[index].MaterialIndex,
                    materials,
                    textures,
                    blendTextures,
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
            short[] skinTable = StudioSkins.Read(modelFile);
            int families = StudioSkins.Families(modelFile);
            int references = StudioSkins.References(modelFile);

            List<Dictionary<int, int>> byFamily = [];

            for (int family = 1; family < families; family++)
            {
                Dictionary<int, int> swap = [];

                for (int index = 0; index < materialByMesh.Length; index++)
                {
                    int reference = model.Meshes[index].MaterialIndex;
                    int at = (family * references) + reference;

                    if (reference < 0 || at < 0 || at >= skinTable.Length)
                    {
                        continue;
                    }

                    int swapped = Register(
                        model, skinTable[at], materials, textures, blendTextures, materialIndices, load);

                    if (swapped >= 0 && materialByMesh[index] >= 0)
                    {
                        swap[materialByMesh[index]] = swapped;
                    }
                }

                byFamily.Add(swap);
            }

            if (families > 1)
            {
                ViewerLog.Write(
                    "props",
                    $"skins {path}: {families} families over {references} references, " +
                    $"{string.Join(", ", byFamily.Select(swap => swap.Count + " materials swapped"))}");
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
                    ViewerLog.Write(
                        "props",
                        $"pairing {path}: " + string.Join(
                            ", ",
                            model.Meshes.Select((mesh, at) =>
                                $"[{at}] part {mesh.BodyPart} alt {mesh.BodyModel} " +
                                $"mdl {mesh.VertexCount}v vtx {(at < meshes.Count ? meshes[at].Count : -1)}c " +
                        $"mat {(at < materialByMesh.Length ? materialByMesh[at] : -1)}" +
                        $" '{(at < materialByMesh.Length && materialByMesh[at] >= 0 && materialByMesh[at] < materials.Count ? materials[materialByMesh[at]].Name : "?")}'")));
                }

                for (int index = 0; index < materialByMesh.Length; index++)
                {
                    StudioMesh mesh = model.Meshes[index];

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

                ViewerLog.Write(
                    "props",
                    $"seam {path} anim {animation}: first and last frame differ by {apart:0.####} " +
                    $"units at most ({(apart < 0.01f ? "DUPLICATE, drop it" : "DISTINCT, keep it")})");
            }

            if (baked.Count > 1)
            {
                ViewerLog.Write(
                    "props",
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
                ViewerLog.Write(
                    "props",
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
                            StudioSequences.PoseParameters(modelFile))
                        : null,
                    IlluminationOf(modelFile),
                    byFamily,
                    model.BodyParts));
        }
        catch (InvalidDataException failure)
        {
            // Includes the checksum mismatch, which is the engine's own guard against a model
            // whose three files do not belong together.
            ViewerLog.Warn("props", $"reading {path}", failure);
            return null;
        }
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
    private static int Register(
        StudioModelInfo model,
        int materialIndex,
        List<BspMaterial> materials,
        List<MapTexture?> textures,
        List<MapTexture?> blendTextures,
        Dictionary<string, int> indices,
        Func<string, ResolvedMaterial?> load)
    {
        if (materialIndex < 0 || materialIndex >= model.Materials.Count)
        {
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

            // **The table and the textures grow together**, because the renderer indexes both by
            // the same number. Appending to one without the other silently paints every prop from
            // that point on with the wrong image.
            int index = materials.Count;

            materials.Add(new BspMaterial(candidate, (0.5f, 0.5f, 0.5f), painted.Width, painted.Height));
            textures.Add(painted);

            // **Three lists, not two, and for the same reason the comment above gives.** A material
            // with a second texture is indexed by the same number as its first, so a prop that
            // appends to one list and not the other leaves the renderer reading another material's
            // second texture — or, as it did, nothing at all: the slots were padded with null and
            // every prop fell back to sampling its base twice.
            //
            // That is invisible for a vertex-alpha mix, which is an identity against itself, and
            // very visible for UnLitTwoTexture, which squares it instead of multiplying by the
            // texture the material named. It is why a capture point beam kept its stripes only for
            // BLU, whose base texture IS the stripes.
            blendTextures.Add(texture.Blend);
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
        ViewerLog.Warn(
            "props",
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
    /// <param name="PoseParameters">The model's pose parameters, which its blend grids index.</param>
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
    internal sealed record SkinnedModel(
        IReadOnlyList<StudioBone> Bones,
        IReadOnlyList<byte[]> Models,
        StudioSequenceTable Sequences,
        IReadOnlyList<(int Group, IReadOnlyList<StudioSequence> Sequences)> Groups,
        IReadOnlyList<StudioPoseParameter> PoseParameters)
    {
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
        public StudioSkeleton Skeleton(int sequence, int frame, IReadOnlyList<float> poseValues)
        {
            ArgumentNullException.ThrowIfNull(poseValues);

            if (Sequences.At(sequence) is not { } where ||
                where.Group >= Models.Count ||
                where.Local >= Groups[where.Group].Sequences.Count)
            {
                return StudioBones.RestPose(Bones);
            }

            StudioSequence chosen = Groups[where.Group].Sequences[where.Local];

            if (chosen.Blend is not { Blends: true } grid || poseValues.Count == 0)
            {
                return StudioBones.Posed(Bones, PoseOf(where.Group, chosen.Animation, frame));
            }

            (int x, float settingX) = grid.Locate(0, PoseParameters, poseValues);
            (int y, float settingY) = grid.Locate(1, PoseParameters, poseValues);

            (int[] animations, float[] weights) = grid.ThreeWay(x, y, settingX, settingY);

            IReadOnlyList<StudioBonePose> pose = PoseOf(where.Group, animations[0], frame);

            // **On the diagonal the middle corner drops out**, and the remaining two are blended
            // by their share of what is left rather than by weight[2] outright.
            if (weights[1] < 0.001f)
            {
                float share = weights[0] + weights[2];

                return StudioBones.Posed(
                    Bones,
                    share <= 0f
                        ? pose
                        : StudioPoseBlend.Blend(
                            Bones, pose, PoseOf(where.Group, animations[2], frame),
                            weights[2] / share));
            }

            float pair = weights[0] + weights[1];

            if (pair > 0f)
            {
                pose = StudioPoseBlend.Blend(
                    Bones, pose, PoseOf(where.Group, animations[1], frame), weights[1] / pair);
            }

            pose = StudioPoseBlend.Blend(
                Bones, pose, PoseOf(where.Group, animations[2], frame), weights[2]);

            return StudioBones.Posed(Bones, pose);
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

            ViewerLog.Write(
                "props",
                $"remap group {group}: {matched} of {built.Length} bones matched the base " +
                $"skeleton's {Bones.Count}");

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
    /// <remarks>
    /// **The indirection is the point.** A demo networks a SEQUENCE and a CYCLE; the geometry is
    /// per ANIMATION and per FRAME. Collapsing the two would draw whatever animation happened to
    /// sit at the sequence's number, which is motion that looks deliberate and is wrong.
    /// </remarks>
    internal sealed record ModelFrames(
        IReadOnlyList<IReadOnlyList<PropVertex>> Geometry,
        IReadOnlyDictionary<int, (int Start, int Frames, float CyclesPerSecond)> Layout,
        IReadOnlyList<int> SequenceAnimation,
        IReadOnlyList<bool> SequenceLoops,
        SkinnedModel? Skinned = null,
        (float X, float Y, float Z) Illumination = default,
        IReadOnlyList<IReadOnlyDictionary<int, int>>? SkinSwaps = null,
        IReadOnlyList<(int Base, int Count)>? BodyParts = null)
    {
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
        /// <param name="sequence">The networked sequence, or −1 when the demo has not said.</param>
        /// <param name="cycle">How far through it, where one is the end.</param>
        /// <param name="seconds">Demo time, for advancing the cycle as the client does.</param>
        /// <returns>An index into <see cref="Geometry"/>, always in range.</returns>
        /// <remarks>
        /// **An unknown sequence draws the first frame rather than nothing.** A demo can name a
        /// sequence this model does not have - a wrong model resolved for an entity, or a sequence
        /// added in a later game version than the recording - and a prop that vanishes is a worse
        /// answer than one that stands still.
        /// </remarks>
        public int Frame(int sequence, float cycle, double seconds) =>
            Select(sequence, cycle, seconds).Frame;

        /// <summary>Which baked frames a sequence and cycle fall between, and how far.</summary>
        /// <param name="sequence">The networked sequence, or −1 when the demo has not said.</param>
        /// <param name="cycle">How far through it, where one is the end.</param>
        /// <param name="seconds">Demo time, for advancing the cycle as the client does.</param>
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
        public (int Frame, int Next, float Blend) Select(int sequence, float cycle, double seconds)
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
            double advanced = cycle + (seconds * where.CyclesPerSecond);

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
