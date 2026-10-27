using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
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
/// <param name="FlexRange">The slots of <paramref name="Flex"/> a flex can move, or null for all of them (B597).</param>
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
    float[]? Flex = null,

    // **The only slots of the face that can be non-zero** (B597): the first to the last a flex of this model names, so the
    // renderer sends that range and keeps the zeros either side on the device. Null sends the whole array.
    FlexSlots? FlexRange = null);

/// <summary>A run of buffer slots, the first and how many (B597).</summary>
/// <param name="First">The first slot.</param>
/// <param name="Count">How many slots, from <paramref name="First"/>.</param>
public readonly record struct FlexSlots(int First, int Count);

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

    /// <summary>Of <see cref="SetupTicks"/>: roots' own calls, ever — what the threaded pre-pass would move (finding 105 A6).</summary>
    public long SetupRootTicks { get; private set; }

    /// <summary>Of <see cref="SetupTicks"/>: children's own calls, less their parents, ever (finding 105 A6).</summary>
    public long SetupChildTicks { get; private set; }

    /// <summary>Roots the draw loop built, ever (finding 105 A6).</summary>
    public int SetupRootBuilds { get; private set; }

    /// <summary>Children the draw loop built, ever (finding 105 A6).</summary>
    public int SetupChildBuilds { get; private set; }

    /// <summary>Of <see cref="SetupRootTicks"/>: the roots a threaded pre-pass would take (<see cref="BoneSetupMeter"/>), ever.</summary>
    public long SetupEligibleTicks { get; private set; }

    /// <summary>Eligible roots the draw loop built, ever (finding 105 A6).</summary>
    public int SetupEligibleBuilds { get; private set; }

    /// <summary>The slowest single eligible root built by the last pass's draw loop — the fork's critical path (finding 105 A6). A level.</summary>
    public long MaxRootTicks { get; private set; }

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

    /// <summary>Static props whose colour mesh was built (a cache miss in <c>BakedColoursOf</c>), ever (B595).</summary>
    public int BakedBuilds { get; private set; }

    /// <summary>What those builds cost, ever, in stopwatch ticks (B595).</summary>
    public long BakedBuildTicks { get; private set; }

    /// <summary>Model paths packed for the first time, ever (B595).</summary>
    public int ModelsPacked { get; private set; }

    /// <summary>What the packing calls that packed anything cost, ever, in stopwatch ticks (B595).</summary>
    public long PackTicks { get; private set; }

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
        int CollateByIdentity = 0,
        long SetupRoot = 0,
        long SetupChild = 0,
        int RootBuilds = 0,
        int ChildBuilds = 0,
        long MaxRoot = 0,
        long SetupEligible = 0,
        int EligibleBuilds = 0)
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
                CollateByIdentity - before.CollateByIdentity,
                SetupRoot - before.SetupRoot,
                SetupChild - before.SetupChild,
                RootBuilds - before.RootBuilds,
                ChildBuilds - before.ChildBuilds,

                // A level, carried like CorpsesHeld: the slowest eligible root of the pass just run.
                MaxRoot,
                SetupEligible - before.SetupEligible,
                EligibleBuilds - before.EligibleBuilds);
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
        CollatedByIdentity,
        SetupRootTicks,
        SetupChildTicks,
        SetupRootBuilds,
        SetupChildBuilds,
        MaxRootTicks,
        SetupEligibleTicks,
        SetupEligibleBuilds);

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
