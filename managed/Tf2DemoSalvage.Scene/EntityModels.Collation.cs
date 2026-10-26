using System;
using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

public sealed partial class EntityModelSet
{
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

    /// <summary>Which entities the frustum and the leaf cull accepted, for the corpse fade.</summary>
    /// <remarks>
    /// **This was `PosedEntities`, it was filled at bone setup, and it served two questions that are
    /// not the same question** (B385). Its own remark claimed *"the engine gates interpolation on
    /// `IsVisible()`, which is the LAST render's answer … it is what Valve does"* — and `IsVisible()`
    /// is `m_hRender != INVALID_CLIENT_RENDER_HANDLE` (`c_baseentity.h:691`), which
    /// `UpdateVisibility` sets from `ShouldDraw() &amp;&amp; !IsDormant()` (`c_baseentity.cpp:1421`).
    /// No frustum, no frame, no skeleton. Filled where it was, it held no brush entity at all, so no
    /// door in any map was ever interpolated. `DemoTimeline` answers `ShouldInterpolate` itself now.
    ///
    /// **What is left is the one consumer that genuinely wanted a cull.**
    /// `C_TFRagdoll::IsRagdollVisible` is `engine->IsBoxInViewCluster` and then `engine->CullBox`
    /// around the corpse's origin (`c_tf_player.cpp:1350`), so a corpse's fade timer really does turn
    /// on the frustum — and now gets a set filled by the frustum rather than by bone setup, which also
    /// admits the brushless and the baked.
    /// </remarks>
    public IReadOnlySet<int> InView => _inView;

    /// <summary>The world pass's name, which decides whether <see cref="InView"/> is cleared.</summary>
    /// <remarks>
    /// A constant because the string is written in three places — the default argument, the clear, and
    /// the tally's own line — and two of them agreeing is not enough.
    /// </remarks>
    public const string WorldPass = "world";

    /// <summary>
    /// The pass for players posed but not drawn — the first-person one. `DoAnimationEvents` returns when
    /// <c>!IsVisible()</c> (`c_baseanimating.cpp:3569`), so nothing this pass walks fires an event.
    /// </summary>
    public const string UndrawnPass = "undrawn";

    /// <summary>
    /// The pass for the zoomed local player, whom no view draws, a monitor's included (B598): simulated and in the scene
    /// for a decal's bones, never posed, and like <see cref="UndrawnPass"/> it fires no event.
    /// </summary>
    public const string HiddenPass = "hidden";

    /// <summary>Whether the undrawn pass builds instances — true only when a monitor will draw them this frame (B511).</summary>
    public bool PosesUndrawn { get; set; } = true;

    /// <summary>Whether a pass is posed for no view: <see cref="HiddenPass"/>, or <see cref="UndrawnPass"/> with no monitor.</summary>
    private bool PosesNothing(string pass) =>
        string.Equals(pass, HiddenPass, StringComparison.Ordinal) ||
        (!PosesUndrawn && string.Equals(pass, UndrawnPass, StringComparison.Ordinal));

    /// <summary>Whether a pass fires animation events: not one whose props are invisible (`c_baseanimating.cpp:3569`).</summary>
    private static bool FiresEvents(string pass) =>
        !string.Equals(pass, UndrawnPass, StringComparison.Ordinal) && !string.Equals(pass, HiddenPass, StringComparison.Ordinal);

    /// <summary>The viewmodel's pass — the arms and the gun, posed after the world (`MomentScene.AddViewmodel`).</summary>
    public const string ViewmodelPass = "viewmodel";

    private readonly HashSet<int> _inView = [];

    /// <summary>The last world pass's views, which the particle effects collate by; null before the first.</summary>
    public CollationView? WorldView { get; private set; }

    public int Posed { get; private set; }

    /// <summary>The renderables of the world pass, kept per leaf across frames — <c>CClientLeafSystem</c> (B262).</summary>
    private ClientLeafSystem _leafSystem = OneLeaf();

