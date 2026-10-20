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
/// <param name="Sequence">The sequence the walk crossed it in.</param>
/// <param name="Cycle">The cycle the walk reached this frame.</param>
/// <remarks>
/// **<c>FireEvent( GetAbsOrigin(), GetAbsAngles(), event, options )</c>** — the engine hands the
/// handler the entity's place along with the event, and for <c>AE_CL_PLAYSOUND</c> that is the
/// position the sound is emitted at when the model has no attachments
/// (<c>c_baseanimating.cpp:3988</c>).
/// </remarks>
public readonly record struct FiredAnimationEvent(
    int EntityIndex,
    StudioEvent Event,
    (float X, float Y, float Z) Origin,
    int Sequence = -1,
    float Cycle = 0f);

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
/// <param name="Burn">
/// How alight this entity's wearer is, 0 to 1 (B336). Feeds TF2's <c>BurnLevel</c> proxy, which
/// writes <c>$detailblendfactor</c> on 6,715 of the 6,718 shipped materials that run it — so it is
/// what blends in the fire overlay a player material already carries in <c>$detail</c>. Zero for
/// everything not on fire, which is the proxy's own resting value.
/// </param>
/// <param name="Urine">
/// The jarate multiplier for this entity's wearer (B336), or null for white. <c>CProxyUrineLevel</c>
/// answers <c>(6,9,2)</c> on RED and <c>(7,5,1)</c> on BLU while the player is in
/// <c>TF_COND_URINE</c> — multipliers above one, which is how the effect brightens into yellow
/// rather than tinting toward it.
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
/// <param name="EntityIndex">
/// Which entity this instance is, or −1 when it was built by hand (B356). A per-model diagnostic
/// keyed without it cannot tell two of the same model apart.
/// </param>
/// <param name="BakedColours">
/// A baked static prop's colour mesh in its model buffer's order, or null for a model lit per draw (B426).
/// </param>
/// <param name="LeafPlace">Its place in its view's leaf list, where the scene collated it (B262).</param>
/// <param name="SizeBucket">The size bucket of the box it was collated by; null when the scene did not collate it.</param>
/// <param name="InSky">Whether the 3D skybox view collated it, so the sky pass draws it.</param>
/// <param name="Cloak">What <c>spy_invis</c> and <c>invis</c> write into <c>$cloakfactor</c> for this entity.</param>
/// <param name="Flex">The face's position and normal deltas per buffer vertex, or null (B513).</param>
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

    // **How alight the wearer is** (B336), feeding TF2's `BurnLevel` proxy at the same bind. Per
    // entity for the same reason the paint is: two players in the same class share one material,
    // and only one of them may be on fire.
    float Burn = 0f,

    // **The jarate multiplier** (B336), feeding `CProxyUrineLevel`. White for everyone not hit,
    // which is a multiply by one and therefore the same picture an unevaluated proxy gave.
    //
    // **Nullable because a default triple is (0,0,0) and this is a MULTIPLIER**, so an unset one
    // would draw every player black. A parameter default cannot be `(1,1,1)` — it is not a constant
    // expression — and `docs/memory/a-neutral-default-must-be-neutral.md` is about precisely this
    // substitution going wrong once already, where `_white` turned out to be the magenta chequer.
    (float Red, float Green, float Blue)? Urine = null,
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
    int RenderMode = 0,

    // **Which entity this instance IS**, so a per-model diagnostic can tell two of the same model
    // apart (B356). A map carrying two resupply lockers put both through one slot of the render-group
    // guard, and two instances at different animation frames report a change on every draw — 11,576
    // lines from `_locker` alone on one two-minute run, 5,788 in each direction, none of them real.
    //
    // **A field on a struct, so it costs no allocation** — which is what the entry filing this
    // worried about. An instance is built per prop per frame and never boxed; four bytes beside
    // twenty-odd other fields is not the hot-path cost it looked like.
    //
    // **−1 rather than 0, because 0 is the worldspawn** and a test or viewmodel that builds an
    // instance by hand must not claim to be it. Anything reading this must treat −1 as "unidentified"
    // rather than grouping every such instance together — the same fault, one value along.
    int EntityIndex = -1,

    // **A baked static prop's colour mesh** (B426): red, green, blue per vertex of the model's buffer, in
    // the buffer's order, overbright applied. `engine.dll` `0x1800f1bd0` draws a prop with baked colours
    // with them and with no cube and no local lights. Null for everything else — and null is the switch
    // B424 needs: a prop whose colours are dropped for a frame is drawn with full lighting instead.
    float[]? BakedColours = null,

    // **Where collation put it** (B262): the place in its view's leaf list, the size bucket of the box it was
    // collated by, and whether that view was the 3D skybox's. A model the scene did not collate — a detail model,
    // a viewmodel — has no bucket, and the device files it itself.
    int LeafPlace = 0,
    int? SizeBucket = null,
    bool InSky = false,

    // **What the invisibility proxies write into `$cloakfactor` for this entity** (`spy_invis`, `invis`). Default is
    // nobody cloaked, which is both proxies' value for everyone but a spy.
    CloakBind Cloak = default,

    // **The face** (B513): a position delta then a normal delta per vertex of the model's buffer, in its order, which the
    // shader adds before skinning (`ApplyMorph`, common_vs_fxc.h:384-387). Null for a model with no flex this frame.
    float[]? Flex = null);

