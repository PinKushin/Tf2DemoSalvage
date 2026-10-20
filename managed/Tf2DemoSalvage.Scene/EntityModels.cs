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

    /// <summary>Whether a pose walks animation events — false for a pass the engine never makes, such as a decal replay.</summary>
    /// <remarks>
    /// The walk's state is the previous frame's cycle (`m_flPrevEventCycle`), so a pass of our own that walked would move
    /// it to a moment the engine's client never rendered, and the next real frame would cross the wrong span (B504).
    /// </remarks>
    public bool WalksEvents { get; set; } = true;

    /// <summary>Whether this frame covers no demo time — `gpGlobals->frametime == 0`, a paused demo — so no event is walked (B541).</summary>
    /// <remarks>
    /// `C_BaseAnimating::Simulate` calls `DoAnimationEvents` only `if ( gpGlobals->frametime != 0.0f )`
    /// (`c_baseanimating.cpp:5162`), and the client's frametime is zero while `demoplayer->IsPlaybackPaused()`
    /// (`CClientState::GetFrameTime`, engine.dll FUN_1800a2d40). **Not while it skips**: `IsPlaybackPaused` answers
    /// false for the length of a skip whatever the pause flag says (FUN_1800729f0), so a skip's frames walk. The walk's
    /// state is untouched by a paused frame, so the first frame time moves again walks from the last one it did.
    /// </remarks>
    public bool Paused { get; set; }

    /// <summary>Whether a pose only advances cycles and walks events — a skip's frame, which draws nothing (B504).</summary>
    public bool SimulatesOnly { get; set; }

    /// <summary>Brings every entity's animation state up to date, before any bones are built.</summary>
    /// <param name="props">What exists at this tick.</param>
    /// <param name="seconds">Demo time, for advancing cycles.</param>
    /// <param name="fireEvents">Whether these entities are visible, so `DoAnimationEvents` walks them.</param>
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
    private void Simulate(IReadOnlyList<SceneProp> props, double seconds, bool fireEvents = true)
    {
        // **Nothing composed while the pass's entities are brought up to date is kept** (B551) — a parent's bones may
        // not be posed yet. Set and cleared here, around the whole of it, so no return or throw can leave it set.
        _simulating = true;

        try
        {
            SimulatePass(props, seconds, fireEvents);
        }
        finally
        {
            _simulating = false;
        }
    }

    /// <summary><see cref="Simulate"/>'s body.</summary>
    private void SimulatePass(IReadOnlyList<SceneProp> props, double seconds, bool fireEvents)
    {
        _simulatedSeconds = seconds;

        // `if ( gpGlobals->frametime != 0.0f ) DoAnimationEvents(...)` (`c_baseanimating.cpp:5162`) — see `Paused`.
        bool walks = fireEvents && WalksEvents && !Paused;

        // **Indexed first, because a placement follows its parent chain up** and the chain can
        // point anywhere in the list. This is what CalcAbsolutePosition walks (c_baseentity.cpp:4387).
        _propsByEntity.Clear();

        // Resolved attachments belong to this pass's camera, not to the entity — see the field.
        _attachments.Clear();
        _absolute.Clear();

        // Events are a per-FRAME answer; the WALK's state is per entity and survives. **Cleared when the time moves, not
        // per pass**: a frame runs this three times (drawn, undrawn, viewmodel), and clearing per pass left only the
        // viewmodel's events, so no player's footstep ever reached a listener (B172).
        // By bits: the question is "the very value this frame handed every pass", not nearness.
        long frame = BitConverter.DoubleToInt64Bits(seconds);

        if (frame != _firedFrame)
        {
            _fired.Clear();
            _walked.Clear();
            _firedFrame = frame;
        }

        foreach (SceneProp prop in props)
        {
            _propsByEntity[prop.EntityIndex] = prop;
        }

        SimulateEach(props, seconds, walks);

        // Parent links second, so every entity exists before anything points at one.
        _drawnPlacements.Clear();
        _parentPlacements.Clear();

        SettleEach(props, seconds);
    }

    /// <summary>The per-entity half of <see cref="SimulatePass"/>, for whichever props reach it.</summary>
    /// <remarks>
    /// **Split out so the map's static props can reach it from the in-view gather alone** (finding 83 item 1): a
    /// <c>CStaticProp</c> is never on the client entity list, so <c>SimulateEntities</c> never visits one, and it is
    /// reached only by <c>BuildRenderablesList</c>. <see cref="Simulated"/> is counted here, in the loop itself (B243).
    /// </remarks>
    private void SimulateEach(IReadOnlyList<SceneProp> props, double seconds, bool walks)
    {
        foreach (SceneProp prop in props)
        {
            Simulated++;

            // Stryker disable all : a mutant that empties the guard body, or that short-circuits
            // past the TryGetValue, leaves 'skinned' unassigned below (CS0165), and Safe Mode then
            // drops every mutation in this method — B410.
            if (ResolvedFor(prop).Frames is not { } entry ||
                entry.Skinned is not { } skinned)
            {
                continue;
            }

            // Stryker restore all

            AnimatingEntity animating = EntityFor(prop, skinned);

            // Stryker disable once : a mutant that empties the guard body leaves 'posed'
            // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
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
            float[] poseValues = PoseValues(skinned, where, sequence);
            double advanced = prop.ClientSideAnimated
                ? Advance(prop, elapsed, seconds, skinned.BlendedCyclesPerSecond(sequence, poseValues) * where.PlaybackRate)
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

            // **A frame that draws nothing stops here** (B504): what follows — layers, transitions, IK, the barrel, the duck
            // jump — is `SetupBones`' input, and the engine builds bones only for what it draws. `C_BaseAnimating::Simulate`
            // advances the cycle and walks the events, which is all a skip's frame needs.
            if (SimulatesOnly)
            {
                if (walks && _walked.Add(prop.EntityIndex))
                {
                    AnimationEvents(prop, skinned, sequence, phase);
                }

                continue;
            }

            // **`MaintainSequenceTransitions` first, then `AccumulateLayers`**, which is the order
            // `StandardBlendingRules` runs them in (`c_baseanimating.cpp:1957`) — and the order
            // matters, because each accumulates onto the result of the last. The sequence being
            // faded out is part of the BODY; a gesture goes over the top of whatever the body
            // settled on (B286).

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
            if (walks && _walked.Add(prop.EntityIndex))
            {
                AnimationEvents(prop, skinned, sequence, phase);
            }

            // Computed once above for the autolayer envelopes and reused here rather than asked
            // twice: the two-pass move_x rescale inside it opens the model.
            posed.PoseValues = poseValues;

            // **`CalcBoneAdj`'s two halves, which come from different places** (B288). The model
            // says which bone each input drives and over what range; the demo says what the input
            // is, networked as eleven bits over nought to one. Neither alone bends anything, and
            // both have been read and dropped until now.
            posed.Controllers = skinned.Controllers;
            posed.BoneControllers = where.BoneControllers;

            // **`CTFMinigun::StandardBlendingRules`, which overwrites one bone after everything
            // else** (B347). The barrel's angle is not on the wire — the client integrates it from
            // `m_iWeaponState` alone — so this is the same shape as the burn clock: watch the
            // networked state and run the engine's own arithmetic from it.
            SpinBarrel(prop, skinned, posed, seconds);

            // **The grenade launcher's chamber, the other weapon in this override family** (B348).
            // A different mechanism from the minigun's — a fixed-length keyframed animation rather
            // than an integrated velocity — and it runs after, because only one of the two can
            // apply to any model and the second call finds no bone.
            TurnChamber(prop, skinned, posed, seconds);

            // **The ROOT model's bytes, for the jiggle bones' own parameters** (B293).
            // `mstudiobone_t::pProcedure()` is an offset from the BONE, and the bones being posed
            // are `Models[0]`'s — reading it against an included animation model's bytes would land
            // on arbitrary floats and produce springs with nonsense constants rather than an error.
            // The same index-space rule the bone controllers follow.
            // **The null arm is spelled out, and it has to be** (B333). `Models[0]` is a `byte[]`,
            // so a conditional against a bare `null` takes `byte[]` as its own type and lifts the
            // RESULT into the property — turning the absent case into `ReadOnlyMemory.Empty`, which
            // is PRESENT. Both of `SkeletonPose`'s `JiggleSource is not { }` guards were dead for
            // that reason.
            posed.JiggleSource = skinned.Models.Count > 0
                ? skinned.Models[0]
                : (ReadOnlyMemory<byte>?)null;

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

            // **B515: a left-handed viewmodel's mirror, on its ROOT bones** — `C_BaseViewModel::ApplyBoneMatrixTransform`
            // through the player's view. Not on a bone-merged `c_` weapon: its merged bones arrive mirrored, and
            // `C_ViewmodelAttachmentModel` inherits the base body, which mirrors nothing. Its jiggle still takes the
            // flag, through `C_BaseAnimating::ShouldFlipViewModel`'s parent walk (`c_baseanimating.cpp:2097`).
            posed.RootReflection = prop.FlipViewModel && prop.AttachedTo is null && ViewmodelProjection is { } view
                ? ViewmodelFlip.Reflection(view.Eye, view.Right)
                : null;
            posed.Flipped = prop.FlipViewModel;

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
    }

    /// <summary>The placement half of <see cref="SimulatePass"/>: light points, parent and drawn placements, attachment.</summary>
    private void SettleEach(IReadOnlyList<SceneProp> props, double seconds)
    {
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
            // The object's own resolution, so no path is hashed (finding 82); `Batches`' clamp, from the batches it holds.
            ResolvedModel model = ResolvedFor(prop);

            if (IsDrawable(prop.Kind) && model.Batches is { Count: > 0 } baked &&
                baked[Math.Clamp(SelectWith(model.Frames, prop, seconds).Frame, 0, baked.Count - 1)].Count > 0)
            {
                _drawnPlacements[prop.EntityIndex] = placed;
            }

            if (!_entities.TryGetValue(prop.EntityIndex, out AnimatingEntity? animating))
            {
                continue;
            }

            // **Followed only when bone-merged** — `IsFollowingEntity` is `EF_BONEMERGE && MOVETYPE_NONE && GetMoveParent()`
            // (the `IsFollowingEntity` note further down this file). A plain move child following its parent merged any bone it
            // shared by name and stood at the parent's attachment, not attachment × local (B551).
            animating.Follows = prop is { AttachedTo: { } wearer, BoneMerged: true }
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
        // **A move child that is NOT bone-merged is attachment × LOCAL, which `PlacementOf` already composed through
        // `Absolute`** (B551, `c_baseentity.cpp:4330-4401`); the bare attachment matrix below would drop its own offset.
        if (!prop.BoneMerged)
        {
            return;
        }

        // Stryker disable all : a mutant that empties the guard body leaves 'point', 'wearer',
        // 'posed', 'wearerModel' and 'attachments' unassigned below (CS0165), and Safe Mode then
        // drops every mutation in this method — B410.
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

        // Stryker restore all

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

        // Stryker disable once : a mutant that empties the guard body leaves 'placement'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
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

    /// <summary>The demo time the last pass simulated to, which an attachment asked between passes is posed at.</summary>
    private double _simulatedSeconds;

    /// <summary>Where one of an entity's named attachments is — <c>LookupAttachment</c> then <c>GetAttachment</c>.</summary>
    /// <param name="entity">The entity, such as a player's active weapon.</param>
    /// <param name="name">The attachment's name; <c>FireBullet</c> asks for <c>muzzle</c>.</param>
    /// <returns>Its world position, or null when the entity is not posed here or its model has no such point.</returns>
    /// <remarks>
    /// **Case-insensitive, and absence is an answer.** `Studio_FindAttachment` compares with `stricmp`, and
    /// `LookupAttachment` returns 0 for a miss, which `GetAttachment` refuses, so `FireBullet` keeps its own start.
    /// Resolved through the same per-pass table the worn items use, so a weapon asked twice in one pass costs one
    /// bone setup — `SetupBones( …, BONE_USED_BY_ATTACHMENT, … )` is what `GetAttachment` runs.
    /// </remarks>
    public (float X, float Y, float Z)? AttachmentPosition(int entity, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Attachment(entity, attachments => Named(attachments, name));
    }

    /// <summary>Where an entity's attachment is by the engine's own number — `GetAttachment( iAttachment )`, 1-based.</summary>
    /// <param name="entity">The entity, such as a sentry gun.</param>
    /// <param name="number">The attachment number a dispatch carries; 0 is none.</param>
    /// <returns>Its world position, or null when the entity is not posed here or has no such attachment.</returns>
    public (float X, float Y, float Z)? AttachmentPosition(int entity, int number) =>
        Attachment(entity, attachments => number >= 1 && number <= attachments.Count ? number - 1 : -1);

    /// <summary>An attachment as a particle control point: `PATTACH_POINT_FOLLOW`'s origin and orientation.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="number">The attachment number, 1-based.</param>
    /// <returns>The point, or null when the entity is not posed here or has no such attachment.</returns>
    /// <remarks>
    /// `MatrixVectors( attachmentToWorld, &amp;forward, &amp;right, &amp;up )`: forward is the matrix's first column, up its third,
    /// and right the NEGATED second, because a Source matrix's second axis points left.
    /// </remarks>
    public ParticleControlPoint? AttachmentPoint(int entity, int number) =>
        PointOf(AttachmentMatrix(entity, attachments => number >= 1 && number <= attachments.Count ? number - 1 : -1));

    /// <summary>A named attachment as a particle control point — `PATTACH_POINT_FOLLOW` on `LookupAttachment( name )`.</summary>
    /// <param name="entity">The entity, such as a weapon.</param>
    /// <param name="name">The attachment's name, such as <c>muzzle</c>.</param>
    /// <returns>The point, or null when the entity is not posed here or its model has no such attachment.</returns>
    public ParticleControlPoint? AttachmentPoint(int entity, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return PointOf(AttachmentMatrix(entity, attachments => Named(attachments, name)));
    }

    /// <summary>`Studio_FindAttachment`: the first attachment of that name, compared without case; −1 for none.</summary>
    private static int Named(IReadOnlyList<StudioAttachment> attachments, string name)
    {
        for (int index = 0; index < attachments.Count; index++)
        {
            if (string.Equals(attachments[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>An attachment's 3×4 matrix as a control point: forward, the negated left column as right, up.</summary>
    private static ParticleControlPoint? PointOf(float[]? matrix) =>
        matrix is null
            ? null
            : new ParticleControlPoint(
                new Vector3(matrix[3], matrix[7], matrix[11]),
                new Vector3(matrix[0], matrix[4], matrix[8]),
                new Vector3(-matrix[1], -matrix[5], -matrix[9]),
                new Vector3(matrix[2], matrix[6], matrix[10]));

    /// <summary>`GetAttachment` for the attachment <paramref name="pick"/> chooses, −1 for none.</summary>
    private (float X, float Y, float Z)? Attachment(int entity, Func<IReadOnlyList<StudioAttachment>, int> pick) =>
        AttachmentMatrix(entity, pick) is { } placement ? (placement[3], placement[7], placement[11]) : null;

    /// <summary>The attachment's world matrix, 3×4 row-major, for the attachment <paramref name="pick"/> chooses.</summary>
    private float[]? AttachmentMatrix(int entity, Func<IReadOnlyList<StudioAttachment>, int> pick) =>
        InScene(entity) is null ? null : PosedAttachmentMatrix(entity, pick);

    /// <summary>An entity's readable bones by usage bit, for a test of what a query built; 0 for one not held.</summary>
    internal int ReadableBonesOf(int entity) => _entities.TryGetValue(entity, out AnimatingEntity? animating) ? animating.ReadableBones : 0;

    /// <summary>Attachment queries that reached a bone setup, ever (finding 82).</summary>
    public long AttachmentQueries { get; private set; }

    /// <summary>Skeletons those queries built, counted at the build (finding 82).</summary>
    public long AttachmentBuilds { get; private set; }

    /// <summary>Queries that built a skeleton the entity had already built this frame (finding 82).</summary>
    public long AttachmentRebuilds { get; private set; }

    /// <summary>Time spent in those queries' bone setups, in stopwatch ticks (finding 82).</summary>
    public long AttachmentTicks { get; private set; }

    /// <summary>The attachment matrix of an entity posed in the pass in flight, which the scene does not list yet.</summary>
    /// <remarks>For a move child's parent during `Simulate` (B551); everything asked from outside goes through
    /// <see cref="AttachmentMatrix"/>, which refuses an entity that has left the scene.</remarks>
    private float[]? PosedAttachmentMatrix(int entity, Func<IReadOnlyList<StudioAttachment>, int> pick)
    {
        // Stryker disable all : a mutant that empties the guard body leaves the out variables unassigned
        // below (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (!_entities.TryGetValue(entity, out AnimatingEntity? animating) ||
            !_entityModels.TryGetValue(entity, out string? model) ||
            !_frames.TryGetValue(model, out PropModels.ModelFrames? frames) ||
            frames.Attachments is not { Count: > 0 } attachments)
        {
            return null;
        }

        // Stryker restore all
        int point = pick(attachments);

        if (point < 0)
        {
            return null;
        }

        // **Counted where the bones are asked for** (finding 82, B243): how many queries, how many built a skeleton, how
        // many of those rebuilt one already built this frame, and the time, all carried from this call.
        long askedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        int buildsBefore = SkeletonPose.PoseBuilds;
        bool builtBefore = animating.BuiltThisFrame;
        // **`CalcAttachments` is `SetupBones( NULL, -1, BONE_USED_BY_ATTACHMENT, gpGlobals->curtime )`** (finding 82,
        // `c_baseanimating.cpp:2088-2094`): an attachment asks for the attachment bones, and `SetupBones` widens that by
        // what is readable and by last frame's mask (`:2920`). This asked for every bone, so an entity nothing drew this
        // frame had its whole skeleton built to place one effect.
        bool posed = animating.SetupBones(StudioBoneFlags.UsedByAttachment, _simulatedSeconds);
        int built = SkeletonPose.PoseBuilds - buildsBefore;

        // Only queries from outside the pass — effects, decals, sounds — which is the cost finding 82 is ranking.
        if (!_simulating)
        {
            AttachmentQueries++;
            AttachmentBuilds += built;
            AttachmentRebuilds += builtBefore && built > 0 ? 1 : 0;
            AttachmentTicks += System.Diagnostics.Stopwatch.GetTimestamp() - askedAt;
        }

        return posed ? AttachmentsOf(entity, animating, attachments)[point] : null;
    }

    /// <summary>How far along a ray an entity's posed hitboxes let it get — <c>C_BaseAnimating::TestHitboxes</c>.</summary>
    /// <param name="entity">The entity, such as a player.</param>
    /// <param name="start">`ray.m_Start`.</param>
    /// <param name="delta">`ray.m_Delta`.</param>
    /// <param name="mask">The trace's contents mask.</param>
    /// <returns>The fraction, or null when it is not hit, not posed here, or has no hitboxes.</returns>
    /// <remarks>
    /// Set 0, which is `m_nHitboxSet` for every TF2 player; the bones as the last pass posed them, which is
    /// `GetBoneCache` after `SetupBones( …, BONE_USED_BY_HITBOX, … )`.
    /// </remarks>
    public float? TraceHitboxes(int entity, System.Numerics.Vector3 start, System.Numerics.Vector3 delta, int mask)
    {
        // Stryker disable all : a mutant that empties the guard body leaves the out variables unassigned
        // below (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (InScene(entity) is null ||
            !_entities.TryGetValue(entity, out AnimatingEntity? animating) ||
            !_entityModels.TryGetValue(entity, out string? model) ||
            !_frames.TryGetValue(model, out PropModels.ModelFrames? frames) ||
            frames.Hitboxes is not { Count: > 0 } sets ||
            !animating.SetupBones(StudioBoneFlags.UsedByAnything, _simulatedSeconds))
        {
            return null;
        }

        // Stryker restore all
        return StudioHitboxes.Trace(sets[0], bone => animating.Bones.Bone(bone).ToArray(), start, delta, mask);
    }

    /// <summary>
    /// An entity's hitboxes as the last pass posed them — <c>CParticleSystemQuery::GetControllingObjectHitBoxInfo</c>
    /// (`particlesystemquery.cpp:392-462`), for a particle effect that is born on and rides a model (B538).
    /// </summary>
    /// <param name="entity">The entity, such as a player.</param>
    /// <returns>
    /// Set 0's boxes on their bones, in set order, and the origin the entity had when they were posed; null when it is
    /// not posed here or has none.
    /// </returns>
    /// <remarks>
    /// **The origin is the pose's own, not the frame's.** The frame a seek lands on asks before its models are posed
    /// again, and bones from the tick before paired with this tick's origin put a two-second catch-up hundreds of
    /// units off its player — seen, as a patient's pluses hanging where he had stood a minute earlier.
    /// </remarks>
    public (IReadOnlyList<ModelHitbox> Boxes, System.Numerics.Vector3 PosedAt)? PosedHitboxes(int entity)
    {
        // Stryker disable all : a mutant that empties the guard body leaves the out variables unassigned
        // below (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (InScene(entity) is not { } seen ||
            !_entities.TryGetValue(entity, out AnimatingEntity? animating) ||
            !_entityModels.TryGetValue(entity, out string? model) ||
            !_frames.TryGetValue(model, out PropModels.ModelFrames? frames) ||
            frames.Hitboxes is not { Count: > 0 } sets ||
            !animating.SetupBones(StudioBoneFlags.UsedByAnything, _simulatedSeconds))
        {
            return null;
        }

        // Stryker restore all
        ModelHitbox[] posed = new ModelHitbox[sets[0].Count];

        for (int index = 0; index < posed.Length; index++)
        {
            StudioHitbox box = sets[0][index];

            posed[index] = ModelHitbox.On(box.Min, box.Max, animating.Bones.Bone(box.Bone));
        }

        return (posed, new System.Numerics.Vector3(seen.Pose.X, seen.Pose.Y, seen.Pose.Z));
    }

    /// <summary>How far a point lies outside one of an entity's posed hitboxes, in the box's own frame — an instrument.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="box">The box's index in set 0, as a server `Impact`'s `m_nHitBox` names it.</param>
    /// <param name="point">The world point, such as the server's hit.</param>
    /// <returns>The distance from the box's surface, zero inside it, and the offset from its centre in its bone's
    /// frame; null when the entity or box is not there.</returns>
    public (float Outside, System.Numerics.Vector3 FromCentre)? HitboxGap(int entity, int box, System.Numerics.Vector3 point)
    {
        if (InScene(entity) is null ||
            !_entities.TryGetValue(entity, out AnimatingEntity? animating) ||
            !_entityModels.TryGetValue(entity, out string? model) ||
            !_frames.TryGetValue(model, out PropModels.ModelFrames? frames) ||
            frames.Hitboxes is not { Count: > 0 } sets || box < 0 || box >= sets[0].Count ||
            !animating.SetupBones(StudioBoneFlags.UsedByAnything, _simulatedSeconds))
        {
            return null;
        }

        StudioHitbox hitbox = sets[0][box];
        ReadOnlySpan<float> m = animating.Bones.Bone(hitbox.Bone);
        System.Numerics.Vector3 relative = point - new System.Numerics.Vector3(m[3], m[7], m[11]);

        // Into the bone's frame: the rotation's transpose.
        System.Numerics.Vector3 local = new(
            (m[0] * relative.X) + (m[4] * relative.Y) + (m[8] * relative.Z),
            (m[1] * relative.X) + (m[5] * relative.Y) + (m[9] * relative.Z),
            (m[2] * relative.X) + (m[6] * relative.Y) + (m[10] * relative.Z));

        System.Numerics.Vector3 clamped = System.Numerics.Vector3.Clamp(local, hitbox.Min, hitbox.Max);

        return (System.Numerics.Vector3.Distance(local, clamped), local - ((hitbox.Min + hitbox.Max) * 0.5f));
    }

    /// <summary>
    /// The point on one of an entity's posed hitboxes nearest a viewer — <c>ComputeBeamEntPosition</c>'s hitbox branch
    /// (`view_beams.cpp:233-257`), for a beam flagged <c>FBEAM_USE_HITBOXES</c>.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="number">The hitbox, ONE-based as the beam's attachment index carries it: <c>pHitbox( nAttachment - 1 )</c>.</param>
    /// <param name="viewer"><c>MainViewOrigin()</c>.</param>
    /// <returns>The world point, or null when the entity is not posed here or has no such box.</returns>
    /// <remarks>
    /// <code>
    /// VectorITransform( vecViewPt, *hitboxbones[ pHitbox-&gt;bone ], vecLocalViewPt );
    /// CalcClosestPointOnAABB( pHitbox-&gt;bbmin, pHitbox-&gt;bbmax, vecLocalViewPt, vecLocalClosestPt );
    /// VectorTransform( vecLocalClosestPt, *hitboxbones[ pHitbox-&gt;bone ], pt );
    /// </code>
    ///
    /// Set 0, which is <c>m_nHitboxSet</c> for everything TF2 draws; the guard is the engine's
    /// <c>set-&gt;numhitboxes &gt;= nAttachment &amp;&amp; nAttachment &gt; 0</c>.
    /// </remarks>
    public System.Numerics.Vector3? HitboxPointNearest(int entity, int number, System.Numerics.Vector3 viewer)
    {
        if (InScene(entity) is null ||
            !_entities.TryGetValue(entity, out AnimatingEntity? animating) ||
            !_entityModels.TryGetValue(entity, out string? model) ||
            !_frames.TryGetValue(model, out PropModels.ModelFrames? frames) ||
            frames.Hitboxes is not { Count: > 0 } sets || number < 1 || number > sets[0].Count ||
            !animating.SetupBones(StudioBoneFlags.UsedByAnything, _simulatedSeconds))
        {
            return null;
        }

        StudioHitbox hitbox = sets[0][number - 1];
        ReadOnlySpan<float> m = animating.Bones.Bone(hitbox.Bone);
        System.Numerics.Vector3 relative = viewer - new System.Numerics.Vector3(m[3], m[7], m[11]);

        // Into the bone's frame — the rotation's transpose, which is `VectorITransform`.
        System.Numerics.Vector3 local = new(
            (m[0] * relative.X) + (m[4] * relative.Y) + (m[8] * relative.Z),
            (m[1] * relative.X) + (m[5] * relative.Y) + (m[9] * relative.Z),
            (m[2] * relative.X) + (m[6] * relative.Y) + (m[10] * relative.Z));

        System.Numerics.Vector3 closest = System.Numerics.Vector3.Clamp(local, hitbox.Min, hitbox.Max);

        return new System.Numerics.Vector3(
            (m[0] * closest.X) + (m[1] * closest.Y) + (m[2] * closest.Z) + m[3],
            (m[4] * closest.X) + (m[5] * closest.Y) + (m[6] * closest.Z) + m[7],
            (m[8] * closest.X) + (m[9] * closest.Y) + (m[10] * closest.Z) + m[11]);
    }

    /// <summary>Each pass's entities as its last run saw them, by pass name — see <see cref="Instances"/>.</summary>
    private readonly Dictionary<string, Dictionary<int, SceneProp>> _scenes = new(StringComparer.Ordinal);

    /// <summary>The entity as the latest pass that holds it saw it, or null when no pass does — it has left the scene.</summary>
    private SceneProp? InScene(int entity)
    {
        foreach (Dictionary<int, SceneProp> scene in _scenes.Values)
        {
            if (scene.TryGetValue(entity, out SceneProp? prop))
            {
                return prop;
            }
        }

        // A static prop is in the level whether or not a view reached it this frame (finding 83 item 1).
        return _staticByEntity.GetValueOrDefault(entity);
    }

    /// <summary>Whether an entity has been posed by a pass here, so a question about its attachments has an answer.</summary>
    /// <param name="entity">The entity index.</param>
    /// <returns><c>true</c> once a pass has built its skeleton.</returns>
    public bool IsPosed(int entity) => _entities.ContainsKey(entity);

    /// <summary>
    /// An entity's model, skinning matrices and body as the last pass drew it — the pose `CModelRender::AddDecal` sets up
    /// bones for when a decal lands (B415); null for an entity not in this pass or with no skinned model.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The model path, its pose-to-world matrices, `m_nBody` and the model's body parts.</returns>
    /// <remarks>
    /// **Posed on demand, drawn or not** — `CModelRender::AddDecal` calls `SetupBones` itself, so a player out of view
    /// still takes the decal. Reading only what the draw pass left refused 60 of 100 f12 hits and projected the rest of
    /// an off-screen player's onto the pose he last drew with. `SetupBones` is cached per frame, so a drawn entity costs
    /// nothing more.
    /// </remarks>
    public (string Model, IReadOnlyList<float[]> Bones, int Body, IReadOnlyList<(int Base, int Count)>? Parts)? SkinningOf(int entity)
    {
        if (InScene(entity) is not { } prop ||
            !_entities.TryGetValue(entity, out AnimatingEntity? animating) ||
            !_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? frames) ||
            frames.Skinned is not { } skinned ||
            !animating.SetupBones(StudioBoneFlags.UsedByAnything, _simulatedSeconds))
        {
            return null;
        }

        return (prop.ModelPath, Skinning(entity, skinned.Bones, animating.Bones), prop.Pose.Body, frames.BodyParts);
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
        ModelResolved?.Invoke(
            prop.EntityIndex, LoopingPoseParameters(skinned), skinned.StaticProp);

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
}
