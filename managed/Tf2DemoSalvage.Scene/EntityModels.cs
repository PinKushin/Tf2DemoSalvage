using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>One animation event an entity's cycle crossed this frame.</summary>
/// <param name="EntityIndex">Whose animation fired it.</param>
/// <param name="Event">The event, as the model declares it.</param>
/// <param name="Origin">Where that entity stands, which is where the engine plays it from.</param>
/// <remarks>
/// **<c>FireEvent( GetAbsOrigin(), GetAbsAngles(), event, options )</c>** — the engine hands the
/// handler the entity's place along with the event, and for <c>AE_CL_PLAYSOUND</c> that is the
/// position the sound is emitted at when the model has no attachments
/// (<c>c_baseanimating.cpp:3988</c>).
/// </remarks>
public readonly record struct FiredAnimationEvent(
    int EntityIndex,
    StudioEvent Event,
    (float X, float Y, float Z) Origin);

/// <summary>One model to draw, where it stands, and the light reaching it.</summary>
/// <param name="ModelPath">Which packed model to draw.</param>
/// <param name="Matrix">Sixteen floats, row major, for the shader's model constant.</param>
/// <param name="Light">The ambient cube of the leaf it stands in.</param>
/// <param name="Sun">The sun, when this model traced to sky; null when it stands in shade.</param>
/// <param name="Frame">Which baked animation frame to draw, from the demo's sequence and cycle.</param>
/// <param name="Blend">How far toward the next baked frame, so the shader can smooth between them.</param>
/// <param name="Bones">Bone matrices for a model skinned on the GPU, or null when it is baked.</param>
/// <param name="SkinSwap">Which material replaces which for its team, or null.</param>
/// <param name="BodyParts">The model's body parts, for reading its body number.</param>
/// <param name="Body">Which alternative each body part shows, as m_nBody packs it.</param>
/// <param name="MaterialOverride">
/// One VMT path replacing EVERY material the model has, or null for the ordinary case (B325) —
/// the engine's <c>ForcedMaterialOverride</c>, which is how a gold or iced corpse is drawn. Not a
/// <paramref name="SkinSwap"/>: a skin picks another entry from the model's own table, and this
/// ignores the table.
/// </param>
/// <param name="Paint">
/// The colour this item is painted, or null for an unpainted one (B330). Feeds TF2's
/// <c>ItemTintColor</c> proxy, which is per ENTITY — two players in the same hat and different
/// paints share a material and must draw different colours, so this cannot live on the material.
/// </param>
/// <param name="Mirrored">
/// Whether this is a viewmodel, drawn mirrored — which reverses its winding, so the cull has to
/// flip with it or the weapon draws inside out.
/// </param>
/// <param name="Origin">
/// Where the model stands, for choosing which of the map's cubemaps it reflects. Null falls back to
/// the translation of <paramref name="Matrix"/>, which is right for a baked model and reads as the
/// map origin for a skinned one, whose placement is carried by its bones (B170).
/// </param>
/// <param name="Tint">
/// Valve's colour for a brush entity's class, applied in the category view only (B219, B156).
/// </param>
/// <param name="Locals">
/// The direct lights near this model, at most four, which the ambient cube no longer carries
/// (B170). Empty rather than null where none reach it.
/// </param>
/// <param name="WorldBounds">
/// The box the engine would cull and bucket this model by, already in world space —
/// <c>CalcRenderableWorldSpaceAABB</c>. Placed here rather than by the renderer because only this
/// side knows what places a given model.
/// </param>
/// <param name="TwoPass">
/// Whether the model declares <c>$mostlyopaque</c>, so the engine would draw its solid parts in the
/// opaque pass and its blended parts in the translucent one.
/// </param>
/// <param name="Alpha">
/// <c>GetFxBlend()</c> for this frame, nought to 255 — what <c>C_BaseEntity::ComputeFxBlend</c>
/// produced from the entity's <c>m_clrRender</c>, <c>m_nRenderFX</c> and <c>m_nRenderMode</c>
/// (B221). 255 means fully opaque and is the default, so an instance built by hand behaves as it
/// did before this existed.
/// </param>
/// <param name="RenderMode">
/// <c>m_nRenderMode</c>. Anything but <c>kRenderNormal</c> makes the entity transparent, and
/// <c>kRenderEnvironmental</c> makes it undrawn — <c>RenderGroups.For</c>.
/// </param>
public readonly record struct ModelInstance(
    string ModelPath,
    float[] Matrix,

    // **Null means "lightmapped", not "unlit" (B131).** A studio model carries a cube sampled at
    // its illumination point; a brush entity carries lightmap coordinates on its vertices instead,
    // and the shader's cube branch would overwrite them. The renderer reads null as "no cube was
    // supplied" and leaves the atlas sample standing, which is what LightmappedGeneric does.
    AmbientCube? Light,
    SunLight? Sun,
    int Frame = 0,
    float Blend = 0f,
    IReadOnlyList<float[]>? Bones = null,
    IReadOnlyDictionary<int, int>? SkinSwap = null,
    IReadOnlyList<(int Base, int Count)>? BodyParts = null,
    int Body = 0,

    // **One material replacing every one the model has, by VMT path** (B325) — the engine's
    // `ForcedMaterialOverride`, which a gold or iced corpse draws with. Null for everything else.
    // Not a `SkinSwap`: that picks another entry from the model's OWN table, and this ignores it.
    string? MaterialOverride = null,

    // **The colour this item is PAINTED** (B330), feeding TF2's `ItemTintColor` proxy at the bind.
    // Per entity rather than per material, which is what a proxy is: two players wearing the same
    // hat in different paints share one material and draw different colours.
    (float Red, float Green, float Blue)? Paint = null,
    bool Mirrored = false,

    // **Where the model stands, which its Matrix does not always say** (B170). A baked model is put
    // in the world by its matrix; a SKINNED one is put there by its bones and leaves the matrix at
    // identity, so `Matrix`'s translation reads as the map origin. The renderer needs a real
    // position to choose which of the map's cubemaps the model reflects, and this is it — the same
    // illumination point the ambient cube was sampled at, so lighting and reflection agree about
    // where the model is.
    (float X, float Y, float Z)? Origin = null,

    // **Valve's colour for a brush entity's class, in the category view only** (B219, B156). A
    // door, a lift, an areaportal and a trigger are all plain brushwork until something says which
    // is which, and Hammer says it with these colours.
    //
    // Per INSTANCE rather than baked into the vertices, which is what it was until 2026-08-27 —
    // and baking it is why switching the view had to rebuild the map. Null for anything that is not
    // a brush entity, and null carries its own meaning here: the map may not name the model, the
    // class may state no colour, or the FGDs may not be readable at all. A default at any of those
    // points would report "Valve says grey" where the truth is "nobody said".
    (float Red, float Green, float Blue)? Tint = null,

    // **The direct lights near this model, which the cube above no longer carries** (B170).
    // `PixelShaderDoLightingLinear` adds an ambient cube and up to four local lights, each shaded
    // against the surface normal — so a lamp reaching a model through this list gives it shape and
    // a highlight, where the same lamp folded into the cube gives it neither.
    //
    // Empty rather than null, because "no lamp near enough" and "this model takes no local lights"
    // are the same instruction to the renderer, and a nullable would invite a third reading.
    IReadOnlyList<LocalLight>? Locals = null,

    // **The box the engine culls and buckets this model by, ALREADY PLACED** — the answer
    // `CalcRenderableWorldSpaceAABB` gives. World space, not model space, and computed here rather
    // than by the renderer because only this side knows how a model is placed: a baked prop by its
    // pose, a skinned one by its bones, and a bone-merged one by its WEARER's box (Valve's
    // `IsFollowingEntity` rule, `clientleafsystem.cpp:344`).
    //
    // **It was a model-space box until 2026-08-28, and the renderer placed it with the matrix.**
    // That is only correct for a baked prop, and it shipped two defects in one evening: skinned
    // players and brush entities both leave the matrix somewhere that is not where they are, so
    // both were culled against the map origin. Handing the renderer a finished box removes the
    // question rather than answering it again downstream.
    (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) WorldBounds = default,

    // **Whether the model carries STUDIOHDR_FLAGS_TRANSLUCENT_TWOPASS**, which is the only thing
    // that entitles it to be drawn in both passes — `C_BaseEntity::IsTwoPass` forwards straight to
    // `modelinfo->IsTranslucentTwoPass`, so it is a property of the MODEL and nothing else.
    //
    // Whether it is DRAWN twice is a further question, and the renderer asks it: the entity must
    // also be translucent and at full alpha. See `RenderGroups`.
    bool TwoPass = false,

    // **`GetFxBlend()`, nought to 255 — what `C_BaseEntity::ComputeFxBlend` produced this frame**
    // (B221). Computed once where the instance is built, which gives the same once-per-frame
    // guarantee the engine gets from caching by frame count, without a cache that can go stale.
    //
    // Defaults to opaque so a caller that builds an instance by hand — every test, and the
    // viewmodel path — gets the behaviour that was there before this existed.
    int Alpha = 255,

    // **`m_nRenderMode`, which decides which list the entity joins.** Anything that is not
    // `kRenderNormal` makes it transparent, and `kRenderEnvironmental` makes it undrawn — see
    // `RenderGroups.For`, which has taken this parameter since D114 and received the default from
    // every caller until now.
    int RenderMode = 0);

/// <summary>
/// The models a demo's entities wear, packed once and posed by the GPU.
/// </summary>
/// <remarks>
/// **This is the engine's arrangement, and the reason for it is speed.**
/// <c>IMaterialSystem::LoadBoneMatrix</c> hands a transform to the shader as a constant and the GPU
/// moves the vertices; <c>imesh.h</c> carries a bone weight and index per vertex, and the material
/// system has a <c>MATERIAL_MODEL</c> matrix mode for exactly this. It is why TF2 draws a great
/// many animated models without noticing them.
///
/// So a model's geometry is read once, in its own coordinates, into a buffer that never changes,
/// and an instance is a matrix. Transforming vertices on the processor every frame — the obvious
/// first implementation, and the one this replaced — is precisely the work that path exists to
/// avoid, and a viewer that did it would feel slow where the game does not.
///
/// A rigid entity is the one-bone case. Animation adds a matrix per bone and a weight per vertex;
/// nothing about the packing changes.
/// </remarks>
public sealed class EntityModelSet
{
    /// <summary>What was posed and how, reported under `props` as the prop loader does.</summary>
    private readonly ILogger _props;

    /// <summary>What could not be drawn, reported under `render` (D83).</summary>
    /// <remarks>
    /// **Two categories because this writes to two areas**, and folding them would reclassify
    /// lines somebody greps for. A model lit by nothing is a `render` fact; the pose it was given
    /// is a `props` one.
    /// </remarks>
    private readonly ILogger _render;

    /// <summary>Creates an empty set.</summary>
    /// <param name="loggers">
    /// Where it reports, or <c>null</c> for nowhere. Optional with a null-object default because
    /// every test that builds one of these wants geometry rather than commentary.
    /// </param>
    public EntityModelSet(ILoggerFactory? loggers = null)
    {
        ILoggerFactory factory = loggers ?? NullLoggerFactory.Instance;

        _props = factory.CreateLogger("props");
        _render = factory.CreateLogger("render");

        // **Three collaborators, and each was a slab of the draw loop** (B181). Constructed here
        // rather than injected because each one's state is this set's state — the lighting cache is
        // keyed by entity, and the report sets remember what has already been said about which
        // model. Handing them in would mean two sets could share a "have we said this" flag, which
        // is a silence nobody could explain.
        _lighting = new ModelLighting(IlluminationPoint, _render);
        _reports = new ModelReports(_render);
        _tally = new DrawTally(_props);
        _loggers = factory;
    }

    /// <summary>Extra models an item hangs on itself, asked per prop, or null for none.</summary>
    /// <remarks>
    /// **`CEconEntity::UpdateAttachmentModels` builds the list and
    /// `DrawEconEntityAttachedModels` draws it on the item's own transform** — see the call site in
    /// <see cref="Instances"/>. A delegate rather than a schema reference because the answer needs
    /// the OWNER'S TEAM (`GetNumAttachedModels( GetTeamNumber() )`), and the players are the
    /// scene's to know, not this set's.
    ///
    /// Null when nothing supplies it, which is every test that does not care and every viewer
    /// without a game install.
    /// </remarks>
    public Func<SceneProp, IReadOnlyList<string>>? Attachments { get; set; }

    /// <summary>The colour a prop's item is painted, asked per prop, or null for none (B330).</summary>
    /// <remarks>
    /// **A delegate for the same reason <see cref="Attachments"/> is one**: the answer needs the
    /// econ attribute resolution and `items_game.txt`, which live a layer up, and it needs the
    /// owner's TEAM to choose between a two-tone paint's two colours. Production supplies
    /// `WeaponModels.PaintFor`.
    ///
    /// Null when nothing supplies it — every test that does not care, and every viewer with no game
    /// install, where an unpainted item is the right answer rather than a guessed one.
    /// </remarks>
    public Func<SceneProp, (float Red, float Green, float Blue)?>? Paint { get; set; }

    /// <summary>Where the entities this set builds report, so the bone merge can say what paired.</summary>
    private readonly ILoggerFactory _loggers;

    /// <summary>The ambient cube and sun each model draws with.</summary>
    private readonly ModelLighting _lighting;

    /// <summary>Stopwatch ticks spent lighting models, accumulated until the caller resets it.</summary>
    /// <remarks>
    /// **Kept on this type after the lighting moved out** (B181), because the viewer's per-frame
    /// ledger reads it and moving the property would have changed a public surface for no reason
    /// beyond where the code lives. It forwards to <see cref="ModelLighting.Ticks"/>.
    ///
    /// Posing owned about nine hundred milliseconds of every second (B99) doing two different jobs,
    /// and this is what separated them: bones are per-frame work an animation genuinely needs, while
    /// a stationary model's lighting cannot have changed since the last frame.
    /// </remarks>
    public long LightingTicks
    {
        get => _lighting.Ticks;
        set => _lighting.Ticks = value;
    }

    /// <summary>What <c>SetupBones</c> has cost, ever — the pose itself and any merge it drives.</summary>
    /// <remarks>
    /// **The last split in the pose phase, and every earlier one came back flat** (B189). On a
    /// moment where bone work took 136 ms against a 3 ms median, lighting was 1.5 ms, the viewmodel
    /// 0.2 ms, animation 0.7 ms over 50 calls, and nothing was newly built — so the cost is inside
    /// one of these two and a single "bones" column could not say which.
    /// </remarks>
    public long SetupTicks { get; set; }

    /// <summary>What composing skinning matrices has cost, ever.</summary>
    public long SkinTicks { get; set; }

    /// <summary>What bringing every entity's state up to date has cost, ever.</summary>
    public long SimulateTicks { get; set; }

    /// <summary>Every pose-phase counter at one instant, for diffing across a call.</summary>
    /// <remarks>
    /// **One value instead of ten out-parameters.** Each counter is a running total, so a caller
    /// wanting "what did THIS call cost" reads them either side and subtracts — which was ten pairs
    /// of local variables at the call site and thirteen parameters on the report that consumed
    /// them. The arithmetic is identical; only the bookkeeping moves.
    ///
    /// A per-second total cannot attribute a single freeze: it says how much was spent, never
    /// whether it was spent all at once. That is why these are read per call rather than reported
    /// per second (B189, B191).
    /// </remarks>
    public readonly record struct PoseCounters(
        long Lighting,
        long Simulate,
        long WornLight,
        long Report,
        long ReportLog,
        long Setup,
        long Skin,
        long Animation,
        int AnimationCalls,
        int Built)
    {
        /// <summary>What happened between an earlier snapshot and this one.</summary>
        /// <param name="before">The earlier snapshot.</param>
        /// <returns>The difference, field by field.</returns>
        public PoseCounters Since(PoseCounters before) =>
            new(
                Lighting - before.Lighting,
                Simulate - before.Simulate,
                WornLight - before.WornLight,
                Report - before.Report,
                ReportLog - before.ReportLog,
                Setup - before.Setup,
                Skin - before.Skin,
                Animation - before.Animation,
                AnimationCalls - before.AnimationCalls,
                Built - before.Built);
    }

    /// <summary>Every pose-phase counter as it stands now.</summary>
    public PoseCounters Counters => new(
        _lighting.Ticks,
        SimulateTicks,
        WornLightTicks,
        ReportTicks,
        _reports.LogTicks,
        SetupTicks,
        SkinTicks,
        SkeletonPose.AnimationTicks,
        SkeletonPose.AnimationCalls,
        EntitiesBuilt);

    /// <summary>What per-prop reporting has cost, ever.</summary>
    public long ReportTicks { get; set; }

    /// <summary>How much of <see cref="ReportTicks"/> was spent inside the log sink.</summary>
    public long ReportLogTicks => _reports.LogTicks;

    /// <summary>What sampling light for WORN items has cost, ever — the uncached path.</summary>
    /// <remarks>
    /// **Valve has both halves of this and we have one.** `m_hLightingOrigin` makes an entity take
    /// its light from somewhere other than its own position (<c>c_baseanimating.cpp:3301-3309</c>),
    /// which is exactly what a bone-merged item needs and what this does. But the engine also holds
    /// a `LightCacheHandle_t` per model instance
    /// (<c>ivmodelrender.h:122</c>, `CreateInstance( IClientRenderable*, LightCacheHandle_t* )`),
    /// so the sample is not recomputed per frame — and that half is missing here.
    /// </remarks>
    public long WornLightTicks { get; set; }

    /// <summary>How many animating entities have been built, ever.</summary>
    /// <remarks>
    /// A running total rather than a per-call one, so a caller reads it either side of
    /// <see cref="Instances"/> and gets the count for that moment. The same shape as
    /// <see cref="LightingTicks"/> and for the same reason: a per-second total cannot attribute a
    /// single freeze.
    /// </remarks>
    public int EntitiesBuilt { get; private set; }

    /// <summary>What the draw loop says about each model, once.</summary>
    private readonly ModelReports _reports;

    /// <summary>How many props were asked for, drew, or were rejected and why.</summary>
    private readonly DrawTally _tally;

    private readonly List<WorldVertex> _vertices = [];

    /// <summary>Every packed model's batches, one list per baked animation frame.</summary>
    /// <remarks>
    /// **A frame is a vertex range, not a transform.** Each of an animated model's frames is
    /// skinned once at load and packed like a separate model, so drawing one is picking a range —
    /// no per-frame work on either processor. A model that does not animate has exactly one entry
    /// and costs what it always did.
    /// </remarks>
    private readonly Dictionary<string, List<List<WorldBatch>>> _byModel =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, PropModels.ModelFrames> _frames =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Models already reported as animating, so the log states it once.</summary>
    private readonly HashSet<string> _reportedFrames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which material replaces which, per model and skin family.</summary>
    /// <remarks>
    /// **A skin is a substitution, not a second model.** The batching, the vertex ranges and the
    /// geometry are identical between a RED player and a BLU one; only which material paints each
    /// run differs. So this is a handful of integers per model rather than a copy of anything, and
    /// resolving it at draw time means a player who switches teams is right on the next frame.
    /// </remarks>
    private readonly Dictionary<string, IReadOnlyList<IReadOnlyDictionary<int, int>>> _swaps =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where a model's light should be sampled, in world space.</summary>
    private (float X, float Y, float Z) IlluminationPoint(SceneProp prop, ScenePose pose)
    {
        // **A bone-merged item is lit where its WEARER is, for every quantity at once** (B189; the
        // outside audit's finding 6). Its own pose is (0,0,0) by construction — `FollowEntity`
        // zeroes it and the client takes the parent's bones outright — so its own point is the map
        // origin, whose leaf is usually solid and lightless. This used to be patched downstream:
        // an override in the draw loop replaced the cube, the lamps and the reflection origin with
        // wearer-point samples but NOT the sun, so every cosmetic's direct light was a sky ray
        // traced from the map origin; and the override went past `ModelLighting.For`'s cache, so
        // it re-traced per item per frame. Answering the wearer's point HERE sends all four
        // quantities through the one sampler and its exact-point cache — one place or it drifts.
        if (prop is { AttachedTo: { } wearer, BoneMerged: true } &&
            _lightPoints.TryGetValue(wearer, out (float X, float Y, float Z) worn))
        {
            return worn;
        }

        if (!_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? entry))
        {
            return (pose.X, pose.Y, pose.Z);
        }

        (float x, float y, float z) = entry.Illumination;

        if (x == 0f && y == 0f && z == 0f)
        {
            return (pose.X, pose.Y, pose.Z);
        }

        float radians = pose.Yaw * (MathF.PI / 180f);
        (float sine, float cosine) = MathF.SinCos(radians);