    private BspLeafTree? _tree;
    private int[] _everyLeaf = [0];

    private static readonly int[] PlaceZero = [0];

    private static ClientLeafSystem OneLeaf() => new(1, static (_, _, into) => into.Add(0));

    private readonly Dictionary<(int Entity, string Model, int Occurrence), int> _handleOf = [];
    private readonly List<(int Entity, string Model, int Occurrence)> _staleEntities = [];
    private readonly CollatedRenderables _mainCollated = new();
    private readonly CollatedRenderables _skyCollated = new();
    // The box the collation filed each prop by — `CalcRenderableWorldSpaceAABB`, computed once a frame and carried to the
    // instance rather than computed again; null for a pass that is not collated.
    private readonly List<(SceneProp Prop, int Place, int? Bucket, bool InSky, (float, float, float, float, float, float)? Box)> _ordered = [];
    private static readonly Func<int, LeafRenderGroup> Opaque = static _ => LeafRenderGroup.Opaque;

    /// <summary>Registers this pass's props and collates each view's leaves into <see cref="_ordered"/>.</summary>
    /// <param name="props">What exists at this tick.</param>
    /// <param name="frustum">The main view's volume; an unbuilt one culls nothing.</param>
    /// <param name="views">The views' leaf lists.</param>
    /// <param name="pass">The pass; only the world pass is collated.</param>
    /// <remarks>
    /// **The registration is reconciled against the tick's props**: a prop keeps its handle while its entity
    /// persists, is re-linked only when its box moved (<c>RenderableChanged</c>), and leaves when it does — so a seek
    /// is just a frame in which many boxes moved, and nothing can be filed where it no longer is (D131's hazard).
    ///
    /// **The render group is not asked here**: whether a model is translucent is the material system's answer, in
    /// the render layer; everything is collated as opaque so the bucket comes from the box, and the device files the
    /// translucent from this order. The answer to WHICH renderables and in WHAT order is the same either way.
    ///
    /// **A view list that is null means no view walk ran** (a caller with no device): every leaf is listed, so the
    /// frustum alone decides. **An empty list collates nothing**, which is the engine's loop over zero leaves.
    /// </remarks>
    private void Collate(IReadOnlyList<SceneProp> props, ViewFrustum frustum, RenderableViews views, string pass)
    {
        _ordered.Clear();

        if (!string.Equals(pass, WorldPass, StringComparison.Ordinal))
        {
            foreach (SceneProp prop in props)
            {
                if (CanDraw(prop))
                {
                    _ordered.Add((prop, 0, null, false, null));
                }
                else
                {
                    _tally.NotDrawable(prop);
                }
            }

            return;
        }

        SweepResolved();

        // Each handle claimed this frame and what it draws, filled as the walk claims it.
        Dictionary<int, (SceneProp Prop, (float, float, float, float, float, float) Box)> byHandleNow = _propByHandle;
        byHandleNow.Clear();

        foreach (SceneProp prop in props)
        {
            // **An empty path joins the undrawable kinds** (PARITY-AUDIT finding 5): no-batches for one model is a
            // load failure, a prop with no model at all is a gap — `DrawTally` keeps them apart.
            if (!CanDraw(prop))
            {
                _tally.NotDrawable(prop);
                continue;
            }

            bool fixedProp = prop.AttachedTo is null;

            if (fixedProp)
            {
                CollateFixed++;
            }

            // **The handle lives with the object, as the engine's lives on the entity** (finding 82). `C_BaseEntity` holds
            // its `m_hRender`, and `PreRender` touches it only after it raised `RenderableChanged` (`clientleafsystem.cpp:543`).
            // Ours is told nothing, but a prop is an immutable record and the sample hands an unchanged track back as the
            // SAME object (B259 stage C), so the same unparented object over the same frames is the not-dirty case: no keyed
            // lookup, no box, no re-link. Measured on f12: 1,577 of 1,605 unparented props a frame. Anything else — a new
            // object, a parented prop, a new level, a handle already claimed this frame — takes the keyed path below.
            if (fixedProp && _filedLast.TryGetValue(prop, out FiledObject last) && !byHandleNow.ContainsKey(last.Handle))
            {
                CollatedByIdentity++;
                _filedNow[prop] = last;
                byHandleNow[last.Handle] = (prop, last.Box);
                continue;
            }

            // Keyed by entity, model and occurrence: a synthesised prop carries entity 0, and two can share it. The
            // occurrence is the first one no object has claimed this frame, so an object filed by identity keeps its own.
            int seen = 0;

            while (_handleOf.TryGetValue((prop.EntityIndex, prop.ModelPath, seen), out int claimed) && byHandleNow.ContainsKey(claimed))
            {
                seen++;
            }

            (int, string, int) key = (prop.EntityIndex, prop.ModelPath, seen);

            // **Filed again only when it moved** — `CStaticPropMgr` inserts a static prop at level load and never re-links
            // it, and an entity re-links on `RenderableChanged`, which it raises when its origin, angles or model change. A
            // prop with no parent is placed from exactly the fields compared here (`Placed`), so an unchanged placement
            // over the same frames IS an unchanged box; a parented one follows its parent and is placed every frame.
            _frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? filedFrames);
            FiledPlacement placement = new(
                prop.Pose.X, prop.Pose.Y, prop.Pose.Z, prop.Pose.Pitch, prop.Pose.Yaw, prop.Pose.Roll, prop.Pose.Scale, prop.Pose.Sequence);
            bool unmoved = false;
            (float, float, float, float, float, float) box = default;

            if (fixedProp &&
                _filedBoxes.TryGetValue(key, out (FiledPlacement Placement, PropModels.ModelFrames? Frames, (float, float, float, float, float, float) Box) filed) &&
                filed.Placement == placement && ReferenceEquals(filed.Frames, filedFrames))
            {
                unmoved = true;
                box = filed.Box;
            }
            else
            {
                box = WorldBoxFor(prop);

                if (fixedProp)
                {
                    _filedBoxes[key] = (placement, filedFrames, box);
                }
            }

            if (!fixedProp)
            {
                // A parented prop follows its parent: nothing filed for it may outlive the parenting.
                _filedBoxes.Remove(key);
            }

            if (_handleOf.TryGetValue(key, out int handle))
            {
                if (!unmoved)
                {
                    _leafSystem.RenderableChanged(handle, box);
                    Relinked++;
                }
            }
            else
            {
                handle = _leafSystem.AddRenderable(box);
                _handleOf[key] = handle;
            }

            if (fixedProp)
            {
                _filedNow[prop] = new FiledObject(handle, box);
            }

            byHandleNow[handle] = (prop, box);
        }