/// <summary>
/// The views collation walks — <c>m_pWorldListInfo->m_pLeafList</c> per view (B262).
/// </summary>
/// <param name="MainLeaves">The main view's leaves, nearest first; null when no view walk has run.</param>
/// <param name="SkyLeaves">The 3D skybox view's leaves; null or empty when it does not draw.</param>
/// <param name="SkyFrustum">The skybox view's volume.</param>
public readonly record struct RenderableViews(
    IReadOnlyList<int>? MainLeaves,
    IReadOnlyList<int>? SkyLeaves,
    ViewFrustum SkyFrustum);

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
public sealed partial class EntityModelSet : Hud.IMdlCache, IPosedEntities
{
    /// <inheritdoc/>
    public PropModels.ModelFrames? FindMdl(string path) =>
        _frames.TryGetValue(path, out PropModels.ModelFrames? frames) ? frames : null;

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

    /// <summary>How alight each prop's wearer is, for <c>CProxyBurnLevel</c> (B336).</summary>
    /// <remarks>
    /// **The same shape as <see cref="Paint"/> and for the same reason**: the value needs the
    /// player's conditions, which live a layer up, and the engine binds the proxy against the
    /// entity being drawn. Production supplies `MomentScene.BurnLevelOf`.
    ///
    /// Null when nothing supplies it, and a null delegate means zero — which is
    /// `CProxyBurnLevel`'s own resting value rather than an invented one, so a viewer with no
    /// scene behind it draws exactly what an unburnt player draws.
    /// </remarks>
    public Func<SceneProp, float>? BurnLevel { get; set; }

    /// <summary>The jarate multiplier for each prop's wearer, for <c>CProxyUrineLevel</c> (B336).</summary>
    /// <remarks>
    /// White when nothing supplies it, which is the proxy's own resting value — a multiply by one.
    /// </remarks>
    public Func<SceneProp, (float Red, float Green, float Blue)>? UrineTint { get; set; }

    /// <summary>What the invisibility proxies bind for each prop; production supplies `MomentScene.CloakOf`.</summary>
    /// <remarks>Null means nobody is cloaked, which is both proxies' value for an entity no spy owns.</remarks>
    public Func<SceneProp, CloakBind>? Cloak { get; set; }

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

    /// <summary>Skeletons built while simulating, ever — attachments and merges asked before the draw.</summary>
    public int BuildsSimulating { get; set; }

    /// <summary>Skeletons built by the draw loop's own `SetupBones`, ever — parents of merged items included.</summary>
    public int BuildsDrawing { get; set; }

    /// <summary>Skeletons built while advancing the corpses — a seed's death pose — ever.</summary>
    public int BuildsCorpses { get; set; }

    /// <summary>Skeletons built while placing a drawn instance — a parent's attachment — ever.</summary>
    public int BuildsPlacing { get; set; }

    /// <summary>What collating the pass's props into views has cost, ever — `BuildRenderablesList`.</summary>
    public long CollateTicks { get; set; }

    /// <summary>What a static prop's colour mesh and its full-lighting state have cost, ever.</summary>
    public long BakedTicks { get; set; }

    /// <summary>What placing, fading and building each drawn instance has cost, ever — the loop after the skin.</summary>
    public long PlaceTicks { get; set; }

    /// <summary>Of <see cref="PlaceTicks"/>: the debug guards and the parent composition, up to the fade.</summary>
    public long PlaceComposeTicks { get; set; }

    /// <summary>Of <see cref="PlaceTicks"/>: the distance and screen fades, `ComputeFxBlend` and the portal window blend.</summary>
    public long PlaceFadeTicks { get; set; }

    /// <summary>Of <see cref="PlaceTicks"/>: the proxies' binds (cloak, paint, burn, urine), the flex weights and the instance.</summary>
    public long PlaceBuildTicks { get; set; }

