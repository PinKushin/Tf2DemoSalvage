using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

public sealed partial class EntityModelSet
{
    /// <summary>Where geometry comes from, set once when a map is read.</summary>
    /// <remarks>
    /// **A global the renderer dereferences, which is Valve's arrangement rather than ours.** The
    /// client reaches model geometry through <c>modelinfo</c> —
    /// <c>virtual studiohdr_t *GetStudiomodel( const model_t *mod )</c>,
    /// <c>src/public/engine/IVModelInfo.h:146</c> — an interface pointer set at init, not a
    /// parameter threaded through every call. Passing the source per call was our invention, and it
    /// is what kept <c>MainForm.ModelGeometry</c> alive: a five-line dictionary lookup that existed
    /// only because three call sites in the window had to hand it over (B188, D90).
    ///
    /// Answers nothing until a map sets it, so the frames drawn before one is open take the same
    /// path as any other rather than a null check at each call site.
    /// </remarks>
    public Func<string, PropModels.ModelFrames?> Geometry { get; set; } = NoGeometry;

    /// <summary>Valve's colour for a brush entity's class, by model path (B219).</summary>
    /// <remarks>
    /// **Set by a map load beside <see cref="Geometry"/>, and for the same reason** — it is content,
    /// so it arrives with the map rather than being reached for through a reference the scene would
    /// otherwise have to hold. Answers null until one is open, which is what a map with no FGDs and
    /// a frame drawn before any map both mean.
    /// </remarks>
    public Func<string, (float Red, float Green, float Blue)?> EntityTint { get; set; } = NoTint;

    /// <summary>The resting tint lookup: nothing is a brush entity until a map says so.</summary>
    public static Func<string, (float Red, float Green, float Blue)?> NoTint { get; } = _ => null;

    /// <summary>The source a viewer with no map open reads from, which has nothing in it.</summary>
    public static Func<string, PropModels.ModelFrames?> NoGeometry { get; } = _ => null;

    /// <summary>Forgets everything the level put here — models, packed vertices, entity state.</summary>
    /// <remarks>
    /// **A model path is map-scoped, and this set outlives the map** (the outside audit's finding
    /// 1). Two facts make the caches wrong to keep: an inline brush model is <c>*N</c>, a run of
    /// faces in one particular BSP, so the same name on the next map is different geometry; and
    /// the loader consults the map's own pak before the archives, so any stock path can be a
    /// per-map override — including "this path is missing", which
    /// <see cref="Add(IReadOnlyList{SceneProp}, Func{string, PropModels.ModelFrames?})"/>
    /// deliberately caches so the loader is not re-asked every frame. Correct within a level, and
    /// a permanent lie across one.
    ///
    /// **The engine's shape**: the world and its brush models are freed with the map, and level
    /// transition unloads unreferenced models — at this viewer's shutdown, everything is
    /// unreferenced. The next load repacks what its demo names (`DemoModels.Precache`), which puts
    /// the cost on the load screen where the engine pays it too.
    ///
    /// **Entity state goes with the level as well**, not just path-keyed geometry: entity indices
    /// are per-demo, and Valve destroys every entity at level shutdown. A surviving
    /// animation-cycle or placement entry for entity 545 would greet the next demo's entity 545 as
    /// though it had been here all along.
    /// </remarks>
    public void LevelShutdown()
    {
        _vertices.Clear();
        VertexBase = 0;
        _releaseOnUpload = false;
        _byModel.Clear();
        _frames.Clear();

        // A path's frames are written once a level (`Add` skips a path it has), so the level is what can change them, and
        // nothing filed by identity outlives it (finding 82).
        _filedLast.Clear();
        _modelsGeneration++;

        // The static props are packed and filed again from the next level's set.
        _staticsPacked = false;
        UnfileStaticProps();
        _resolved.Clear();
        _resolvedLast.Clear();
        _restingFace.Clear();
        _faces.Clear();
        _delays.Clear();
        _lastFace.Clear();
        _swaps.Clear();
        _raw.Clear();
        _packedRebuilt.Clear();
        _packedOrder.Clear();
        _bakedByEntity.Clear();

        _entities.Clear();
        _pendingBlends.Clear();
        _entityModels.Clear();
        _scenes.Clear();
        _filedBoxes.Clear();
        _placements.Clear();
        _lightPoints.Clear();
        _drawnPlacements.Clear();
        _parentPlacements.Clear();
        _propsByEntity.Clear();
        _skinning.Clear();
        _inView.Clear();
        _eventStates.Clear();
        _clientCycles.Clear();
        _mainSequences.Clear();
        _restartCycles.Clear();
        _fired.Clear();

        // Log dedup is per-level too: the next map's missing frames and poses deserve their own
        // report, and a brush height cached for entity N is the OLD map's brush.
        _reportedFrames.Clear();
        _reportedPoses.Clear();
        _posedSize.Clear();
        _reports.LevelShutdown();
    }