        (_filedLast, _filedNow) = (_filedNow, _filedLast);
        _filedNow.Clear();

        // What no prop claimed this frame has left: by handle, an integer, so the sweep hashes no path.
        _staleEntities.Clear();

        foreach (((int, string, int) key, int handle) in _handleOf)
        {
            if (!byHandleNow.ContainsKey(handle))
            {
                _staleEntities.Add(key);
            }
        }

        foreach ((int, string, int) key in _staleEntities)
        {
            _leafSystem.RemoveRenderable(_handleOf[key]);
            _handleOf.Remove(key);
            _filedBoxes.Remove(key);
        }

        FileStaticProps();

        _leafSystem.PreRender();

        IReadOnlyList<int> mainLeaves = views.MainLeaves ?? _everyLeaf;

        // What this frame's views were, for the particle effects to collate by the same (finding 84, D1).
        WorldView = new CollationView(frustum, views, _tree);

        _leafSystem.BuildRenderablesList(mainLeaves, frustum, Opaque, _mainCollated);

        bool sky = views.SkyLeaves is { Count: > 0 };

        if (sky)
        {
            _leafSystem.BuildRenderablesList(views.SkyLeaves!, views.SkyFrustum, Opaque, _skyCollated, includeUnplaced: false);
        }

        Dictionary<int, (SceneProp Prop, (float, float, float, float, float, float) Box)> byHandle = byHandleNow;