    /// <summary>Of <see cref="PlaceBuildTicks"/>: the four proxy binds — cloak, paint, burn, urine.</summary>
    public long PlaceBindsTicks { get; set; }

    /// <summary>Of <see cref="PlaceBuildTicks"/>: the matrix, skin swap, tint, world box and flex, before the instance.</summary>
    public long PlaceLookupsTicks { get; set; }

    /// <summary>Of <see cref="PlaceLookupsTicks"/>: the model-to-world matrix.</summary>
    public long PlaceMatrixTicks { get; set; }

    /// <summary>Of <see cref="PlaceLookupsTicks"/>: the skin swap, the brush tint and the world box.</summary>
    public long PlaceSwapTicks { get; set; }

    /// <summary>Of <see cref="PlaceLookupsTicks"/>: the flex weights.</summary>
    public long PlaceFlexTicks { get; set; }

    /// <summary>What gathering corpse requests and advancing the corpse environment has cost, ever, stepping included.</summary>
    public long AdvanceTicks { get; set; }

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
        int Built,
        int PoseBuilds,
        long Corpses = 0,
        int CorpseSteps = 0,
        int CorpsesHeld = 0,
        long Collate = 0,
        long Baked = 0,
        long Place = 0,
        long Advance = 0,
        int CorpseRebuilds = 0,
        long CorpseBuilding = 0,
        int BuildsSimulating = 0,
        int BuildsDrawing = 0,
        int BuildsCorpses = 0,
        int BuildsPlacing = 0,
        long PlaceCompose = 0,
        long PlaceFade = 0,
        long PlaceBuild = 0,
        long PlaceBinds = 0,
        long PlaceLookups = 0,
        long PlaceMatrix = 0,
        long PlaceSwap = 0,
        long PlaceFlex = 0,
        int CollateFixed = 0,
        int CollateByIdentity = 0)
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
                Built - before.Built,
                PoseBuilds - before.PoseBuilds,
                Corpses - before.Corpses,
                CorpseSteps - before.CorpseSteps,

                // **A LEVEL, so it is carried rather than differenced** (B413). Every other field here is a running total
                // whose difference is the work done between two snapshots; this one is how many corpses the environment is
                // holding right now, and subtracting an earlier reading would answer "how many did it gain", which is not
                // the question — the question is whether it plateaus.
                CorpsesHeld,
                Collate - before.Collate,
                Baked - before.Baked,
                Place - before.Place,
                Advance - before.Advance,
                CorpseRebuilds - before.CorpseRebuilds,
                CorpseBuilding - before.CorpseBuilding,
                BuildsSimulating - before.BuildsSimulating,
                BuildsDrawing - before.BuildsDrawing,
                BuildsCorpses - before.BuildsCorpses,
                BuildsPlacing - before.BuildsPlacing,
                PlaceCompose - before.PlaceCompose,
                PlaceFade - before.PlaceFade,
                PlaceBuild - before.PlaceBuild,
                PlaceBinds - before.PlaceBinds,
                PlaceLookups - before.PlaceLookups,
                PlaceMatrix - before.PlaceMatrix,
                PlaceSwap - before.PlaceSwap,
                PlaceFlex - before.PlaceFlex,
                CollateFixed - before.CollateFixed,
                CollateByIdentity - before.CollateByIdentity);
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
        EntitiesBuilt,
        SkeletonPose.PoseBuilds,
        Corpses.SteppingTicks,
        Corpses.Steps,
        Corpses.Count,
        CollateTicks,
        BakedTicks,
        PlaceTicks,
        AdvanceTicks,
        Corpses.Rebuilds,
        Corpses.BuildingTicks,
        BuildsSimulating,
        BuildsDrawing,
        BuildsCorpses,
        BuildsPlacing,
        PlaceComposeTicks,
        PlaceFadeTicks,
        PlaceBuildTicks,
        PlaceBindsTicks,
        PlaceLookupsTicks,
        PlaceMatrixTicks,
        PlaceSwapTicks,
        PlaceFlexTicks,
        CollateFixed,
        CollatedByIdentity);

    /// <summary>Unparented props the world collation filed, ever (finding 82).</summary>
    public int CollateFixed { get; private set; }

    /// <summary>Of those, how many were filed by identity — the same object as the previous collation's, so unchanged (finding 82).</summary>
    /// <remarks>Counted where the identity path is taken, so it reports the path the code used (B243).</remarks>
    public int CollatedByIdentity { get; private set; }

    /// <summary>What the previous world collation filed each object as, by reference — the engine's handle on the entity (finding 82).</summary>
    private Dictionary<SceneProp, FiledObject> _filedLast = new(ReferenceEqualityComparer.Instance);

    private Dictionary<SceneProp, FiledObject> _filedNow = new(ReferenceEqualityComparer.Instance);

    /// <summary>Model resolutions done, ever: one per object per generation of the model set (finding 82).</summary>
    /// <remarks>Counted where the resolution happens, so it reports what the code did (B243).</remarks>
    public int ModelsResolved { get; private set; }

    /// <summary>What a prop object's model resolved to, and its placement while unparented — held by the object (finding 82).</summary>
    /// <remarks>
    /// **The engine resolves a model when the entity takes an update** — `SetModel` from `PostDataUpdate` — and keeps the
    /// absolute transform until `EFL_DIRTY_ABSTRANSFORM` says it moved (`CalcAbsolutePosition` returns at once when the flag is clear, `c_baseentity.cpp:4360`).
    /// A prop object is immutable and the sample hands an unchanged track back as the same object (B259 stage C), so a
    /// new object IS the update and the dirty flag.
    ///
    /// **A plain dictionary swept for objects not seen lately, not a `ConditionalWeakTable`.** The weak table was tried
    /// first and measured: its dependent handles are scanned by every collection, and the collector's pause went from
    /// 2.3 to 17.8 ms a frame on f12.
    ///
    /// **And swept every frame of every set, the corpse record's included.** The first dictionary version measured worse —
    /// GC pause about 1 ms a frame higher on f12, in each of three alternating pairs, GC heap 4.5 GB against 3.8 — because
    /// only the world collation swept it, and the D181 record's private set has none: it held every corpse object of every
    /// tick it had recorded. Two maps swapped each frame keep only the objects asked for this frame and the last.
    /// </remarks>
    private Dictionary<SceneProp, ResolvedModel> _resolved = new(ReferenceEqualityComparer.Instance);

    /// <summary>The previous world pass's resolutions, which an object still current moves out of when it is asked.</summary>
    private Dictionary<SceneProp, ResolvedModel> _resolvedLast = new(ReferenceEqualityComparer.Instance);

    /// <summary>Forgets every resolution not asked for since the last world pass (finding 82).</summary>
    private void SweepResolved()
    {
        (_resolvedLast, _resolved) = (_resolved, _resolvedLast);
        _resolved.Clear();
    }

    /// <summary>How many prop objects hold a resolution now — a level, for the sweep's test (finding 82).</summary>
    public int ResolutionsHeld => _resolved.Count + _resolvedLast.Count;

    /// <summary>Bumped whenever the model set gains or loses a model, so a resolution against the old set is redone.</summary>
    private int _modelsGeneration;

    /// <summary>One object's resolution: the generation it was made in, its frames and baked batches, its placement.</summary>
    private sealed class ResolvedModel
    {
        public int Generation { get; set; } = -1;

        public PropModels.ModelFrames? Frames { get; set; }

        public List<List<WorldBatch>>? Batches { get; set; }

        /// <summary>The unparented placement, the 3×4 `PlacementOf` writes; null until asked or for a parented prop.</summary>
        public float[]? Placement { get; set; }
    }

    /// <summary>The prop object's model, resolved once per generation (finding 82).</summary>
    private ResolvedModel ResolvedFor(SceneProp prop)
    {
        if (!_resolved.TryGetValue(prop, out ResolvedModel? resolved))
        {
            if (!_resolvedLast.Remove(prop, out resolved))
            {
                resolved = new ResolvedModel();
            }

            _resolved[prop] = resolved;
        }

        if (resolved.Generation != _modelsGeneration)
        {
            resolved.Generation = _modelsGeneration;
            resolved.Frames = _frames.GetValueOrDefault(prop.ModelPath);
            resolved.Batches = _byModel.GetValueOrDefault(prop.ModelPath);
            resolved.Placement = null;
            ModelsResolved++;
        }

        return resolved;
    }

    /// <summary>One object's filing: its leaf handle and its box.</summary>
    private readonly record struct FiledObject(int Handle, (float, float, float, float, float, float) Box);

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

    /// <summary>The flex deltas' accumulation arrays, reused by every face that changes (finding 80); grown to the model drawn.</summary>
    private (float[] Positions, float[] Normals) _flexScratch = ([], []);

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

    /// <summary>Per packed model, which corner of its geometry each vertex of its buffer is, every frame end to end.</summary>
    /// <remarks>
    /// **The buffer groups corners by material, so its order is not the geometry's**, and a per-placement
    /// colour stream indexed by buffer vertex has to be laid out in this order (B426).
    /// </remarks>
    private readonly Dictionary<string, int[]> _packedOrder = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per model with flexes, the face it wears in no scene — null when that moves nothing (B513).</summary>
    private readonly Dictionary<string, float[]?> _restingFace = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many instances were given a moving face, since the set was made (B513).</summary>
    /// <remarks>Counted where the stream is attached to the instance, so it reports what the draw USES (B243).</remarks>
    public int FlexedInstances { get; private set; }

    /// <summary>The face a player wears now: the scene's expressions through the rules, in buffer order (B513).</summary>
    /// <remarks>
    /// **The scene is the VCD gesture's**, the one this viewer tracks per player; it ends when the scene stops
    /// playing back, because <c>C_SceneEntity::StopPlayback</c> clears the actor's scene events. A scene running
    /// alongside another on the same actor is not tracked (B513 remainder).
    /// </remarks>
    private float[]? FlexFor(SceneProp prop, PropModels.ModelFrames? model, double seconds)
    {
        if (model is null || !model.Flex.HasVertexAnimation || model.CornerVertex.Count == 0 ||
            !_packedOrder.TryGetValue(prop.ModelPath, out int[]? order))
        {
            return null;
        }

        float[]? face;

        if (FaceDriving(prop, seconds) is { } global)
        {
            float[] weights = FaceFlex.Rules(model.Flex, FaceFlex.Local(model.Flex, global));
            // A corpse runs its player's whole SetupWeights, so the delay is the player's own (`m_flFlexDelayedWeight`).
            float[] delayed = Delayed(prop.FaceOf ?? prop.EntityIndex, weights, seconds);

            // The same weights draw the same face: an idle loop's expression holds still for seconds at a time.
            if (_lastFace.TryGetValue(prop.EntityIndex, out (float[] Weights, float[] Delayed, float[]? Face, float[]? Stream) last) &&
                last.Weights.AsSpan().SequenceEqual(weights) && last.Delayed.AsSpan().SequenceEqual(delayed))
            {
                face = last.Face;
            }
            else
            {
                // **Written into arrays this set keeps, not new ones per frame** (finding 80): a talking face changes every
                // frame, and three .vvd-sized arrays a face a frame were 2 GB of large-object garbage in 20 s of f12. The
                // deltas are scratch; the stream is the entity's own, rewritten in place (the renderer uploads per draw).
                // Grown only: faces of different classes alternate, and an exact size would reallocate at every switch.
                if (_flexScratch.Positions.Length < model.VertexCount * 3)
                {
                    _flexScratch = (new float[model.VertexCount * 3], new float[model.VertexCount * 3]);
                }

                // Kept apart from the face, which is null while nothing moves: a face that stops and starts again would
                // otherwise lose its stream at every stop and allocate a new one at every start.
                float[] stream = last.Stream is { } kept && kept.Length == order.Length * 6 ? kept : new float[order.Length * 6];

                face = FaceFlex.Deltas(model.Flex, weights, delayed, _flexScratch.Positions, _flexScratch.Normals) is { } deltas
                    ? FaceFlex.InBufferOrder(deltas, model.CornerVertex, order, stream)
                    : null;
                _lastFace[prop.EntityIndex] = (weights, (float[])delayed.Clone(), face, stream);
            }
        }
        else if (!_restingFace.TryGetValue(RestingKey(prop), out face))
        {
            // **No face of its own**: a bone-merged item whose parent is not a player is a C_BaseFlex at its zeroed
            // controllers (each at its minimum, `c_baseflex.cpp:1222`); anything else is a C_BaseAnimating, whose
            // `SetupWeights` is empty, so it draws with the weights `LockFlexWeights` zeroed (studiorender
            // 0x1800584b0) — which still moves a flex whose ramp is not zero at zero (B513). A corpse whose player is
            // alive again is the first kind: `C_TFRagdoll::SetupWeights` takes its own `BaseClass` branch
            // (`c_tf_player.cpp:630-633`), a C_BaseFlex nothing ever set.
            float[] weights = IsCBaseFlex(prop)
                ? FaceFlex.Rules(model.Flex, StudioFlexRules.Resting(model.Flex))
                : new float[model.Flex.Descriptors.Count];
            face = FaceFlex.Deltas(model.Flex, weights, weights, model.VertexCount) is { } deltas
                ? FaceFlex.InBufferOrder(deltas, model.CornerVertex, order)
                : null;
            _restingFace[RestingKey(prop)] = face;
        }

        if (face is not null)
        {
            FlexedInstances++;
        }

        return face;
    }

    private static string RestingKey(SceneProp prop) => (IsCBaseFlex(prop) ? "1:" : "0:") + prop.ModelPath;

    /// <summary>Whether a prop with no player face behind it is still a <c>C_BaseFlex</c>: a worn item or a corpse.</summary>
    private static bool IsCBaseFlex(SceneProp prop) => prop.AttachedTo is not null || prop.FaceOf is not null;

    /// <summary>
    /// Whether an entity is a player and alive now (<c>C_TFRagdoll::SetupWeights</c> asks <c>IsAlive()</c>): null for an
    /// entity that is not a player — a class model placed by a map is a prop, not a face. Unset, every class model is
    /// taken for a player and every corpse's player for dead.
    /// </summary>
    public Func<int, bool?>? PlayerAlive { get; set; }

    /// <summary>Each TF player's face, by entity index, with the model it was reset for.</summary>
    private readonly Dictionary<int, (string Model, ActorFace Face)> _faces = [];

    /// <summary>Each drawing entity's <c>m_flFlexDelayedWeight</c> and <c>m_flFlexDelayTime</c>.</summary>
    private readonly Dictionary<int, (float[] Delayed, double Time)> _delays = [];

    /// <summary>Each drawing entity's last weights and the face they made, reused while they hold.</summary>
    private readonly Dictionary<int, (float[] Weights, float[] Delayed, float[]? Face, float[]? Stream)> _lastFace = [];

    /// <summary>What drives faces: every scene and voice in the recording (B513); null drives none.</summary>
    public FaceSources? Faces { get; set; }

    /// <summary>
    /// The global weights a prop's rules read, or null for a prop with no TF player behind it: a player's own face; a
    /// bone-merged item on a player reads the player's (<c>CEconEntity::SetupWeights</c>, <c>econ_entity.cpp:1377</c>); a
    /// corpse reads its dead player's (<c>C_TFRagdoll::SetupWeights</c>, <c>c_tf_player.cpp:636</c>).
    /// </summary>
    private IReadOnlyDictionary<string, float>? FaceDriving(SceneProp prop, double seconds)
    {
        int actor;

        if (prop.FaceOf is { } dead)
        {
            // `( pPlayer && pPlayer->IsAlive() ) || !pPlayer` takes the corpse's own face (c_tf_player.cpp:630).
            if (PlayerAlive?.Invoke(dead) == true)
            {
                return null;
            }

            actor = dead;

            // A player who died before the first frame drawn has no face yet; the corpse wears their class model, so
            // the face is that model's (only when absent: a living player's own draw keeps it current).
            if (!_faces.ContainsKey(actor))
            {
                EnsureFace(actor, prop.ModelPath, seconds);
            }
        }
        else if (prop.BoneMerged && prop.AttachedTo is { } parent && _faces.ContainsKey(parent))
        {
            actor = parent;
        }
        else if (IsPlayerModel(prop) && (PlayerAlive is null || PlayerAlive(prop.EntityIndex) is not null))
        {
            actor = prop.EntityIndex;
            EnsureFace(actor, prop.ModelPath, seconds);
        }
        else
        {
            return null;
        }

        if (!_faces.TryGetValue(actor, out (string Model, ActorFace Face) face))
        {
            return null;
        }

        face.Face.Step(
            seconds,
            prop.EntityIndex,
            Faces?.Scenes(actor, seconds) ?? GestureScenes(prop),
            Faces?.Voices(actor, seconds) ?? []);

        return face.Face.Global;
    }

    /// <summary>
    /// Every player's frame start, drawn or not: the engine interpolates and latches each client-animated entity every
    /// frame (`InterpolateServerEntities`, `UpdateClientSideAnimations`, `cdll_client_int.cpp:2156`, `:2189`) and each
    /// scene thinks every frame (`C_SceneEntity::ClientThink`), whatever is on screen — so a face that returns to view is
    /// current. Only `SetupGlobalWeights` — the decay, the flex animations, the expressions — waits for a draw.
    /// </summary>
    private void BeginFaces(IReadOnlyList<SceneProp> props, double seconds)
    {
        foreach (SceneProp prop in props)
        {
            if (IsPlayerModel(prop) && (PlayerAlive is null || PlayerAlive(prop.EntityIndex) is not null))
            {
                EnsureFace(prop.EntityIndex, prop.ModelPath, seconds);

                if (_faces.TryGetValue(prop.EntityIndex, out (string Model, ActorFace Face) face))
                {
                    face.Face.BeginFrame(seconds, Faces?.Scenes(prop.EntityIndex, seconds) ?? GestureScenes(prop));
                }
            }
        }
    }

    /// <summary>A player's face for a model: kept while the model holds, reset when it changes or the clock runs back.</summary>
    private void EnsureFace(int actor, string modelPath, double seconds)
    {
        if (!_frames.TryGetValue(modelPath, out PropModels.ModelFrames? own))
        {
            return;
        }

        if (!_faces.TryGetValue(actor, out (string Model, ActorFace Face) known) ||
            !string.Equals(known.Model, modelPath, StringComparison.OrdinalIgnoreCase) ||
            seconds < known.Face.LastSeconds)
        {
            // `C_TFPlayer::OnNewModel` resets the face; a seek back starts it again too (interpolated: the engine
            // never plays a demo backwards).
            _faces[actor] = (modelPath, new ActorFace(
                own.Flex, own.Flex.ModelName, Faces,
                Faces?.InterpolationSeconds ?? ScenePropTrack.DelayTicksFor(IntervalPerTick) * (double)IntervalPerTick,
                seconds));
            _delays.Remove(actor);
        }
    }

    /// <summary>A TF class model, <c>models/player/&lt;class&gt;.mdl</c> — the one shape a <c>C_TFPlayer</c> draws.</summary>
    private static bool IsPlayerModel(SceneProp prop) =>
        !prop.BoneMerged && prop.FaceOf is null &&
        prop.ModelPath.StartsWith("models/player/", StringComparison.OrdinalIgnoreCase) &&
        prop.ModelPath.IndexOf('/', "models/player/".Length) < 0;

    /// <summary>Without a recording's sources, the gesture slot's scenes — the scene a test hands a prop.</summary>
    private static List<FaceScene> GestureScenes(SceneProp prop) =>
        [.. (prop.Pose.Gestures ?? []).Where(g => g.Taunt is not null)
            .Select(g => new FaceScene(g.Taunt!, g.StartedSeconds, g.StoppedSeconds))];

    /// <summary><c>RunFlexDelay</c> on the drawing entity's own delayed weights.</summary>
    private float[] Delayed(int entity, float[] weights, double seconds)
    {
        if (!_delays.TryGetValue(entity, out (float[] Delayed, double Time) state) ||
            state.Delayed.Length != weights.Length || seconds < state.Time)
        {
            state = (new float[weights.Length], 0d);
        }

        // A second draw in one frame blends nothing: the time it left is now (`flFlexDelayTime < curtime` fails).
        double time = state.Time;
        FaceFlex.Delay(weights, state.Delayed, ref time, seconds, seconds - state.Time);

        _delays[entity] = (state.Delayed, time);
        return state.Delayed;
    }

    /// <summary>Each baked static prop's colours in its model buffer's order, built once per entity.</summary>
    private readonly Dictionary<int, float[]?> _bakedByEntity = [];

    /// <summary>
    /// Each baked static prop's `.vhv` colours, by entity index — red, green, blue per corner of its model's
    /// geometry, overbright already applied — set when a map is read (B426).
    /// </summary>
    /// <remarks>
    /// `engine.dll` `0x1800f1bd0`: a static prop with baked colours is drawn with its per-placement colour
    /// mesh and no ambient cube and no local lights. An entity found here draws that way; one that is not
    /// is lit per draw.
    /// </remarks>
    public IReadOnlyDictionary<int, float[]> StaticPropColours
    {
        get;
        set
        {
            field = value;
            _bakedByEntity.Clear();
        }
    } = new Dictionary<int, float[]>();

    /// <summary>
    /// The styled and dynamic lights a static prop with a colour mesh draws on top of its colours, by entity and lighting
    /// origin (<see cref="LevelLighting.StaticPlusDynamicLights"/>, B424, B425); null gives it none.
    /// </summary>
    public Func<int, float, float, float, IReadOnlyList<LocalLight>>? StaticPlusDynamicLights { get; set; }

    /// <summary>
    /// The whole lighting state of a static prop drawn without a colour mesh, by entity and lighting origin
    /// (<see cref="LevelLighting.StaticPropFullLightingAt"/>, B514); null leaves it the model path's light.
    /// </summary>
    public Func<int, float, float, float, (PointLighting Lighting, SunLight? Sun)>? StaticPropFullLighting { get; set; }

    /// <summary>
    /// A static prop handle's static lighting state at its lighting origin (<see cref="LevelLighting.StaticPropLightingAt"/>),
    /// which an unbaked <c>$staticprop</c> model's CPU colour mesh is lit with (B429); null lights none on the CPU.
    /// </summary>
    public Func<float, float, float, (PointLighting Lighting, SunLight? Sun)>? StaticPropLighting
    {
        get;
        set
        {
            field = value;
            _bakedByEntity.Clear();
        }
    }

    /// <summary>
    /// An unbaked static prop's colour mesh lit on the CPU, per corner of its model's geometry (B429): `engine.dll`
    /// `FUN_1800f36e0` when the `.vhv` is absent, for a model compiled <c>$staticprop</c> (`FUN_1800eac60`).
    /// </summary>
    /// <remarks>
    /// Built once and never rebuilt. The engine rebuilds it when a listed one-frame style changes (`FUN_1801bb8b0`), but
    /// it builds it from the handle's STATIC state (`FUN_1801ba590` flags 1), which holds no styled light — so the
    /// rebuild reproduces these colours, as it reproduces a `.vhv` prop's by reloading the same file.
    /// </remarks>
    private bool TryLightOnCpu(SceneProp prop, ScenePose pose, float x, float y, float z, out float[] byCorner)
    {
        byCorner = [];

        if (StaticPropLighting is not { } lightingAt ||
            prop.EntityIndex < PropModels.FirstStaticPropEntityIndex ||
            !_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? frames) ||
            !StaticPropVertexLighting.Lights(frames.StudioFlags) ||
            !_raw.TryGetValue(prop.ModelPath, out IReadOnlyList<PropVertex>? corners))
        {
            return false;
        }

        (PointLighting lighting, SunLight? sun) = lightingAt(x, y, z);

        byCorner = StaticPropVertexLighting.Colours(
            corners,
            new PropTransform(pose.X, pose.Y, pose.Z, pose.Pitch, pose.Yaw, pose.Roll, 1f),
            lighting,
            sun,
            StaticPropVertexLighting.ConstantDot(frames.StudioFlags,frames.ConstantDirectionalLightDot));

        return true;
    }

    /// <summary>A baked static prop's colours in its model buffer's order, or null when it has none.</summary>
    /// <remarks>
    /// Null also when the colours do not cover the model's corners — a geometry this set packed differently
    /// from the one the colours were read against — which is refused, said once, and falls to full lighting.
    /// </remarks>
    private float[]? BakedColoursOf(SceneProp prop, ScenePose pose, float x, float y, float z)
    {
        if (_bakedByEntity.TryGetValue(prop.EntityIndex, out float[]? built))
        {
            return built;
        }

        if (StaticPropColours.TryGetValue(prop.EntityIndex, out float[]? byCorner))
        {
            // A `.vhv` mesh `FUN_1800f1550` never wrote takes the CPU bake (B436); with none, the prop is lit per draw.
            if (Array.Exists(byCorner, float.IsNaN))
            {
                byCorner = (float[])byCorner.Clone();

                if (!PropModels.FillUnfilled(byCorner, TryLightOnCpu(prop, pose, x, y, z, out float[] cpu) ? cpu : null))
                {
                    _bakedByEntity[prop.EntityIndex] = null;
                    return null;
                }
            }
        }
        else if (!TryLightOnCpu(prop, pose, x, y, z, out byCorner))
        {
            return null;
        }

        built = null;

        if (_packedOrder.TryGetValue(prop.ModelPath, out int[]? order) &&
            _raw.TryGetValue(prop.ModelPath, out IReadOnlyList<PropVertex>? corners) &&
            byCorner.Length == corners.Count * 3)
        {
            built = new float[order.Length * 3];

            for (int at = 0; at < order.Length; at++)
            {
                Array.Copy(byCorner, order[at] * 3, built, at * 3, 3);
            }
        }
        else
        {
            _render.LogWarning(
                "{Message}",
                $"baked colours for {prop.ModelPath} (entity {prop.EntityIndex}) do not cover its packed geometry; lit per draw instead");
        }

        _bakedByEntity[prop.EntityIndex] = built;
        return built;
    }

    /// <summary>Where a model's light should be sampled, in world space.</summary>
    /// <param name="prop">The prop; its model must have been added.</param>
    /// <param name="pose">Where it stands.</param>
    /// <returns>The point. Public so a probe asks the set itself rather than repeating its rule (`prop-light`).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prop"/> is null.</exception>
    public (float X, float Y, float Z) IlluminationPoint(SceneProp prop, ScenePose pose)
    {
        ArgumentNullException.ThrowIfNull(prop);

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

        // A flagged static prop's lump point replaces the illumination point outright (B427).
        if (prop.LightingOrigin is { } origin)
        {
            return origin;
        }

        // **A move child's pose is LOCAL, so it is lit where the chain puts it** (B551). The wire sends
        // `GetLocalOrigin()` (`server/baseentity.cpp:199`) and the engine samples light at the absolute place
        // `CalcAbsolutePosition` composes (`c_baseentity.cpp:4401`); lighting a `func_door`'s grate at its offset lit it
        // from wherever that offset happens to fall in the map.
        if (prop is { AttachedTo: not null, BoneMerged: false })
        {
            pose = Absolute(prop, AnimatingEntity.MaximumFollowDepth);
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
}