    /// <summary>Packs whatever a moment needs that is not packed already.</summary>
    /// <param name="props">What exists at this tick, from the timeline.</param>
    /// <returns>Whether anything was added, so the caller knows to re-upload.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="props"/> is null.</exception>
    /// <remarks>
    /// Reads through <see cref="Geometry"/>, which a map load sets. **This is the production
    /// call** — the overload taking an explicit source exists for tests, which need a different
    /// loader per case and are the reason the seam is worth keeping.
    /// </remarks>
    public bool Add(IReadOnlyList<SceneProp> props) => Add(props, Geometry);

    /// <summary>Packs a moment's models, reading through a source given here.</summary>
    /// <param name="props">What exists at this tick, from the timeline.</param>
    /// <param name="load">Reads a model in its own coordinates, or answers null.</param>
    /// <returns>Whether anything was added, so the caller knows to re-upload.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **A <c>*N</c> reference is an inline BSP submodel**, so its geometry comes from the map
    /// rather than from a <c>.mdl</c> — but it arrives through the same loader as a model like
    /// any other, which is why one packing path serves both. A sprite is a camera-facing quad
    /// with no geometry at all, and handing one to a model loader draws nothing and reports
    /// nothing.
    ///
    /// A model that fails to load is remembered as empty rather than retried every frame — the
    /// loader reports it once, and asking again sixty times a second would bury the log in the
    /// same line.
    /// </remarks>
    public bool Add(IReadOnlyList<SceneProp> props, Func<string, PropModels.ModelFrames?> load)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(load);

        long packAt = System.Diagnostics.Stopwatch.GetTimestamp();
        int before = _byModel.Count;

        bool added = PackModels(props, load);

        // **The static props once a level, after the moment's own** — the order they were packed in when they rode the
        // draw list behind the entities every frame (finding 83 item 1).
        bool staticsNow = !_staticsPacked;

        if (staticsNow)
        {
            _staticsPacked = true;
            added |= PackModels(_staticProps, load);
        }

        if (added)
        {
            ModelsPacked += _byModel.Count - before;
            PackTicks += System.Diagnostics.Stopwatch.GetTimestamp() - packAt;
        }

        // Their lighting with them, as the level loads (B607), not on each one's first draw — on its own clock, after the
        // pack's: it holds the colour meshes, static states and styled lists, so `BakedBuildTicks` and the static states'
        // ticks nest inside it.
        if (staticsNow)
        {
            TimedPrecache();
        }