        _staticsInView.Clear();

        int collated = Gathered(_mainCollated, byHandle, inSky: false);

        if (sky)
        {
            collated += Gathered(_skyCollated, byHandle, inSky: true);
        }

        // **Carried from the collation, not re-derived** (B243): what no view collated, split by whether the frustum
        // or the leaf list turned it away. The static props count only on a frame that gathers them.
        int offered = byHandle.Count + _staticByHandle.Count;
        Culled = Math.Max(offered - collated, 0);
        CulledByVisibility = Math.Max(Culled - _mainCollated.FrustumRejected, 0);
        Unjudgeable = _mainCollated.Unplaced;

        for (int at = 0; at < Culled; at++)
        {
            _tally.Culled();
        }
    }

    private readonly Dictionary<int, (SceneProp Prop, (float, float, float, float, float, float) Box)> _propByHandle = [];

    /// <summary>Adds one view's collated entries to <see cref="_ordered"/>, entities and static props alike.</summary>
    /// <returns>How many it added.</returns>
    private int Gathered(
        CollatedRenderables view,
        Dictionary<int, (SceneProp Prop, (float, float, float, float, float, float) Box)> byHandle,
        bool inSky)
    {
        int added = 0;

        foreach ((int handle, int place, int? bucket) in view.Entries)
        {
            if (!byHandle.TryGetValue(handle, out (SceneProp Prop, (float, float, float, float, float, float) Box) filed))
            {
                // A static prop's handle: filed once at load and reached only here (finding 83 item 1).
                filed = _staticByHandle[handle];
                _staticsInView.Add(filed.Prop);
            }

            _ordered.Add((filed.Prop, place, bucket, inSky, filed.Box));
            added++;
        }

        return added;
    }

    /// <summary>The map's static props, handed over once a level (B426, D198).</summary>
    /// <remarks>
    /// **Registered with the leaf system ONCE, as the engine does** (finding 83 item 1). A static prop's handle is made with
    /// <c>RENDER_FLAGS_STATIC_PROP</c> at level load (<c>CreateRenderableHandle</c>, <c>clientleafsystem.cpp:642</c>,
    /// <c>:660-666</c>), is never dirty and never on
    /// the client entity list, and is reached only by the in-view gather (<c>BuildRenderablesList</c>, drawn through
    /// <c>DrawStaticProps</c>, <c>viewrender.cpp:4041</c>). So no per-entity pass visits one: only the props a view
    /// collates are simulated, lit and placed, in <see cref="Instances"/>. Packed by the first <see cref="Add(IReadOnlyList{SceneProp}, Func{string, PropModels.ModelFrames?})"/>
    /// after it is set, and filed by the first world pass after that.
    /// </remarks>
    public IReadOnlyList<SceneProp> StaticProps
    {
        get => _staticProps;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _staticProps = value;
            _drawableStatics = value.Count(CanDraw);
            _staticsPacked = false;
            UnfileStaticProps();
        }
    }

    /// <summary>Whether a world pass gathers the static props — false on a skip's frame, which simulates entities only (B504).</summary>
    public bool GathersStaticProps { get; set; } = true;

    /// <summary>How many props the simulate loop visited in the last <see cref="Instances"/> call — counted in the loop (B243).</summary>
    public int Simulated { get; private set; }

    /// <summary>How many static props were ever filed with the leaf system — counted at <c>AddRenderable</c> (B243).</summary>
    public int StaticPropsFiled { get; private set; }

    private IReadOnlyList<SceneProp> _staticProps = [];
    private bool _staticsPacked;

    // The static props a pass files — the drawable ones — so the tally is asked for what the handles count.
    private int _drawableStatics;
    private bool _staticsFiled;
    private readonly Dictionary<int, (SceneProp Prop, (float, float, float, float, float, float) Box)> _staticByHandle = [];
    private readonly Dictionary<int, SceneProp> _staticByEntity = [];
    private readonly List<SceneProp> _staticsInView = [];

    /// <summary>Files every drawable static prop with the leaf system, once its models are packed.</summary>
    private void FileStaticProps()
    {
        // **A skip's frame collates entities alone** (B504), so its cull counts only them, as when static props rode the
        // draw list and a skip left them off it. Out of the tree for the skip, filed again by the first frame that draws.
        if (!GathersStaticProps)
        {
            RemoveStaticHandles();
            return;
        }

        if (_staticsFiled || !_staticsPacked)
        {
            return;
        }

        _staticsFiled = true;

        foreach (SceneProp prop in _staticProps)
        {
            _staticByEntity[prop.EntityIndex] = prop;

            if (!CanDraw(prop))
            {
                continue;
            }

            (float, float, float, float, float, float) box = WorldBoxFor(prop);
            _staticByHandle[_leafSystem.AddRenderable(box)] = (prop, box);
            StaticPropsFiled++;
        }
    }

    /// <summary>Takes the static props out of the leaf system — a new list, a new tree, or a new level.</summary>
    /// <param name="removing">Whether the handles are the current leaf system's; a replaced one is simply dropped.</param>
    private void UnfileStaticProps(bool removing = true)
    {
        if (removing)
        {
            RemoveStaticHandles();
        }

        _staticByHandle.Clear();
        _staticByEntity.Clear();
        _staticsFiled = false;
    }

    /// <summary>Takes the static props' handles out of the current leaf system, keeping the level's list by entity.</summary>
    private void RemoveStaticHandles()
    {
        foreach (int handle in _staticByHandle.Keys)
        {
            _leafSystem.RemoveRenderable(handle);
        }

        _staticByHandle.Clear();
        _staticsFiled = false;
    }

    /// <summary>Simulates and places the static props the gather reached, exactly as the entity loop does its own.</summary>
    /// <remarks>
    /// The same two halves of <see cref="SimulatePass"/> the entities take, so an in-view static prop is drawn exactly as it
    /// was when every static prop was walked every frame — what changed is only which ones are visited.
    /// </remarks>
    private void SimulateStaticsInView(double seconds, Dictionary<int, SceneProp> scene)
    {
        if (_staticsInView.Count == 0)
        {
            return;
        }

        _simulating = true;

        try
        {
            SimulateEach(_staticsInView, seconds, WalksEvents && !Paused);
            SettleEach(_staticsInView, seconds);
        }
        finally
        {
            _simulating = false;
        }

        foreach (SceneProp prop in _staticsInView)
        {
            scene[prop.EntityIndex] = prop;
        }
    }

    /// <summary>Each unparented prop's filed box, with the placement and frames it was filed from — re-filed when either changes.</summary>
    private readonly Dictionary<(int Entity, string Model, int Occurrence), (FiledPlacement Placement, PropModels.ModelFrames? Frames, (float, float, float, float, float, float) Box)> _filedBoxes = [];

    /// <summary>How many times a filed renderable was re-linked (`RenderableChanged`), ever — counted at the call.</summary>
    public int Relinked { get; private set; }

    /// <summary>What an unparented prop's box is placed from — compared whole, so any change files it again.</summary>
    /// <remarks>
    /// **Every input of <c>WorldBoxFor</c> for a prop with no parent, and nothing else**: `Placed(prop)` reads the model's
    /// frames (compared by reference beside this), `RenderBoundsFor(Pose.Sequence)`, `Pose.Scale`, and the origin and
    /// angles. A parent adds `_propsByEntity`, which is why a parented prop is never filed. Pinned by
    /// `Instances_AnUnparentedPropChangingAnyPlacementField_IsRelinked`; a new input to `WorldBoxFor` belongs here too.
    /// </remarks>
    private readonly record struct FiledPlacement(
        float X, float Y, float Z, float Pitch, float Yaw, float Roll, float Scale, int Sequence);
}