        return (
            pose.X + (x * cosine) - (y * sine),
            pose.Y + (x * sine) + (y * cosine),
            pose.Z + z);
    }

    /// <summary>Skinned models whose posed extents have been reported.</summary>
    private readonly HashSet<string> _reportedPoses = new(StringComparer.OrdinalIgnoreCase);

    // **Six fields and a depth sort used to live here, and they are gone** (D88, B181): _ordered,
    // _worn, _wanted, _wearerBones, _parents and Depth(). Together they guaranteed that a wearer was
    // posed before anything hanging off it, because the loop that followed had no other way to know.
    //
    // The engine guarantees it by asking. CBoneMergeCache::MergeMatchingBones calls SetupBones on
    // the followed entity where it stands (bone_merge_cache.cpp:130), and the readable-bones
    // early-out makes a repeat one integer comparison — so a player worn by six items is posed once
    // with no list, no pass and no sort anywhere in it.
    //
    // Kept as a comment rather than deleted silently because the departure was DECLARED under D86
    // and the reasoning behind it was wrong in a way worth remembering: it was defended as a
    // trade-off against Valve's recursion, and Valve has no ordering code at all. It was forty
    // lines against zero.

    /// <summary>One animating entity per drawn entity index, holding its own bones.</summary>
    /// <remarks>
    /// **The registry that replaced the ordering** (D88, B181). What used to be six fields and a
    /// depth sort is this dictionary plus <see cref="AnimatingEntity.SetupBones(int, double)"/>
    /// being idempotent: an entity that needs its parent asks for it, and a repeat is an integer
    /// comparison.
    /// </remarks>
    private readonly Dictionary<int, AnimatingEntity> _entities = [];

    /// <summary>Which model each entity's skeleton was built for.</summary>
    /// <remarks>
    /// A player who switches class, or a weapon slot reused by a different weapon, keeps its entity
    /// index and changes its model. The skeleton has to be rebuilt for that, and the alternative —
    /// posing a heavy's animation onto a scout's bone count — reads as a scrambled model rather
    /// than as an error.
    /// </remarks>
    private readonly Dictionary<int, string> _entityModels = [];

    /// <summary>Each entity's outgoing sequences, still fading — Valve's animation queue.</summary>
    /// <remarks>
    /// **`CSequenceTransitioner`, which the engine keeps ON the entity**
    /// (`C_BaseAnimating::m_SequenceTransitioner`), so this does too. Without it every sequence
    /// change is a cut: a player who stops running snaps from the run pose to the idle in one
    /// frame, and a door that starts opening jumps to its first frame.
    ///
    /// **A list rather than one entry, because Valve's is a queue.** Two changes inside one fade
    /// window leave two sequences fading at once, and dropping the older would make a fast
    /// direction change snap in a way a slow one does not.
    /// </remarks>
    private readonly Dictionary<int, List<FadingSequence>> _transitions = [];

    /// <summary>Each entity's duck-jump interpolation, which is state across frames (B314).</summary>
    private readonly Dictionary<int, DuckJump> _duckJumps = [];

    /// <summary>How much shorter TF2's crouch hull is than its standing one, in units.</summary>
    /// <remarks>
    /// **`hullSizeNormal - hullSizeCrouch` on the Z axis.** TF2's hulls are `(24, 24, 82)` standing
    /// and `(24, 24, 62)` ducking (`tf_gamerules.cpp:1313`), and the X and Y extents are identical,
    /// so the difference is twenty units of height and nothing sideways. The engine computes the
    /// whole vector and subtracts it; only this component is ever non-zero.
    /// </remarks>
    private const float DuckHullDifference = 20f;

    /// <summary>One sequence an entity has left, still contributing while it fades.</summary>
    /// <param name="Sequence">The sequence being left.</param>
    /// <param name="Cycle">Where its cycle stood when it stopped being current.</param>
    /// <param name="LeftAtSeconds">Demo time at that moment — Valve's <c>m_flLayerAnimtime</c>.</param>
    /// <param name="FadeOutSeconds">
    /// How long it has to fade, <c>MIN( prevseqdesc.fadeouttime, seqdesc.fadeintime )</c>.
    /// </param>
    /// <param name="PlaybackRate">Its rate, so it keeps advancing while it fades.</param>
    /// <remarks>
    /// **It keeps PLAYING while it fades**, which is easy to miss:
    /// `MaintainSequenceTransitions` advances the outgoing cycle by
    /// `dt * m_flPlaybackRate * GetSequenceCycleRate( … )` before accumulating it
    /// (`c_baseanimating.cpp:1853`). Freezing it instead would blend toward a still frame.
    /// </remarks>
    private readonly record struct FadingSequence(
        int Sequence,
        float Cycle,
        double LeftAtSeconds,
        float FadeOutSeconds,
        float PlaybackRate);

    /// <summary>Which frame the bone caches belong to; advanced once per call.</summary>
    private readonly BoneFrameCounter _boneFrames = new();

    /// <summary>Scratch for one entity's placement, so the transform is not reallocated per frame.</summary>
    private readonly Dictionary<int, float[]> _placements = [];

    /// <summary>The raw geometry of each packed model, for checking a pose against.</summary>
    private readonly Dictionary<string, IReadOnlyList<PropVertex>> _raw =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Brings every entity's animation state up to date, before any bones are built.</summary>
    /// <param name="props">What exists at this tick.</param>
    /// <param name="seconds">Demo time, for advancing cycles.</param>
    /// <remarks>
    /// **A separate pass, and it is NOT an ordering pass.** This is the engine's own phase split:
    /// <c>SimulateEntities()</c> runs, then <c>ThreadedBoneSetup()</c>, then rendering
    /// (<c>cdll_client_int.cpp:2206-2210</c>). Updating what an entity is DOING is a different job
    /// from deciding when its bones get built, and separating them is what lets the second job be
    /// demand-driven.
    ///
    /// It matters here for a concrete reason: with no ordering, a prop can be reached through a
    /// merge before the loop gets to it, so its sequence and placement must already be right when
    /// that happens. The old code guaranteed that with a sort. This guarantees it by doing all the
    /// state first, which is both simpler and what the engine does.
    /// </remarks>
    private void Simulate(IReadOnlyList<SceneProp> props, double seconds)
    {
        // **Indexed first, because a placement follows its parent chain up** and the chain can
        // point anywhere in the list. This is what CalcAbsolutePosition walks (c_baseentity.cpp:4387).
        _propsByEntity.Clear();

        // Resolved attachments belong to this pass's camera, not to the entity — see the field.
        _attachments.Clear();

        // Events are a per-frame answer; the WALK's state is per entity and survives.
        _fired.Clear();

        foreach (SceneProp prop in props)
        {
            _propsByEntity[prop.EntityIndex] = prop;
        }

        foreach (SceneProp prop in props)
        {

            if (!_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? entry) ||
                entry.Skinned is not { } skinned)
            {
                continue;
            }

            AnimatingEntity animating = EntityFor(prop, skinned);

            if (animating.Pose is not SkeletonPose posed)
            {
                continue;
            }

            // **The engine's list membership, applied as a test** (B259).
            // `UpdateClientSideAnimations` walks `g_ClientSideAnimationList`, and
            // `C_BaseAnimating::PostDataUpdate` puts an entity on that list only when
            // `m_bClientSideAnimation` is set, taking it off again when it is not
            // (`c_baseanimating.cpp:4689`). An entity that did not ask takes its cycle off the wire
            // as an ordinary interpolated value and the client advances nothing for it.
            //
            // **BELOW `EntityFor`, and the first attempt put it above.** This loop does two jobs:
            // it creates the animating entity that the pose step later looks up, and it advances
            // the cycle. Only the second is `UpdateClientSideAnimation` — gating both stopped every
            // prop being posed at all, which four tests said immediately.
            //
            // **A test rather than a maintained list, and the difference is honest.** The engine
            // keeps a list because it streams; this project holds the whole demo and can seek, so a
            // list mutated across frames would be wrong the moment somebody scrubs backwards. The
            // membership RULE is the same and is what decides the work.
            ScenePose where = prop.Pose;
            int sequence = Math.Max(0, where.Sequence);

            // **Advanced from demo time, because nothing networks a player's cycle.** The client
            // runs its own in C_BaseAnimating::FrameAdvance and treats any sent cycle as a
            // correction; a player's is never sent at all, so replaying it holds one frame of a
            // real animation — a convincing statue.
            // **A viewmodel measures its cycle from when its animation STARTED, not from demo time.**
            // `C_BaseViewModel::UpdateAnimationParity` (`c_baseviewmodel.cpp:467`) sets
            // `SetCycle( 0 )` and `m_flAnimTime = curtime` on a parity change, so a restarted
            // animation begins at frame zero rather than wherever a free-running clock happens to
            // be. Everything else leaves `AnimationStartSeconds` at zero and is unaffected.
            double elapsed = seconds - where.AnimationStartSeconds;

            // **Advanced only for an entity that asked the CLIENT to run its cycle** (B259).
            // `UpdateClientSideAnimations` walks `g_ClientSideAnimationList`, which
            // `C_BaseAnimating::PostDataUpdate` joins only when `m_bClientSideAnimation` is set and
            // leaves when it is not (`c_baseanimating.cpp:4689`). Everything else takes `m_flCycle`
            // off the wire as an ordinary interpolated value, so advancing it here runs a
            // server-animated entity at demo time on top of the cycle the server already stated.
            //
            // **The gate is here and not around the loop, which the first attempt tried.** This
            // body also creates the animating entity the pose step looks up and sets
            // `EntityTransform`, the placement — both of which the engine does for every entity
            // regardless. Skipping them stopped every prop being posed, and four tests said so.
            // **Times `m_flPlaybackRate`, which is the third factor in the engine's own line and
            // was missing here** (B281). `C_BaseAnimating::FrameAdvance`, `c_baseanimating.cpp:5493`:
            //
            //     float cyclerate = GetSequenceCycleRate( hdr, GetSequence() );
            //     float addcycle = flInterval * cyclerate * m_flPlaybackRate;
            //
            // The same product appears in `Interpolate` (`c_baseanimating.cpp:5351`) and in the
            // viewmodel's advance (`c_baseviewmodel.cpp:197`), so it is the engine's definition of
            // an advance rather than one function's detail. The field has been decoded since B237
            // and reaches here on the pose; only the BAKED vertex path multiplied by it, so every
            // skinned entity played at rate 1 whatever the demo said.
            double advanced = prop.ClientSideAnimated
                ? where.Cycle + (elapsed * skinned.CyclesPerSecond(sequence) * where.PlaybackRate)
                : where.Cycle;

            // **Wrapped only if the sequence LOOPS** (`C_BaseAnimating::ClampCycle`,
            // `c_baseanimating.cpp:1431`). This was `advanced - Math.Floor(advanced)`, which is the
            // looping branch applied to everything — so a one-shot sequence that finished started
            // again, for ever. The owner's report: *"the health cab is in a animation loop, it
            // doesnt stop"*; `resupply_locker.mdl`'s `idle`, `open` and `close` are all flags 0x0.
            //
            // `FrameFor` holds a finished one-shot on its last frame and takes the loop flag to do
            // it, and could never run that branch, because the wrap here had already erased the
            // evidence that the cycle went past one.
            // **`STUDIO_REALTIME` discards all of that** (B309). It is `CalcPoseSingle`'s FIRST
            // branch, before anything else the function does with a cycle
            // (`bone_setup.cpp:1955`):
            //
            //     if (seqdesc.flags & STUDIO_REALTIME)
            //     {
            //         float cps = Studio_CPS( pStudioHdr, seqdesc, sequence, poseParameter );
            //         cycle = flTime * cps;
            //         cycle = cycle - (int)cycle;
            //     }
            //
            // The flag's own words are *"cycle index is taken from a real-time clock, not the
            // animations cycle index"* — so the entity's cycle is not corrected, it is ignored,
            // and a SERVER-animated entity still animates when its sequence carries this.
            //
            // **The wrap is a truncation and not `ClampCycle`**, which is why this is its own
            // branch: `cycle - (int)cycle` ignores `STUDIO_LOOPING`, so a non-looping realtime
            // sequence wraps where an ordinary one would be held on its last frame.
            float phase = skinned.Realtime(sequence)
                ? Fraction((float)(seconds * skinned.CyclesPerSecond(sequence)))
                : StudioSequences.ClampCycle((float)advanced, skinned.Loops(sequence));

            posed.Sequence = sequence;

            // **The frame AND how far past it, which is `CalcPoseSingle`'s two lines** (B279).
            // `iFrame = (int)fFrame; s = (fFrame - iFrame);` — the fraction is what the bone
            // sampling blends with, and without it an animation plays its authored frames and
            // nothing between them.
            (posed.Frame, posed.FrameFraction) = StudioSequences.FrameAt(
                phase, skinned.Frames(sequence), skinned.Loops(sequence));

            // **`MaintainSequenceTransitions` first, then `AccumulateLayers`**, which is the order
            // `StandardBlendingRules` runs them in (`c_baseanimating.cpp:1957`) — and the order
            // matters, because each accumulates onto the result of the last. The sequence being
            // faded out is part of the BODY; a gesture goes over the top of whatever the body
            // settled on (B286).
            float[] poseValues = PoseValues(skinned, where, sequence);

            // **`AddLocalLayers` FIRST, at weight one, because it composes into the sequence's own
            // pose before that pose is blended in** (`bone_setup.cpp:2439`, called with a literal
            // 1.0 under a comment admitting the weight is wrong for IK). For the main sequence that
            // pose IS the base here, so a local layer at full weight over it is exact — see B294
            // for the case this does not reproduce.
            List<PoseLayer> composed =
                AutoLayersFor(
                    skinned, sequence, phase, 1f, poseValues, local: true, LayerDepth, seconds);

            // **Then `AddSequenceLayers` for the main sequence, before the transitions.**
            // `AccumulatePose` runs it as its last step (`:2449`), and `StandardBlendingRules` calls
            // `AccumulatePose` for the main sequence before `MaintainSequenceTransitions` — so the
            // main sequence's own layers land ahead of anything fading out behind it.
            composed.AddRange(
                AutoLayersFor(
                    skinned, sequence, phase, 1f, poseValues, local: false, LayerDepth, seconds));

            composed.AddRange(TransitionsFor(prop, skinned, sequence, phase, seconds));

            composed.AddRange(LayersFor(prop, skinned, seconds));

            // **`CalcAutoplaySequences` LAST of the three, which is where the engine runs it**
            // (`c_baseanimating.cpp:1996`, after `AccumulateLayers` and before `CalcBoneAdj`) —
            // B291. Order matters because each accumulates onto the result of the last: a flag
            // that autoplays goes over whatever the body settled on, not underneath it.
            composed.AddRange(AutoplayFor(skinned, seconds));

            posed.Layers = composed;


            // **`DoAnimationEvents`, and it has to be HERE** (B275). The walk asks what the cycle
            // crossed since last frame, and for a player the cycle is not on the wire at all — the
            // client advances it, which is the `phase` computed a few lines up. A probe reading
            // `m_flCycle` off the pose instead reported zero events on a demo full of them, because
            // for every player that value is a constant.
            //
            // The engine's own call site is the same place: `C_BaseAnimating::FrameAdvance` runs
            // the cycle forward and then dispatches, so an event is noticed on the frame the
            // animation reached it rather than on the packet that mentioned the sequence.
            AnimationEvents(prop, skinned, sequence, phase);
            // Computed once above for the autolayer envelopes and reused here rather than asked
            // twice: the two-pass move_x rescale inside it opens the model.
            posed.PoseValues = poseValues;

            // **`CalcBoneAdj`'s two halves, which come from different places** (B288). The model
            // says which bone each input drives and over what range; the demo says what the input
            // is, networked as eleven bits over nought to one. Neither alone bends anything, and
            // both have been read and dropped until now.
            posed.Controllers = skinned.Controllers;
            posed.BoneControllers = where.BoneControllers;

            // **The ROOT model's bytes, for the jiggle bones' own parameters** (B293).
            // `mstudiobone_t::pProcedure()` is an offset from the BONE, and the bones being posed
            // are `Models[0]`'s — reading it against an included animation model's bytes would land
            // on arbitrary floats and produce springs with nonsense constants rather than an error.
            // The same index-space rule the bone controllers follow.
            posed.JiggleSource = skinned.Models.Count > 0 ? skinned.Models[0] : null;

            // **The IK rules, resolved here because the BLEND WEIGHTS live here** (B296). A rule's
            // influence is accumulated across the same up-to-three animations the sequence blends,
            // and only this side knows which they are; the skeleton is handed the answer rather
            // than the means to recompute it.
            // **A SCALED model opts out of IK entirely, and the engine says why**
            // (`c_baseanimating.cpp:2841`):
            //
            //     // NOTE: For model scaling, we need to opt out of IK because it will mark the
            //     // bones as already being calculated
            //     if ( !IsModelScaled() ) { ...allocate m_pIk... }
            //     else                    { if ( m_pIk ) { delete m_pIk; m_pIk = NULL; } }
            //
            // Deleting the context is not a saving, it is a correctness measure: IK marks bones as
            // already calculated, and a scaled skeleton's are not where an unscaled solve put them.
            //
            // **The test is against FLT_EPSILON rather than exact** — `m_flModelScale >
            // 1.0f+FLT_EPSILON || m_flModelScale < 1.0f-FLT_EPSILON` (`c_baseanimating.h:780`) —
            // so a scale a float's last bit away from one is NOT scaled, which matters because a
            // value off the wire and one written as a literal need not be bit-identical.
            //
            // Nothing in the committed corpus is scaled; TF2 scales MvM giants and some Halloween
            // bosses. Implemented because the engine does it.
            bool scaled =
                prop.Pose.Scale > 1f + float.Epsilon || prop.Pose.Scale < 1f - float.Epsilon;

            posed.IkChains = scaled ? [] : skinned.IkChains;

            // **The MAIN sequence's locks** (B311). A layer carries its own; this is the first
            // `AccumulatePose`'s bracket, whose "before" is the bind pose.
            //
            // **Dropped for a scaled model for the same reason the chains are** (B301): a solver
            // measures the chain's links from the posed skeleton, so a model the entity has resized
            // reports link lengths that do not match the animation's, and the solve reaches for a
            // point the leg can no longer make.
            posed.Locks = scaled ? [] : skinned.LocksOf(sequence);

            // **The three per-BONE scales, carried from the wire** (B312). Unlike the IK above,
            // these are NOT dropped for a scaled model: `m_flModelScale` and `m_flHeadScale` are
            // different mechanisms that TF2 applies together — a mini-sentry at 0.75 with a
            // Halloween head is both.
            // **The duck-jump correction** (B314). Ducking in mid-air shrinks the player's hull
            // from 82 units to 62, which moves their ORIGIN — so the engine draws the skeleton the
            // difference lower at that instant and eases it to zero over 0.15 seconds. Without it
            // the model pops twenty units up on every crouch jump, and roughly a fifth of the
            // player states in a real demo are airborne and ducking.
            //
            // **State per entity, because the answer depends on when the duck began** and when it
            // last held rather than on this frame — none of which the demo carries, since the
            // client derives all three from the flags over time.
            if (!_duckJumps.TryGetValue(prop.EntityIndex, out DuckJump? duck))
            {
                duck = new DuckJump();
                _duckJumps[prop.EntityIndex] = duck;
            }

            // `VEC_HULL_MAX_SCALED( this )` — the hulls scale with the model, so a mini-sentry's
            // rider or a resized player gets a proportional correction rather than a fixed one.
            posed.DuckJumpOffset = duck.Update(
                prop.Pose.Flags is { } flags && (flags & PlayerActivityState.Ducking) != 0,
                prop.Pose.Flags is { } state && (state & PlayerActivityState.OnGround) == 0,
                seconds) * DuckHullDifference * prop.Pose.Scale;

            posed.HeadScale = prop.Pose.HeadScale;
            posed.TorsoScale = prop.Pose.TorsoScale;
            posed.HandScale = prop.Pose.HandScale;

            // **Every accumulated sequence contributes its rules, not just the main one** (B297).
            // `AccumulatePose` calls `AddDependencies` for each sequence it accumulates and
            // `AddSequenceLayers` then recurses, so a layer's rules count at the layer's own
            // weight. This is not a detail: TF2's aim matrices are AUTOLAYERS of the movement
            // sequences — `stand_PRIMARY` layers `PRIMARY_aimmatrix_idle`, `run_PRIMARY` layers
            // `PRIMARY_aimmatrix_run` — and every solving IK rule in the game lives on them. Read
            // from the main sequence alone, IK finds nothing but releases for ever.
            List<(StudioIkRule, Vector3, Quaternion, float)> asked = [];

            if (posed.IkChains.Count > 0)
            {
                IkFor(skinned, sequence, phase, 1f, poseValues, asked);

                foreach (PoseLayer layer in composed)
                {
                    IkFor(
                        skinned,
                        layer.Sequence,
                        FractionOf(skinned, layer),
                        layer.Weight,
                        poseValues,
                        asked);
                }
            }

            posed.IkErrors = asked;

            // **The placement, which is what makes the built bones WORLD space.** A merged item
            // takes its wearer's bones and those already carry the wearer's placement, so nothing
            // downstream has to know where a wearer stands — see D88 and finding 35 section 7a.
            posed.EntityTransform = PlacementOf(prop);

            // **IsEnabled first, because the KEY allocates.** `path + "#skin"` builds a string per
            // prop per frame before the set is even consulted, and this sits in Simulate, which
            // walks every prop (B191).
            if (_render.IsEnabled(LogLevel.Debug) && _reportedFrames.Add(prop.ModelPath + "#skin"))
            {
                _render.LogDebug(
                    "{Message}",
                    $"skinned {prop.ModelPath}: sequence {sequence}" +
                    $"{(skinned.IsDelta(sequence) ? " DELTA" : string.Empty)}" +
                    $"{skinned.UnimplementedFor(sequence)}, " +
                    $"{skinned.Frames(sequence)} frames at " +
                    $"{skinned.CyclesPerSecond(sequence):0.###} cycles a second, " +
                    $"phase {phase:0.###} -> frame {posed.Frame}");
            }
        }

        // Parent links second, so every entity exists before anything points at one.
        _drawnPlacements.Clear();
        _parentPlacements.Clear();

        foreach (SceneProp prop in props)
        {
            _lightPoints[prop.EntityIndex] = IlluminationPoint(prop, prop.Pose);

            ScenePose at = prop.Pose;

            PropTransform placed =
                new(at.X, at.Y, at.Z, at.Pitch, at.Yaw, at.Roll, at.Scale);

            // **Every entity, drawn or not, because a TRANSFORM is not a question about drawing**
            // (B231). `CalcAbsolutePosition` composes a child onto its parent's transform with no
            // reference to whether the parent is rendered — and on `cp_fulgur` the parent of every
            // gate is an invisible `func_door`, so the one map that matters here is the one that
            // includes entities nobody draws.
            _parentPlacements[prop.EntityIndex] = placed;

            // Recorded only for props that will actually be drawn, so "the wearer is not being
            // drawn" is answerable without depending on the draw loop's order — which is the whole
            // point of there no longer being one.
            //
            // **Kept SEPARATE from the map above, and the difference is Valve's.** A bone-merged
            // item follows `if ( baseDrawn )` — no wearer on screen, no hat — while a parented
            // entity follows its parent's transform regardless. Merging the two would either draw
            // hats on invisible players or delete props hung on invisible movers, and this project
            // has now done the second one for real.
            if (IsDrawable(prop.Kind) &&
                Batches(prop.ModelPath, SelectFor(prop, seconds).Frame).Count > 0)
            {
                _drawnPlacements[prop.EntityIndex] = placed;
            }

            if (!_entities.TryGetValue(prop.EntityIndex, out AnimatingEntity? animating))
            {
                continue;
            }

            animating.Follows = prop.AttachedTo is { } wearer
                ? _entities.GetValueOrDefault(wearer)
                : null;

            Attach(prop, animating, seconds);
        }
    }

    /// <summary>Places an item that hangs from a named attachment point rather than merging.</summary>
    /// <remarks>
    /// **A hat shares bone names with the player and takes their matrices; a halo, an MvM canteen, a
    /// spellbook and a spy's sapper share none** — the spellbook's only bone is called <c>mvm</c>
    /// and no player has one — so the merge matches nothing and the item would keep its own pose,
    /// which for an attached entity is the map origin (B82).
    ///
    /// The engine hangs those off the WEARER's attachment table:
    /// <c>ConcatTransforms( GetBone( iBone ), pattachment.local, world )</c>, one-based.
    ///
    /// **Resolved into the item's own placement rather than into a draw-time matrix**, which is the
    /// change D88 makes here. An attachment gives a WORLD transform, and world is where this
    /// project's bones now live — so it belongs where the entity's placement belongs, and the merge
    /// overwrites whatever bones do match on top of it. Setting both is correct precisely because
    /// the two cases do not overlap: an item either shares bone names or it does not.
    ///
    /// **Asking the wearer for bones here is Valve's own shape**, not a layering slip:
    /// <c>CalcAttachments()</c> is a call to <c>SetupBones( NULL, -1, BONE_USED_BY_ATTACHMENT,
    /// ... )</c>. Resolving an attachment IS a bone setup, and the cache makes the draw pass's
    /// repeat free.
    /// </remarks>
    private void Attach(SceneProp prop, AnimatingEntity animating, double seconds)
    {
        if (prop.AttachmentPoint is not { } point ||
            animating.Follows is not { } wearer ||
            animating.Pose is not SkeletonPose posed ||
            !_entityModels.TryGetValue(prop.AttachedTo ?? -1, out string? wearerModel) ||
            !_frames.TryGetValue(wearerModel, out PropModels.ModelFrames? worn) ||
            worn.Attachments is not { Count: > 0 } attachments ||
            point < 1 || point > attachments.Count)
        {
            return;
        }

        if (!wearer.SetupBones(StudioBoneFlags.UsedByAnything, seconds))
        {
            return;
        }

        // **The whole TABLE, once per entity, which is the shape of the engine's own call.**
        // `SetupBones_AttachmentHelper` (`c_baseanimating.cpp:2055`) loops every attachment the
        // model declares and caches each with `PutAttachment( i + 1, world )`, and `SetupBones`
        // runs it exactly once per entity per frame:
        //
        //     if( !( oldReadableBones & BONE_USED_BY_ATTACHMENT ) &&
        //         ( boneMask & BONE_USED_BY_ATTACHMENT ) )
        //
        // — "not readable before, wanted now". Resolving one attachment per CHILD, as this did,
        // gives the same arithmetic and repeats it: two items on one attachment point concatenated
        // the same matrices twice, and a wearer with several did the bone lookup once per item.
        float[][] resolved = AttachmentsOf(prop.AttachedTo ?? -1, wearer, attachments);

        if (point > resolved.Length || resolved[point - 1] is not { } placement)
        {
            return;
        }

        StudioAttachment attachment = attachments[point - 1];

        // Identity for the wearer's own transform, because its bones are already in world space —
        // the placement it used to need is folded into them (finding 35 section 7a).
        //
        // Back through MatrixConvention, because AttachmentPlacement returns a MODEL matrix and an
        // entity placement is a matrix3x4_t. Same boundary as PlacementOf, same one crossing point.
        posed.EntityTransform = placement;

        if (_props.IsEnabled(LogLevel.Debug) && _reportedPoses.Add(prop.ModelPath + "#attached"))
        {
            _props.LogDebug(
                "{Message}",
                $"attached {prop.ModelPath} to {attachment.Name} " +
                $"(point {point}, bone {attachment.Bone}) on {wearerModel}");
        }
    }

    /// <summary>Every attachment of one entity, resolved once — Valve's <c>PutAttachment</c>.</summary>
    /// <remarks>
    /// **Cleared per PASS rather than per frame**, in `Simulate` beside `_propsByEntity`. The
    /// viewmodel pass corrects its attachments for the viewmodel's projection and the world pass
    /// does not, so a table carried between them would be wrong for one of the two. The reserved
    /// viewmodel entity indices mean the two passes cannot collide today; clearing anyway is what
    /// keeps that from becoming load-bearing.
    /// </remarks>
    private readonly Dictionary<int, float[][]> _attachments = [];

    /// <summary>Resolves every attachment a wearer declares, once.</summary>
    /// <remarks>
    /// **<c>SetupBones_AttachmentHelper</c>, `c_baseanimating.cpp:2055`**, loop and all:
    ///
    /// <code>
    ///   for (int i = 0; i &lt; hdr-&gt;GetNumAttachments(); i++)
    ///   {
    ///       const mstudioattachment_t &amp;pattachment = hdr-&gt;pAttachment( i );
    ///       int iBone = hdr-&gt;GetAttachmentBone( i );
    ///       if ( (pattachment.flags &amp; ATTACHMENT_FLAG_WORLD_ALIGN) == 0 )
    ///           ConcatTransforms( GetBone( iBone ), pattachment.local, world );
    ///       else
    ///           ...position only, identity rotation...
    ///       FormatViewModelAttachment( i, world );
    ///       PutAttachment( i + 1, world );
    ///   }
    /// </code>
    ///
    /// **The correction is inside the loop, applied to every attachment**, which is why it lives
    /// here rather than at the one call site that used to need it.
    /// </remarks>
    private float[][] AttachmentsOf(
        int entity, AnimatingEntity wearer, IReadOnlyList<StudioAttachment> attachments)
    {
        if (_attachments.TryGetValue(entity, out float[][]? cached))
        {
            return cached;
        }

        float[][] resolved = new float[attachments.Count][];

        for (int index = 0; index < attachments.Count; index++)
        {
            StudioAttachment attachment = attachments[index];

            if (attachment.Bone < 0 || attachment.Bone >= wearer.Bones.Count)
            {
                continue;
            }

            float[] placement = MatrixConvention.ToBoneMatrix(
                AttachmentPlacement.Matrix(
                    wearer.Bones.Bone(attachment.Bone).ToArray(),
                    attachment.Local,
                    PropTransform.Identity.ToMatrix(),
                    attachment.IsWorldAligned));

            // **`FormatViewModelAttachment`, a virtual whose base body is EMPTY** — nothing for a
            // world model, everything for a viewmodel, which is why a null projection here is that
            // empty body rather than a missing feature. Only the POSITION moves: Valve reads the
            // matrix's translation, corrects it and writes it back with `PositionMatrix`, leaving
            // the rotation the bone gave it.
            if (ViewmodelProjection is { } projection && IsViewmodel(entity))
            {
                (float x, float y, float z) = ViewmodelAttachment.Correct(
                    (placement[3], placement[7], placement[11]),
                    projection.Eye,
                    projection.Right,
                    projection.Up,
                    projection.Forward,
                    projection.WorldFieldOfView,
                    projection.ViewmodelFieldOfView);

                placement[3] = x;
                placement[7] = y;
                placement[11] = z;
            }

            resolved[index] = placement;
        }

        _attachments[entity] = resolved;

        return resolved;
    }

    /// <summary>Whether an entity is one of the viewmodel pass's own.</summary>
    /// <remarks>
    /// **The reserved indices rather than a new flag**, because <see cref="ViewmodelScene"/> already
    /// owns that numbering and a second answer to "is this a viewmodel" is a second thing to
    /// disagree with.
    /// </remarks>
    private static bool IsViewmodel(int entity) =>
        entity is ViewmodelScene.ArmsEntityIndex
            or ViewmodelScene.WeaponEntityIndex
            or ViewmodelScene.OffHandEntityIndex;

    /// <summary>Where each entity's light is sampled, so a worn item can borrow its wearer's.</summary>
    private readonly Dictionary<int, (float X, float Y, float Z)> _lightPoints = [];

    /// <summary>Every DRAWABLE entity's placement, for items that have no bones to carry one.</summary>
    /// <remarks>
    /// **A model this project bakes rather than skins still gets worn on things**, and it has no
    /// skeleton to fold its wearer's placement into — so it needs the wearer's transform the way
    /// everything did before D88. Two viewer tests caught this within minutes of the swap, which is
    /// exactly what they are for.
    ///
    /// **The engine has no such case, and that is worth being precise about rather than glossing.**
    /// Valve's equivalent is <c>STUDIOHDR_FLAGS_STATIC_PROP</c>, which still goes through
    /// <c>SetupBones</c> and gets ONE bone — <c>MatrixCopy( parentTransform, GetBoneForWrite( 0 ) )</c>
    /// at <c>c_baseanimating.cpp:2953</c>. Ours is a different distinction: a performance choice
    /// about which models are worth skinning at all, made by this project and not by the format. So
    /// the fallback is ours to carry, and it is declared here rather than left to look like Valve's.
    ///
    /// Keyed only for props that pass the drawable and batch checks, so "the wearer is not being
    /// drawn" is answerable without depending on which order the draw loop reaches them in.
    /// </remarks>
    private readonly Dictionary<int, PropTransform> _drawnPlacements = [];

    /// <summary>Where EVERY entity is this frame, drawn or not, for resolving a parent.</summary>
    /// <remarks>
    /// **Separate from `_drawnPlacements` because they answer different questions**
    /// (B231). That one says "is my wearer on screen", which is what `if ( baseDrawn )` asks before
    /// hanging a hat; this one says "where is my parent", which `CalcAbsolutePosition` asks with no
    /// regard for whether the parent renders. Every gate on `cp_fulgur` hangs off an invisible
    /// `func_door`, so the second map is the only one that can place them.
    /// </remarks>
    private readonly Dictionary<int, PropTransform> _parentPlacements = [];

    /// <summary>This entity's animating object, rebuilt when its model changes.</summary>
    private AnimatingEntity EntityFor(SceneProp prop, PropModels.SkinnedModel skinned)
    {
        if (_entities.TryGetValue(prop.EntityIndex, out AnimatingEntity? existing) &&
            _entityModels.TryGetValue(prop.EntityIndex, out string? was) &&
            string.Equals(was, prop.ModelPath, StringComparison.Ordinal))
        {
            return existing;
        }

        // The factory rather than nothing, so the bone-merge pairing reaches the log. That line was
        // deleted with EntityModelSet.Merge and its absence left a viewer run unable to say whether
        // weapons had paired with the players holding them.
        // **Counted because a spike needs a cause and this is the only per-moment discontinuity in
        // the pose path.** Everything else here runs for every entity every frame; this runs the
        // first time an entity is seen and when its model changes. Five of seven slow moments were
        // dominated by bone work at 50-162 ms against a 3 ms median, and a median that low says the
        // steady path is not what spikes (B189).
        EntitiesBuilt++;

        AnimatingEntity animating = new(
            new SkeletonPose(skinned.Bones, skinned.Locals), _boneFrames, _loggers);

        _entities[prop.EntityIndex] = animating;
        _entityModels[prop.EntityIndex] = prop.ModelPath;

        // **`C_BaseAnimating::OnNewModel`'s pose-parameter half** (`c_baseanimating.cpp:1130`),
        // fired from the one place that knows an entity's model has just been resolved. Only the
        // model says which parameters wrap, and the interpolation that needs to know runs a layer
        // below models — so it is told rather than left to look.
        ModelResolved?.Invoke(prop.EntityIndex, LoopingPoseParameters(skinned));

        return animating;
    }

    /// <summary>The pose parameters an entity was last posed with.</summary>
    /// <param name="entityIndex">Slot in the entity table.</param>
    /// <returns>The values the blend received, or empty if the entity has not been posed.</returns>
    /// <remarks>
    /// **The value CARRIED, not a second computation of it** (B243). This reports the array
    /// `Simulate` handed to the skeleton, so a probe or a test reading it is asking what the blend
    /// actually used rather than re-deriving what it should have been — the distinction that made
    /// eight diagnostics lie in two sessions.
    /// </remarks>
    public IReadOnlyList<float> PoseValuesOf(int entityIndex) =>
        _entities.TryGetValue(entityIndex, out AnimatingEntity? animating) &&
        animating.Pose is SkeletonPose posed
            ? posed.PoseValues
            : [];

    /// <summary>What an entity's event walk remembers, and the parity it last saw.</summary>
    private readonly Dictionary<int, (AnimationEventState State, int Parity)> _eventStates = [];

    /// <summary>Scratch for one entity's events, so the walk allocates nothing per frame.</summary>
    private readonly List<StudioEvent> _firedScratch = [];

    /// <summary>Every client animation event that fired this frame, with where it fired.</summary>
    private readonly List<FiredAnimationEvent> _fired = [];

    /// <summary>Every client animation event this frame crossed.</summary>
    /// <remarks>
    /// **Rebuilt per frame, in the order the engine fires them** — which on a loop is the tail of
    /// the old lap before the head of the new one. A consumer reads it after `Instances` and
    /// before the next frame; nothing retains it.
    ///
    /// **The first consumer is a probe, and the intended one is sound.** Event 5004 is
    /// `AE_CL_PLAYSOUND` and names a sound script outright — `Taunt.Soldier01HeelClick` and its
    /// like — so a viewer can honour it with what the audio layer already has. Event 7001 is TF2's
    /// footstep and needs the ground surface under the foot, which is B172.
    /// </remarks>
    public IReadOnlyList<FiredAnimationEvent> FiredEvents => _fired;

    /// <summary>Resolves an entity's gestures into layers this model can actually play.</summary>
    /// <param name="prop">The entity.</param>
    /// <param name="skinned">Its model.</param>
    /// <param name="seconds">Demo time now.</param>
    /// <returns>The layers, in slot order, or empty.</returns>
    /// <remarks>
    /// **This is <c>AddToGestureSlot</c> and <c>UpdateGestureLayer</c> together**
    /// (<c>multiplayer_animstate.cpp:616</c> and <c>:1275</c>), which is where the engine turns an
    /// activity into a sequence and advances the layer's cycle:
    ///
    /// <code>
    ///   int iGestureSequence = pPlayer-&gt;SelectWeightedSequence( iGestureActivity );
    ///   if ( iGestureSequence &lt;= 0 ) return;                       // abandoned, not drawn
    ///   ...
    ///   flCycle += GetSequenceCycleRate( … ) * gpGlobals-&gt;frametime * …;
    ///   if ( flCycle &gt; 1.0f ) { if ( bAutoKill ) ResetGestureSlot(…); else flCycle = 1.0f; }
    /// </code>
    ///
    /// **Resolved here rather than in the timeline because only this layer has the model**, and a
    /// gesture that resolves to nothing is dropped exactly as the engine drops it — a class whose
    /// model has no such activity plays no gesture rather than a wrong one.
    ///
    /// **The weight is one, always**, because <c>AddToGestureSlot</c> sets
    /// <c>m_pAnimLayer-&gt;m_flWeight = 1.0f</c> and nothing on a networked gesture lowers it:
    /// <c>BlendWeight</c> returns immediately unless <c>m_bClientBlend</c>
    /// (<c>animationlayer.h:183</c>), which is client-only state a demo never carries. The per-bone
    /// list does all the shaping.
    ///
    /// **Not reproduced:** <c>GetGesturePlaybackRate()</c>, which scales the advance for a taunt,
    /// and the layer's own <c>m_flPlaybackRate</c>, which the engine keeps per layer and a gesture
    /// leaves at one. Named rather than silently omitted.
    /// </remarks>
    private static List<PoseLayer> LayersFor(
        SceneProp prop, PropModels.SkinnedModel skinned, double seconds)
    {
        List<PoseLayer> layers = [];

        // **The layers the entity itself sends, walked in `m_nOrder`** (B285). A player has none —
        // `tf_player.cpp:774` excludes the array — so this loop is empty for them and the gesture
        // loop below is empty for everything else. `AccumulateLayers` draws both the same way.
        //
        // **No activity resolution and no lifetime**, unlike a gesture: the server chose the
        // sequence, states the cycle and states the weight, so every question this layer needs is
        // already answered on the wire.
        foreach (SceneAnimationLayer sent in prop.Pose.Layers)
        {
            // `if (m_AnimOverlay[i].m_nSequence >= nSequences) continue;` — a sequence the model
            // does not have is skipped rather than clamped (`c_baseanimatingoverlay.cpp:341`).
            if (sent.Sequence < 0 || sent.Sequence >= skinned.Sequences.Count || sent.Weight <= 0f)
            {
                continue;
            }

            // `fCycle = ClampCycle( fCycle, IsSequenceLooping( … ) )`, the same wrap the main
            // sequence takes — and the same `STUDIO_REALTIME` override before it (B309).
            //
            // **The override belongs to every sequence, not to the main one.** Valve applies it
            // inside `CalcPoseSingle` (`bone_setup.cpp:1955`), which `AccumulatePose` runs for the
            // main sequence, for each layer and for each autolayer alike. Putting it only on the
            // main sequence would be half a mechanism — a realtime sequence would take the clock
            // when played and the wire's cycle when layered.
            float wrapped = skinned.Realtime(sent.Sequence)
                ? Fraction((float)(seconds * skinned.CyclesPerSecond(sent.Sequence)))
                : StudioSequences.ClampCycle(sent.Cycle, skinned.Loops(sent.Sequence));

            (int at, float part) = StudioSequences.FrameAt(
                wrapped, skinned.Frames(sent.Sequence), skinned.Loops(sent.Sequence));

            layers.Add(new PoseLayer(
                sent.Sequence,
                at,
                part,
                sent.Weight,
                skinned.BoneWeights(sent.Sequence),
                Delta: skinned.IsDelta(sent.Sequence),
                Post: skinned.IsPost(sent.Sequence),
                Locks: skinned.LocksOf(sent.Sequence)));
        }

        if (prop.Pose.Gestures is not { Count: > 0 } gestures)
        {
            return layers;
        }

        foreach (SceneGesture gesture in gestures)
        {

            // `SelectWeightedSequence( iGestureActivity )`, and its `<= 0` abandonment. An activity
            // number rather than a name is the two custom-gesture events, which carry the activity
            // on the wire; nothing resolves those yet, so they are skipped rather than guessed at.
            if (gesture.ActivityName is not { Length: > 0 } named)
            {
                continue;
            }

            // **The weapon rewrites the activity, and it is a TABLE rather than a suffix** (B284).
            // `CTFPlayerAnimState::TranslateActivity` (`tf_playeranimstate.cpp:124`) calls
            // `pWeapon->ActivityOverride( … )`, which walks that weapon role's own `acttable_t` —
            // twelve of them in `tf_weaponbase.cpp:3660` onward. A model declares only the
            // rewritten names, so an activity that is not put through this resolves to −1 and draws
            // nothing.
            //
            // **Here rather than in Core, because only this layer knows the role.** It comes from
            // the installed game's own scripts through `Appearance.WeaponSuffix`, the same source
            // the main sequence uses.
            //
            // The first attempt at this appended the role as a string, which is right for reloads
            // and landings and wrong for every attack: `ACT_MP_ATTACK_STAND_PRIMARYFIRE` maps to
            // `ACT_MP_ATTACK_STAND_PRIMARY`, a rename rather than a suffix, and
            // `ACT_MP_CROUCH_DEPLOYED` maps to `ACT_MP_CROUCHWALK_DEPLOYED`.
            string activity = WeaponActivityTable.Override(prop.Pose.Slot ?? "PRIMARY", named);

            // **`ForActivity`, not `Find`, and the difference is the whole mechanism.** `Find`
            // matches a sequence LABEL the way `Studio_LookupSequence` does; the engine resolves a
            // gesture through `SelectWeightedSequence( iGestureActivity )`, which matches the
            // ACTIVITY and picks among ties by `actweight`. A gesture names an activity —
            // `ACT_MP_GESTURE_FLINCH_CHEST` — and no sequence is labelled that, so asking `Find`
            // returns −1 for every gesture on every model. Measured: a flinch half a second old,
            // three gestures reaching the drawn prop, and zero layers.
            int sequence = skinned.ForActivity(activity);

            // `if ( iGestureSequence <= 0 ) return;` — the engine abandons a gesture whose activity
            // this model does not have, rather than substituting one.
            if (sequence <= 0)
            {
                continue;
            }

            float rate = skinned.CyclesPerSecond(sequence);

            // A sequence with no rate has one frame and nothing to advance through; the engine's
            // cycle would stay at zero, which is that single frame.
            double cycle = rate > 0f ? (seconds - gesture.StartedSeconds) * rate : 0d;

            if (cycle < 0d)
            {
                continue;
            }

            if (cycle > 1d)
            {
                // `if ( pGesture->m_bAutoKill ) ResetGestureSlot(…)` — the slot is gone, so the
                // layer is not drawn at all. Otherwise it holds on its last frame, for ever, which
                // is what a `_BEGIN` gesture is for.
                if (gesture.AutoKill)
                {
                    continue;
                }

                cycle = 1d;
            }

            (int frame, float fraction) = StudioSequences.FrameAt(
                (float)cycle, skinned.Frames(sequence), skinned.Loops(sequence));

            layers.Add(new PoseLayer(
                sequence,
                frame,
                fraction,
                1f,
                skinned.BoneWeights(sequence),

                // **Every TF2 player gesture is a DELTA**, measured: `PRIMARY_reload_start` and
                // `jumpland_primary` both carry the bit. `SlerpBones` composes those additively
                // rather than blending toward them (B284).
                Delta: skinned.IsDelta(sequence),
                Post: skinned.IsPost(sequence),
                Locks: skinned.LocksOf(sequence)));
        }

        return layers;
    }

    /// <summary>How deep an autolayer chain may go before it is abandoned.</summary>
    /// <remarks>
    /// **A bound on data from a file, not a guess at content.** Valve reaches a layered sequence's
    /// own layers through `AccumulatePose`, which has no depth limit at all and would not terminate
    /// on a model whose layers cycle. Four is far past anything measured — the deepest real case is
    /// one level — and a chain longer than that is a corrupt model rather than an animator's intent.
    /// </remarks>
    private const int LayerDepth = 4;

    /// <summary>Where each of a sequence's IK rules wants its chain, at this cycle.</summary>
    /// <param name="skinned">The model.</param>
    /// <param name="sequence">The merged sequence being played.</param>
    /// <param name="cycle">Where its cycle stands, wrapped.</param>
    /// <param name="influenceOfSequence">
    /// How much this sequence itself counts — one for the main sequence, a layer's own weight for a
    /// layer. `AddDependencies` takes exactly this as `flWeight`.
    /// </param>
    /// <param name="values">The pose parameters in force, which choose the blend corners.</param>
    /// <param name="errors">Where the rules that asked for something are added, in place.</param>
    /// <remarks>
    /// **<c>Studio_IKSequenceError</c> then <c>Studio_IKAnimationError</c>**
    /// (<c>bone_setup.cpp:3043</c> and <c>:2994</c>), which between them do two accumulations across
    /// the same up-to-four animations the sequence blends.
    ///
    /// **The ENVELOPE is accumulated first, weighted**, because the four animations may not agree
    /// about when the rule runs:
    ///
    /// <code>
    ///   ikRule.start += (pRule->start + dt) * weight[i];   // and peak, tail, end
    ///   if (ikRule.start > 1.0) { ikRule.start -= 1.0; … }
    /// </code>
    ///
    /// **The <c>dt</c> is the part that would be missed.** When one animation's rule starts near
    /// zero and another's near one, they are the same footstep either side of a loop — so Valve
    /// shifts a rule more than half a cycle from the first by a whole cycle before averaging, which
    /// is what stops the mean landing in the middle of the animation instead of at its edge.
    ///
    /// **Then the ERROR is accumulated, weighted, over the same animations**, each read at the
    /// frame the shared envelope picks.
    ///
    /// **Only the rules of the FIRST animation are enumerated**, which is Valve's own constraint:
    /// `if (iRule >= panim[i]->numikrules || panim[i]->numikrules != panim[0]->numikrules) return
    /// false;`. Blended animations are expected to declare matching rule lists, and one that does
    /// not is abandoned rather than reconciled.
    /// </remarks>

    private static void IkFor(
        PropModels.SkinnedModel skinned,
        int sequence,
        float cycle,
        float influenceOfSequence,
        IReadOnlyList<float> values,
        List<(StudioIkRule, Vector3, Quaternion, float)> errors)
    {
        if (skinned.IkChains.Count == 0 || influenceOfSequence <= 0f)
        {
            return;
        }

        (int group, IReadOnlyList<(int Animation, float Weight)> blend) =
            skinned.BlendedAnimations(sequence, values);

        if (blend.Count == 0 || group >= skinned.Models.Count)
        {
            return;
        }

        ReadOnlyMemory<byte> model = skinned.Models[group];

        IReadOnlyList<StudioIkRule> rules = StudioIkRules.Read(model, blend[0].Animation);

        if (rules.Count == 0)
        {
            return;
        }

        for (int rule = 0; rule < rules.Count; rule++)
        {
            // The envelope, averaged across the corners, with Valve's loop-shift.
            float start = 0f;
            float peak = 0f;
            float tail = 0f;
            float end = 0f;
            float first = float.NaN;
            bool matched = true;

            foreach ((int animation, float weight) in blend)
            {
                IReadOnlyList<StudioIkRule> theirs = StudioIkRules.Read(model, animation);

                if (rule >= theirs.Count || theirs.Count != rules.Count)
                {
                    matched = false;
                    break;
                }

                StudioIkRule other = theirs[rule];

                float shift = 0f;

                if (!float.IsNaN(first))
                {
                    if (other.Start - first > 0.5f)
                    {
                        shift = -1f;
                    }
                    else if (other.Start - first < -0.5f)
                    {
                        shift = 1f;
                    }
                }
                else
                {
                    first = other.Start;
                }

                start += (other.Start + shift) * weight;
                peak += (other.Peak + shift) * weight;
                tail += (other.Tail + shift) * weight;
                end += (other.End + shift) * weight;
            }

            if (!matched)
            {
                continue;
            }

            if (start > 1f)
            {
                start -= 1f;
                peak -= 1f;
                tail -= 1f;
                end -= 1f;
            }
            else if (start < 0f)
            {
                start += 1f;
                peak += 1f;
                tail += 1f;
                end += 1f;
            }

            StudioIkRule shared = rules[rule] with
            {
                Start = start, Peak = peak, Tail = tail, End = end,
            };

            bool carriesAnError =
                shared.Type is StudioIkRuleType.Self or StudioIkRuleType.World
                    or StudioIkRuleType.Ground or StudioIkRuleType.Attachment;

            // The error, accumulated over the same corners at the frame the shared envelope picks.
            Vector3 position = default;
            Quaternion rotation = default;
            float total = 0f;

            // The ENVELOPE weight, which every corner of one grid computes from the same shared
            // envelope and the same cycle — so it is one value, not a sum, exactly as Valve takes
            // it once before the corner loop.
            float envelope = 0f;

            foreach ((int animation, float weight) in blend)
            {
                float influence = StudioIkRules.Weight(
                    shared,
                    skinned.FramesOfAnimation(group, animation),
                    cycle,
                    out int frame,
                    out float fraction);

                // `if (pRule->type != IK_GROUND && flWeight < 0.0001) return false;` — a rule with
                // no influence is skipped, except a ground rule, which has to keep tracking where
                // its foot was planted. TF2 declares no ground rules at all.
                if (influence < 0.0001f)
                {
                    continue;
                }

                // **Only four of the six types carry an error track, and Valve says so in a
                // comment** — `// only check rules with error values`, over the switch at
                // `bone_setup.cpp:3157`. `IK_SELF`, `IK_WORLD`, `IK_GROUND` and `IK_ATTACHMENT`
                // read one; `IK_RELEASE` and `IK_UNLATCH` fall to `default: total += weight[i];`,
                // which counts the corner's weight WITHOUT reading anything.
                //
                // **Requiring an error dropped every release**, and a release is the type TF2
                // declares most: 13359 against 1674 selves over every animation `z1800` draws. So
                // the corrections a release exists to give back were never given back, and every
                // self ran at full strength (B299).
                if (carriesAnError)
                {
                    if (!StudioIkRules.Error(
                        model,
                        animation,
                        rule,
                        frame,
                        fraction,
                        out (float X, float Y, float Z) at,
                        out (float X, float Y, float Z, float W) turn))
                    {
                        continue;
                    }

                    position += new Vector3(at.X, at.Y, at.Z) * weight;

                    // `QuaternionAccumulate( ikRule.q, weight[i], q1, ikRule.q )` — an aligned add
                    // rather than a slerp, which is what lets several corners sum.
                    Quaternion corner = new(turn.X, turn.Y, turn.Z, turn.W);

                    rotation += corner * (Quaternion.Dot(rotation, corner) < 0f ? -weight : weight);
                }

                // **`total += weight[i]`, the corner's blend weight ALONE** — `default:` and the
                // success arm of the switch both add exactly that (`bone_setup.cpp:3175`). It was
                // `weight * influence` here, which conflated the two quantities below.
                total += weight;

                envelope = influence;
            }

            // **`total` is a NORMALISER, and `flWeight` is the envelope. They are not the same
            // number and using one as the other is a divergence that hides in the common case.**
            // `Studio_IKSequenceError` computes the envelope once, up front, and returns false when
            // it is nothing:
            //
            //     ikRule.flWeight = Studio_IKRuleWeight( ikRule, flCycle );
            //     if (ikRule.flWeight <= 0.001f) { ...IK_GROUND looping special case... return false; }
            //
            // then uses `total` for one thing only, at `:3188`:
            //
            //     if (total <= 0.0001f) return false;
            //     if (total < 0.999f)
            //     {
            //         VectorScale( ikRule.pos, 1.0f / total, ikRule.pos );
            //         QuaternionScale( ikRule.q, 1.0f / total, ikRule.q );
            //     }
            //
            // and the weight the solver applies is `pRule->flWeight * pRule->flRuleWeight` — the
            // envelope times the sequence's influence, with `total` nowhere in it.
            //
            // **The two agree whenever every corner reports**, because the corner weights sum to
            // one; they part company when one corner's error read fails. Valve keeps the full
            // envelope weight and rescales the error by the share that DID report. Ours reduced the
            // weight instead, which asks for less correction rather than for the same correction
            // derived from less data.
            if (total <= 0.0001f || envelope <= 0.001f ||
                (carriesAnError && rotation.LengthSquared() <= 0f))
            {
                continue;
            }

            if (total < 0.999f)
            {
                position /= total;

                // **`QuaternionScale`, which scales the ANGLE rather than the components**
                // (`mathlib_base.cpp:1757`, `sinsom = sin( asin( sinom ) * t )`, carrying the sign
                // of `w` across under Valve's own note *"keep sign of rotation"*). A component
                // divide would be a different rotation, and `Quaternion.Normalize` — which this
                // used unconditionally — is a third thing again.
                (float qx, float qy, float qz, float qw) = StudioBones.Scale(
                    (rotation.X, rotation.Y, rotation.Z, rotation.W), 1f / total);

                rotation = new Quaternion(qx, qy, qz, qw);
            }

            // **Scaled by the SEQUENCE's own influence**, which is what `AddDependencies` receives
            // as `flWeight` — an autolayer at half weight asks for half the correction, exactly as
            // its pose contributes half.
            // `flWeight * flRuleWeight` — the envelope times the sequence's influence, which is
            // what `SolveDependencies` multiplies (`bone_setup.cpp:4103`). `total` is not in it.
            float asked = Math.Clamp(envelope * influenceOfSequence, 0f, 1f);

            // **A rule at full strength clears every rule already on its chain, and a RELEASE at
            // full strength is then not added at all** — `AddDependencies`, `bone_setup.cpp:3319`:
            //
            //     if (ikrule.flRuleWeight * ikrule.flWeight > 0.999)
            //     {
            //         if ( ikrule.type != IK_UNLATCH)
            //         {
            //             m_ikChainRule.Element( ikrule.chain ).RemoveAll( );
            //             if ( ikrule.type == IK_RELEASE) continue;
            //         }
            //     }
            //
            // So a chain whose release is fully in force reaches the solver EMPTY rather than
            // carrying a correction and its cancellation — which is both faster and, for the
            // rotation, not the same answer as blending the two.
            if (asked > 0.999f && shared.Type != StudioIkRuleType.Unlatch)
            {
                errors.RemoveAll(held => held.Item1.Chain == shared.Chain);

                if (shared.Type == StudioIkRuleType.Release)
                {
                    continue;
                }
            }

            errors.Add((
                shared,
                position,
                carriesAnError ? Quaternion.Normalize(rotation) : Quaternion.Identity,
                asked));
        }
    }

    /// <summary>A layer's cycle, recovered from the frame it was resolved to.</summary>
    /// <remarks>
    /// **A `PoseLayer` carries a FRAME, and an IK rule's envelope is in CYCLE.** The layer list is
    /// built for bone sampling, which wants a frame; the rules want the fraction of the animation
    /// that frame sits at. Recomputing it from the frame is exact enough for the envelope, whose
    /// resolution is the same frame count.
    ///
    /// **A one-frame sequence is cycle zero rather than a division by zero**, which is also what
    /// the engine's `numframes - 1` scaling implies for it.
    /// </remarks>
    /// <summary>Valve's <c>cycle = cycle - (int)cycle</c>, for a realtime sequence.</summary>
    /// <param name="cycle">Time times cycles a second.</param>
    /// <returns>Its fractional part.</returns>
    /// <remarks>
    /// **A C cast to <c>int</c> truncates toward zero**, so this is not `Math.Floor` and the two
    /// disagree for a negative input. Demo time never runs backwards past zero, so the difference
    /// cannot arise here — written the engine's way regardless, because the one that matches is
    /// free and the one that nearly matches is a bug waiting for a caller.
    ///
    /// **No test can tell this from <c>ClampCycle(x, true)</c>, and a sabotage said so rather than
    /// a reading.** Swapping one for the other reddened nothing: the two are the same function for
    /// every <c>x >= 0</c>, and every value this branch has ever seen is a product of demo seconds
    /// and a positive cycle rate. The distinguishing input is a NEGATIVE product, which nothing
    /// constructs. Stated here instead of chased, because a test written to force it would be
    /// asserting against an input the program cannot produce.
    /// </remarks>
    private static float Fraction(float cycle) => cycle - (int)cycle;

    private static float FractionOf(PropModels.SkinnedModel skinned, PoseLayer layer)
    {
        int frames = skinned.Frames(layer.Sequence);

        return frames > 1 ? (layer.Frame + layer.FrameFraction) / (frames - 1) : 0f;
    }

    /// <summary>The sequences one sequence automatically layers over itself.</summary>
    /// <param name="skinned">The model.</param>
    /// <param name="sequence">The merged sequence being played.</param>
    /// <param name="cycle">Where its cycle stands, wrapped.</param>
    /// <param name="weight">The weight that sequence is being accumulated at.</param>
    /// <param name="values">The pose parameters in force, for a <c>STUDIO_AL_POSE</c> layer.</param>
    /// <param name="local">Which of Valve's two passes to run.</param>
    /// <param name="budget">How much deeper the recursion may go.</param>
    /// <param name="seconds">
    /// Demo time, for a layer whose sequence carries <c>STUDIO_REALTIME</c> and therefore takes its
    /// cycle from the clock rather than from the parent's (B309).
    /// </param>
    /// <returns>A layer per autolayer whose envelope is open, in file order.</returns>
    /// <remarks>
    /// **Two passes over one array, and every autolayer belongs to exactly one of them.**
    /// `AddSequenceLayers` (<c>bone_setup.cpp:2125</c>) skips a layer carrying `STUDIO_AL_LOCAL`;
    /// `AddLocalLayers` (<c>:2218</c>) skips one without it, and returns immediately unless the
    /// SEQUENCE carries `STUDIO_LOCAL`.
    ///
    /// **They differ in where and when they compose.** The local pass goes into the sequence's own
    /// pose at weight ONE before that pose is blended in; the other goes onto the accumulator
    /// afterwards at the parent's weight. Both are here because both are used by real TF2 content
    /// (B294): `sentry3`'s idle and `c_rocketpack`'s deploy layer non-locally, and
    /// `c_engineer_arms`' `throw_draw`, `throw_idle` and `throw_fire` layer locally.
    ///
    /// **The envelope is skipped entirely when <c>start == end</c>**, which is not a degenerate
    /// case to guard against but the common one: four of the seven autolayers measured have a
    /// window of all zeros, and Valve's `if (pLayer->start != pLayer->end)` then leaves the layer
    /// at the parent's own cycle and weight.
    ///
    /// **Under <c>STUDIO_AL_POSE</c> the window is in the POSE PARAMETER's range, not the cycle's**
    /// — the same four numbers mean something different — and the layer's cycle is NOT rewritten,
    /// where the cycle-driven case remaps it into the window. One flag changing the meaning of five
    /// values is the part most likely to be read past.
    /// </remarks>
    private static List<PoseLayer> AutoLayersFor(
        PropModels.SkinnedModel skinned,
        int sequence,
        float cycle,
        float weight,
        IReadOnlyList<float> values,
        bool local,
        int budget,
        double seconds)
    {
        List<PoseLayer> layers = [];

        // `if (!(seqdesc.flags & STUDIO_LOCAL)) return;` — the local pass is gated on the SEQUENCE,
        // so a sequence declaring local autolayers without the flag has layers nothing applies.
        if (budget <= 0 || (local && !skinned.IsLocal(sequence)))
        {
            return layers;
        }

        foreach (StudioAutoLayer entry in skinned.AutoLayersOf(sequence))
        {
            if (entry.IsLocal != local)
            {
                continue;
            }

            int target = skinned.RelativeSequence(sequence, entry.Sequence);

            if (target < 0)
            {
                continue;
            }

            // `float layerCycle = cycle; float layerWeight = flWeight;` before the window is
            // considered at all — and the local pass is called with a weight of 1.0 by its caller.
            float layerCycle = cycle;
            float layerWeight = weight;

            // **Three exact comparisons, and all three are the engine's** — `if (pLayer->start !=
            // pLayer->end)`, `pLayer->start != pLayer->peak`, `pLayer->end != pLayer->tail`. The
            // analyser wants a tolerance and a tolerance would be wrong: these are two numbers an
            // animator typed, written into the file unchanged, and the test asks whether they are
            // THE SAME NUMBER rather than whether they are close. Four of the seven autolayers
            // measured in TF2 have a window of all zeros, so the first comparison is the common
            // path and not a degenerate guard; the other two divide by the difference immediately
            // afterwards, which is what they exist to prevent.
#pragma warning disable S1244
            if (entry.Start != entry.End)
            {
                // **`STUDIO_AL_POSE` belongs to the non-local pass alone** (B307). They are two
                // separate engine functions and only one of them has the branch:
                // `AddSequenceLayers` (`bone_setup.cpp:2148`) chooses between the cycle and a pose
                // parameter, while `AddLocalLayers` (`bone_setup.cpp:2244`) reads `cycle` and
                // nothing else — no `index`, no `iPose`, no `m_flPoseParameter`.
                //
                // **Not an oversight of Valve's: a local layer's window is in CYCLE units.** The
                // local pass composes into the sequence's own pose before that pose is blended, so
                // the only variable that has walked anywhere by then is the sequence's own cycle.
                float index = entry.DrivenByPose && !local
                    ? PoseIndex(skinned, target, entry, values)
                    : cycle;

                if (index < entry.Start || index >= entry.End)
                {
                    continue;
                }

                float ramp = 1f;

                if (index < entry.Peak && entry.Start != entry.Peak)
                {
                    ramp = (index - entry.Start) / (entry.Peak - entry.Start);
                }
                else if (index > entry.Tail && entry.End != entry.Tail)
                {
                    ramp = (entry.End - index) / (entry.End - entry.Tail);
                }

                if (entry.IsSpline)
                {
                    ramp = (3f * ramp * ramp) - (2f * ramp * ramp * ramp);
                }

                // **The cross-fade arm applies only past the TAIL**, on the way out, and is one at
                // ramp one whatever the parent weighs — which is the point of Valve's comment about
                // a second layer also accumulating.
                if (entry.CrossFades && index > entry.Tail)
                {
                    layerWeight = ramp * weight / (1f - weight + (ramp * weight));
                }
                else if (entry.IgnoresWeight)
                {
                    layerWeight = ramp;
                }
                else
                {
                    layerWeight = weight * ramp;
                }

                // **Not remapped for a pose-driven layer**, whose cycle stays the parent's — and
                // `AddSequenceLayers` is the only pass that says so. `AddLocalLayers` ends with a
                // bare `layerCycle = (cycle - pLayer->start) / (pLayer->end - pLayer->start);`
                // under no guard at all, because its window was in cycle units to begin with.
                if (!entry.DrivenByPose || local)
                {
                    layerCycle = (cycle - entry.Start) / (entry.End - entry.Start);
                }
            }
#pragma warning restore S1244

            // **`flWeight = clamp( flWeight, 0.0f, 1.0f );`** — `AccumulatePose`'s own first act
            // (`bone_setup.cpp:2408`), under an Assert and a comment saying it should not be
            // necessary. It is: the ramp is extrapolated outside its window whenever `start` and
            // `peak` differ, so a layer just below its own start computes a negative weight. Valve
            // clamps it to zero and `SlerpBones` then skips it on `if (s2 <= 0.0f) continue`, which
            // is what dropping it here reproduces.
            layerWeight = Math.Clamp(layerWeight, 0f, 1f);

            if (layerWeight <= 0f)
            {
                continue;
            }

            // **`STUDIO_REALTIME` on the layered sequence, and this is where TF2 actually puts it**
            // (B309). All 32 sequences carrying the flag are named `layer_*` — MvM bot
            // `layer_primary_jump_floatNoise` and its neighbours — so an autolayer target is the
            // case the flag exists for. The window arithmetic above still decides WHETHER the layer
            // plays and at what weight; the flag decides only where in the animation it is sampled.
            float sampled = skinned.Realtime(target)
                ? Fraction((float)(seconds * skinned.CyclesPerSecond(target)))
                : StudioSequences.ClampCycle(layerCycle, skinned.Loops(target));

            (int frame, float fraction) = StudioSequences.FrameAt(
                sampled,
                skinned.Frames(target),
                skinned.Loops(target));

            layers.Add(new PoseLayer(
                target,
                frame,
                fraction,
                layerWeight,
                skinned.BoneWeights(target),
                Delta: skinned.IsDelta(target),
                Post: skinned.IsPost(target),
                Locks: skinned.LocksOf(target)));

            // **A layered sequence can layer further sequences**, because Valve reaches them through
            // `AccumulatePose` and that calls both passes again. Bounded rather than trusted: the
            // recursion is over data from a file, and a model whose layers cycle would not
            // terminate.
            layers.AddRange(
                AutoLayersFor(
                    skinned,
                    target,
                    layerCycle,
                    layerWeight,
                    values,
                    local: false,
                    budget - 1,
                    seconds));
        }

        return layers;
    }

    /// <summary>Where a pose-driven autolayer sits in its parameter's own range.</summary>
    /// <remarks>
    /// **<c>index = m_flPoseParameter[iPose] * (Pose.end - Pose.start) + Pose.start</c>** — the
    /// stored value is normalised and the window is in the parameter's authored units, so the
    /// normalised number has to be expanded before it can be compared. Using it directly would
    /// compare a nought-to-one figure against a window in degrees.
    ///
    /// **Zero when the parameter does not resolve**, which is Valve's own `else index = 0`.
    /// </remarks>
    private static float PoseIndex(
        PropModels.SkinnedModel skinned,
        int sequence,
        StudioAutoLayer entry,
        IReadOnlyList<float> values)
    {
        int shared = skinned.SharedPoseParameter(sequence, entry.PoseParameter);

        if (shared < 0 || shared >= skinned.PoseParameters.Count || shared >= values.Count)
        {
            return 0f;
        }

        StudioPoseParameter parameter = skinned.PoseParameters[shared];

        return (values[shared] * (parameter.End - parameter.Start)) + parameter.Start;
    }

    /// <summary>The sequences a model plays on its own, off the clock.</summary>
    /// <param name="skinned">The model.</param>
    /// <param name="seconds">Demo time now, which is Valve's <c>flRealTime</c>.</param>
    /// <returns>A layer per autoplay sequence, at full weight.</returns>
    /// <remarks>
    /// **`CalcAutoplaySequences`, <c>bone_setup.cpp:4457</c>**, whose whole body is:
    ///
    /// <code>
    ///   int count = m_pStudioHdr->GetAutoplayList( &amp;pList );
    ///   for (i = 0; i &lt; count; i++)
    ///   {
    ///       int sequenceIndex = pList[i];
    ///       if (seqdesc.flags &amp; STUDIO_AUTOPLAY)
    ///       {
    ///           float cps = Studio_CPS( m_pStudioHdr, seqdesc, sequenceIndex, m_flPoseParameter );
    ///           cycle = flRealTime * cps;
    ///           cycle = cycle - (int)cycle;
    ///           AccumulatePose( pos, q, sequenceIndex, cycle, 1.0, flRealTime, pIKContext );
    ///       }
    ///   }
    /// </code>
    ///
    /// **This is how a model animates part of itself with nothing driving it** — a flag in the
    /// wind, a chain, an idle machine. For such a model it is not decoration: the entity's own
    /// sequence is the idle it is already holding, so autoplay is the only thing that moves.
    ///
    /// **The cycle comes from REAL TIME rather than from the entity**, which is what separates this
    /// from every other layer here. It runs on an entity standing still, it runs on an entity that
    /// is not client-side animated, and two copies of one model are always in step because both
    /// read the same clock rather than their own state.
    ///
    /// **The wrap is `cycle - (int)cycle`, C's truncation toward zero**, which for a demo's
    /// non-negative time is the same as a floor. Written through
    /// <see cref="StudioSequences.ClampCycle(float, bool)"/>'s looping arm, which is that
    /// expression with the negative guard Valve applies to a looping cycle.
    ///
    /// **Weight is a literal 1.0 and never fades.** There is no queue, no lifetime and no
    /// auto-kill: an autoplay sequence is on for as long as the model is drawn.
    /// </remarks>
    private static List<PoseLayer> AutoplayFor(PropModels.SkinnedModel skinned, double seconds)
    {
        List<PoseLayer> layers = [];

        foreach (int sequence in skinned.AutoplaySequences())
        {
            float rate = skinned.CyclesPerSecond(sequence);

            // `if (weight[i] > 0 && panim[i]->numframes > 1)` — Studio_CPS sums nothing for a
            // one-frame animation and returns zero, and `cycle = flRealTime * 0` is zero. That is
            // the sequence's single frame, held, which is what a one-frame autoplay means.
            float cycle = rate > 0f
                ? StudioSequences.ClampCycle((float)(seconds * rate), loops: true)
                : 0f;

            (int frame, float fraction) = StudioSequences.FrameAt(
                cycle, skinned.Frames(sequence), skinned.Loops(sequence));

            layers.Add(new PoseLayer(
                sequence,
                frame,
                fraction,

                // `AccumulatePose( pos, q, sequenceIndex, cycle, 1.0, … )` — the literal.
                1f,
                skinned.BoneWeights(sequence),
                Delta: skinned.IsDelta(sequence),
                Post: skinned.IsPost(sequence),
                Locks: skinned.LocksOf(sequence)));
        }

        return layers;
    }

    /// <summary>Keeps an entity's outgoing sequences alive while they fade.</summary>
    /// <param name="prop">The entity.</param>
    /// <param name="skinned">Its model.</param>
    /// <param name="sequence">The sequence it is playing now.</param>
    /// <param name="cycle">Where that sequence's cycle stands.</param>
    /// <param name="seconds">Demo time now.</param>
    /// <returns>A layer per sequence still fading, oldest first.</returns>
    /// <remarks>
    /// **`CSequenceTransitioner` and `MaintainSequenceTransitions` together**
    /// (`sequence_Transitioner.cpp:17` and `c_baseanimating.cpp:1815`). Without them every sequence
    /// change is a CUT — a player who stops running snaps out of the run pose in one frame, and a
    /// door that starts opening jumps to its first frame (B286).
    ///
    /// **On a change, the outgoing sequence is pushed with a fade window taken from BOTH
    /// sequences**: `MIN( prevseqdesc.fadeouttime, seqdesc.fadeintime )`. `STUDIO_SNAP` empties the
    /// queue instead, which is how an authored cut stays a cut.
    ///
    /// **Each fading sequence keeps PLAYING**, advanced by
    /// `dt * m_flPlaybackRate * GetSequenceCycleRate( … )` before it is accumulated
    /// (`c_baseanimating.cpp:1853`) — freezing it would blend toward a still frame.
    ///
    /// **Weights come from `GetFadeout`'s spline**, and an entry at or below zero is removed rather
    /// than accumulated at nothing, which is what bounds the queue.
    ///
    /// **Not reproduced: `m_nNewSequenceParity`.** The engine also forces a transition when the
    /// parity counter moves, which restarts a sequence that has not changed number. This project
    /// carries that counter on the pose as `ResetEventsParity`'s neighbour and uses it for the
    /// animation start, so a restart already resets the cycle — but a restart of the SAME sequence
    /// does not currently cross-fade.
    /// </remarks>
    private List<PoseLayer> TransitionsFor(
        SceneProp prop, PropModels.SkinnedModel skinned, int sequence, float cycle, double seconds)
    {
        if (!_transitions.TryGetValue(prop.EntityIndex, out List<FadingSequence>? queue))
        {
            queue = [];
            _transitions[prop.EntityIndex] = queue;
        }

        if (!_currentSequence.TryGetValue(
            prop.EntityIndex, out (int Sequence, float Cycle, double StartedAt) was))
        {
            _currentSequence[prop.EntityIndex] =
                (sequence, cycle, prop.Pose.AnimationStartSeconds);

            return [];
        }

        // **A sequence can begin again without its NUMBER changing, and Valve's second term is
        // exactly for that.** `CheckForSequenceChange` triggers on
        // `currentblend->m_nSequence != nCurSequence || bForceNewSequence`
        // (`sequence_Transitioner.cpp:38`), and `bForceNewSequence` is
        // `m_nNewSequenceParity != m_nPrevNewSequenceParity` (`c_baseanimating.cpp:1831`) — a
        // counter the server bumps on every `SetSequence`, so a cabinet opened twice restarts twice
        // and only the counter says the second one began.
        //
        // **Reaching here the restart is already an `AnimationStartSeconds`**, which the timeline
        // stamps from that same parity (and, for a client-side entity, from
        // `m_bClientSideFrameReset`). Comparing it is the same event one hop later rather than a
        // second derivation of it — the parity itself is not carried this far, and adding a second
        // copy of the signal is how two answers to one question start disagreeing.
        //
        // **Exact, deliberately.** This is not a computed quantity being tested for near-equality:
        // it is a stamp the timeline wrote once and this copies verbatim, so it either is the same
        // stamp or names a different run. A tolerance here would merge two restarts a frame apart,
        // which is the case a repeated gesture produces.
#pragma warning disable S1244
        bool restarted = prop.Pose.AnimationStartSeconds != was.StartedAt;
#pragma warning restore S1244

        if (was.Sequence != sequence || restarted)
        {
            // `if ((seqdesc.flags & STUDIO_SNAP) || !bInterpolate) m_animationQueue.RemoveAll();`
            if (skinned.SnapsTo(sequence))
            {
                queue.Clear();
            }
            else
            {
                float window = MathF.Min(
                    skinned.FadeOut(was.Sequence), skinned.FadeIn(sequence));

                if (window > 0f)
                {
                    queue.Add(new FadingSequence(
                        was.Sequence, was.Cycle, seconds, window, prop.Pose.PlaybackRate));
                }
            }
        }

        _currentSequence[prop.EntityIndex] = (sequence, cycle, prop.Pose.AnimationStartSeconds);

        if (queue.Count == 0)
        {
            return [];
        }

        List<PoseLayer> fading = [];

        for (int index = queue.Count - 1; index >= 0; index--)
        {
            FadingSequence leaving = queue[index];

            double elapsed = seconds - leaving.LeftAtSeconds;

            float weight = StudioSequenceFade.Fadeout(elapsed, leaving.FadeOutSeconds);

            // `if (s > 0) … else m_animationQueue.Remove( i )` — a finished entry leaves the queue
            // rather than being accumulated at no weight, which is what stops it growing.
            if (weight <= 0f)
            {
                queue.RemoveAt(index);
                continue;
            }

            double advanced = leaving.Cycle +
                (elapsed * skinned.CyclesPerSecond(leaving.Sequence) * leaving.PlaybackRate);

            // **The FOURTH site of `STUDIO_REALTIME`, found by asking where else `AccumulatePose`
            // runs** (B309). `MaintainSequenceTransitions` ends with
            // `boneSetup.AccumulatePose( pos, q, blend->m_nSequence, flCycle, … )`
            // (`c_baseanimating.cpp:1863`), so a sequence FADING OUT goes through `CalcPoseSingle`
            // like every other and takes the clock if it is flagged. The engine computes and clamps
            // `flCycle` just above that call and `CalcPoseSingle` then discards it — which is easy
            // to read as the clamp being the final word.
            float wrapped = skinned.Realtime(leaving.Sequence)
                ? Fraction((float)(seconds * skinned.CyclesPerSecond(leaving.Sequence)))
                : StudioSequences.ClampCycle((float)advanced, skinned.Loops(leaving.Sequence));

            (int at, float part) = StudioSequences.FrameAt(
                wrapped, skinned.Frames(leaving.Sequence), skinned.Loops(leaving.Sequence));

            fading.Add(new PoseLayer(
                leaving.Sequence,
                at,
                part,
                weight,
                skinned.BoneWeights(leaving.Sequence),
                Delta: skinned.IsDelta(leaving.Sequence),
                Post: skinned.IsPost(leaving.Sequence),
                Locks: skinned.LocksOf(leaving.Sequence)));
        }

        // Oldest first, because the engine walks its queue from the front and each accumulates onto
        // the last — and the loop above ran backwards so it could remove as it went.
        fading.Reverse();

        return fading;
    }

    /// <summary>What sequence each entity was playing last frame, and where its cycle stood.</summary>
    /// <remarks>
    /// **`UpdateCurrent`'s job**, kept per entity for the same reason the queue is. The cycle is
    /// remembered as well as the number because a sequence being left has to carry on from where it
    /// was rather than restarting.
    /// </remarks>
    private readonly Dictionary<int, (int Sequence, float Cycle, double StartedAt)>
        _currentSequence = [];

    /// <summary>Walks one entity's events for this frame.</summary>
    /// <param name="prop">The entity.</param>
    /// <param name="skinned">Its model, for the MERGED sequence list.</param>
    /// <param name="sequence">The sequence it is playing.</param>
    /// <param name="phase">Its cycle now, after the client's own advance.</param>
    private void AnimationEvents(
        SceneProp prop, PropModels.SkinnedModel skinned, int sequence, float phase)
    {
        // **Through the merged table, because a TF2 player model declares no events at all.** They
        // live in the included `<class>_animations.mdl`, so asking the root model answers "none"
        // for every player in every demo.
        IReadOnlyList<StudioEvent> events = skinned.Events(sequence);

        if (events.Count == 0)
        {
            return;
        }

        _eventStates.TryGetValue(
            prop.EntityIndex, out (AnimationEventState State, int Parity) remembered);

        _firedScratch.Clear();

        AnimationEventState next = AnimationEventFiring.Fired(
            events,
            sequence,
            remembered.State,
            phase,
            resetEvents: remembered.Parity != prop.Pose.ResetEventsParity,
            into: _firedScratch);

        _eventStates[prop.EntityIndex] = (next, prop.Pose.ResetEventsParity);

        foreach (StudioEvent fired in _firedScratch)
        {
            _fired.Add(new FiredAnimationEvent(
                prop.EntityIndex,
                fired,
                (prop.Pose.X, prop.Pose.Y, prop.Pose.Z)));
        }
    }

    /// <summary>The frame and fraction an entity's skeleton was last posed at.</summary>
    /// <param name="entityIndex">Slot in the entity table.</param>
    /// <returns>What `Simulate` computed, or null if the entity has not been posed.</returns>
    /// <remarks>
    /// **The value CARRIED, for the same reason as <see cref="PoseValuesOf"/>** (B243). Whether a
    /// player's cycle is advancing cannot be read off the pose — a player's cycle is not on the
    /// wire, so the pose says zero for ever — and cannot be recomputed by a probe without the
    /// probe becoming a second implementation of `Simulate`. This is the number the sampler was
    /// handed, per frame, which is the only thing that can distinguish "gliding" from "animating"
    /// (B280).
    /// </remarks>
    public (int Sequence, int Frame, float Fraction)? FrameOf(int entityIndex) =>
        _entities.TryGetValue(entityIndex, out AnimatingEntity? animating) &&
        animating.Pose is SkeletonPose posed
            ? (posed.Sequence, posed.Frame, posed.FrameFraction)
            : null;

    /// <summary>The animation layers an entity's skeleton was handed, or null.</summary>
    /// <param name="entityIndex">The entity.</param>
    /// <returns>Its layers, or null when it has no skeleton here.</returns>
    /// <remarks>
    /// **Carried, not recomputed** (B243), and for the same reason as <see cref="FrameOf"/>: a
    /// gesture resolves to a sequence only if the model has that activity, so asking the model a
    /// second time answers what COULD have been layered rather than what was. This is the list the
    /// pose actually accumulated.
    /// </remarks>
    /// <summary>One entity's duck-jump offset, in units, or null when it has no skeleton.</summary>
    /// <remarks>
    /// **Per entity rather than summed, because the question is which player is corrected** — an
    /// aggregate would report a number while saying nothing about whether the airborne crouching
    /// one got it and the standing one did not (B314).
    /// </remarks>
    public float? DuckJumpOffsetOf(int entityIndex) =>
        _entities.TryGetValue(entityIndex, out AnimatingEntity? entity) &&
        entity.Pose is SkeletonPose skeleton
            ? skeleton.DuckJumpOffset
            : null;

    public IReadOnlyList<PoseLayer>? LayersOf(int entityIndex) =>
        _entities.TryGetValue(entityIndex, out AnimatingEntity? animating) &&
        animating.Pose is SkeletonPose posed
            ? posed.Layers
            : null;

    /// <summary>How many jiggle bones the spring simulation ran on, across every entity posed.</summary>
    /// <remarks>
    /// **The only thing that can say the simulation is WIRED** (B293). The reader, the flag test and
    /// the physics each have their own tests and all three passing says nothing about whether the
    /// pose path reaches them — which is the shape that has shipped three no-ops here with a green
    /// suite. This counts what actually ran.
    ///
    /// **Summed rather than per entity**, because the question it answers is about the demo: does
    /// anything on screen jiggle at all. A per-entity number is one dictionary lookup away for
    /// anyone who needs it.
    /// </remarks>
    public int JigglingBones
    {
        get
        {
            int simulated = 0;

            foreach (AnimatingEntity animating in _entities.Values)
            {
                if (animating.Pose is SkeletonPose posed)
                {
                    simulated += posed.JigglingBones;
                }
            }

            return simulated;
        }
    }

    /// <summary>
    /// How many bones the last pose pass posed by a quaternion-interpolation rule, across every
    /// entity.
    /// </summary>
    /// <remarks>
    /// **The only thing that can say `STUDIO_PROC_QUATINTERP` is WIRED** (B317), and it is here for
    /// the reason its jiggle neighbour is: the reader, the arithmetic and the dispatch each have
    /// their own tests, and every one of them passing says nothing about whether a real model on a
    /// real demo reaches the rule. Summed from the poses rather than counted from what the models
    /// DECLARE, since a number derived by a second route stays right while the code does nothing.
    /// </remarks>
    public int QuatInterpBones
    {
        get
        {
            int driven = 0;

            foreach (AnimatingEntity animating in _entities.Values)
            {
                if (animating.Pose is SkeletonPose posed)
                {
                    driven += posed.QuatInterpBonesBuilt;
                }
            }

            return driven;
        }
    }

    /// <summary>The furthest any quaternion-interpolation rule moved a bone, across every entity.</summary>
    /// <remarks>
    /// **The other half of the pair above**, and the one that says the rule changed the picture
    /// rather than merely running — a magnitude, like `IkLocks`' furthest move. Zero with a non-zero
    /// count would mean the triggers reproduce the animated pose, which is a real possible outcome
    /// and not one a count can report.
    /// </remarks>
    public float QuatInterpFurthestMove
    {
        get
        {
            float furthest = 0f;

            foreach (AnimatingEntity animating in _entities.Values)
            {
                if (animating.Pose is SkeletonPose posed)
                {
                    furthest = Math.Max(furthest, posed.QuatInterpFurthestMove);
                }
            }

            return furthest;
        }
    }

    /// <summary>How many IK chains the last pose pass actually solved, across every entity.</summary>
    /// <remarks>
    /// **The only thing that can say IK is WIRED** (B296). The solver, the rule reader, the error
    /// decode and the context each have their own tests, and all of them passing says nothing about
    /// whether the pose path reaches them — the shape that has shipped three no-ops here with a
    /// green suite. This counts chains that actually reached the solver.
    /// </remarks>
    public int SolvedIkChains
    {
        get
        {
            int solved = 0;

            foreach (AnimatingEntity animating in _entities.Values)
            {
                if (animating.Pose is SkeletonPose posed)
                {
                    solved += posed.SolvedChains;
                }
            }

            return solved;
        }
    }

    /// <summary>How many sequence IK locks the pose path actually applied.</summary>
    /// <remarks>
    /// **The same question `SolvedIkChains` answers for rules, asked of locks** (B311): the unit
    /// tests prove `IkLocks` pins an effector when it is called, and only this says whether a real
    /// demo ever calls it. A lock naming a chain the model lacks, or a chain `Studio_SolveIK`
    /// refuses, leaves this at zero while every part looks wired.
    /// </remarks>
    public int AppliedIkLocks
    {
        get
        {
            int applied = 0;

            foreach (AnimatingEntity animating in _entities.Values)
            {
                if (animating.Pose is SkeletonPose posed)
                {
                    applied += posed.AppliedLocks;
                }
            }

            return applied;
        }
    }

    /// <summary>How many applied locks actually moved an effector, and the furthest move.</summary>
    /// <remarks>
    /// **The question a screenshot cannot answer**, made numeric: whether a lock holds a foot or
    /// merely runs. `AppliedIkLocks` says the bracket executed; this says it changed the pose, and
    /// by how far. Zero moves with a non-zero apply count would mean the solve is computing the
    /// position the sequence already had — which looks identical on screen and is a different
    /// defect from not running at all.
    /// </remarks>
    public (int Moved, float Furthest) IkLockEffect
    {
        get
        {
            int moved = 0;
            float furthest = 0f;

            foreach (AnimatingEntity animating in _entities.Values)
            {
                if (animating.Pose is SkeletonPose posed)
                {
                    (int entityMoved, float entityFurthest) = posed.LockEffect;

                    moved += entityMoved;
                    furthest = MathF.Max(furthest, entityFurthest);
                }
            }

            return (moved, furthest);
        }
    }

    /// <summary>What the IK pass was handed, for telling apart the ways it can do nothing.</summary>
    /// <remarks>
    /// **Three numbers because "nothing solved" has three causes** and they need different fixes: no
    /// entity carries chains, or chains but no rules were read, or rules but none weighed anything.
    /// A single count cannot distinguish them, and this project has twice spent a measurement on
    /// the wrong one of a pair.
    /// </remarks>
    public (int Chained, int Ruled, int Weighed) IkWork
    {
        get
        {
            int chained = 0;
            int ruled = 0;
            int weighed = 0;

            foreach (AnimatingEntity animating in _entities.Values)
            {
                if (animating.Pose is not SkeletonPose posed)
                {
                    continue;
                }

                chained += posed.IkChains.Count;
                weighed += posed.IkErrors.Count;

                // **Counting the rules that can RAISE a chain's weight, which only `IK_SELF` does.**
                // A release blends the target back without touching the weight, so a chain holding
                // only releases is never solved — reporting them together would make "a correction
                // is playing" and "a correction is being given back" the same number.
                //
                // **`weighed` and `ruled` being equal is now the NORMAL reading**, and it is not a
                // sign releases were dropped. `AddDependencies` removes every rule on a chain when
                // one arrives at full strength and drops a full-strength release outright
                // (`bone_setup.cpp:3319`), and a release in force is usually at full strength — so
                // what survives to here is mostly selves that nothing cancelled (B299).
                foreach ((StudioIkRule rule, _, _, _) in posed.IkErrors)
                {
                    if (rule.Type == StudioIkRuleType.Self)
                    {
                        ruled++;
                    }
                }
            }

            return (chained, ruled, weighed);
        }
    }

    /// <summary>Told when an entity's model is resolved, with which pose parameters wrap.</summary>
    /// <remarks>
    /// **A callback rather than a call, because this layer must not know what is listening.** The
    /// only consumer is the interpolator, which lives under the scene rather than beside it; the
    /// window wires the two together where it already registers every other system.
    /// </remarks>
    public Action<int, IReadOnlyList<bool>>? ModelResolved { get; set; }

    /// <summary>Which of a model's pose parameters wrap, or empty when none do.</summary>
    /// <remarks>
    /// **Empty is the common answer and is cheaper than an array of false.** Of a sentry gun's two
    /// parameters only <c>aim_yaw</c> loops, and most models have none at all — the caller reads a
    /// missing index as "does not wrap", which is what <c>SetLooping(false)</c> leaves it at.
    /// </remarks>
    private static bool[] LoopingPoseParameters(PropModels.SkinnedModel model)
    {
        IReadOnlyList<StudioPoseParameter> parameters = model.PoseParameters;
        bool[]? looping = null;

        for (int index = 0; index < parameters.Count; index++)
        {
            if (parameters[index].Loop != 0f)
            {
                looping ??= new bool[parameters.Count];
                looping[index] = true;
            }
        }

        return looping ?? [];
    }

    /// <summary>Where a prop stands, as a row-major 3×4, reusing the array it had last frame.</summary>
    /// <remarks>
    /// **A bone-merged entity takes its PARENT's placement, and that is Valve's, not bookkeeping.**
    /// <c>CalcAbsolutePosition</c> branches on it explicitly:
    ///
    /// <code>
    /// if ( IsEffectActive(EF_BONEMERGE) )
    /// {
    ///     MoveToAimEnt();
    ///     return;
    /// }
    /// </code>
    ///
    /// at <c>c_baseentity.cpp:4387</c>, and <c>MoveToAimEnt</c> is
    /// <c>GetAimEntOrigin( GetMoveParent(), … )</c> followed by <c>SetAbsOrigin</c> /
    /// <c>SetAbsAngles</c> (<c>:4294</c>). So a followed entity's ABSOLUTE origin is its parent's,
    /// while its LOCAL origin is the zero <c>FollowEntity</c> wrote — and <c>SetupBones</c> builds
    /// its <c>parentTransform</c> from <c>GetRenderOrigin()</c>, which is the absolute one.
    ///
    /// **This was deleted on 2026-08-24 and put back the same night**, which is the part worth
    /// recording. The old code did it as <c>transform = worn.Where</c> and D88 removed it as
    /// leftover bookkeeping, on the strength of having read <c>BuildTransformations</c> and not
    /// <c>CalcAbsolutePosition</c>. The result was eight of nine weapons drawn at the map origin:
    /// a weapon shares few or no bone names with a player, so the merge places little, and what is
    /// left builds at the entity's own placement — which for a followed entity is (0,0,0) unless
    /// this resolves it.
    ///
    /// The owner's diagnosis was the right one and arrived before the reading did: *"if everything
    /// was following valve then it would work"*. It was not following Valve. It had deleted a piece
    /// of Valve while believing the opposite.
    /// </remarks>
    private float[] PlacementOf(SceneProp prop)
    {
        if (!_placements.TryGetValue(prop.EntityIndex, out float[]? placement))
        {
            placement = new float[12];
            _placements[prop.EntityIndex] = placement;
        }

        ScenePose pose = Absolute(prop, AnimatingEntity.MaximumFollowDepth);

        // **Through MatrixConvention, because the two forms are not the same shape OR the same
        // layout.** ToMatrix is the shader's row-vector 4×4 with translation in the last ROW;
        // EntityTransform is Valve's matrix3x4_t with translation in column 3. Crossing that by
        // hand is what threw `Destination array was not long enough` on the first frame of
        // playback — and the length was the lucky half, since a transpose of the same size would
        // have drawn a plausible wrong pose instead of failing.
        MatrixConvention.ToBoneMatrix(
                new PropTransform(
                    pose.X, pose.Y, pose.Z, pose.Pitch, pose.Yaw, pose.Roll, pose.Scale).ToMatrix())
            .CopyTo(placement, 0);

        return placement;
    }

    /// <summary>The pose a prop is actually AT, following its parent chain up.</summary>
    /// <remarks>
    /// **<c>CalcAbsolutePosition</c>'s bone-merge branch** (<c>c_baseentity.cpp:4387</c>): a
    /// followed entity's absolute origin and angles are its parent's, however deep the chain runs.
    /// An attachment on a weapon on a player therefore ends up at the player, which is what lets
    /// its unmatched bones land somewhere sensible.
    ///
    /// Bounded by the same depth <see cref="AnimatingEntity"/> uses, for the same reason: the wire
    /// should never carry a cycle and a demo this project exists to open may carry anything. A
    /// chain that runs past it keeps the prop's own pose, which draws it in the wrong place rather
    /// than not at all — the milder of the two failures, and the one that leaves something to see.
    /// </remarks>
    private ScenePose Absolute(SceneProp prop, int budget)
    {
        if (prop.AttachedTo is not { } wearer || budget <= 0)
        {
            return prop.Pose;
        }

        if (!_propsByEntity.TryGetValue(wearer, out SceneProp? parent))
        {
            return prop.Pose;
        }

        ScenePose above = Absolute(parent, budget - 1);

        // **Branch 2 — `EF_BONEMERGE`.** `MoveToAimEnt` gives the follower its parent's place
        // outright, and its own origin is the zero `FollowEntity` wrote.
        if (prop.BoneMerged)
        {
            return above;
        }

        // **Branch 3, which this method used to skip** (B241). `c_baseentity.cpp:4396`:
        //
        //   AngleMatrix( GetLocalAngles(), matEntityToParent );
        //   MatrixSetColumn( GetLocalOrigin(), 3, matEntityToParent );
        //   ConcatTransforms( GetParentToWorldTransform( … ), matEntityToParent, m_rgflCoordinateFrame );
        //
        // Returning the parent's pose for everything is the bone-merge branch applied to entities
        // that are not merged, and it throws the child's own ANGLES away. A setup gate's grate is
        // rotated to face its doorway and the three gates on `cp_fulgur` face different ways, so
        // one of them drew a quarter turn out — the owner's *"that one gate on the left is rotated
        // 90 degreed"*, reported before any of this was understood.
        PropTransform composed = new PropTransform(
                above.X, above.Y, above.Z, above.Pitch, above.Yaw, above.Roll, above.Scale)
            .Concat(new PropTransform(
                prop.Pose.X, prop.Pose.Y, prop.Pose.Z,
                prop.Pose.Pitch, prop.Pose.Yaw, prop.Pose.Roll, prop.Pose.Scale));

        (float x, float y, float z) = composed.Apply(0f, 0f, 0f);

        // **The angle shortcut is Valve's and it is CONDITIONAL** (`:4406`): a child with no angles
        // of its own and no parent attachment copies the parent's absolute angles; anything else
        // extracts them from the composed matrix. Applying the shortcut unconditionally is what
        // discarded the gate's quarter turn.
        bool declaresNoAngles =
            prop.Pose.Pitch == 0f && prop.Pose.Yaw == 0f && prop.Pose.Roll == 0f;

        (float pitch, float yaw, float roll) = declaresNoAngles && prop.AttachmentPoint is null
            ? (above.Pitch, above.Yaw, above.Roll)
            : composed.Angles();

        return prop.Pose with
        {
            X = x,
            Y = y,
            Z = z,
            Pitch = pitch,
            Yaw = yaw,
            Roll = roll,
        };
    }

    /// <summary>This frame's props by entity index, so a parent chain can be walked.</summary>
    private readonly Dictionary<int, SceneProp> _propsByEntity = [];

    /// <summary>Bone-to-world folded with the model's bind pose, which is what the shader skins by.</summary>
    /// <remarks>
    /// **The one place <c>poseToBone</c> is applied, and it belongs here** — finding 35 section 7a.
    /// <c>IStudioRender::DrawModel</c> takes bone-to-world and nothing else
    /// (<c>istudiorender.h:329</c>); the composition happens at the boundary that owns the vertices.
    /// Keeping it out of the pose path is what leaves one array per entity with nothing to choose
    /// wrongly between.
    /// </remarks>
    private float[][] Skinning(int entity, IReadOnlyList<StudioBone> bones, BoneAccessor accessor)
    {
        // **Reused per entity, because this ran once per bone per drawn prop per FRAME.** At around
        // 250 props of eighty bones that is twenty thousand twelve-float arrays every frame, and
        // the allocating overload was being called for every one of them.
        //
        // Measured 2026-08-25: gen0 was collecting 34 times a second. The first attempt at this
        // fixed FromQuaternion and the O(n²) override scan and bought about 20 ms of the 545 —
        // because THIS was the cost, and it was left alone. A plausible fix to the wrong line reads
        // exactly like a fix that did not help.
        //
        // Safe to reuse because an instance list is consumed within the frame that built it, and
        // the viewmodel's entities carry their own indices (4096-4098) so they cannot collide with
        // a world entity's buffer.
        if (!_skinning.TryGetValue(entity, out float[][]? buffer) ||
            buffer.Length != accessor.Count)
        {
            buffer = new float[accessor.Count][];

            for (int bone = 0; bone < buffer.Length; bone++)
            {
                buffer[bone] = new float[12];
            }

            _skinning[entity] = buffer;
        }

        for (int bone = 0; bone < accessor.Count; bone++)
        {
            StudioBones.Concatenate(
                accessor.Bone(bone), bones[bone].PoseToBone.Span, buffer[bone]);
        }

        return buffer;
    }

    /// <summary>Each entity's skinning matrices, reused between frames.</summary>
    private readonly Dictionary<int, float[][]> _skinning = [];

    /// <summary>Reports a viewmodel whose posed VERTICES stop covering any space.</summary>
    /// <param name="modelPath">Which model.</param>
    /// <param name="bones">Its skinning matrices this frame.</param>
    /// <param name="body">The entity's <c>m_nBody</c>, so hidden bodygroups are not measured.</param>
    /// <remarks>
    /// **The real measurement, replacing a proxy that had no established meaning** (B222). The first
    /// attempt measured the span of the skinning matrices' translation columns — but a skinning
    /// matrix is <c>Concatenate(boneToWorld, poseToBone)</c>, so its translation is not the bone's
    /// world position and the span of those columns is a mixture of placement and bind offset. It
    /// correlated with the defect by timing and could not be interpreted.
    ///
    /// This applies the matrices to the model's own corners exactly as the GPU will, and measures
    /// the box they land in. That is the quantity the symptom is about: a model covering no space
    /// draws nothing, whatever its matrices look like individually.
    ///
    /// **Sampled every sixteenth corner**, because this runs per frame for the viewmodel rather than
    /// once per model. A collapse is a property of the whole set, so a sixteenth of it is ample —
    /// and the alternative is walking twenty thousand corners a frame to answer a yes/no.
    ///
    /// **Transition-logged**, like every other instrument this hunt produced: the event lasts 60 ms
    /// and a sampled line cannot see it. See `docs/memory/log-the-event-not-a-sample-of-it.md`.
    /// </remarks>
    private void ReportPosedSize(string modelPath, IReadOnlyList<float[]> bones, int body)
    {
        if (!_raw.TryGetValue(modelPath, out IReadOnlyList<PropVertex>? corners) ||
            corners.Count == 0)
        {
            return;
        }

        // **Hidden bodygroups are excluded, and leaving them in made this instrument lie** (B222).
        // The first version measured every corner, so a StatTrak module — a bodygroup the entity is
        // NOT showing, hanging off `c_weapon_stattrack`, which is the one bone that does not merge
        // onto the arms — drifted with the weapon's own animation and inflated the measured span
        // from 27 units to 97 on a 28-unit model. That read as the weapon being torn apart when
        // nothing drawn had moved at all.
        //
        // A measurement of geometry the renderer discards is a measurement of nothing, and this one
        // produced a confident finding before it was caught.
        IReadOnlyList<(int Base, int Count)>? parts =
            _frames.TryGetValue(modelPath, out PropModels.ModelFrames? frames)
                ? frames.BodyParts
                : null;

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        // **Where each BONE's own vertices end up, so the outlier can be named rather than
        // inferred** (B222). The span alone says the model is stretched; it cannot say which bone is
        // dragging it. Accumulated per bone over the vertices that are weighted mostly to it — the
        // one whose centroid sits far from the others is the bone that is not following the rest,
        // and that is the last hop between "the weapon is the wrong shape" and a cause.
        Span<float> boneX = stackalloc float[MaximumReportedBones];
        Span<float> boneY = stackalloc float[MaximumReportedBones];
        Span<float> boneZ = stackalloc float[MaximumReportedBones];
        Span<int> boneCount = stackalloc int[MaximumReportedBones];

        boneX.Clear();
        boneY.Clear();
        boneZ.Clear();
        boneCount.Clear();

        for (int at = 0; at < corners.Count; at += 16)
        {
            PropVertex corner = corners[at];

            float total = corner.Weights.First + corner.Weights.Second + corner.Weights.Third;

            if (total <= 0f || !Shows(parts, corner.BodyPart, corner.BodyModel, body))
            {
                continue;
            }

            Span<byte> which = [corner.Bones.First, corner.Bones.Second, corner.Bones.Third];
            Span<float> howMuch =
                [corner.Weights.First, corner.Weights.Second, corner.Weights.Third];

            float x = 0f, y = 0f, z = 0f;

            for (int slot = 0; slot < 3; slot++)
            {
                if (howMuch[slot] <= 0f || which[slot] >= bones.Count)
                {
                    continue;
                }

                float[] m = bones[which[slot]];
                float share = howMuch[slot] / total;

                x += share * ((m[0] * corner.X) + (m[1] * corner.Y) + (m[2] * corner.Z) + m[3]);
                y += share * ((m[4] * corner.X) + (m[5] * corner.Y) + (m[6] * corner.Z) + m[7]);
                z += share * ((m[8] * corner.X) + (m[9] * corner.Y) + (m[10] * corner.Z) + m[11]);
            }

            minX = MathF.Min(minX, x);
            minY = MathF.Min(minY, y);
            minZ = MathF.Min(minZ, z);
            maxX = MathF.Max(maxX, x);
            maxY = MathF.Max(maxY, y);
            maxZ = MathF.Max(maxZ, z);

            // Attributed to the bone carrying the most of this vertex, which is the one that
            // decides where it lands.
            int dominant = corner.Bones.First;
            float heaviest = corner.Weights.First;

            if (corner.Weights.Second > heaviest)
            {
                dominant = corner.Bones.Second;
                heaviest = corner.Weights.Second;
            }

            if (corner.Weights.Third > heaviest)
            {
                dominant = corner.Bones.Third;
            }

            if (dominant < MaximumReportedBones)
            {
                boneX[dominant] += x;
                boneY[dominant] += y;
                boneZ[dominant] += z;
                boneCount[dominant]++;
            }
        }

        if (maxX < minX)
        {
            return;
        }

        float size = MathF.Max(maxX - minX, MathF.Max(maxY - minY, maxZ - minZ));

        // A weapon is tens of units long. Under one unit it is not on screen as anything.
        string band = size < 1f ? "NO SIZE" : "drawable";

        (float X, float Y, float Z) centre =
            ((minX + maxX) / 2f, (minY + maxY) / 2f, (minZ + maxZ) / 2f);

        // **Where it is relative to the HANDS, from the same posed vertices.** Size alone answered
        // its question — every viewmodel stays a healthy 28 to 65 units across, including while the
        // weapon is invisible — so what is left is placement, and this is the only measurement of it
        // that means anything: both centres come from real corners under the real matrices, taken in
        // the SAME frame. An earlier attempt compared two centres three seconds apart and reported a
        // 4,400-unit error that did not exist (B222).
        if (modelPath.Contains("_arms", StringComparison.OrdinalIgnoreCase))
        {
            _armsCentre = centre;
        }

        // **Where the ARMS are relative to the EYE, which nothing has measured.** Every placement
        // number so far has been the weapon's distance from the arms, and the arms reported only
        // "arms" — so a pose that carries the whole viewmodel behind the near plane keeps its size,
        // keeps its shape, keeps the weapon correctly attached to it, and blanks both models with
        // every instrument silent. That is the shape of the reported defect: sticky-specific (the
        // charge is what changes the arms' sequence), and both models vanishing together (the weapon
        // merges onto the arms, so it goes wherever they go).
        string place = "arms";

        if (modelPath.Contains("_arms", StringComparison.OrdinalIgnoreCase) &&
            ViewmodelEye is { } eye)
        {
            float ex = centre.X - eye.X;
            float ey = centre.Y - eye.Y;
            float ez = centre.Z - eye.Z;

            float fromEye = MathF.Sqrt((ex * ex) + (ey * ey) + (ez * ez));

            place = $"{(int)(fromEye / 5f) * 5}-{((int)(fromEye / 5f) * 5) + 5}u from eye";
        }

        if (_armsCentre is { } hands &&
            !modelPath.Contains("_arms", StringComparison.OrdinalIgnoreCase))
        {
            float dx = centre.X - hands.X;
            float dy = centre.Y - hands.Y;
            float dz = centre.Z - hands.Z;

            // **Bucketed to five units, not thresholded at a hundred.** The first version asked
            // "is it further than 100 units from the hands" and answered no throughout, which is
            // true and useless: a weapon fifty units from the eye is entirely off screen and reads
            // as "with the hands". A weapon sits within tens of units of the eye, so the bucket has
            // to be of that order for movement to appear at all. Same mistake as the one-second
            // sample and the hundred-unit threshold before it — a resolution chosen without asking
            // what size of effect had to be visible.
            float away = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            place = $"{(int)(away / 5f) * 5}-{((int)(away / 5f) * 5) + 5}u from hands";
        }

        _posedSize.TryGetValue(modelPath, out string? was);

        if ($"{band}/{place}" == was)
        {
            return;
        }

        _posedSize[modelPath] = $"{band}/{place}";

        // **The per-bone centroids, printed when the model is stretched.** A sticky launcher is 28
        // units long; past 40 it is being pulled apart, and this says by which bone. Their names
        // come from the `bone merge` line, which prints `name[index]`.
        string spread = string.Empty;

        // 35 rather than 40, so the ARMS report too. Their normal span is around 28 and they have
        // been measured at 49 to 55 — which was read as ordinary variation all evening and may be
        // the same stretch, less obvious only because far fewer of their vertices ride the bone
        // that is out of place.
        if (size > 35f)
        {
            List<string> where = [];

            for (int bone = 0; bone < MaximumReportedBones; bone++)
            {
                if (boneCount[bone] == 0)
                {
                    continue;
                }

                where.Add(
                    $"[{bone}] {boneCount[bone]}v at ({boneX[bone] / boneCount[bone]:0.#}, " +
                    $"{boneY[bone] / boneCount[bone]:0.#}, {boneZ[bone] / boneCount[bone]:0.#})");
            }

            spread = $" bones: {string.Join("; ", where)}";
        }

        _props.LogDebug(
            "{Message}",
            $"{System.IO.Path.GetFileNameWithoutExtension(modelPath)} posed size changed: " +
            $"{was ?? "(first)"} -> {band}/{place}, {size:0.##} units across, " +
            $"at ({centre.X:0.#}, {centre.Y:0.#}, {centre.Z:0.#}){spread}");
    }

    /// <summary>How many bones the stretch report will attribute vertices to.</summary>
    /// <remarks>
    /// A viewmodel weapon has a handful — the sticky launcher has five. Bounded so the accumulators
    /// can sit on the stack and a player-sized skeleton cannot be walked here by accident.
    /// </remarks>
    private const int MaximumReportedBones = 16;

    /// <summary>Whether a corner's body part is the alternative this entity shows.</summary>
    /// <remarks>
    /// <c>GetBodygroup</c>, <c>shared/animation.cpp:876</c> — a part's choice is the body number
    /// divided by that part's base, modulo how many alternatives it has. Same arithmetic the
    /// renderer applies to batches; applied here so a diagnostic measures what is DRAWN.
    /// </remarks>
    private static bool Shows(
        IReadOnlyList<(int Base, int Count)>? parts, int bodyPart, int bodyModel, int body)
    {
        if (parts is not { Count: > 0 } || bodyPart < 0 || bodyPart >= parts.Count)
        {
            return bodyModel == 0;
        }

        (int place, int count) = parts[bodyPart];

        return place <= 0 || count <= 0
            ? bodyModel == 0
            : bodyModel == (body / place) % count;
    }

    /// <summary>Whether each viewmodel's posed vertices last covered any space, and where.</summary>
    private readonly Dictionary<string, string> _posedSize = [];

    /// <summary>Where the first-person eye is, so a viewmodel can be measured against it (B222).</summary>
    /// <remarks>
    /// Set by the caller that knows the camera, before the viewmodel props are instanced. Null for
    /// every world pass, which is what keeps this out of the two hundred props a frame.
    /// </remarks>
    public (float X, float Y, float Z)? ViewmodelEye { get; set; }

    /// <summary>The view a viewmodel is drawn through, for <c>FormatViewModelAttachment</c>.</summary>
    /// <remarks>
    /// **Set by the caller that knows the camera, beside <see cref="ViewmodelEye"/>**, and null for
    /// every world pass — which is what keeps the correction off the two hundred props a frame that
    /// must not receive it. `SetupBones_AttachmentHelper` calls the correction unconditionally and
    /// relies on the base implementation being an empty body; null here is that empty body.
    ///
    /// Carries the whole of what `FormatViewModelAttachment` reads out of `CViewSetup` and the main
    /// view vectors, so the correction cannot derive any of it a second way.
    /// </remarks>
    public ViewmodelProjection? ViewmodelProjection { get; set; }

    /// <summary>Where the view is, for the distance fade; null leaves everything unfaded.</summary>
    /// <remarks>
    /// **Set from the same `MomentInfo` the frustum came from** (B268), so the fade and the cull
    /// measure from one camera. Null when no eye has been established — a frame before a demo is
    /// open — and the fade then answers 255 rather than measuring from the map origin, which
    /// would fade out everything far from (0,0,0).
    /// </remarks>
    public (float X, float Y, float Z)? ViewOrigin { get; set; }

    /// <summary>Where the arms were posed this frame, as the weapon's reference.</summary>
    /// <remarks>
    /// The props are built arms first, so this is set before any weapon consults it. Null until the
    /// first arms model is posed, which is the state where a weapon has nothing to be measured
    /// against and is reported as being with the hands rather than guessed at.
    /// </remarks>
    private (float X, float Y, float Z)? _armsCentre;

    /// <summary>Measures a skinned model with its pose applied on the processor.</summary>
    private void ReportPosedExtents(
        string modelPath, IReadOnlyList<float[]> bones, string? label = null)
    {
        if (!_raw.TryGetValue(modelPath, out IReadOnlyList<PropVertex>? corners))
        {
            return;
        }

        float minimumX = float.MaxValue, minimumY = float.MaxValue, minimumZ = float.MaxValue;
        float maximumX = float.MinValue, maximumY = float.MinValue, maximumZ = float.MinValue;
        int weighted = 0;

        foreach (PropVertex corner in corners)
        {
            float total = corner.Weights.First + corner.Weights.Second + corner.Weights.Third;

            if (total <= 0f)
            {
                continue;
            }

            weighted++;

            Span<byte> which = [corner.Bones.First, corner.Bones.Second, corner.Bones.Third];
            Span<float> howMuch =
                [corner.Weights.First, corner.Weights.Second, corner.Weights.Third];

            float x = 0f, y = 0f, z = 0f;

            for (int slot = 0; slot < 3; slot++)
            {
                if (howMuch[slot] <= 0f || which[slot] >= bones.Count)
                {
                    continue;
                }

                float[] matrix = bones[which[slot]];
                float share = howMuch[slot] / total;

                x += share * ((matrix[0] * corner.X) + (matrix[1] * corner.Y) + (matrix[2] * corner.Z) + matrix[3]);
                y += share * ((matrix[4] * corner.X) + (matrix[5] * corner.Y) + (matrix[6] * corner.Z) + matrix[7]);
                z += share * ((matrix[8] * corner.X) + (matrix[9] * corner.Y) + (matrix[10] * corner.Z) + matrix[11]);
            }

            minimumX = MathF.Min(minimumX, x);
            minimumY = MathF.Min(minimumY, y);
            minimumZ = MathF.Min(minimumZ, z);
            maximumX = MathF.Max(maximumX, x);
            maximumY = MathF.Max(maximumY, y);
            maximumZ = MathF.Max(maximumZ, z);
        }

        // **All three ranges, because which axis is "up" is a property of the model.** A hat is a
        // few inches tall wherever it is, so its SIZE says nothing about whether it is on a head;
        // where it sits does. Reporting only the z range assumed z was up and this bind pose is
        // Y-up - a player model measures 84 along Y and bip_head rests at (0, 75, -1) - so a hat
        // correctly on the head reads as "z from -16 to -2" and looks like a hat on the floor.
        //
        // That mistake cost a full round of investigation, and it is the same one as measuring at
        // a tick the demo does not contain: an instrument answering confidently about the wrong
        // quantity.
        // **The root bone's own matrix, because extents cannot separate a rotation from a move.**
        // Two poses of one model came out as the same three ranges with the axes cycled, which is
        // a basis change rather than a different pose — but the extents alone cannot say whether
        // the translation came with it. The matrix can: its last column is where the root sits and
        // its first three are what it does to the axes.
        string root = bones.Count > 0 && bones[0] is { Length: >= 12 } first
            ? $"root [{first[0]:0.##} {first[1]:0.##} {first[2]:0.##} | " +
              $"{first[4]:0.##} {first[5]:0.##} {first[6]:0.##} | " +
              $"{first[8]:0.##} {first[9]:0.##} {first[10]:0.##}] " +
              $"at ({first[3]:0.#}, {first[7]:0.#}, {first[11]:0.#})"
            : "root none";

        // **Debug, and this one had the largest volume of the lot** — 338 lines in four minutes,
        // because it fires the first time each model, each worn item and each corner comparison is
        // posed, and models keep first appearing throughout a match. Each line is a disk flush
        // (B191), and this call also does real work to produce it: extents over every corner, plus
        // a second full skeleton built for the CORNER comparison.
        _props.LogDebug(
            "{Message}",
            $"posed {label ?? modelPath}: {weighted} of {corners.Count} corners weighted, " +
            $"{bones.Count} bones, x {minimumX:0.#}..{maximumX:0.#} " +
            $"y {minimumY:0.#}..{maximumY:0.#} z {minimumZ:0.#}..{maximumZ:0.#}, {root}");
    }

    /// <summary>Every packed model's triangles, in model space.</summary>
    /// <remarks>
    /// Uploaded once. The vertices never move again — that is the whole point of the arrangement.
    /// </remarks>
    public IReadOnlyList<WorldVertex> Vertices => _vertices;

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

    /// <summary>The first sequence of a model whose activity contains a fragment.</summary>
    /// <param name="modelPath">The model.</param>
    /// <param name="fragment">Part of an activity name, such as <c>VM_IDLE</c>.</param>
    /// <returns>A merged sequence number, or −1 when nothing claims it.</returns>
    /// <remarks>
    /// **Asked by NAME because a demo's sequence number cannot be checked against anything.** The
    /// viewmodel arms were playing merged sequence 1, which on an arms model is <c>r_handposes</c> —
    /// a one-frame pose holder that leaves the root bone at identity and the model in its authored
    /// Y-up space. The animations a viewmodel actually plays begin at 2 and carry
    /// <c>ACT_*_VM_IDLE</c> and its neighbours.
    /// </remarks>
    public int SequenceByActivity(string modelPath, string fragment)
    {
        if (!_frames.TryGetValue(modelPath, out PropModels.ModelFrames? frames))
        {
            // **Three ways to answer "no" and they are different faults**, which is the whole
            // reason they are separated: the model was never packed, the model was packed without
            // a skeleton, or the skeleton simply has no such activity. A single −1 sent this
            // investigation at a sequence list that turned out to contain exactly what was being
            // looked for.
            _render.LogWarning(
                "no packed frames for {Model}, so no activity lookup", modelPath);
            return -1;
        }

        if (frames.Skinned is not { } skinned)
        {
            _render.LogWarning(
                "{Model} was baked rather than skinned, so it has no sequence table", modelPath);
            return -1;
        }

        return skinned.SequenceByActivity(fragment);
    }

    /// <summary>Chooses each drawn player's sequence, now that their models are loaded.</summary>
    /// <param name="drawn">The draw list, updated in place.</param>
    /// <exception cref="ArgumentNullException"><paramref name="drawn"/> is null.</exception>
    /// <remarks>
    /// **Named for Valve's own pass, because it IS Valve's own pass.**
    /// <c>C_BaseAnimating::UpdateClientSideAnimations()</c> (<c>c_baseanimating.cpp:6368</c>) is a
    /// static batch walk over <c>g_ClientSideAnimationList</c> calling
    /// <c>UpdateClientSideAnimation()</c> on each — a loop over a list, exactly this shape, rather
    /// than something each entity does for itself. Per entity it reaches
    /// <c>CMultiPlayerAnimState::ComputeMainSequence</c> (<c>multiplayer_animstate.cpp:1125</c>),
    /// which is what <see cref="SequenceFor"/> stands in for.
    ///
    /// **And the ORDER is Valve's too**, which is worth stating because it is the part that is easy
    /// to get wrong later. `cdll_client_int.cpp:2188-2210` runs
    /// `UpdateClientSideAnimations()` → `SimulateEntities()` → `ThreadedBoneSetup()`, so sequence
    /// selection happens BEFORE simulation and before any bone is built. Ours matches: this runs
    /// before <see cref="Instances"/>, which does <c>Simulate</c> and then the bones.
    ///
    /// **After the models are loaded, and that is a real constraint rather than a convenience.**
    /// Nothing on the wire carries a player's sequence, and choosing one needs the model's merged
    /// sequence table — which does not exist until <see cref="Add(IReadOnlyList{SceneProp})"/> has
    /// read it. Asked earlier it
    /// answers -1, and -1 is a real answer meaning "no such sequence", so an early call looks like a
    /// lookup that failed rather than one that ran too soon.
    ///
    /// Lived in <c>MainForm.ShowMoment</c> until 2026-08-25 (B188).
    /// </remarks>
    public void UpdateClientSideAnimations(IList<SceneProp> drawn)
    {
        ArgumentNullException.ThrowIfNull(drawn);

        for (int index = 0; index < drawn.Count; index++)
        {
            SceneProp prop = drawn[index];

            // **A corpse's death animation, resolved here because this is where a prop's sequence is
            // decided** (B323). It comes BEFORE the speed guard rather than after: a corpse carries
            // no speed — it is not a player and never reaches the activity state machine — so a
            // corpse placed below that `continue` would be skipped and the branch would be another
            // no-op with a green suite.
            //
            // `ResetSequence( iDeathSeq )` is what the engine does with the answer
            // (`c_tf_player.cpp:851`); the label comes from `LookupSequence`, so it is matched by
            // label and never by activity.
            if (prop.DeathSequence is { Length: > 0 } wanted)
            {
                int death = _frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? bodies) &&
                    bodies.Skinned is { } corpse
                    ? corpse.SequenceByLabel(wanted)
                    : -1;

                if (death >= 0)
                {
                    drawn[index] = prop with { Pose = prop.Pose with { Sequence = death } };
                }

                continue;
            }

            if (prop.Pose.Speed is not { } speed)
            {
                continue;
            }

            int chosen = SequenceFor(
                prop.ModelPath,
                speed,
                prop.Pose.Flags,

                // **True because the dead never reach here, not because death is ignored.**
                // `PlayerProps.ModelFor` refuses a player the engine would not draw, and TF2 turns
                // a dead player off with EF_NODRAW while a separate CTFRagdoll becomes the corpse.
                //
                // An earlier comment claimed a ragdoll was already doing that job, which was false
                // in both directions: nothing here draws ragdolls, and dead players WERE reaching
                // this call. With their ground flag clear they were then given ACT_MP_JUMP_FLOAT,
                // so seventeen seconds of a respawn drew a soldier falling through the air.
                alive: true,

                // The weapon's suffix, or the primary forms when nothing resolved it — which is
                // what the engine falls back to as well.
                slot: prop.Pose.Slot ?? "PRIMARY",

                // Splits the jump into its push-off and its float.
                airborneSeconds: prop.Pose.AirborneSeconds,

                // Supersedes the jump for a fast-rising player.
                airwalking: prop.Pose.Airwalking,

                // Waist deep turns a jump into a swim.
                waterLevel: prop.Pose.WaterLevel);

            // **A negative answer is left alone rather than written.** -1 means "this model has no
            // such sequence", and storing it would replace a working sequence with one that decodes
            // to nothing — a model frozen on frame zero, which reads as a broken animation rather
            // than as a failed lookup.
            if (chosen >= 0)
            {
                drawn[index] = prop with { Pose = prop.Pose with { Sequence = chosen } };
            }
        }
    }

    /// <summary>Which sequence a player of this model should play.</summary>
    /// <param name="modelPath">The model's path.</param>
    /// <param name="speed">Horizontal speed in units a second.</param>
    /// <param name="flags">
    /// The player's <c>m_fFlags</c>, carrying the crouch and ground bits, or null when the recording
    /// did not say. Not a POV-versus-SourceTV distinction, as this said before B103: the property is
    /// on <c>DT_BasePlayer</c> and reaches every player in the PVS.
    /// </param>
    /// <param name="alive">Whether the player is alive.</param>
    /// <param name="slot">The suffix the held weapon drives, such as <c>SECONDARY</c>.</param>
    /// <param name="airborneSeconds">How long since they left the ground, or null.</param>
    /// <param name="airwalking">Whether they are air-walking, which supersedes the jump.</param>
    /// <param name="waterLevel">How deep in water they are; 2 or more is waist deep.</param>
    /// <returns>A merged sequence number, or −1 when the model is not skinned or has neither.</returns>
    /// <remarks>
    /// Asked of the set rather than of the model directly, because only the set knows whether a
    /// model was loaded skinned - a baked model has no merged sequence table to search.
    /// </remarks>
    public int SequenceFor(
        string modelPath,
        float speed,
        int? flags = null,
        bool alive = true,
        string slot = "PRIMARY",
        float? airborneSeconds = null,
        bool airwalking = false,
        int? waterLevel = null) =>
        _frames.TryGetValue(modelPath, out PropModels.ModelFrames? frames) &&
        frames.Skinned is { } skinned
            ? PlayerAnimation.For(
                skinned, speed, flags, alive, slot, airborneSeconds, airwalking, waterLevel)
            : -1;

    /// <summary>Every baked frame's batches for one model.</summary>
    /// <param name="modelPath">The model's path.</param>
    /// <returns>One entry per baked frame, each a list of runs.</returns>
    public IReadOnlyList<IReadOnlyList<WorldBatch>> AllFrames(string modelPath) =>
        _byModel.TryGetValue(modelPath, out List<List<WorldBatch>>? frames) ? frames : [];

    /// <summary>A body number with one named body part set to one of its alternatives.</summary>
    /// <param name="modelPath">The model whose parts are being addressed.</param>
    /// <param name="group">The part's name, as <c>FindBodygroupByName</c> takes it.</param>
    /// <param name="value">Which alternative.</param>
    /// <returns>The body number, or zero when this model has no such part.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **The pair of engine functions, together, because separately they invite the wrong
    /// arithmetic.** `FindBodygroupByName` (`shared/animation.cpp:927`) turns a name into an index
    /// and `SetBodygroup` (`:863`) folds a value into the number:
    ///
    /// <code>
    ///   int iCurrent = ( body / pbodypart-&gt;base ) % pbodypart-&gt;nummodels;
    ///   body = ( body - ( iCurrent * pbodypart-&gt;base ) + ( iValue * pbodypart-&gt;base ) );
    /// </code>
    ///
    /// Parts share one integer like digits of a mixed-radix number, so this is not an OR.
    ///
    /// **Zero when the model is not loaded YET**, which is a real state: a model is packed on first
    /// sight, so the first frame a spy appears on answers zero and the next answers correctly. The
    /// alternative would be to block the scene on a model load, which is worse than one frame of an
    /// unmasked spy.
    /// </remarks>
    public int WithBodygroup(string modelPath, string group, int value)
    {
        ArgumentNullException.ThrowIfNull(modelPath);
        ArgumentNullException.ThrowIfNull(group);

        if (!_frames.TryGetValue(modelPath, out PropModels.ModelFrames? model)
            || model.BodyParts is not { Count: > 0 } parts
            || model.BodyPartNames is not { Count: > 0 } names)
        {
            return 0;
        }

        for (int part = 0; part < names.Count && part < parts.Count; part++)
        {
            if (!string.Equals(names[part], group, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            (int place, int count) = parts[part];

            return place > 0 && value >= 0 && value < count ? value * place : 0;
        }

        return 0;
    }

    /// <summary>Which material paints which skinref, for one skin family.</summary>
    /// <param name="modelPath">The model's path.</param>
    /// <param name="skin">Which family; out of range falls back to zero, as the engine does.</param>
    /// <returns>The family's row, keyed by skinref, or null for a model with no table.</returns>
    /// <remarks>
    /// **Indexed by family, and keyed by SKINREF** (B229). Both were off by one design: the list
    /// held families 1..N so this subtracted one, and each entry was keyed on family zero's
    /// RESOLVED material index — which made family zero load-bearing for every other family and
    /// left a model whose family-zero texture is not shipped undrawable at every skin.
    ///
    /// A mesh's <c>mstudiomesh_t::material</c> is a skinref and
    /// <c>g_skinref[skin][skinref]</c> turns it into a texture index
    /// (<c>utils/motionmapper/motionmapper.h:134</c>), so the row is the whole answer and family
    /// zero is a row like any other. Returned even for skin zero, because the caller then has no
    /// special case to get wrong.
    /// </remarks>
    public IReadOnlyDictionary<int, int>? SkinSwap(string modelPath, int skin)
    {
        if (!_swaps.TryGetValue(modelPath, out IReadOnlyList<IReadOnlyDictionary<int, int>>? swaps)
            || swaps.Count == 0)
        {
            return null;
        }

        // `props_shared.cpp:1079` — a skin the model does not have falls back to family zero rather
        // than being refused. A demo names an entity's `m_nSkin` and this project does not control
        // what it says.
        return swaps[skin >= 0 && skin < swaps.Count ? skin : 0];
    }

    /// <summary>Which baked frame a prop's sequence and cycle select.</summary>
    /// <param name="prop">The prop, carrying the sequence and cycle the demo networked.</param>
    /// <param name="seconds">Demo time, for advancing the cycle the server does not send.</param>
    /// <returns>A frame index for <see cref="Batches(string, int)"/>.</returns>
    public int FrameFor(SceneProp prop, double seconds) => SelectFor(prop, seconds).Frame;

    /// <summary>Which baked frames a prop falls between, and how far.</summary>
    /// <param name="prop">The prop, carrying the sequence and cycle the demo networked.</param>
    /// <param name="seconds">Demo time, for advancing the cycle the server does not send.</param>
    /// <returns>The frame to draw, the one after it, and the blend between them.</returns>
    public (int Frame, int Next, float Blend) SelectFor(SceneProp prop, double seconds)
    {
        ArgumentNullException.ThrowIfNull(prop);

        return
        _frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? frames)
            ? frames.Select(
                prop.Pose.Sequence,
                prop.Pose.Cycle,
                seconds,
                prop.Pose.PlaybackRate,

                // **The stamp, which this call was not passing** (B237). The timeline records when
                // each animation began — `AnimationStartSeconds`, from the parity counter and the
                // client-side frame reset — and the SKINNED path in `Simulate` has used it since it
                // was added. This one, which every baked prop takes, did not, so a cabinet's `open`
                // was measured from the start of the recording and clamped to its final frame
                // before it drew once.
                prop.Pose.AnimationStartSeconds)
            : (0, 0, 0f);
    }

    /// <summary>Whether a model kind has geometry this renderer can draw.</summary>
    /// <param name="kind">What the model reference resolved to.</param>
    /// <returns>Whether it can be packed and drawn.</returns>
    /// <remarks>
    /// **One predicate, because it was two tests that had to agree and nothing made them.** The
    /// packing loop and the draw loop each carried their own <c>Kind != Studio</c>, so admitting
    /// brush entities meant changing the same rule in two places — and the two failures are not
    /// alike: a model packed but never drawn is silent, while one drawn but never packed is a
    /// lookup miss reported as a load failure.
    ///
    /// A sprite is a camera-facing quad with no geometry of its own and is still not drawn.
    /// Unknown is <c>mod_bad</c>: the reference never resolved, so there is nothing to look up.
    /// </remarks>
    private static bool IsDrawable(SceneModelKind kind) =>
        kind is SceneModelKind.Studio or SceneModelKind.Brush;

    /// <summary>Whether this prop names something that can be drawn at all.</summary>
    /// <param name="prop">The prop.</param>
    /// <returns>Whether it has a drawable kind AND a model path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prop"/> is null.</exception>
    /// <remarks>
    /// **The engine's precondition, not a policy of ours**: an entity whose <c>GetModel()</c> is
    /// null is never added to a renderable list, so nothing downstream of visibility — bones,
    /// lighting, the draw — ever sees it. A drawable KIND with an empty PATH is exactly that case:
    /// a cosmetic whose model the wire never carried and the item schema has not supplied.
    ///
    /// **Extracted so the probes ask the same question production does** (PARITY-AUDIT finding 5).
    /// `DrawnPropProbe` labelled a prop DRAWN off the visibility rules alone, and an empty-path
    /// `CTFWearableRobotArm` therefore reported as drawn while the renderer produced nothing —
    /// the probe reporting rather than the renderer failing. A probe that reimplements the rule it
    /// is checking agrees with whoever wrote the probe, so this is the one definition and both
    /// sides read it.
    /// </remarks>
    public static bool CanDraw(SceneProp prop)
    {
        ArgumentNullException.ThrowIfNull(prop);

        return IsDrawable(prop.Kind) && prop.ModelPath.Length > 0;
    }

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
        _byModel.Clear();
        _frames.Clear();
        _swaps.Clear();
        _raw.Clear();

        _entities.Clear();
        _entityModels.Clear();
        _placements.Clear();
        _lightPoints.Clear();
        _drawnPlacements.Clear();
        _parentPlacements.Clear();
        _propsByEntity.Clear();
        _skinning.Clear();
        _posedEntities.Clear();
        _eventStates.Clear();
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

        bool added = false;

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
                Dictionary<(int Material, int Slot, int Part, int Model), List<WorldVertex>>
                    byMaterial = [];

                for (int index = 0; index < corners.Count; index++)
                {
                    PropVertex corner = corners[index];

                    PropVertex ahead = index < onward.Count ? onward[index] : corner;

                    (int Material, int Slot, int Part, int Model) key = (
                        corner.MaterialIndex,
                        corner.MaterialSlot,
                        corner.BodyPart,
                        corner.BodyModel);

                    if (!byMaterial.TryGetValue(key, out List<WorldVertex>? into))
                    {
                        into = [];
                        byMaterial[key] = into;
                    }

                    // **Model space, untouched.** The shader's model matrix places it.
                    //
                    // **The lightmap coordinates are zero for a studio model and real for a brush
                    // entity (B131).** A `.mdl` is lit by its own vertex colours in the engine too,
                    // and (0, 0) is the atlas's reserved white texel, so the lightmap term is an
                    // identity rather than darkness. A door is the opposite case: vrad lights every
                    // model's faces (vrad.cpp:703) and its samples are already in the same atlas as
                    // the wall's, so passing them is all that separates a lightmapped door from a
                    // flat one. BrushModels fills them; PropModels leaves them at zero.
                    into.Add(new WorldVertex(
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

                        // **Without these the shader skins by nothing.** A skinned model's
                        // geometry is uploaded unposed, so the bones are the only thing that
                        // stands it up - and a vertex with no weights is left exactly where the
                        // artist modelled it, which for a player is lying along Y. The fields
                        // existed on the vertex and in the packer and were never filled in here,
                        // so every player drew in its raw modelling pose while being lit
                        // correctly, which reads as a lighting change rather than a missing
                        // transform.
                        BoneA: corner.Bones.First,
                        BoneB: corner.Bones.Second,
                        BoneC: corner.Bones.Third,
                        WeightA: corner.Weights.First,
                        WeightB: corner.Weights.Second,
                        WeightC: corner.Weights.Third));
                }

                foreach (KeyValuePair<(int Material, int Slot, int Part, int Model),
                    List<WorldVertex>> group in byMaterial)
                {
                    batches.Add(new WorldBatch(
                        group.Key.Material,
                        _vertices.Count,
                        group.Value.Count,
                        group.Key.Part,
                        group.Key.Model,
                        MaterialSlot: group.Key.Slot));

                    _vertices.AddRange(group.Value);
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
            synthetic.Add(new SceneProp(0, path, ScenePropTrack.Classify(path), default));
        }

        return Add(synthetic);
    }

    /// <summary>Where each model stands at this moment.</summary>
    /// <param name="props">What exists at this tick.</param>
    /// <param name="into">Filled with one entry per drawable entity; cleared first.</param>
    /// <param name="lightAt">The ambient cube at a world position, or null to leave models unlit.</param>
    /// <param name="sunAt">The sun at a world position, or null to apply no direct light.</param>
    /// <param name="seconds">Demo time, for advancing animation cycles.</param>
    /// <param name="frustum">
    /// The view being drawn, so a prop off screen is rejected before it is posed — the engine's
    /// order (B254). The default culls nothing.
    /// </param>
    /// <param name="visibleByLeaf">
    /// Which leaves the world cull accepted, indexed by leaf, for the visibility half (B254). An
    /// empty span applies no visibility test.
    /// </param>
    /// <param name="pass">
    /// Which pass is drawing, for the tally's report — <c>world</c> or <c>viewmodel</c>. One
    /// <see cref="DrawTally"/> serves both and its line could not say which it counted, so a
    /// viewmodel pass (which is given no frustum, and therefore always reports nothing culled)
    /// read as evidence about the world cull.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// One matrix per entity, which is all that changes between frames. The geometry it points at
    /// was uploaded once and stays where it is.
    /// </remarks>
    public void Instances(
        IReadOnlyList<SceneProp> props,
        ICollection<ModelInstance> into,
        Func<float, float, float, PointLighting>? lightAt = null,
        Func<float, float, float, SunLight?>? sunAt = null,
        double seconds = 0d,
        ViewFrustum frustum = default,
        ReadOnlySpan<bool> visibleByLeaf = default,
        string pass = "world")
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();
        Culled = 0;
        CulledByVisibility = 0;
        Unjudgeable = 0;
        Posed = 0;
        _posedEntities.Clear();

        // **There is no ordering here any more, and that is the change** (D88, B181). The engine has
        // none either: a merged entity asks its parent for bones where it stands
        // (`bone_merge_cache.cpp:130`) and `SetupBones` being idempotent within a frame makes a
        // repeat an integer comparison. What this replaced was six fields and a depth sort solving a
        // problem Valve's structure never creates.
        //
        // Two phases instead, which is the engine's own split
        // (`cdll_client_int.cpp:2206-2210`): bring every entity's state up to date, then build
        // bones on demand while drawing. State first is what lets a merge reach an entity the draw
        // loop has not got to yet.
        _boneFrames.Advance();

        // **Timed as a phase because it IS one** — Valve's first of two
        // (`cdll_client_int.cpp:2206-2210`) — and because everything else in the pose path has now
        // been measured flat while the total swings 3 ms to 136 ms. Splitting SetupBones (1 ms) and
        // Skinning (0.2 ms) out left 127 ms in the remainder, and this is the larger half of it: a
        // pass over EVERY prop, not just the animated ones (B189).
        long simulatedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        Simulate(props, seconds);

        SimulateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - simulatedAt;

        // **Every prop that does not draw is counted with its reason.** A silent `continue` here is
        // how "all the props went away" became a guessing game: the scene said 14 models, the map
        // showed one, and nothing in between reported which test rejected the other thirteen.
        //
        // Four categories, per the project's rule: asked for, what we have, what was produced, what
        // is missing and why.
        _tally.Begin(props.Count, pass);

        // In the order the scene gave them, because nothing needs any other order now.
        foreach (SceneProp prop in props)
        {
            (int frame, int _, float blend) = SelectFor(prop, seconds);

            int skin = prop.Pose.Skin;

            // **An empty path joins the undrawable kinds here, where it used to fall through to
            // "no batches"** (PARITY-AUDIT finding 5). Those two counts mean different things and
            // `DrawTally` says so: no-batches for one model is a load failure, while a prop with
            // no model at all is a gap somebody has to close — which is why `NotDrawable` already
            // carried a `<no model>` label that nothing could reach. Measured on `z1800` at tick
            // 20000: 24 bone-merged cosmetics name no model, so this is not a rare case.
            if (!CanDraw(prop))
            {
                _tally.NotDrawable(prop);
                continue;
            }

            // **`CollateRenderablesInLeaf`'s frustum test, in the engine's ORDER** (B254,
            // `clientleafsystem.cpp:1574`): `CalcRenderableWorldSpaceAABB` and then
            // `engine->CullBox( absMins, absMaxs )`, with only the survivors reaching `DrawModel`
            // and so `SetupBones`. Bone setup, lighting and skinning are downstream of visibility
            // in the engine.
            //
            // **They were upstream of it here, and that is what this moves.** The cull existed —
            // `Device3D.Culled`, same `ViewFrustum.Cull`, same empty-box rule — but it ran at draw
            // time, after every prop in the tick had been posed. Measured on `tf2-2026-pub-pov-clean`:
            // 600 props posed per rebuild, `pose` 4.8 ms of a 7.8 ms rebuild, every column of it
            // per-entity work multiplied by a count visibility had not yet touched.
            //
            // **The box needs no bones**, which is what makes the move possible at all:
            // `WorldBoxFor` reads the model's render bounds, the prop's own pose and its parent's
            // placement, exactly as `CalcRenderableWorldSpaceAABB` reads render bounds and the
            // entity's origin rather than its skeleton.
            if (Culls(prop, frustum, visibleByLeaf))
            {
                Culled++;
                _tally.Culled();
                continue;
            }

            // **`C_BaseEntity::ShouldDraw`'s first test** (`c_baseentity.cpp:1447`): *"Some
            // rendermodes prevent rendering"*, and `kRenderNone` is the one. Eighteen `func_door`s
            // on `cp_fulgur` declare it, their brushwork is painted `METAL/CHICKEN_WIRE001`, and
            // drawing it stood a coarse wire panel in the doorway in front of the grate props.
            //
            // **HERE and not in `EntityState.IsDrawn`, which is where it went first and broke the
            // gates completely** (B240). That property decides whether an entity is in the scene at
            // all; this decides whether it is drawn. Valve keeps them apart for a reason the gates
            // demonstrate: every grate prop is PARENTED to one of those invisible doors, and
            // `CalcAbsolutePosition` composes a child onto its parent's transform without asking
            // whether the parent renders. Remove the parent from the scene and the child has
            // nothing to hang off.
            //
            // Only this mode refuses. Every other value is a blend that still draws.
            if (prop.Pose.RenderMode == RenderModes.None)
            {
                _tally.NotDrawn();
                continue;
            }

            if (Batches(prop.ModelPath, frame).Count == 0)
            {
                _tally.NoGeometry(prop.ModelPath);
                continue;
            }

            _tally.Drawn();

            // **A parented prop is placed by its PARENT's transform, and this loop never asked**
            // (B241). `Simulate` above composes the chain for a SKINNED model — `posed.
            // EntityTransform = PlacementOf(prop)` — and every BAKED prop came through here and was
            // placed at its own pose, which for a parented entity is its LOCAL offset. Every setup
            // gate's grate is a `CDynamicProp` on a `func_door` with a local origin of (0,0,0), so
            // all six drew at the map origin. The door's brushwork stood where they should have
            // been, which is why nobody could see it until `kRenderNone` stopped drawing that.
            ScenePose pose = prop.Pose;

            // **This was changed to compose the parent chain and changed back** (B241). The theory
            // was that a parented prop drew at its LOCAL offset; the measurement says otherwise —
            // with the composition removed again, the viewer still logs
            // `door_grate003_top draws at (5416, -2168, 552)`, the gate. The pose reaching here
            // already carries the world placement, so composing again would apply it twice.
            //
            // Left as it was, deliberately: an unverifiable change to where EVERY prop is drawn is
            // not worth carrying on a theory the evidence does not support.
            PropTransform transform = new(
                pose.X, pose.Y, pose.Z, pose.Pitch, pose.Yaw, pose.Roll, pose.Scale);

            // **Lit, logged and counted by collaborators rather than here** (B181). Each of these
            // was sixty to eighty lines inside this loop, and none of them is about posing a model —
            // which is how the body reached two hundred lines of code with five jobs in it and the
            // engine's stage boundaries invisible.
            ModelLight lit = _lighting.For(prop, lightAt, sunAt);

            AmbientCube? light = lit.Light;
            SunLight? sun = lit.Sun;
            IReadOnlyList<LocalLight> locals = lit.Locals;

            // **The point the cube was sampled at, carried so the RENDERER can choose a cubemap
            // from it** (B170). `ModelLighting.For` already resolved where this model is — via the
            // model's own `illumposition` — and the reflection needs the same answer. Taking it
            // from here rather than recomputing is what keeps lighting and reflection agreeing
            // about a model's position instead of drifting apart.
            (float X, float Y, float Z) origin = (lit.X, lit.Y, lit.Z);

            // **The last unmeasured thing in this loop, and it runs three times per prop per
            // frame.** Everything else has been split and come back at a millisecond or less while
            // the total holds a deterministic ~130 ms (B189). Reporting is the remaining candidate
            // and it is the one that builds strings.
            long reportedAt = System.Diagnostics.Stopwatch.GetTimestamp();

            _reports.BrushMoved(prop, seconds);

            if (lightAt is not null)
            {
                _reports.Lit(prop, lit, skin);
            }

            _reports.Animating(prop, frame, AllFrames(prop.ModelPath).Count, blend);

            ReportTicks += System.Diagnostics.Stopwatch.GetTimestamp() - reportedAt;

            // **A skinned model is posed here, per instance.** Its geometry was uploaded once and
            // unposed, so the matrices are what puts it in a pose at all - without them it draws
            // in whatever position the artist modelled it, which for a player is lying on its
            // side.
            IReadOnlyList<float[]>? bones = null;

            if (_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? entry) &&
                entry.Skinned is { } skinned &&
                _entities.TryGetValue(prop.EntityIndex, out AnimatingEntity? animating))
            {
                // **One call, and it builds this entity's parents too if it merges onto any.**
                // Everything the old code did with a sort happens inside here now.
                //
                // BONE_USED_BY_ANYTHING because this project draws one level of detail and asks for
                // everything; a narrower mask is the optimisation the accessor exists to allow and
                // is not worth guessing at before something measures it.
                long setupAt = System.Diagnostics.Stopwatch.GetTimestamp();

                bool setUp = animating.SetupBones(StudioBoneFlags.UsedByAnything, seconds);

                SetupTicks += System.Diagnostics.Stopwatch.GetTimestamp() - setupAt;
                Posed++;
                _posedEntities.Add(prop.EntityIndex);

                if (!setUp)
                {
                    // The wearer is not being drawn — dead, out of the visible set, or a model that
                    // failed to load. Valve's `if ( baseDrawn )`: drawing the item anyway leaves it
                    // hanging at the map origin, which is worse than not drawing it.
                    continue;
                }

                long skinAt = System.Diagnostics.Stopwatch.GetTimestamp();

                bones = Skinning(prop.EntityIndex, skinned.Bones, animating.Bones);

                SkinTicks += System.Diagnostics.Stopwatch.GetTimestamp() - skinAt;

                // **Only the viewmodel, because only it is measured every frame** (B222). The
                // viewmodel entities carry their own indices, so this cannot pick up a world prop
                // and pay for two hundred of them.
                // **Guarded on the WORK, not just the write.** This walks a sixteenth of the model's
                // corners every frame and applies three matrices to each; a production run must not
                // pay for a diagnostic. Same rule as B191/CA1873 elsewhere in this file — and the
                // owner's, stated plainly: *"logs cannot live in the production app or it will slow
                // it down too much"*.
                if (prop.EntityIndex >= ViewmodelScene.ArmsEntityIndex &&
                    _props.IsEnabled(LogLevel.Debug))
                {
                    ReportPosedSize(prop.ModelPath, bones, prop.Pose.Body);
                }

                // **The bones are already in world space**, so the model matrix must not place the
                // model a second time (finding 35 section 7a). This is where the merged item's
                // `transform = worn.Where` used to be, and it is gone rather than moved: an item
                // takes its wearer's bones and those already carry the wearer's placement.
                transform = PropTransform.Identity;
            }

            // **Applies the matrices the GPU is about to use, on the processor, and reports the
            // result.** A skinned model that draws wrong could be a bad pose or a bad shader, and
            // an overhead camera cannot tell them apart. If these extents stand the model up, the
            // pose is right and the fault is in the drawing; if they do not, the pose is the
            // fault and the shader is innocent.
            // **The IsEnabled guard covers the WORK, not just the write** (B191, CA1873). Below
            // this, extents are walked over every corner and a SECOND full skeleton is built for
            // the corner comparison — both purely to produce a diagnostic line. A production run
            // was paying for both and then discarding the result.
            if (bones is { Count: > 0 } &&
                _props.IsEnabled(LogLevel.Debug) &&
                !_reportedPoses.Contains(prop.ModelPath))
            {
                _reportedPoses.Add(prop.ModelPath);
                ReportPosedExtents(prop.ModelPath, bones);

                // **The same frame posed WITHOUT the blend, side by side.** Resolving the blend
                // grid was this project's change and taking the grid's corner is what came before
                // it. Three animations mixed at wrong weights crumple a skeleton - a run forward
                // blended halfway against a run backward is not a stand, it is a heap - and that
                // is indistinguishable from a broken decode unless both are measured together.
                if (_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? both) &&
                    both.Skinned is { } plain)
                {
                    ReportPosedExtents(
                        prop.ModelPath,
                        plain.Skeleton(Math.Max(0, prop.Pose.Sequence), 0).Matrices,
                        prop.ModelPath + " CORNER, no pose parameters");
                }
            }

            // **A merged entity takes its wearer's matrices, not its own pose.** This is what
            // EF_BONEMERGE means: the client walks the child's bones, finds the parent's bone of
            // the same name, and uses that matrix outright. A hat has a `bip_head` bone and no
            // animation of its own — posing it from its own rest skeleton puts it at the player's
            // feet facing north, which is what "cosmetics do not work" looked like.
            //
            // Bones the parent does not have keep the child's own, which is the same fallback
            // Remap's −1 already means: an item with a part the player has no bone for keeps the
            // shape the artist gave it rather than collapsing to the origin.
            // **Valve's third branch, and it had no home here until B231**
            // (`c_baseentity.cpp:4393`). An entity with a parent that is NOT bone-merged builds an
            // entity-to-parent matrix from its own local angles and origin and concatenates its
            // parent's transform onto it. Everything with a parent used to take the bone path
            // below, so a `prop_dynamic` hung on a `func_door` searched for a skeleton brushwork
            // does not have and was dropped — every gate on `cp_fulgur` is exactly that pairing.
            //
            // Placed BEFORE the bone-merge branch reads `_drawnPlacements`, because the two are
            // alternatives rather than stages: the engine returns from whichever one applies.
            if (prop is { AttachedTo: { } parent, BoneMerged: false }
                && _parentPlacements.TryGetValue(parent, out PropTransform parentToWorld))
            {
                // `GetParentToWorldTransform` prefers the parent's ATTACHMENT when one is named and
                // resolvable, and falls back to the parent's own transform otherwise
                // (`c_baseentity.cpp:4330`). Brushwork has no attachments, so the fallback is the
                // path every gate takes; an attachment-hung child still reaches the bone code below
                // through `AttachmentPoint`.
                transform = parentToWorld.Concat(
                    new PropTransform(
                        prop.Pose.X, prop.Pose.Y, prop.Pose.Z,
                        prop.Pose.Pitch, prop.Pose.Yaw, prop.Pose.Roll,
                        prop.Pose.Scale));

                // **Says where the composition actually PUT it, once per model.** Every input to
                // this was measured and correct — the parent is the right `func_door`, at the right
                // world position, and it moves — while the prop stayed invisible. The one thing
                // never measured was the output, which is the shape this project keeps meeting:
                // three correct measurements locating the fourth
                // (`docs/memory/measure-every-hop-before-blaming-one.md`).
                if (_render.IsEnabled(LogLevel.Debug) &&
                    _reportedFrames.Add($"{prop.ModelPath}#{prop.EntityIndex}#placed"))
                {
                    _render.LogDebug(
                        "{Message}",
                        $"{prop.ModelPath} composed onto {parent}: parent "
                        + $"({parentToWorld.OriginX:0} {parentToWorld.OriginY:0} "
                        + $"{parentToWorld.OriginZ:0}) + local "
                        + $"({prop.Pose.X:0} {prop.Pose.Y:0} {prop.Pose.Z:0}) = "
                        + $"({transform.OriginX:0} {transform.OriginY:0} {transform.OriginZ:0})");
                }
            }
            else if (prop.AttachedTo is { } wearer)
            {
                // **Says WHY a parented prop was dropped, which nothing did** (B231). A prop that
                // is not bone-merged and whose parent is not in `_parentPlacements` falls to this
                // branch, fails the same lookup against the drawn set, and `continue`s — leaving no
                // census line, no warning, and a model missing from the map with nothing to read.
                // The gates and the spawn locker are exactly that, and three rebuilds were spent
                // guessing at it.
                if (!prop.BoneMerged && _render.IsEnabled(LogLevel.Debug) &&
                    _reportedFrames.Add(prop.ModelPath + "#parent"))
                {
                    _render.LogDebug(
                        "{Message}",
                        $"{prop.ModelPath} is parented to {wearer} and not bone-merged, and that "
                        + $"entity has no placement this frame "
                        + $"(parents known: {_parentPlacements.Count}, drawn: {_drawnPlacements.Count})");
                }

                if (!_drawnPlacements.TryGetValue(wearer, out PropTransform stands))
                {
                    // The wearer is not being drawn — dead, out of the visible set, or a model that
                    // failed to load. Valve's `if ( baseDrawn )`: drawing the item anyway leaves it
                    // at the map origin, which is worse than not drawing it. A SKINNED item is
                    // already refused by SetupBones returning false; this is the same rule for one
                    // with no skeleton to refuse with.
                    continue;
                }

                if (bones is null)
                {
                    // A model this project baked rather than skinned has no bones to carry a
                    // placement, so it takes its wearer's outright — which is what every worn item
                    // did before D88, and is still correct for one that cannot be merged onto
                    // anything.
                    transform = stands;
                }

                // **Lit where its wearer stands — and no longer patched HERE** (B189; the outside
                // audit's finding 6). A merged item's own pose is (0,0,0) by construction, so an
                // override in this loop used to replace the cube, the lamps and the reflection
                // origin with wearer-point samples. It went past `ModelLighting.For`'s cache — so
                // every worn item re-traced its lighting every frame — and it missed the SUN,
                // which stayed sampled at the map origin, leaving every cosmetic without direct
                // light while its wearer stood in it. `IlluminationPoint` now answers the wearer's
                // point for a bone-merged prop, so the `For` call above samples cube, lamps, sun
                // and reflection origin at the right place through the one exact-point cache.
                // `WornLightTicks` still exists and now stays zero, which is the truth: the work
                // moved into the `lighting` column with every other sample.

                // Guarded before `FirstTime`, so a production run does not even build the
                // `path + "#worn"` key — a string allocated per worn prop per frame (B191).
                if (bones is { Count: > 0 } &&
                    _props.IsEnabled(LogLevel.Debug) &&
                    _reports.FirstTime(prop.ModelPath + "#worn"))
                {
                    ReportPosedExtents(prop.ModelPath, bones, prop.ModelPath + " WORN");
                }
            }

            // **A model posed by BONES is placed by those bones, and its matrix is identity**
            // (B241). `IStudioRender::DrawModel` takes bone-to-world matrices and nothing else
            // (`istudiorender.h:329`); Valve has no separate entity transform for a studio model,
            // because `SetupBones` folds the placement into every bone before the draw. So a
            // non-identity matrix beside real bones applies the placement TWICE.
            //
            // **This project already wrote the rule down and did not enforce it.**
            // `WorldRenderer.DrawModel`: *"A baked model is put in the world by its matrix … a
            // SKINNED model is put there by its bones and its matrix stays at identity."* It held
            // only by accident — a player's pose is (0,0,0) and a merged item's is (0,0,0) by
            // construction — so nothing broke until a skinned prop arrived carrying a real origin.
            //
            // A setup gate's grate is exactly that: `CDynamicProp`, one bone, and a networked origin
            // of (5416 −2168 552). Bone and matrix each placed it there, so it drew about ten
            // thousand units off the map, and the doorway you could see straight through was the
            // only symptom. Measured before this line existed:
            //
            //   demo               draws at (0, 0, 0)          bones 84
            //   windowed_door      draws at (0, 0, 0)          bones 1
            //   door_grate003_top  draws at (5416, -2168, 552) bones 1
            //
            // Two props of the same kind parented the same way, placed by two mechanisms.
            if (bones is { Count: > 0 })
            {
                transform = PropTransform.Identity;
            }

            // **Hoisted out of the argument list**, because two arguments now want it: the body
            // parts and the two-pass flag are both facts about the MODEL rather than the entity.
            // Looking it up twice would be two dictionary probes per model per frame for one answer.
            _ = _frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? parts);

            // **`C_BaseEntity::ComputeFxBlend`, once per entity per frame** (B221). The engine
            // computes this on the entity and caches it by frame count; here the frame IS this
            // call, so computing it at the one place a `ModelInstance` is built gives the same
            // once-per-frame guarantee without a cache to go stale.
            //
            // `seconds` is demo time, which is `gpGlobals->curtime` for a viewer — the pulses,
            // strobes and flickers are functions of it, and the entity index de-syncs them.
            // **The distance fade, which `Compute` has always taken and nobody ever supplied**
            // (B268). `GetClientSideFade` on `C_BaseAnimating` is `UTIL_ComputeEntityFade`, and
            // `ComputeFxBlend` multiplies its answer into the blend — so leaving it at the default
            // 255 drew every entity at full alpha however far away it was, with the multiply
            // sitting there correct and unreached.
            //
            // Measured at the prop's illumination point, which is where this loop already decided
            // the model IS — the same point the cubemap and the lighting use, so all three agree
            // about its position rather than each deriving one.
            byte fade = ViewOrigin is { } eye
                ? EntityFade.DistanceAlpha(
                    prop.Pose.FadeMinimumDistance,
                    prop.Pose.FadeMaximumDistance,
                    EntityFade.Distance(eye, origin))
                : (byte)255;

            FxBlendResult fx = FxBlend.Compute(
                prop.Pose.RenderFx,
                prop.Pose.RenderMode,
                prop.Pose.RenderAlpha,
                prop.EntityIndex,
                (float)seconds,
                clientSideFade: fade);

            into.Add(new ModelInstance(
                prop.ModelPath,
                transform.ToMatrix(),
                light,

                // Cached alongside the cube, because the sun costs more than it looks: it traces a
                // ray through the BSP to ask whether the sky is visible from here. It was also
                // being asked TWICE per model — once here and once for the wearer record — and
                // neither answer could differ.
                sun,
                frame,
                blend,
                bones,
                SkinSwap(prop.ModelPath, skin),
                parts?.BodyParts,
                prop.Pose.Body,

                // **`ForcedMaterialOverride` — gold or ice, and nothing else sets it** (B325).
                // `C_TFRagdoll::InternalDrawModel` forces it around the base call
                // (`c_tf_player.cpp:1281-1290`), and each wearable applies its own copy through
                // `GetEconWeaponMaterialOverride` (`econ_entity.cpp:1793`).
                MaterialOverride: prop.MaterialOverride,

                // **TF2's paint, per entity** (B330). Asked here rather than carried on the prop
                // because it needs the econ resolution and the item schema, which this layer
                // reaches through a delegate exactly as it does for attached models.
                Paint: Paint?.Invoke(prop),
                Mirrored: false,
                Origin: origin,

                // Only a brush entity has one; everything else answers null (B219).
                Tint: EntityTint(prop.ModelPath),

                // The lamps near this model, which its cube no longer carries (B170).
                Locals: locals,

                // The placed box the engine culls and buckets by — CalcRenderableWorldSpaceAABB.
                WorldBounds: WorldBoxFor(prop),

                // **$mostlyopaque, off the model's own header.** A model this side never loaded
                // answers false, which is the engine's answer for a model with no flag — and the
                // conservative one, since it draws the model in one pass rather than two.
                TwoPass: parts?.TwoPass ?? false,

                // **`GetFxBlend()` and `m_nRenderMode`, the two inputs `RenderGroups.For` has taken
                // since D114 and never been given** (B221). Until now every caller passed
                // `FullyOpaque` and `Normal`, so a cloaked spy drew solid and nothing could fade.
                Alpha: fx.Blend,
                RenderMode: prop.Pose.RenderMode));

            // **The item's `attached_models`, drawn on the item's own transform and bones.**
            // `DrawEconEntityAttachedModels` (`econ_entity.cpp:103`) copies the parent's
            // `ClientModelRenderInfo_t` whole and swaps only `pModel`:
            //
            //     infoAttached = *pInfo;
            //     infoAttached.pRenderable = pEnt;
            //     infoAttached.pModel      = attachedModel.m_pModel;
            //     modelrender->DrawModelSetup( infoAttached, &state, NULL, &pBoneToWorld );
            //
            // So an attachment is NOT a separate entity and names no attachment point — it is
            // another mesh posed by the item's skeleton, which is why the Degreaser's pilot light
            // sits where the Degreaser does without the schema saying where.
            //
            // Everything else is copied for the same reason the engine copies it: same light, same
            // blend, same render mode, same skin. A pilot light on a cloaked spy's flamethrower
            // fades with the flamethrower.
            //
            // **The one thing NOT copied is the material override, and that is the engine's rule
            // rather than an omission** (B325). An econ entity that applies its own override sets a
            // flag with it —
            //
            //     modelrender->ForcedMaterialOverride( pOverrideMaterial );
            //     flags |= STUDIO_NO_OVERRIDE_FOR_ATTACH; // Don't apply override materials to attachments.
            //
            // `c_baseanimating.cpp:3438-3439` — and `DrawEconEntityAttachedModels` reads it back,
            // clearing the override for the duration of the loop and restoring it afterwards
            // (`econ_entity.cpp:110-117`, `146-147`). So a hat on a golden corpse turns gold and the
            // extra mesh bolted to that hat does not. Read-from-source. Both sites that raise the
            // flag are econ overrides, which is exactly what a corpse's wearables carry.
            if (Attachments is not { } attachmentsFor)
            {
                continue;
            }

            foreach (string attachment in attachmentsFor(prop))
            {
                _ = _frames.TryGetValue(attachment, out PropModels.ModelFrames? attachedParts);

                // **Says that an attachment was EMITTED, and whether it had geometry to emit** —
                // the two failures look identical on screen and neither is an error. An attachment
                // the schema does not declare draws nothing; one declared but never packed also
                // draws nothing, and that second case shipped for an hour because the packing set
                // and the asset loader are different lists (B195).
                //
                // Reports what this draw USED rather than re-deriving it (B243): the parent it
                // rides, and whether frames were found for the attachment itself.
                if (_props.IsEnabled(LogLevel.Debug) &&
                    _reports.FirstTime(attachment + "#attached"))
                {
                    _props.LogDebug(
                        "{Message}",
                        $"{attachment} attached to {prop.ModelPath} (entity {prop.EntityIndex}, "
                        + $"item {prop.ItemDefinitionIndex}), "
                        + $"{(attachedParts is null ? "NO FRAMES — it was never packed" : "posed on its bones")}");
                }

                into.Add(new ModelInstance(
                    attachment,
                    transform.ToMatrix(),
                    light,
                    sun,
                    frame,
                    blend,
                    bones,
                    SkinSwap(attachment, skin),
                    attachedParts?.BodyParts,
                    prop.Pose.Body,
                    Mirrored: false,
                    Origin: origin,
                    Tint: EntityTint(attachment),
                    Locals: locals,
                    WorldBounds: WorldBoxFor(prop),
                    TwoPass: attachedParts?.TwoPass ?? false,
                    Alpha: fx.Blend,
                    RenderMode: prop.Pose.RenderMode));
            }
        }

        _tally.Report();
    }


    /// <summary>How many props the frustum rejected on the last <see cref="Instances"/> call.</summary>
    /// <remarks>
    /// **Exposed so the cull can be proved to be doing something and not everything.** A cull that
    /// rejects nothing is the bug this replaced; a cull that rejects everything is a black screen.
    /// Both are the same code path with a wrong frustum, and only the count tells them apart.
    /// </remarks>
    public int Culled { get; private set; }

    /// <summary>How many the VISIBILITY half rejected, of those the frustum kept (B254).</summary>
    /// <remarks>
    /// Counted apart from the frustum's share because the two answer different questions and only
    /// the split says whether the PVS half is wired: a zero here with a non-zero <see cref="Culled"/>
    /// means the tree or the visible set never arrived, which is indistinguishable from "everything
    /// in the frustum is also in the PVS" without it.
    /// </remarks>
    public int CulledByVisibility { get; private set; }

    /// <summary>How many props the cull could not judge, because their box is degenerate.</summary>
    /// <remarks>
    /// **A model with no render bounds is kept, never point-tested** — the empty-box rule. Counted
    /// because if it is most of them the cull is nearly inert and the count is the only thing that
    /// says so: a constant survivor ratio whatever the camera does looks identical to "everything
    /// really is visible".
    /// </remarks>
    public int Unjudgeable { get; private set; }

    /// <summary>How many props actually reached bone setup.</summary>
    /// <remarks>
    /// **The honest count, and it replaces one that was not.** `Drawn` was reported as
    /// `selected - culled`, which counts every prop the drawability and render-mode filters rejected
    /// as though it had been posed - brush models and sprites among them. That made the survivor
    /// ratio look flat whatever the camera did and nearly sent this audit after the frustum.
    /// </remarks>

    /// <summary>Which entities reached bone setup, for the next frame's interpolation list.</summary>
    /// <remarks>
    /// **The engine gates interpolation on `IsVisible()`, which is the LAST render's answer** (B259,
    /// `c_baseentity.cpp:3038`), so a one-frame-old visible set is not an approximation of what
    /// Valve does - it is what Valve does. Published here because the cull runs after the view and
    /// sampling runs before it, which is the same order the engine has.
    /// </remarks>
    public IReadOnlySet<int> PosedEntities => _posedEntities;

    private readonly HashSet<int> _posedEntities = [];
    public int Posed { get; private set; }

    /// <summary>Whether the view frustum rejects this prop — <c>engine->CullBox</c>.</summary>
    /// <param name="prop">The prop about to be posed.</param>
    /// <param name="frustum">The view being drawn, or the default when there is none.</param>
    /// <param name="visibleByLeaf">The visible-leaf set, or empty to skip the visibility test.</param>
    /// <returns>True when nothing of the prop can be seen, so it need not be posed.</returns>
    /// <remarks>
    /// **A model with no bounds is kept, never point-tested.** `WorldSpaceBounds.IsPlaced` is the
    /// same guard `Device3D.Culled` applies, and it exists because a zero box is a point at the map
    /// origin — which culls the model everywhere except one spot, and reads as a model that flickers
    /// rather than as a cull that is wrong
    /// (`docs/memory/an-empty-box-must-never-cull.md`).
    ///
    /// **An unbuilt frustum keeps everything**, which is what `ViewFrustum.Cull` already does and is
    /// why every caller that passes no frustum is unaffected.
    /// </remarks>
    private bool Culls(SceneProp prop, ViewFrustum frustum, ReadOnlySpan<bool> visibleByLeaf)
    {
        if (!frustum.IsBuilt)
        {
            return false;
        }

        (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) box =
            WorldBoxFor(prop);

        if (!WorldSpaceBounds.IsPlaced(box))
        {
            Unjudgeable++;
            return false;
        }

        if (frustum.Cull(box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ))
        {
            return true;
        }

        // **The visibility half, and it is the one the engine leads with** (B254).
        // `BuildRenderablesList` iterates the VISIBLE LEAF LIST and only frustum-tests what is
        // already in it, so an entity behind a wall never enters the render list at all — the
        // frustum alone keeps everything in the view cone, wall or no wall.
        //
        // **Ordered frustum-first here for cost rather than for parity**: the frustum test is six
        // dot products and rejects most of the map, where this walks the tree. The ANSWER is the
        // same either way — a box is kept only if it passes both — and the engine's ordering is a
        // consequence of it maintaining per-leaf renderable lists across frames, which this does not.
        //
        // **An empty set culls nothing**, which is a map with no visibility data, or any frame
        // before the first world cull has run.
        if (visibleByLeaf.IsEmpty || Tree is not { } tree)
        {
            return false;
        }

        if (tree.TouchesAny(
                box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ, visibleByLeaf))
        {
            return false;
        }

        CulledByVisibility++;

        return true;
    }

    /// <summary>The map's BSP tree, for the visibility half of the cull.</summary>
    /// <remarks>
    /// Null until a map is read, and null leaves the cull frustum-only rather than culling
    /// everything — the safe direction this whole path takes.
    /// </remarks>
    public BspLeafTree? Tree { get; set; }

    /// <summary>The box the engine would cull this model by, placed — <c>CalcRenderableWorldSpaceAABB</c>.</summary>
    /// <param name="prop">The entity being drawn.</param>
    /// <returns>Its world-space box, or an empty one when the model carries no bounds.</returns>
    /// <remarks>
    /// **`DefaultRenderBoundsWorldspace` (`clientleafsystem.cpp:342`), both of its branches.** A
    /// bone-merged entity is culled by its WEARER's box bloated by its own reach; everything else by
    /// its own bounds placed at its render origin and angles.
    ///
    /// **`IsFollowingEntity` is `EF_BONEMERGE &amp;&amp; MOVETYPE_NONE &amp;&amp; GetMoveParent()`**
    /// (`c_baseentity.cpp:3176`), and `GetFollowedEntity` is then just the move parent. This project
    /// records that relation as `SceneProp.AttachedTo`, so the test here is whether the wearer is
    /// known and drawable.
    ///
    /// **Valve recurses** — a parent that is itself following resolves through
    /// `CalcRenderableWorldSpaceAABB_Fast`, which calls itself. One level is taken here because
    /// nothing in TF2 merges onto a merged item, and a cycle in demo data would otherwise hang the
    /// viewer; a wearer that is itself worn falls back to its own placed box.
    ///
    /// **Called once per prop BEFORE the pose now** (B254), which it can be because it reads render
    /// bounds and placement rather than bones.
    /// </remarks>
    private (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) WorldBoxFor(
        SceneProp prop)
    {
        StudioBox local = Scaled(
            _frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? sized)
                ? sized.RenderBoundsFor(prop.Pose.Sequence)
                : default,
            prop.Pose.Scale);

        if (prop.AttachedTo is { } wearer &&
            _propsByEntity.TryGetValue(wearer, out SceneProp? parent))
        {
            return WorldSpaceBounds.Following(
                Placed(parent),
                local,

                // GetLocalOrigin: a merged item's own pose, which is (0,0,0) by construction for
                // almost all of them and is used only to grow the bloat when it is not.
                (prop.Pose.X, prop.Pose.Y, prop.Pose.Z));
        }

        return Placed(prop, local);
    }

    /// <summary>The render bounds grown by the model's scale, as the last two lines of
    /// <c>C_BaseAnimating::GetRenderBounds</c> do.</summary>
    /// <param name="local">The box the header and sequence produced.</param>
    /// <param name="scale">The entity's <c>m_flModelScale</c>.</param>
    /// <returns>The scaled box.</returns>
    /// <remarks>
    /// **Valve's own last step, and skipping it was a live defect.**
    ///
    /// <code>
    /// // Scale this up depending on if our model is currently scaling
    /// const float flScale = GetModelScale();
    /// theMaxs *= flScale;
    /// theMins *= flScale;
    /// </code>
    ///
    /// Scale is decoded, interpolated and applied when this project DRAWS a model — so a scaled
    /// model was being drawn at its real size and culled by a box at its authored one. A giant
    /// draws far outside a box a tenth its size and vanishes at the edge of the screen.
    ///
    /// **Both corners are multiplied, not just the extent**, so a box that is not centred on its
    /// origin moves as well as growing. That is Valve's arithmetic and it is the correct one: the
    /// model's geometry scales about its origin, so its bounds must too.
    /// </remarks>
    private static StudioBox Scaled(StudioBox local, float scale) =>

        // The shortcut is for the overwhelmingly common case and nothing else; a scale that is not
        // exactly one simply takes the multiply, which for 1.0 would be an identity anyway. The
        // analyzer's usual objection — that float equality is a trap — does not apply to a branch
        // whose two sides compute the same answer.
        Math.Abs(scale - 1f) < float.Epsilon
            ? local
            : new StudioBox(
                local.MinX * scale, local.MinY * scale, local.MinZ * scale,
                local.MaxX * scale, local.MaxY * scale, local.MaxZ * scale);

    /// <summary>One entity's own placed box, ignoring anything it may be attached to.</summary>
    private (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) Placed(
        SceneProp prop) =>
        Placed(
            prop,
            Scaled(
                _frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? sized)
                    ? sized.RenderBoundsFor(prop.Pose.Sequence)
                    : default,
                prop.Pose.Scale));

    private static (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) Placed(
        SceneProp prop, StudioBox local) =>
        WorldSpaceBounds.Placed(
            local,
            (prop.Pose.X, prop.Pose.Y, prop.Pose.Z),
            (prop.Pose.Pitch, prop.Pose.Yaw, prop.Pose.Roll));

    /// <summary>A value for each pose parameter the model declares, in its own order.</summary>
    /// <remarks>
    /// **Matched by NAME rather than by position**, because a pose parameter's index is a property
    /// of the model: a scout and a heavy declare their own lists and there is no guarantee
    /// <c>move_x</c> lands at the same index in both. Filling an array positionally works right up
    /// until a class orders them differently, and then that class alone animates from the wrong
    /// input — the kind of defect that looks like a bad animation rather than a bad lookup.
    ///
    /// Anything this project does not compute stays at zero, which is what the engine leaves an
    /// unset parameter at.
    /// </remarks>
    private static float[] PoseValues(PropModels.SkinnedModel model, ScenePose pose, int sequence)
    {
        IReadOnlyList<StudioPoseParameter> parameters = model.PoseParameters;

        if (parameters.Count == 0)
        {
            return [];
        }

        // **What the entity itself sent wins, and for most animating entities that is everything**
        // (B269). `CBaseAnimating` networks the whole array (`server/baseanimating.cpp:243`) and
        // `C_BaseAnimating::GetPoseParameters` (`c_baseanimating.cpp:1401`) hands it straight to
        // the blend, so a sentry's aim comes off the wire and nothing below this line applies to
        // one. The derivation underneath is `CBasePlayerAnimState`, which exists precisely because
        // `tf_player.cpp:769` EXCLUDES the array for players — so the two paths are the engine's
        // own split rather than a preference between them, and an entity can only be on one side.
        //
        // Indexed directly, because the wire's index IS the model's: `GetNumPoseParameters` counts
        // the virtual model's merged list, whose first entries are the root model's own in order
        // (`CVirtualModel::AppendPoseParameters`).
        if (pose.PoseParameters.Count > 0)
        {
            return Sent(parameters.Count, pose.PoseParameters);
        }

        float[] values = Filled(parameters, pose.MoveX, pose.MoveY, pose.EyePitch, pose.AimYaw);

        // **The speed scaling, and it happens HERE rather than in the scene layer because only this
        // side can open a model.** ComputePoseParam_MoveYaw finishes with
        //
        //     float flMaxSpeed = GetSequenceGroundSpeed( GetSequence() );
        //     if ( flMaxSpeed > flSpeed ) { x *= flSpeed / flMaxSpeed; y *= flSpeed / flMaxSpeed; }
        //
        // which pulls a player moving slower than their animation was authored for back towards the
        // middle of the blend grid. Without it a scout walking at 100 units a second animated with
        // the same full-magnitude stride as one sprinting at 400.
        //
        // **The two-pass shape is Valve's, not an accident of this port.** The engine sets move_x
        // and move_y, reads the ground speed WITH THOSE IN PLACE — the parameters choose which
        // cells of the grid are blended, so they choose whose authored speed is being asked about —
        // and only then rescales and sets them again. Reading the speed first would ask about
        // whichever cells the previous frame happened to leave behind.
        if (pose.Speed is not { } speed || speed <= 0f)
        {
            return values;
        }

        float authored = model.GroundSpeed(sequence, values);

        // Valve's guard is `if ( flMaxSpeed > flSpeed )`, so a player moving FASTER than their
        // animation was authored for is left alone rather than scaled past the edge of the grid.
        if (authored <= speed)
        {
            return values;
        }

        float scale = speed / authored;

        return Filled(
            parameters, pose.MoveX * scale, pose.MoveY * scale, pose.EyePitch, pose.AimYaw);
    }

    /// <summary>The values an entity sent, sized to the model that will consume them.</summary>
    /// <remarks>
    /// **The two counts can legitimately differ**, and neither direction is an error. The array is
    /// a fixed 24 slots on the server (`MAXSTUDIOPOSEPARAM`) of which only the model's own are
    /// meaningful, so more values than parameters is the ordinary case; fewer happens when a delta
    /// has named only the low elements of an entity whose model wants more, and the engine's
    /// unsent slots read as the zero `OnNewModel` left them at (`c_baseanimating.cpp:1134`).
    /// </remarks>
    private static float[] Sent(int count, IReadOnlyList<float> values)
    {
        float[] sized = new float[count];

        for (int index = 0; index < count && index < values.Count; index++)
        {
            sized[index] = values[index];
        }

        return sized;
    }

    /// <summary>Every pose parameter's stored value, given the two this project computes.</summary>
    /// <remarks>
    /// Anything not computed stays at a raw zero, which is what the engine leaves an unset
    /// parameter at — and note that zero is normalised like any other value, so a parameter running
    /// −1 to 1 lands in the MIDDLE of its range rather than at the bottom.
    /// </remarks>
    private static float[] Filled(
        IReadOnlyList<StudioPoseParameter> parameters,
        float moveX,
        float moveY,
        float? eyePitch,
        float? aimYaw)
    {
        float[] values = new float[parameters.Count];

        for (int index = 0; index < parameters.Count; index++)
        {
            float raw = parameters[index].Name switch
            {
                "move_x" => moveX,
                "move_y" => moveY,

                // **Negated, which is the whole of ComputePoseParam_AimPitch:**
                // `SetPoseParameter( m_iAimPitch, -flAimPitch )` with flAimPitch the eye pitch. The
                // sign lives here rather than in the stored value, so what the scene carries still
                // matches what the wire said.
                //
                // Zero when the recording sent no eye angles, which is level — the same answer this
                // gave before aiming existed, rather than a guess at where they were looking.
                "body_pitch" => -(eyePitch ?? 0f),

                // **Already negated**, unlike body_pitch above. The twist is computed where the
                // feet are simulated, because it is the difference between two values only that
                // state machine holds — and SetPoseParameter( m_iAimYaw, -flAimYaw ) is applied
                // there with them.
                "body_yaw" => aimYaw ?? 0f,

                _ => 0f,
            };

            // Stored normalised, as the engine stores it - see StudioBlendGrid.Normalize.
            values[index] = StudioBlendGrid.Normalize(parameters[index], raw);
        }

        return values;
    }


    /// <summary>Which axis a model is longest along, named for the log.</summary>
    /// <remarks>
    /// "z, upright" is the expected answer for anything that stands up. Anything else on a
    /// humanoid means the model is on its side.
    /// </remarks>
    private static string Tallest(float spanX, float spanY, float spanZ)
    {
        if (spanZ >= spanX && spanZ >= spanY)
        {
            return "z, upright";
        }

        // **Only flagged when the model is clearly long in the wrong direction.** A medkit is
        // 24 by 17 by 23 and legitimately near-cubic; calling that "on its side" cries wolf on a
        // correct model, which is how a real warning stops being read.
        string axis = spanX >= spanY ? "x" : "y";

        return MathF.Max(spanX, spanY) > spanZ * 1.5f ? axis + ", ON ITS SIDE" : axis;
    }
}