        return added;
    }

    /// <summary><see cref="PrecacheStaticLighting"/> on its own clock, <see cref="PrecacheTicks"/>.</summary>
    private void TimedPrecache()
    {
        long precacheAt = System.Diagnostics.Stopwatch.GetTimestamp();
        PrecacheStaticLighting();
        PrecacheTicks += System.Diagnostics.Stopwatch.GetTimestamp() - precacheAt;
    }

    /// <summary><see cref="Add(IReadOnlyList{SceneProp}, Func{string, PropModels.ModelFrames?})"/>'s body, for one list.</summary>
    private bool PackModels(IReadOnlyList<SceneProp> props, Func<string, PropModels.ModelFrames?> load)
    {
        bool added = false;
        List<(SceneProp Prop, PropModels.ModelFrames Model, List<List<WorldBatch>> Frames)> loaded = [];
        int reserve = 0;

        foreach (SceneProp prop in props)
        {
            // **Brush entities pack like studio models, because by here they are models.** `*12`
            // resolves through the same loader to geometry the map built, so the only thing that
            // ever made this test about `.mdl` files was that nothing else had geometry yet.
            // Sprites still have none, and Unknown means the model reference was never resolved.
            // **A path too, because a Studio prop can reach here without one.** A weapon whose model
            // the wire never carried is named from its item by `WeaponPropModels`, and that lookup
            // answers null when the game is not installed — which is every CI run. Passing the empty
            // string to `load` throws out of `PakFile.ReadFile`.
            if (!CanDraw(prop) || _byModel.ContainsKey(prop.ModelPath))
            {
                continue;
            }

            List<List<WorldBatch>> frames = [];

            _byModel[prop.ModelPath] = frames;
            added = true;
            Grown = true;

            // Stryker disable once : a mutant that empties the guard body leaves 'model'
            // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
            if (load(prop.ModelPath) is not { Geometry.Count: > 0 } model)
            {
                continue;
            }

            _frames[prop.ModelPath] = model;
                _raw[prop.ModelPath] = model.Geometry[0];

                if (model.SkinSwaps is { Count: > 0 } families)
                {
                    _swaps[prop.ModelPath] = families;
                }

            loaded.Add((prop, model, frames));
            reserve += model.Geometry.Sum(frame => frame.Count);
        }

        // **Reserved once for every model this call packs, then filled** (finding 80). Grown model by model, the list
        // doubled its way up, and the arrays it outgrew were 1.45 GB of large-object garbage over f12's precache.
        // Exact, not `EnsureCapacity`: doubling measured worse on f12 (150 against 104 MB per 20 s), because several
        // precache calls each grow a list that is already large.
        if (_vertices.Capacity < _vertices.Count + reserve)
        {
            _vertices.Capacity = _vertices.Count + reserve;
        }

        foreach ((SceneProp prop, PropModels.ModelFrames model, List<List<WorldBatch>> frames) in loaded)
        {
            List<int> order = [];

            for (int slot = 0; slot < model.Geometry.Count; slot++)
            {
                IReadOnlyList<PropVertex> corners = model.Geometry[slot];

                // **The frame this one blends toward, packed into the same vertex.** Both poses
                // reach the shader without a second buffer or a fetch, and a model with one frame
                // carries itself in both and blends to itself.
                IReadOnlyList<PropVertex> onward = model.NextOf(slot);

                List<WorldBatch> batches = [];
                frames.Add(batches);


                // Grouped by material so one bind covers every triangle of this frame that shares
                // it. Every frame carries the same corners in the same order, so the batching is
                // identical between them and only the positions differ.
                // **Keyed by the body part and alternative as well as the material**, because a
                // batch that spanned two alternatives could not be skipped for one of them. A
                // capture point's three signs share a material; merged on material alone they
                // become one run and no per-entity choice can separate them again.
                //
                // **And by the skinref, for the same reason at draw time** (B229). Two meshes can
                // share family zero's material and differ in another family, so a run merged
                // across skinrefs has no single answer to "what paints this at skin 1".
                //
                // **The grouping holds corner indices, not packed vertices** (finding 80): a list of
                // packed vertices per group, grown by doubling and then copied into `_vertices`, was
                // 2.8 GB of large-object garbage over f12's precache. The indices are also which
                // corner each grouped vertex came from (B426).
                Dictionary<(int Material, int Slot, int Part, int Model), List<int>> byMaterial = [];

                for (int index = 0; index < corners.Count; index++)
                {
                    PropVertex corner = corners[index];

                    (int Material, int Slot, int Part, int Model) key = (
                        corner.MaterialIndex,
                        corner.MaterialSlot,
                        corner.BodyPart,
                        corner.BodyModel);

                    if (!byMaterial.TryGetValue(key, out List<int>? into))
                    {
                        into = [];
                        byMaterial[key] = into;
                    }

                    into.Add(index);
                }

                foreach (KeyValuePair<(int Material, int Slot, int Part, int Model), List<int>> group in byMaterial)
                {
                    batches.Add(new WorldBatch(
                        group.Key.Material,
                        VertexBase + _vertices.Count,
                        group.Value.Count,
                        group.Key.Part,
                        group.Key.Model,
                        MaterialSlot: group.Key.Slot));

                    foreach (int index in group.Value)
                    {
                        PropVertex corner = corners[index];
                        PropVertex ahead = index < onward.Count ? onward[index] : corner;

                        // **Model space, untouched.** The shader's model matrix places it.
                        //
                        // **The lightmap coordinates are zero for a studio model and real for a brush
                        // entity (B131).** A `.mdl` is lit by its own vertex colours in the engine too,
                        // and (0, 0) is the atlas's reserved white texel, so the lightmap term is an
                        // identity rather than darkness. A door is the opposite case: vrad lights every
                        // model's faces (vrad.cpp:703) and its samples are already in the same atlas as
                        // the wall's, so passing them is all that separates a lightmapped door from a
                        // flat one. BrushModels fills them; PropModels leaves them at zero.
                        _vertices.Add(Packed(corner, ahead));
                    }

                    order.AddRange(group.Value);
                }

                // **Whether the alternatives survived packing, said once per model.** A model whose
                // batches all carry alternative zero cannot be varied per entity however faithfully
                // m_nBody is decoded, and the picture is then identical to a body number that never
                // arrived — which is exactly the state this was in.
                if (slot == 0 && model.BodyParts is { Count: > 0 } &&
                    _reportedFrames.Add(prop.ModelPath + "#body"))
                {
                    int alternatives = 0;

                    foreach ((int _, int _, int _, int alternative) in byMaterial.Keys)
                    {
                        alternatives = Math.Max(alternatives, alternative + 1);
                    }

                    // Debug: fires from the draw path as models are first seen, and every line is a
                    // disk flush (B191).
                    _render.LogDebug(
                        "{Message}",
                        $"bodygroups {prop.ModelPath}: {model.BodyParts.Count} parts, " +
                        $"{batches.Count} batches spanning {alternatives} alternatives");
                }


            }

            _packedOrder[prop.ModelPath] = [.. order];

            // **A model's own bounding box, logged for every model.** Whether a model stands up is
            // not answerable from an overhead camera - a squat prop looks the same lying down, so
            // the whole prop set can be tipped and read as correct. A humanoid is the first model
            // tall enough to show it.
            //
            // In Source's model space a player is about 83 units tall and far narrower, so an
            // upright model has Z much the largest extent. If Z is the smallest, the model is on
            // its side and the fault is in the transform rather than in any missing animation.
            float minimumX = float.MaxValue, minimumY = float.MaxValue, minimumZ = float.MaxValue;
            float maximumX = float.MinValue, maximumY = float.MinValue, maximumZ = float.MinValue;

            foreach (PropVertex corner in model.Geometry[0])
            {
                minimumX = MathF.Min(minimumX, corner.X);
                minimumY = MathF.Min(minimumY, corner.Y);
                minimumZ = MathF.Min(minimumZ, corner.Z);
                maximumX = MathF.Max(maximumX, corner.X);
                maximumY = MathF.Max(maximumY, corner.Y);
                maximumZ = MathF.Max(maximumZ, corner.Z);
            }

            float spanX = maximumX - minimumX;
            float spanY = maximumY - minimumY;
            float spanZ = maximumZ - minimumZ;

            // **Say which pose this measures, or the number lies.** A baked model's geometry is
            // posed already, so "on its side" means something is wrong. A skinned model's is
            // stored unposed and is SUPPOSED to be lying along Y - the shader stands it up - so
            // the same warning about the same numbers would be false.
            //
            // This is the overlogging failure in miniature: a line that measured the right thing
            // for one kind of model and kept its wording when a second kind arrived.
            // Debug, like every other line this loop writes: it fires as models are first drawn,
            // which is a stream through a match rather than a burst at load, and each is a flush.
            _props.LogDebug(
                "{Message}",
                model.IsSkinned
                    ? $"extents {prop.ModelPath}: x {spanX:0.#} y {spanY:0.#} z {spanZ:0.#} " +
                      $"UNPOSED, skinned on the GPU - the shader poses it, so these are the " +
                      $"artist's coordinates rather than how it is drawn"
                    : $"extents {prop.ModelPath}: x {spanX:0.#} y {spanY:0.#} z {spanZ:0.#} " +
                      $"(z from {minimumZ:0.#} to {maximumZ:0.#}), " +
                      $"tallest axis {Tallest(spanX, spanY, spanZ)}, {frames.Count} baked frames");
        }

        // A model the set did not have may be one an object already resolved as missing (finding 82).
        if (added)
        {
            _modelsGeneration++;

            // **Nothing filed by identity outlives a model it was filed without**: the engine's `SetModelPointer` calls
            // `UpdateVisibility`, whose `AddToLeafSystem` raises `RenderableChanged` for an existing handle
            // (`c_baseentity.cpp:1790`, `:3950`), and `PreRender` re-files it. Every object takes the keyed path once, and that
            // path re-files exactly the ones whose frames changed (finding 82).
            _filedLast.Clear();
        }

        return added;
    }

    /// <summary>Packs a whole set of model paths ahead of playback.</summary>
    /// <param name="paths">Every model the demo will ever show.</param>
    /// <returns>Whether anything was added.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="paths"/> is null.</exception>
    /// <remarks>
    /// **The engine's own timing, and it is a refusal rather than a preference** (D86, D87). Source
    /// treats loading geometry during play as a programming error, and the reason is visible here —
    /// an MDL read lands on the thread that draws.
    ///
    /// **The synthetic props are the trick worth naming.** The packer takes props rather than paths
    /// because that is what a moment supplies, and only the path and the kind are ever read from one
    /// — the rest of a <see cref="SceneProp"/> describes where an entity STANDS, which is not a
    /// question about geometry. So a path becomes a prop at the origin purely to reach the packer,
    /// and that construction belongs beside the packer rather than in whatever called it.
    ///
    /// **Which paths to pass is B195**: this set and the asset loader's disagree today.
    /// </remarks>
    public bool Precache(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        List<SceneProp> synthetic = [];

        foreach (string path in paths)
        {
            // **An EMPTY entry is dropped so this can pack it, and that is what makes a precache
            // deliberate** (B363). `Add` remembers a failed load as an empty entry and skips the
            // path for ever after, which is right for the per-frame path — the loader must not be
            // re-asked sixty times a second for a model that is not there. But it is wrong for a
            // considered request: the map's detail models were packed empty by the DEMO's precache,
            // before the map's geometry loader existed, and every later call was skipped. The
            // geometry then loaded correctly and reached nothing, and the only symptom was the
            // renderer's *"posed before its geometry was uploaded"*.
            //
            // **Only the empty ones.** A path that packed real geometry is left alone, so this
            // cannot throw away work or re-read a file that succeeded.
            if (_byModel.TryGetValue(path, out List<List<WorldBatch>>? packed) && packed.Count == 0)
            {
                _byModel.Remove(path);
            }

            synthetic.Add(new SceneProp(0, path, ScenePropTrack.Classify(path), default));
        }

        return Add(synthetic);
    }

    /// <summary>Every animation event of every sequence of every model packed, for the precache of what they name.</summary>
    /// <returns>The events, model by model, through each model's merged sequence table.</returns>
    /// <remarks>
    /// **What `CModelSoundsCache::BuildAnimationEventSoundList` walks** (`game/shared/ModelSoundsCache.cpp:131-218`), which
    /// the server's `PrecacheModelComponents` reaches for every model it precaches (`game/server/baseentity.cpp:5250-5412`):
    /// so a viewmodel's draw and reload sounds are ready before the first frame plays them (finding 87). An interim stand-in
    /// for the client's route, the demo's soundprecache table (B568). Read on the thread that packed, after packing.
    /// </remarks>
    public IEnumerable<StudioEvent> AnimationEvents() =>
        _frames.Values
            .Select(static frames => frames.Skinned)
            .OfType<PropModels.SkinnedModel>()
            .SelectMany(static skinned => Enumerable.Range(0, skinned.Sequences.Count).SelectMany(skinned.Events));

    /// <summary>One corner as the packed buffer holds it: model space, blending toward <paramref name="ahead"/>.</summary>
    /// <remarks>
    /// **The lightmap coordinates are zero for a studio model and real for a brush entity (B131).** A `.mdl` is lit by its
    /// own vertex colours in the engine too, and (0, 0) is the atlas's reserved white texel.
    ///
    /// **Without the bones and weights the shader skins by nothing.** A skinned model's geometry is uploaded unposed, so
    /// the bones are the only thing that stands it up; a vertex with no weights stays where the artist modelled it, which
    /// for a player is lying along Y.
    /// </remarks>
    private static WorldVertex Packed(PropVertex corner, PropVertex ahead) =>
        new(
            corner.X, corner.Y, corner.Z, corner.U, corner.V,
            corner.LightU, corner.LightV, 0f,
            LightStep: corner.LightStep,
            NormalX: corner.NormalX,
            NormalY: corner.NormalY,
            NormalZ: corner.NormalZ,
            NextX: ahead.X,
            NextY: ahead.Y,
            NextZ: ahead.Z,
            NextNormalX: ahead.NormalX,
            NextNormalY: ahead.NormalY,
            NextNormalZ: ahead.NormalZ,
            BoneA: corner.Bones.First,
            BoneB: corner.Bones.Second,
            BoneC: corner.Bones.Third,
            WeightA: corner.Weights.First,
            WeightB: corner.Weights.Second,
            WeightC: corner.Weights.Third,
            TangentX: corner.TangentX,
            TangentY: corner.TangentY,
            TangentZ: corner.TangentZ,
            TangentW: corner.TangentW);

    /// <summary>A model's first frame as it was packed, rebuilt from its corners and packing order (finding 80).</summary>
    /// <param name="modelPath">The model.</param>
    /// <returns>The vertices, in buffer order, or null for a model not packed.</returns>
    /// <remarks>
    /// **For a decal on a skinned model, which projects onto the model's own mesh** (`CStudioRender::AddDecal`, B415). The
    /// renderer kept a copy of every skinned model's packed vertices for that one reader: 623 MB on f12. The set already
    /// keeps the corners (<see cref="_raw"/>) and the order they were packed in, so the rare decal rebuilds the list.
    /// </remarks>
    public IReadOnlyList<WorldVertex>? PackedVertices(string modelPath)
    {
        ArgumentNullException.ThrowIfNull(modelPath);

        // Once per model struck, kept until the level lets its models go: a rebuild per decal was ~40 MB of large-object
        // garbage per 20 s of f12 (finding 80).
        if (_packedRebuilt.TryGetValue(modelPath, out WorldVertex[]? rebuilt))
        {
            return rebuilt;
        }

        if (!_raw.TryGetValue(modelPath, out IReadOnlyList<PropVertex>? corners) ||
            !_packedOrder.TryGetValue(modelPath, out int[]? order) ||
            !_frames.TryGetValue(modelPath, out PropModels.ModelFrames? model))
        {
            return null;
        }

        IReadOnlyList<PropVertex> onward = model.NextOf(0);
        WorldVertex[] packed = new WorldVertex[Math.Min(corners.Count, order.Length)];

        for (int at = 0; at < packed.Length; at++)
        {
            int corner = order[at];
            packed[at] = Packed(corners[corner], corner < onward.Count ? onward[corner] : corners[corner]);
        }

        _packedRebuilt[modelPath] = packed;
        return packed;
    }

    /// <summary>The packed vertices rebuilt for models a decal has struck, by path (finding 80).</summary>
    private readonly Dictionary<string, WorldVertex[]> _packedRebuilt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the set has grown since the last upload, however it grew.</summary>
    /// <remarks>
    /// **Because "did it grow" was only ever asked of the CALL, and packing does not always happen
    /// in a call the uploader makes** (B363). `MomentScene` uploads when its own `Add` returns true;
    /// a map that packs its detail models at the level boundary grows the set outside that call
    /// entirely, so the geometry sat in this object and never reached the device — and the symptom
    /// was the renderer's own *"was posed before its geometry was uploaded"*, once per instance, on
    /// a model that had packed perfectly.
    ///
    /// **Cleared by <see cref="Uploaded"/> rather than by the getter**, so a reader cannot consume
    /// it by accident. It is the same shape as `HasModels` on the other side of the seam: a fact
    /// about this object, asked rather than remembered by anybody else.
    /// </remarks>
    public bool Grown { get; private set; }

    /// <summary>Notes that the current contents have reached the device.</summary>
    public void Uploaded()
    {
        Grown = false;

        if (_releaseOnUpload)
        {
            DropVertices();
        }
    }

    /// <summary>Every packed model's triangles, in model space.</summary>
    /// <remarks>
    /// Uploaded once. The vertices never move again — that is the whole point of the arrangement.
    /// </remarks>
    public IReadOnlyList<WorldVertex> Vertices => _vertices;

    /// <summary>The global offset of <see cref="Vertices"/>' first element — how many were let go after upload (B407).</summary>
    /// <remarks>A batch's <c>FirstVertex</c> is global; its vertices are at <c>FirstVertex - VertexBase</c> in <see cref="Vertices"/>.</remarks>
    public int VertexBase { get; private set; }

    /// <summary>Whether an upload lets go of the vertices it sent — armed once the map's world is on the device.</summary>
    private bool _releaseOnUpload;

    /// <summary>Lets go of every vertex the device holds, now and after each later upload (B407).</summary>
    /// <remarks>
    /// **655 MB on f12, read only to slice each model once for its GPU buffer.** Armed by the window once the map's own copies
    /// are released too, because a failed world upload clears the renderer and its model buffers together, and the retry then
    /// reads the map again — whose level shutdown resets this set. Before that nothing is dropped, so a failed first upload can
    /// still retry from here. Offsets stay global, so batches already recorded keep meaning what they meant.
    /// </remarks>
    public void ReleaseUploaded()
    {
        _releaseOnUpload = true;

        if (!Grown)
        {
            DropVertices();
        }
    }

    private void DropVertices()
    {
        VertexBase += _vertices.Count;
        _vertices.Clear();
        _vertices.TrimExcess();
    }

    /// <summary>Bytes the vertex list's backing array holds, filled or not — for the memory report (B407).</summary>
    public long VertexBytes => (long)_vertices.Capacity * System.Runtime.CompilerServices.Unsafe.SizeOf<WorldVertex>();

    /// <summary>How many distinct models have been packed.</summary>
    public int Count => _byModel.Count;

    /// <summary>Every packed model's path.</summary>
    public IEnumerable<string> Paths => _byModel.Keys;

    /// <summary>The batches for one model, or empty when it is not packed.</summary>
    /// <param name="modelPath">The model's path.</param>
    /// <returns>One run per material, indexing into <see cref="Vertices"/>.</returns>
    public IReadOnlyList<WorldBatch> Batches(string modelPath) => Batches(modelPath, 0);

    /// <summary>The batches for one model at one baked frame.</summary>
    /// <param name="modelPath">The model's path.</param>
    /// <param name="frame">Which baked frame; clamped into what was packed.</param>
    /// <returns>One run per material, indexing into <see cref="Vertices"/>.</returns>
    /// <remarks>
    /// **Clamped rather than refused.** A demo can name a sequence whose frame count differs from
    /// the model on this machine — a later game version, or a model replaced by a mod — and
    /// holding the last frame is a better answer than drawing nothing.
    /// </remarks>
    public IReadOnlyList<WorldBatch> Batches(string modelPath, int frame)
    {
        if (!_byModel.TryGetValue(modelPath, out List<List<WorldBatch>>? frames) ||
            frames.Count == 0)
        {
            return [];
        }

        return frames[Math.Clamp(frame, 0, frames.Count - 1)];
    }
}
