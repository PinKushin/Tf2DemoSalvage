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

    /// <summary>What an entity's event walk remembers, and the parity it last saw.</summary>
    private readonly Dictionary<int, (AnimationEventState State, int Parity)> _eventStates = [];

    /// <summary>Scratch for one entity's events, so the walk allocates nothing per frame.</summary>
    private readonly List<StudioEvent> _firedScratch = [];

    /// <summary>Every client animation event that fired this frame, with where it fired.</summary>
    private readonly List<FiredAnimationEvent> _fired = [];

    /// <summary>
    /// The entities whose events this frame has walked: `C_BaseAnimating::Simulate` calls `DoAnimationEvents` once per
    /// entity per frame, and this frame's passes can each hold one entity — walked again under a different sequence, the
    /// walk restarts and fires every event up to the cycle once more (B172).
    /// </summary>
    private readonly HashSet<int> _walked = [];

    /// <summary>Each player's cycle, and the demo time it was advanced to — `m_flCycle` as the client keeps it.</summary>
    private readonly Dictionary<int, (double Seconds, double Cycle)> _clientCycles = [];

    /// <summary>The longest step <see cref="Advance"/> integrates; past it, a seek, the cycle is recomputed.</summary>
    private const double LongestCycleStep = 1d;

    /// <summary>`C_BaseAnimating::FrameAdvance`: a client-animated cycle, advanced by this frame's rate.</summary>
    /// <remarks>
    /// **Integrated for a player, because a player's rate moves** (B172). `addcycle = flInterval * cyclerate *
    /// m_flPlaybackRate` each frame (`c_baseanimating.cpp:5493`), and a run's `Studio_CPS` follows the pose parameters.
    /// `start + elapsed × rate now` jumps the phase whenever the rate changes, which crossed footstep cycles that the
    /// engine's feet never reach. Everything else keeps the closed form: a viewmodel restarts from its own
    /// <c>AnimationStartSeconds</c>, and a prop's rate is constant. A seek, or the first frame, starts from the closed form.
    /// </remarks>
    private double Advance(SceneProp prop, double elapsed, double seconds, double rate)
    {
        double closed = prop.Pose.Cycle + (elapsed * rate);

        if (prop.Pose.Speed is null)
        {
            return closed;
        }

        if (_restartCycles.Remove(prop.EntityIndex))
        {
            _clientCycles[prop.EntityIndex] = (seconds, 0d);
            return 0d;
        }

        if (!_clientCycles.TryGetValue(prop.EntityIndex, out (double Seconds, double Cycle) last) ||
            Math.Abs(seconds - last.Seconds) > LongestCycleStep)
        {
            _clientCycles[prop.EntityIndex] = (seconds, closed);
            return closed;
        }

        // A frame a hair behind the last holds the cycle: the engine's never runs backwards, and restarting from the closed
        // form here jumped the phase across a footstep and back, over and over within one tick.
        if (seconds <= last.Seconds)
        {
            return last.Cycle;
        }

        double cycle = last.Cycle + ((seconds - last.Seconds) * rate);

        _clientCycles[prop.EntityIndex] = (seconds, cycle);
        return cycle;
    }

    /// <summary>The bits of the demo time <see cref="_fired"/> belongs to; NaN's before the first frame.</summary>
    private long _firedFrame = BitConverter.DoubleToInt64Bits(double.NaN);

    /// <summary>Moves every client animation event crossed since the last take into <paramref name="into"/>.</summary>
    /// <param name="into">Receives the events, in the order the engine fires them.</param>
    /// <remarks>
    /// **Taken, not read: `DoAnimationEvents` fires an event ONCE**, on the frame its cycle is crossed
    /// (<c>c_baseanimating.cpp:3550</c>). This was a property read every frame, and the list was only cleared when the
    /// demo time moved, so a paused demo handed the sound pass the same footsteps on every frame — 27,882 starts at one
    /// tick in a UI session's log. Order is the engine's, which on a loop is the old lap's tail before the new head.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
    public void TakeFiredEvents(ICollection<FiredAnimationEvent> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        foreach (FiredAnimationEvent fired in _fired)
        {
            into.Add(fired);
        }

        _fired.Clear();
    }

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
    /// <remarks>
    /// **An instance method because it COUNTS its own substitutions** (B350). It was static, and a
    /// counter kept anywhere else would measure this method's intent rather than its work — the
    /// fault B347 shipped and a sabotage caught.
    /// </remarks>
    private List<PoseLayer> LayersFor(
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

        // Stryker disable once : a mutant that empties the guard body leaves 'gestures'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (prop.Pose.Gestures is not { Count: > 0 } gestures)
        {
            return layers;
        }

        // **The attack-and-reload slot's holder, decided before it is drawn** (B437): a voice command waiting behind it
        // takes the slot only if the holder is no longer active when the command arrives. Drawn when the walk moves past
        // the slot, so the layer keeps its place in slot order.
        (SceneGesture Gesture, int Sequence)? holder = null;

        foreach (SceneGesture gesture in gestures)
        {
            if (gesture.Slot == GestureSlot.AttackAndReload)
            {
                int resolved = gesture.ActivityName is { Length: > 0 } asked
                    ? skinned.ForActivity(TranslateGesture(prop, skinned, asked))
                    : 0;

                if (!gesture.OnlyIfSlotIdle ||
                    holder is not { } held ||
                    !ActiveAt(skinned, held.Sequence, held.Gesture, gesture.StartedSeconds))
                {
                    holder = (gesture, resolved);
                }

                continue;
            }

            if (holder is { } playing)
            {
                Layer(layers, skinned, playing.Sequence, playing.Gesture, seconds);
                holder = null;
            }

            // **A VCD gesture names a SEQUENCE, not an activity**, and so takes neither the weapon
            // rewrite nor the activity search below (B351). `C_TFPlayer::StartGestureSceneEvent` does
            // `info->m_nSequence = LookupSequence( event->GetParameters() )`
            // (`c_tf_player.cpp:9456`) — `LookupSequence` matches a sequence LABEL, which is what
            // `Find` does, where `SelectWeightedSequence` matches an activity. Putting a taunt
            // through the activity path resolves nothing, because no sequence is labelled with an
            // activity name.
            if (gesture.Taunt is { } taunt)
            {
                // **The scene's own clock, folded by its LOOP, decides which gesture is playing and
                // how far into it we are.** `DispatchProcessLoop` sets the scene's time back to the
                // loop's parameter (`c_sceneentity.cpp:594`), so a press-and-hold taunt crosses its
                // gesture event again and `StartGestureSceneEvent` restarts the layer — which in
                // closed form is folding the elapsed time into the loop's window.
                float scene = taunt.TimeAt((float)(seconds - gesture.StartedSeconds));

                if (taunt.At(scene) is { } staged)
                {
                    Layer(
                        layers,
                        skinned,
                        skinned.Find(staged.Gesture.Sequence),
                        gesture with { StartedSeconds = seconds - staged.Elapsed },
                        seconds);
                }

                continue;
            }

            // `SelectWeightedSequence( iGestureActivity )`, and its `<= 0` abandonment. An activity
            // number rather than a name is the two custom-gesture events, which carry the activity
            // on the wire; nothing resolves those yet, so they are skipped rather than guessed at.
            // Stryker disable once : a mutant that empties the guard body leaves 'named'
            // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
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
            //
            // **The player's own table comes first** (B437): `TranslateActivity` asks `ActivityOverride` before the
            // weapon, so a humiliated loser's landing is `ACT_MP_JUMP_LAND_LOSERSTATE` and a carrying engineer's
            // voice gestures are the `_BUILDING` ones.
            string activity = TranslateGesture(prop, skinned, named);

            // **`ForActivity`, not `Find`, and the difference is the whole mechanism.** `Find`
            // matches a sequence LABEL the way `Studio_LookupSequence` does; the engine resolves a
            // gesture through `SelectWeightedSequence( iGestureActivity )`, which matches the
            // ACTIVITY and picks among ties by `actweight`. A gesture names an activity —
            // `ACT_MP_GESTURE_FLINCH_CHEST` — and no sequence is labelled that, so asking `Find`
            // returns −1 for every gesture on every model. Measured: a flinch half a second old,
            // three gestures reaching the drawn prop, and zero layers.
            int sequence = skinned.ForActivity(activity);

            // **A FLINCH is the one gesture the engine substitutes rather than abandoning** (B350),
            // and it does so before `AddToGestureSlot` is ever reached
            // (`multiplayer_animstate.cpp:376`):
            //
            //     if ( iActivity != ACT_MP_GESTURE_FLINCH_CHEST &&
            //          GetBasePlayer()->SelectWeightedSequence( iActivity ) == -1 )
            //         RestartGesture( GESTURE_SLOT_FLINCH, ACT_MP_GESTURE_FLINCH_CHEST );
            //
            // **Measured, and it is most of them.** TF2's class models declare only
            // `ACT_MP_GESTURE_FLINCH_CHEST` in the merged table — no HEAD, LEFTARM, RIGHTARM,
            // LEFTLEG or RIGHTLEG. `tf2-2026-pub-pov-cheater` fires 55 chest flinches and 69
            // non-chest ones, every one of which this dropped.
            //
            // **Resolved here rather than at event time, which is where the engine decides.** The
            // substitution needs the model, and only this layer has it; the layer produced is the
            // same either way because nothing between the two moments can change which sequences a
            // model declares.
            //
            // **`== -1`, not "no answer"** (B460): a flinch found at sequence ZERO is not substituted — the engine restarts
            // it as asked and `AddToGestureSlot`'s `<= 0` below abandons it.
            if (sequence == -1 &&
                gesture.Slot == GestureSlot.Flinch &&
                !string.Equals(activity, FlinchChestActivity, StringComparison.Ordinal))
            {
                sequence = skinned.ForActivity(FlinchChestActivity);

                if (sequence > 0)
                {
                    SubstitutedFlinches++;
                }
            }

            // `if ( iGestureSequence <= 0 ) return;` — the engine abandons a gesture whose activity
            // this model does not have, rather than substituting one. That still holds for every
            // other slot, and for a flinch on a model carrying no flinch at all: the substitution
            // above reaches this same check with the chest activity and is abandoned on it.
            Layer(layers, skinned, sequence, gesture, seconds);
        }

        if (holder is { } last)
        {
            Layer(layers, skinned, last.Sequence, last.Gesture, seconds);
        }

        return layers;
    }

    /// <summary>`TranslateActivity` for a gesture of this player: their own table, the weapon's, the item's, the winner's (B437).</summary>
    private static string TranslateGesture(SceneProp prop, PropModels.SkinnedModel skinned, string activity) =>
        PlayerActivityTable.Translate(
            activity,
            prop.Pose.Slot ?? "PRIMARY",
            prop.Pose.ActivityOverride,
            prop.Pose.CompetitiveWinnerClass,
            prop.Pose.ItemActivities,
            name => skinned.ForActivity(name) >= 0);

    /// <summary>`IsGestureSlotActive`: whether a gesture still holds its slot at a moment (B437).</summary>
    /// <remarks>
    /// `m_bActive` is set when `AddToGestureSlot` finds a sequence and cleared only when an auto-kill gesture's cycle
    /// passes one (`UpdateGestureLayer`'s `ResetGestureSlot`); one that does not auto-kill holds until replaced. A
    /// gesture whose activity resolved to nothing never became active.
    /// </remarks>
    private static bool ActiveAt(PropModels.SkinnedModel skinned, int sequence, SceneGesture gesture, double seconds)
    {
        if (sequence <= 0)
        {
            return false;
        }

        float rate = skinned.CyclesPerSecond(sequence);

        return !gesture.AutoKill || (rate > 0f ? (seconds - gesture.StartedSeconds) * rate : 0d) <= 1d;
    }

    /// <summary>Turns one resolved gesture sequence into a pose layer, or into nothing.</summary>
    /// <param name="layers">Where the layer is appended, in place.</param>
    /// <param name="skinned">The model the sequence belongs to.</param>
    /// <param name="sequence">What the activity or the label resolved to; zero or below is abandoned.</param>
    /// <param name="gesture">The gesture, for its start and its auto-kill.</param>
    /// <param name="seconds">Demo time now.</param>
    /// <remarks>
    /// **Shared by the activity path and the VCD path because everything after the lookup is
    /// identical.** The engine's own split is the same shape: `AddToGestureSlot` and
    /// `AddVCDSequenceToGestureSlot` differ only in how they arrive at `m_nSequence`, and then set
    /// the same `m_flWeight = 1.0f`, `m_flPlaybackRate = 1.0f` and `m_nOrder = iGestureSlot`
    /// (<c>multiplayer_animstate.cpp:718</c>). Two copies of the cycle arithmetic would be two
    /// chances for a taunt and a reload to age differently.
    /// </remarks>
    private static void Layer(
        List<PoseLayer> layers,
        PropModels.SkinnedModel skinned,
        int sequence,
        SceneGesture gesture,
        double seconds)
    {
        // `if ( iGestureSequence <= 0 ) return;` — the engine abandons a gesture the model does not
        // have rather than substituting one.
        if (sequence <= 0)
        {
            return;
        }

        float rate = skinned.CyclesPerSecond(sequence);

        // A sequence with no rate has one frame and nothing to advance through; the engine's
        // cycle would stay at zero, which is that single frame.
        double cycle = rate > 0f ? (seconds - gesture.StartedSeconds) * rate : 0d;

        if (cycle < 0d)
        {
            return;
        }

        if (cycle > 1d)
        {
            // `if ( pGesture->m_bAutoKill ) ResetGestureSlot(…)` — the slot is gone, so the
            // layer is not drawn at all. Otherwise it holds on its last frame, for ever, which
            // is what a `_BEGIN` gesture is for.
            if (gesture.AutoKill)
            {
                return;
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

    internal static void IkFor(
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

        IReadOnlyList<StudioIkRule> rules = skinned.IkRules(group, blend[0].Animation);

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

            for (int corner = 0; corner < blend.Count; corner++)
            {
                (int animation, float weight) = blend[corner];

                IReadOnlyList<StudioIkRule> theirs = skinned.IkRules(group, animation);

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

            for (int blended = 0; blended < blend.Count; blended++)
            {
                (int animation, float weight) = blend[blended];

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
            prop.EntityIndex, out (int Sequence, float Cycle, double StartedAt, double JumpedAt) was))
        {
            _currentSequence[prop.EntityIndex] =
                (sequence, cycle, prop.Pose.AnimationStartSeconds,
                    prop.Pose.DiscontinuitySeconds);

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

        // **The other half of the same guard, and it was quoted here while only half of it ran**
        // (B346). `bInterpolate` is `!IsNoInterpolationFrame()` (`c_baseanimating.cpp:1832`), which
        // is `m_ubOldInterpolationFrame != m_ubInterpolationFrame` (`c_baseentity.h:2166`) — a
        // networked parity whose declaration says what it is for: "Call this to cause a
        // discontinuity (teleport)" (`baseentity.h:878`).
        //
        // **Exact, for the reason the restart stamp above is exact**: a time the timeline wrote
        // once and this copies verbatim, not a computed quantity. A tolerance would merge two
        // discontinuities a frame apart.
#pragma warning disable S1244
        bool jumped = prop.Pose.DiscontinuitySeconds != was.JumpedAt;
#pragma warning restore S1244

        if (was.Sequence != sequence || restarted)
        {
            // `if ((seqdesc.flags & STUDIO_SNAP) || !bInterpolate) m_animationQueue.RemoveAll();`
            //
            // **A teleport CUTS.** TF2 bumps the parity when a dead player becomes respawnable,
            // straight after `StopAnimation()` (`tf_player.cpp:14005`), and `CBaseEntity::Teleport`
            // bumps it for every entity given a new position (`baseentity.cpp:4955`) — every
            // teleporter in the game. A transition only exists when the sequence changed, so this
            // bites exactly where the two coincide, which is what a respawn is: without it the
            // pre-death pose fades into the spawn animation instead of cutting.
            if (skinned.SnapsTo(sequence) || jumped)
            {
                // **Counted where the clear HAPPENS, not recomputed by a second route** (B243).
                // The unit tests prove the guard clears a queue when it is called with a jump;
                // only this says a real demo ever reaches it, and it separates the two halves so a
                // reader can tell a teleport from an authored cut rather than seeing one total.
                if (jumped)
                {
                    QueuesClearedByADiscontinuity++;
                }
                else
                {
                    QueuesClearedBySnap++;
                }

                queue.Clear();
            }
            else
            {
                float window = MathF.Min(
                    skinned.FadeOut(was.Sequence), skinned.FadeIn(sequence));

                if (window > 0f)
                {
                    TransitionsCreated++;

                    queue.Add(new FadingSequence(
                        was.Sequence, was.Cycle, seconds, window, prop.Pose.PlaybackRate));
                }
            }

            // **The CONTROL for the two clear counters, and it is not optional here.** An absence
            // means nothing without something that must be present: zero clears can mean the guard
            // never fires or that nothing ever changed sequence, and only this separates them. The
            // first instrument written for B346 reported "0 and 0" for a demo that certainly
            // contains both, and the cause was the probe sampling a single tick — a fact about the
            // measurement rather than about the code.
            SequenceChangesSeen++;
        }

        _currentSequence[prop.EntityIndex] =
            (sequence, cycle, prop.Pose.AnimationStartSeconds, prop.Pose.DiscontinuitySeconds);

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

    /// <summary>The bone a model spins procedurally, by the name the engine looks it up under.</summary>
    /// <remarks>
    /// **Two weapons and two names**: `LookupBone("barrel")` for the minigun
    /// (<c>tf_weapon_minigun.cpp:1048</c>) and `LookupBone("procedural_chamber")` for the grenade
    /// launcher (<c>tf_weapon_grenadelauncher.cpp:602</c>). Only the first is driven here; the
    /// second turns on a keyframed spline over `m_iGoalTube`, which is a different mechanism and is
    /// filed rather than half-built.
    ///
    /// **Cached per model, because the lookup is a string compare over every bone** and the engine
    /// does it once in `OnNewModel` rather than per frame.
    /// </remarks>
    private readonly Dictionary<(string Model, string Name), int> _barrelBones = [];

    /// <summary>Each spinning barrel's velocity and angle, integrated across frames.</summary>
    private readonly Dictionary<int, (float Velocity, float Angle)> _barrelSpin = [];

    /// <summary>Demo time at the previous pose pass, so a frame's duration can be differenced.</summary>
    /// <remarks>
    /// **`gpGlobals->frametime`, which the engine has and this layer has to derive.** Clamped at
    /// zero rather than allowed negative: frametime never is, so scrubbing BACKWARDS holds the
    /// barrel where it is instead of unwinding it — the engine has no opinion about a case it
    /// cannot reach, and unwinding would be ours.
    /// </remarks>
    private double _barrelSeconds = double.NaN;

    /// <summary>Spins a minigun's barrel bone, as its own blending rules do.</summary>
    /// <param name="prop">The entity.</param>
    /// <param name="skinned">Its model, for the bone list.</param>
    /// <param name="posed">The pose being built.</param>
    /// <param name="seconds">Demo time now.</param>
    /// <remarks>
    /// **Per FRAME, which is TF2's arithmetic rather than a misreading.** `UpdateBarrelMovement`
    /// steps the velocity by a literal 0.1 once per call and is reached from
    /// `StandardBlendingRules`, so a minigun genuinely winds up faster at a higher framerate. That
    /// is reproduced rather than corrected — a per-second rate would be our number, not the game's,
    /// and `docs/DECISIONS.md` D89 settles which one this project wants.
    /// </remarks>
    private void SpinBarrel(
        SceneProp prop, PropModels.SkinnedModel skinned, SkeletonPose posed, double seconds)
    {
        // Stryker disable once : a mutant that empties the guard body leaves 'state'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (prop.Pose.MinigunState is not { } state)
        {
            return;
        }

        int bone = BoneNamed(skinned, prop.ModelPath, "barrel");

        if (bone < 0)
        {
            return;
        }

        float frame = double.IsNaN(_barrelSeconds)
            ? 0f
            : (float)Math.Max(seconds - _barrelSeconds, 0d);

        _barrelSpin.TryGetValue(prop.EntityIndex, out (float Velocity, float Angle) spin);

        // **`CanHolsterWhileSpinning` is an econ ATTRIBUTE — the Tomislav's** — and this layer does
        // not read attributes, so the ordinary 0.1 is used for every minigun. Named rather than
        // silently assumed: a Tomislav winds up five times faster and will look slow here.
        spin.Velocity = MinigunBarrel.Approach(
            MinigunBarrel.TargetVelocity((MinigunState)state),
            spin.Velocity,
            MinigunBarrel.Acceleration(canHolsterWhileSpinning: false));

        spin.Angle = MinigunBarrel.Advance(spin.Angle, spin.Velocity, frame);

        _barrelSpin[prop.EntityIndex] = spin;

        posed.BarrelBone = bone;
        posed.BarrelRotation = MinigunBarrel.Rotation(spin.Angle);

        FurthestBarrelAngle = MathF.Max(FurthestBarrelAngle, MathF.Abs(spin.Angle));
    }

    /// <summary>Turns a grenade launcher's chamber, as its own blending rules do.</summary>
    /// <param name="prop">The entity.</param>
    /// <param name="skinned">Its model, for the bone list.</param>
    /// <param name="posed">The pose being built.</param>
    /// <param name="seconds">Demo time now.</param>
    /// <remarks>
    /// **Driven by absolute time, not a per-frame integration** (B348), which is the difference from
    /// the minigun beside it. `UpdateBarrelMovement` computes
    /// `tVal = ( gpGlobals->curtime - m_flBarrelRotateBeginTime ) / cProceduralBarrelRotationTime`
    /// (<c>tf_weapon_grenadelauncher.cpp:645</c>) — a fraction of a fixed-length animation, so the
    /// same tick always gives the same angle however the viewer arrived at it.
    ///
    /// **The tube advances locally when the animation ends**, exactly as the engine does
    /// (<c>:687</c>): `m_iCurrentTube = m_iGoalTube;`. Without it the chamber would hold its old
    /// base angle until the wire restated the tube, which is a visible stall rather than a jump.
    /// </remarks>
    private void TurnChamber(
        SceneProp prop, PropModels.SkinnedModel skinned, SkeletonPose posed, double seconds)
    {
        // Stryker disable once : a mutant that empties the guard body leaves the fields of
        // 'chamber' ('Current', 'Goal', 'StartedSeconds') unassigned (CS0170), and Safe Mode then
        // drops every mutation in this method — B410.
        if (prop.Pose.Chamber is not { } chamber)
        {
            return;
        }

        int bone = BoneNamed(skinned, prop.ModelPath, "procedural_chamber");

        if (bone < 0)
        {
            return;
        }

        float fraction = ChamberRotation.Fraction(seconds - chamber.StartedSeconds);

        // **Turning only while the two differ**, which is the engine's own guard (`:641`). A settled
        // chamber still gets its bone written, at the base angle of the tube it sits on — the
        // override is outside that test, so a chamber that has stopped is not a chamber the
        // animation reclaims.
        bool turning = chamber.Current != chamber.Goal;

        float partial = turning ? ChamberRotation.Degrees(fraction) : 0f;

        int tube = turning && fraction >= 1f ? chamber.Goal : chamber.Current;

        posed.BarrelBone = bone;
        posed.BarrelRotation = MinigunBarrel.Rotation(ChamberRotation.Angle(tube, partial));

        TurnedChambers++;
        FurthestChamberAngle = MathF.Max(
            FurthestChamberAngle, MathF.Abs(ChamberRotation.Angle(tube, partial)));
    }

    /// <summary>The index of a model's bone with the given name, or -1.</summary>
    /// <remarks>
    /// **The name is a parameter because the engine looks up a different one per weapon** —
    /// `LookupBone( "barrel" )` (<c>tf_weapon_minigun.cpp:1048</c>) and
    /// `LookupBone( "procedural_chamber" )` (<c>tf_weapon_grenadelauncher.cpp:602</c>). Keyed by
    /// model AND name, since one model may be asked for either.
    /// </remarks>
    private int BoneNamed(PropModels.SkinnedModel skinned, string model, string name)
    {
        // **A tuple key, not a concatenation.** Joining the two strings makes
        // `("a", "bc")` and `("ab", "c")` the same entry, and it allocates on every lookup of a
        // path this asks for once per frame per prop.
        (string Model, string Name) key = (model, name);

        if (_barrelBones.TryGetValue(key, out int cached))
        {
            return cached;
        }

        int found = -1;

        for (int bone = 0; bone < skinned.Bones.Count; bone++)
        {
            if (string.Equals(skinned.Bones[bone].Name, name, StringComparison.Ordinal))
            {
                found = bone;
                break;
            }
        }

        _barrelBones[key] = found;
        return found;
    }

    /// <summary>What sequence each entity was playing last frame, and where its cycle stood.</summary>
    /// <remarks>
    /// **`UpdateCurrent`'s job**, kept per entity for the same reason the queue is. The cycle is
    /// remembered as well as the number because a sequence being left has to carry on from where it
    /// was rather than restarting.
    /// </remarks>
    private readonly Dictionary<int, (int Sequence, float Cycle, double StartedAt, double JumpedAt)>
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

        if (_firedScratch.Count > 0 && _render.IsEnabled(LogLevel.Debug))
        {
            _render.LogDebug(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"event walk entity {prop.EntityIndex} model {prop.ModelPath} sequence {remembered.State.Sequence}->{sequence} cycle {remembered.State.PreviousCycle:0.000}->{phase:0.000} parity {remembered.Parity}->{prop.Pose.ResetEventsParity}: {_firedScratch.Count} fired"));
        }

        _eventStates[prop.EntityIndex] = (next, prop.Pose.ResetEventsParity);

        foreach (StudioEvent fired in _firedScratch)
        {
            _fired.Add(new FiredAnimationEvent(
                prop.EntityIndex,
                fired,
                (prop.Pose.X, prop.Pose.Y, prop.Pose.Z),
                sequence,
                phase));
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

    /// <summary>The flinch every class model declares, and the one the others fall back to.</summary>
    /// <remarks>
    /// **`ACT_MP_GESTURE_FLINCH_CHEST` is the engine's own literal**
    /// (<c>multiplayer_animstate.cpp:376</c>), not a choice of ours — the substitution names it
    /// twice in four lines, as both the guard and the replacement.
    /// </remarks>
    private const string FlinchChestActivity = "ACT_MP_GESTURE_FLINCH_CHEST";

    /// <summary>Flinches that fell back to the chest animation — the wiring check for B350.</summary>
    /// <remarks>
    /// **Counted where the substitution happens, and only when it SUCCEEDS.** A conformance test
    /// proves the fallback picks chest when asked; only this says a real demo asks. Zero here beside
    /// a demo that fires 69 non-chest flinch events would mean the events never reach the gesture
    /// feed, which is a different defect from the one this fixes.
    /// </remarks>
    public int SubstitutedFlinches { get; private set; }

    /// <summary>What this set has drawn and failed to draw over its whole life.</summary>
    /// <remarks>
    /// **Exposed so a whole recording can be audited rather than a frame watched.** The owner's
    /// question is what a demo asks for and never gets — *"missing meshes or materials or textures, or
    /// something isnt being draw"* — and every existing report is per frame, rate-limited and only on
    /// a change, so a model that fails once in a hundred thousand frames appears in no line anybody
    /// reads.
    ///
    /// **Carried from the draw loop's own calls** (B243), not recomputed by a second walk.
    /// </remarks>
    public DrawTally Tally => _tally;

    /// <summary>Areaportal windows given their distance blend — the wiring check for B358.</summary>
    /// <remarks>
    /// **Counted where the blend is WRITTEN, not where a window is recognised**, which is the
    /// distinction B347's counter got wrong: incremented at the intent rather than at the work, it
    /// reported success for a rotation nothing applied. Zero here on `koth_harvest_final`, which
    /// carries six of them, means the three floats never arrived or the view origin was never set.
    /// </remarks>
    public int PortalWindowsFaded { get; private set; }

    /// <summary>The alpha a fully solid entity draws at, as a byte.</summary>
    private const int FullyOpaqueAlpha = 255;

    /// <summary>Barrel bones actually WRITTEN — the wiring check for B347.</summary>
    /// <remarks>
    /// **Summed out of the poses that did the work, not counted here** (B243). The first version
    /// incremented a field in <see cref="SpinBarrel"/> as soon as it had a state and a bone index,
    /// which measured this method's INTENT: sabotaging `SkeletonPose.SpinBarrel`, the code that
    /// actually writes the rotation, left every assertion green. A count kept beside the work is a
    /// second route, and a second route is free to be wrong.
    ///
    /// **The same shape as <see cref="AppliedIkLocks"/>**, which reads `posed.AppliedLocks` for
    /// exactly this reason.
    /// </remarks>
    public int SpunBarrels
    {
        get
        {
            int spun = 0;

            foreach (AnimatingEntity animating in _entities.Values)
            {
                if (animating.Pose is SkeletonPose posed)
                {
                    spun += posed.SpunBarrels;
                }
            }

            return spun;
        }
    }

    /// <summary>Chambers whose bone this pass wrote — the wiring check for B348.</summary>
    /// <remarks>
    /// **Counted here rather than carried out of <c>SkeletonPose</c>, unlike <see cref="SpunBarrels"/>,
    /// and the difference is deliberate.** The pose writes ONE barrel bone, so a single counter
    /// there cannot say which weapon asked for it. This says the chamber path ran; `SpunBarrels`
    /// says a bone was actually written, and both being non-zero is what says the two agree.
    /// </remarks>
    public int TurnedChambers { get; private set; }

    /// <summary>The furthest a chamber has turned, in radians.</summary>
    public float FurthestChamberAngle { get; private set; }

    /// <summary>The furthest any barrel has turned, in radians.</summary>
    /// <remarks>
    /// **The question a count cannot answer**: whether the barrels MOVE. A non-zero count with a
    /// zero angle would mean every minigun is idle, which looks identical on screen to the spin
    /// never running and is a different fact about the demo.
    /// </remarks>
    public float FurthestBarrelAngle { get; private set; }

    /// <summary>Transition queues emptied because the entity JUMPED — the <c>bInterpolate</c> half.</summary>
    /// <remarks>
    /// **Separated from the <c>STUDIO_SNAP</c> half deliberately** (B346). One total would let an
    /// authored cut stand in for a teleport, and the whole question this answers is whether the
    /// half that was missing until now ever fires on a real recording.
    /// </remarks>
    public int QueuesClearedByADiscontinuity { get; private set; }

    /// <summary>Transition queues emptied because the incoming sequence carries <c>STUDIO_SNAP</c>.</summary>
    public int QueuesClearedBySnap { get; private set; }

    /// <summary>Sequence changes the transitioner saw at all — the CONTROL for the two above.</summary>
    /// <remarks>
    /// **An absence is uninterpretable without it.** Zero clears can mean the guard never fires or
    /// that no entity ever changed sequence in the window, and only this tells them apart.
    /// </remarks>
    public int SequenceChangesSeen { get; private set; }

    /// <summary>Cross-fades actually queued, which is what the clears above prevent.</summary>
    public int TransitionsCreated { get; private set; }

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
                // Stryker disable once : a mutant that empties the guard body leaves 'posed'
                // unassigned (CS0165), and Safe Mode then drops every mutation in this method —
                // B410.
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

    /// <summary>Told when an entity's model is resolved, with the facts the interpolator needs.</summary>
    /// <remarks>
    /// **A callback rather than a call, because this layer must not know what is listening.** The
    /// only consumer is the interpolator, which lives under the scene rather than beside it; the
    /// window wires the two together where it already registers every other system.
    ///
    /// **Entity, then which parameters wrap, then whether the model is a static prop** — the three things
    /// `C_BaseAnimating::OnNewModel` and `PostDataUpdate` need from the studio header and cannot get
    /// anywhere else (<c>c_baseanimating.cpp:1124</c>, <c>:4740</c>).
    /// </remarks>
    public Action<int, IReadOnlyList<bool>, bool>? ModelResolved { get; set; }

    /// <summary>Which of a model's pose parameters wrap, one entry per parameter it declares.</summary>
    /// <remarks>
    /// **Its LENGTH is the model's parameter count and is load-bearing** (B383) — it is what sizes the
    /// interpolation history, because `SetMaxCount( hdr->GetNumPoseParameters() )` precedes the
    /// per-parameter `SetLooping` in the engine. Of a sentry gun's two parameters only <c>aim_yaw</c>
    /// loops, so an all-false array is the common case and must still be two long.
    /// </remarks>
    private static bool[] LoopingPoseParameters(PropModels.SkinnedModel model)
    {
        IReadOnlyList<StudioPoseParameter> parameters = model.PoseParameters;

        // **Always the model's parameter COUNT, even when none of them loops** (B383). This returned an
        // empty array in that case — an allocation saved, and the count destroyed with it. The count is
        // what sizes the interpolation history: `m_iv_flPoseParameter.SetMaxCount(
        // hdr->GetNumPoseParameters() )` comes BEFORE the per-parameter `SetLooping`
        // (`c_baseanimating.cpp:1124`), and they are two calls in the engine because they are two facts.
        // Collapsing them into one `bool[]` and then eliding the all-false case reported every ordinary
        // model as having no parameters at all.
        bool[] looping = new bool[parameters.Count];

        for (int index = 0; index < parameters.Count; index++)
        {
            looping[index] = parameters[index].Loop != 0f;
        }

        return looping;
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

        // **An unparented object's placement is its own pose, which cannot change** (finding 82): computed once and copied
        // in, the engine's clear `EFL_DIRTY_ABSTRANSFORM`. A parented one follows its parent and is composed every time.
        ResolvedModel resolved = ResolvedFor(prop);

        if (prop.AttachedTo is null && resolved.Placement is { } known)
        {
            known.CopyTo(placement, 0);
            return placement;
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

        if (prop.AttachedTo is null)
        {
            resolved.Placement = [.. placement];
        }

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
    ///
    /// **The composition is <see cref="ParentChain.Absolute(SceneProp, Func{int, SceneProp?}, int)"/>**, shared with everything outside the model pass that
    /// asks <c>GetAbsOrigin()</c>. The owner's report that drove its third branch — *"that one gate on the left is
    /// rotated 90 degreed"*, from `cp_fulgur`'s setup gates (B241) — is why the angle shortcut there is conditional.
    /// </remarks>
    /// <para>
    /// **Parents come from the pass first and the frame second** (B551): the pass's copy is the one this pass posed, and
    /// <see cref="PlaceFrame"/>'s map supplies a parent the pass does not hold. **Memoized per pass** at the full depth,
    /// cleared where the pass's attachments are, because an attachment-hung child is only as fresh as that table.
    /// </para>
    private ScenePose Absolute(SceneProp prop, int budget)
    {
        bool full = budget == AnimatingEntity.MaximumFollowDepth && !_simulating;

        if (full && _absolute.TryGetValue(prop.EntityIndex, out ScenePose known))
        {
            return known;
        }

        _parentOf ??= ParentOf;
        _attachmentOf ??= (entity, number) =>
            PosedAttachmentMatrix(entity, attachments => number >= 1 && number <= attachments.Count ? number - 1 : -1);

        ScenePose pose = ParentChain.Absolute(prop, _parentOf, budget, _attachmentOf);

        if (full)
        {
            _absolute[prop.EntityIndex] = pose;
        }

        return pose;
    }

    /// <summary>An entity's prop: this pass's copy, else the frame's (<see cref="PlaceFrame"/>).</summary>
    private SceneProp? ParentOf(int entity) =>
        _propsByEntity.TryGetValue(entity, out SceneProp? inPass) ? inPass : _frameProps.GetValueOrDefault(entity);

    /// <summary><see cref="Absolute"/>'s parent lookup, made once rather than per call.</summary>
    private Func<int, SceneProp?>? _parentOf;

    /// <summary><see cref="Absolute"/>'s attachment lookup, made once rather than per call.</summary>
    private Func<int, int, float[]?>? _attachmentOf;

    /// <summary>This frame's props by entity index, so a parent chain can be walked.</summary>
    private readonly Dictionary<int, SceneProp> _propsByEntity = [];

    /// <summary>Every prop of this frame, whatever pass draws it — the parents a pass's own list may lack (B551).</summary>
    private readonly Dictionary<int, SceneProp> _frameProps = [];

    /// <summary>Hands over every prop of the frame before its passes, so a move parent is found in any of them.</summary>
    /// <param name="props">The frame's props, drawn or not.</param>
    /// <remarks>
    /// **A pass's list is not the frame.** `Simulate` indexes only the props of its own pass — drawn, undrawn or
    /// viewmodel — and the scene's visibility rules remove props from those lists, while <c>CalcAbsolutePosition</c>
    /// composes a child onto its parent with no reference to whether the parent is drawn (`c_baseentity.cpp:4393`).
    /// A parent missing from the child's pass gave it its LOCAL pose, which is B551 again.
    ///
    /// **They live until the next call, and nothing else ends them.** `MomentScene` calls this every frame; a seek's
    /// corpse replay (`AdvanceCorpsesAlone`) simulates at its own tick in between and neither claims nor clears them.
    /// </remarks>
    public void PlaceFrame(IEnumerable<SceneProp> props)
    {
        ArgumentNullException.ThrowIfNull(props);

        _frameProps.Clear();
        _absolute.Clear();

        foreach (SceneProp prop in props)
        {
            _frameProps[prop.EntityIndex] = prop;
        }
    }

    /// <summary>Each entity's absolute pose, composed once per pass — the engine's cached <c>m_rgflCoordinateFrame</c>.</summary>
    private readonly Dictionary<int, ScenePose> _absolute = [];

    /// <summary>
    /// Whether `Simulate` is still bringing the pass's entities up to date — so a parent's bones may not be posed yet and
    /// nothing composed from them is kept. Defensive: no construction tried (a child listed before a moving,
    /// attachment-bearing parent) shows a stale answer without it, so dropping it survives every test; kept on review.
    /// </summary>
    private bool _simulating;

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

        BoneSkinning.Fill(bones, accessor, buffer);

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
    /// and a sampled line cannot see it. See `docs/memory/instrument-bugs-outnumber-decoder-bugs.md#log-the-event-not-a-sample-of-it`.
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

    /// <summary>The main view a static prop's screen width is measured through (B432); null measures none.</summary>
    public ScreenFadeView? ScreenView { get; set; }

    /// <summary>The level screen fade, from <c>worldspawn</c> when a map is read (B432); the default is disabled.</summary>
    public ScreenFadeRange LevelScreenFade { get; set; }

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
}
