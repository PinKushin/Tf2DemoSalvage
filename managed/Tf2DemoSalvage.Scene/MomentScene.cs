using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.GameSystems;

namespace Tf2DemoSalvage.Scene;

/// <summary>Where one scene rebuild spent its time, so a slow moment names a phase.</summary>
/// <param name="Total">The whole rebuild.</param>
/// <param name="DrawList">Building the draw list and applying every visibility rule.</param>
/// <param name="Models">Reading models and uploading the vertex buffer.</param>
/// <param name="Pose">Posing, bones and the viewmodel.</param>
/// <param name="Weapons">The weapon report.</param>
/// <param name="Viewmodel">What the viewmodel cost, inside <paramref name="Pose"/> but outside the counters.</param>
/// <param name="Counters">Every pose-phase counter for THIS moment, already differenced.</param>
/// <param name="Drawn">How many props were posed, the control for <c>Counters.Built</c>.</param>
/// <remarks>
/// **Durations, not timestamps.** The version this replaced mixed the two — a total in ticks beside
/// five absolute marks — and every consumer had to subtract them back into what it wanted. Naming
/// the phases is the whole point of the record.
///
/// **A ledger with a residual, not a threshold on one event** (B163, B191). Every direct column
/// being small while the remainder is large is what says the cost is in something still unmeasured —
/// the pattern that found B191, where `reports` held 129 ms of a 133 ms pose and its `sink` half
/// held all of that.
///
/// Stopwatch ticks throughout, so a consumer divides by <c>Stopwatch.Frequency</c> once.
/// </remarks>
public readonly record struct MomentPhases(
    long Total,
    long DrawList,
    long Models,
    long Pose,
    long Weapons,
    long Viewmodel,
    EntityModelSet.PoseCounters Counters,
    int Drawn)
{
    /// <summary>What the ledger measured but did not name, which is the column that matters.</summary>
    /// <remarks>
    /// **Arrived at by subtraction on purpose.** A derived column inherits every error of the ones
    /// it is derived from, which is exactly why it is worth reading: when every direct column is
    /// small and this is large, the cost is in something no timer covers yet.
    /// </remarks>
    public long Unaccounted => Total - DrawList - Models - Pose - Weapons;
}

/// <summary>Assembles what one moment of a demo draws.</summary>
/// <remarks>
/// **This was <c>MainForm.ShowMoment</c> and the members it drove** — <c>AddViewmodel</c>,
/// <c>ReportWeapons</c> and the instance report — about four hundred lines, none of which was window
/// work (B188, D90). The single thing in it that needed a window was one call to upload a vertex
/// buffer, and that is now <see cref="IModelUpload"/>.
///
/// **The order is Valve's, and it is the part most worth keeping right**, because getting it wrong
/// does not fail — it draws the previous frame's pose. <c>cdll_client_int.cpp:2188-2210</c>:
///
/// <code>
/// C_BaseAnimating::UpdateClientSideAnimations();
/// ...
/// SimulateEntities();
/// C_BaseAnimating::ThreadedBoneSetup();
/// </code>
///
/// Sequence selection happens BEFORE simulation and before any bone is built. Ours is the same:
/// <see cref="EntityModelSet.UpdateClientSideAnimations"/>, then <see cref="EntityModelSet.Instances"/>,
/// which simulates and then builds bones.
///
/// **It is TOLD what it needs** through <see cref="MomentInfo"/>, which is <c>SetupRenderInfo_t</c>'s
/// arrangement (<c>clientleafsystem.h:75</c>) rather than reaching back into a form for the camera
/// mode, the followed entity and the tick.
/// </remarks>
public sealed class MomentScene : IGameSystemPerFrame
{
    /// <inheritdoc/>
    public string Name => "clientleafsystem";

    /// <summary>Forgets the level: its lighting, and the fact that its geometry was uploaded.</summary>
    /// <remarks>
    /// **This is a game system because Valve's equivalent is one.** `IClientLeafSystem` derives from
    /// `IClientLeafSystemEngine` AND `IGameSystemPerFrame` (`clientleafsystem.h:135`), so the thing
    /// that builds the renderables list is told about levels by the same walk that tells everything
    /// else. Ours already took `SetupRenderInfo_t`'s shape; this is the other half of it.
    ///
    /// **`Uploaded` is the load-bearing reset.** It records that THIS level's geometry reached the
    /// GPU; carried into the next one it says the new map is already uploaded, and nothing draws.
    /// The lighting goes back to <see cref="LevelLighting.Unlit"/> rather than null for the reason
    /// D83 gives — a null object that reports itself beats a null that throws somewhere later.
    /// </remarks>
    public void LevelShutdownPreEntity()
    {
        Uploaded = false;
        Lighting = LevelLighting.Unlit(_render);

        _drawn.Clear();
        _instances.Clear();
    }

    private readonly EntityModelSet _models;
    private readonly ViewmodelScene _viewmodels;
    private readonly ILogger _render;

    private readonly List<SceneProp> _drawn = [];
    private readonly List<ModelInstance> _instances = [];
    private readonly List<ModelInstance> _viewmodelInstances = [];

    private int _lastInstanceCount = -1;
    private int? _lastFirstPersonReport;
    private long _weaponReportedAt;

    /// <summary>Creates a scene over a packed model set.</summary>
    /// <param name="models">The packed geometry, which lives across frames.</param>
    /// <param name="viewmodels">Decides what the first-person view contains.</param>
    /// <param name="render">Where the rebuild reports what it drew.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public MomentScene(EntityModelSet models, ViewmodelScene viewmodels, ILogger render)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(viewmodels);
        ArgumentNullException.ThrowIfNull(render);

        _models = models;
        _viewmodels = viewmodels;
        _render = render;
    }

    /// <summary>What light the map casts, set when a map is read.</summary>
    public LevelLighting Lighting { get; set; } = LevelLighting.Unlit(NullLogger.Instance);

    /// <summary>How a player is dressed, set once the game's archives are open.</summary>
    public IPlayerAppearance Appearance { get; set; } = NoAppearance.Instance;

    /// <summary>Where packed geometry goes, set when a device exists.</summary>
    /// <remarks>Null before one does, which is every frame until the viewport has a handle.</remarks>
    public IModelUpload? Upload { get; set; }

    /// <summary>Where first-person weapons come from, set when a demo is loaded.</summary>
    public IViewmodelSource? Viewmodels { get; set; }

    /// <summary>What model is in a player's hands, from the game's item schema.</summary>
    /// <remarks>
    /// **Asked here rather than handed in, which is what deleted the last shim.** `MainForm` was
    /// computing the arms model and the held weapon to fill in two `MomentInfo` fields — but the
    /// scene already has the players list, so it can find the followed one and resolve both without
    /// the window knowing either question exists (B188, D90).
    /// </remarks>
    public WeaponModels Weapons { get; set; } = WeaponModels.None(NullLogger.Instance);

    /// <summary>What this moment draws, after every visibility rule.</summary>
    public IReadOnlyList<SceneProp> Drawn => _drawn;

    /// <summary>One matrix per drawn entity.</summary>
    public IReadOnlyList<ModelInstance> Instances => _instances;

    /// <summary>The first-person pass's own instances, drawn with their own camera.</summary>
    public IReadOnlyList<ModelInstance> ViewmodelInstances => _viewmodelInstances;

    /// <summary>The camera the viewmodel pass draws with, or null when it draws none.</summary>
    public FreeCamera? ViewmodelCamera { get; private set; }

    /// <summary>Whether the packed set has ever been uploaded to a device.</summary>
    /// <remarks>
    /// **Reset by the caller when the device's world is cleared**, because the packed set outlives
    /// it. After a demo switch the set already holds what the new demo needs and does not grow — but
    /// the GPU buffer it was uploaded into is gone, so `grew` alone leaves every model posed against
    /// geometry the renderer does not have (B148).
    /// </remarks>
    public bool Uploaded { get; set; }

    /// <summary>Rebuilds the scene for one moment.</summary>
    /// <param name="players">Everyone the timeline says is present.</param>
    /// <param name="props">Every non-player entity it says is present.</param>
    /// <param name="info">Everything else the rebuild needs, rather than reaching for it.</param>
    /// <returns>Where the time went.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public MomentPhases Build(
        IReadOnlyList<ScenePlayer> players, IReadOnlyList<SceneProp> props, in MomentInfo info)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(props);

        long momentAt = Stopwatch.GetTimestamp();

        // **Players become props, rather than getting a pipeline of their own.** A player is a model
        // at a pose, which is exactly what the prop path already draws, lights and interpolates — and
        // a second implementation would agree with the first only until one of them gained a feature.
        _drawn.Clear();
        _drawn.AddRange(props);

        ReportUndressedPlayers(players);

        PlayerProps.Add(players, _drawn, Appearance);

        // **The engine does not draw the player whose eyes you are using**, and cosmetics merge onto
        // their wearer's bones, so the hat goes with them. Without this the first-person view is the
        // inside of the recorder's own model and a hat hanging over the lens.
        if (info.FirstPerson && info.Followed is { } looking)
        {
            ReportFirstPersonKeeps(looking);

            DrawList.KeepOnly(_drawn, FirstPersonVisibility.Visible(_drawn, looking));
        }

        // **Every camera, not just first person.** `C_BaseCombatWeapon::ShouldDraw` hides a player's
        // holstered weapons from everybody — it is a property of the weapon rather than of who is
        // looking — so this sits outside the first-person block above. A player carries three and
        // holds one; without it all three bone-merge into the same hand.
        DrawList.KeepOnly(_drawn, WeaponVisibility.Visible(_drawn));

        long rolesAt = Stopwatch.GetTimestamp();

        Pack();

        long uploadedAt = Stopwatch.GetTimestamp();

        EntityModelSet.PoseCounters before = _models.Counters;

        _models.Instances(
            _drawn, _instances, Lighting.ComputeLighting, Lighting.SunAt, info.Seconds);

        EntityModelSet.PoseCounters pose = _models.Counters.Since(before);

        // **Timed apart from the counters above, because the pose phase spans this too.** They are
        // read across `Instances` alone, so every millisecond spent building the viewmodel scene was
        // landing in the "bones" column — which is arrived at by subtraction, and a derived column
        // inherits every error of the ones it is derived from (B191).
        long viewmodelAt = Stopwatch.GetTimestamp();

        AddViewmodel(players, info);

        long viewmodelTicks = Stopwatch.GetTimestamp() - viewmodelAt;
        long posedAt = Stopwatch.GetTimestamp();

        ReportWeapons();

        long reportedAt = Stopwatch.GetTimestamp();

        ReportInstances();

        return new MomentPhases(
            Total: Stopwatch.GetTimestamp() - momentAt,
            DrawList: rolesAt - momentAt,
            Models: uploadedAt - rolesAt,
            Pose: posedAt - uploadedAt,
            Weapons: reportedAt - posedAt,
            Viewmodel: viewmodelTicks,
            Counters: pose,
            Drawn: _drawn.Count);
    }

    /// <summary>Reads whatever geometry this moment needs, and uploads it if the set grew.</summary>
    /// <remarks>
    /// **Timed because nothing else times it, which is how it hid.** `Add` reads and decodes an MDL
    /// the first time a model path appears, and the upload rebuilds the whole packed vertex buffer —
    /// both in one frame, and both outside the pose and draw counters. A frame that spent a second
    /// here reported no time anywhere and only ever showed as "everything freezes for half a second".
    /// </remarks>
    private void Pack()
    {
        long addedAt = Stopwatch.GetTimestamp();

        bool grew = _models.Add(_drawn);

        double addSeconds = (Stopwatch.GetTimestamp() - addedAt) / (double)Stopwatch.Frequency;

        if (addSeconds > StallSeconds)
        {
            _render.LogWarning(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"STALL reading models took {addSeconds * 1000d:0} ms for {_drawn.Count} props " +
                    $"({_models.Count} packed); this frame is a freeze"));
        }

        // **Valve's own pass, under Valve's own name.** `UpdateClientSideAnimations` →
        // `SimulateEntities` → `ThreadedBoneSetup` is the engine's order
        // (`cdll_client_int.cpp:2188-2210`). It has to follow `Add` because nothing on the wire
        // carries a player's sequence and choosing one needs the model's merged sequence table.
        _models.UpdateClientSideAnimations(_drawn);

        // **`grew` alone is wrong the moment a second demo is opened (B148).** The packed set lives
        // across demos, so after a switch it already holds what the new demo needs and does not grow
        // — but the GPU buffer it was uploaded into is gone. Every posed model then took the "posed
        // before any geometry was uploaded" branch, 440,412 times in one five-minute run.
        if (!grew && Uploaded)
        {
            return;
        }

        if (Upload is not { } upload)
        {
            // **Reported once, because forgetting this draws NOTHING and says nothing** (B193). The
            // set has grown — there is geometry to hand over — and no device to hand it to, so every
            // model will be posed against a vertex buffer the renderer never received. That is
            // B148's symptom, and B148 took a 37 MB log to find.
            //
            // **Conditioned on there being VERTICES, not merely on having taken this branch.** The
            // first version fired for an idle viewer with nothing packed at all — where an absent
            // device is not a fault and cannot be one — which a control test caught. A warning that
            // fires before anything is wrong is how a real warning stops being read.
            if (!_reportedNoUpload && _models.Vertices.Count > 0)
            {
                _reportedNoUpload = true;

                _render.LogWarning(
                    "{Message}",
                    "no model upload: the packed set grew but nothing set MomentScene.Upload, so " +
                    "no entity geometry will reach the device");
            }

            return;
        }

        Uploaded = true;

        long uploadedAt = Stopwatch.GetTimestamp();

        upload.UploadModels(_models);

        double uploadSeconds = (Stopwatch.GetTimestamp() - uploadedAt) / (double)Stopwatch.Frequency;

        if (uploadSeconds > StallSeconds)
        {
            // **The whole buffer is rebuilt whenever the set GROWS**, so this is not a one-off cost
            // at load: it is paid again every time a model nobody has seen yet comes into view, and
            // it gets more expensive as the set gets bigger.
            _render.LogWarning(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"STALL uploading {_models.Vertices.Count} vertices took " +
                    $"{uploadSeconds * 1000d:0} ms because the model set grew to {_models.Count}"));
        }

        // **Logged because a model that draws nothing looks exactly like one that was never
        // uploaded.** The counts separate the two: no vertices means the packing failed, and
        // vertices with no instances means the posing did.
        _render.LogDebug(
            "{Message}",
            $"entity models: {_models.Count} packed, {_models.Vertices.Count} vertices");

        if (!_render.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        // **Named, not counted.** A count says how many arrived and nothing about which are missing,
        // and "the health packs are not drawing" is a question about names.
        foreach (string path in _models.Paths)
        {
            string indices = string.Join(
                ", ",
                _models.Batches(path).Select(batch => $"{batch.MaterialIndex}x{batch.VertexCount}"));

            _render.LogDebug("{Message}", $"  packed {path}: {indices}");
        }
    }

    /// <summary>Poses whatever the first-person view shows at this moment.</summary>
    /// <remarks>
    /// **A viewmodel has no position of its own, so this is where it gets one.** Its table is
    /// declared <c>BEGIN_NETWORK_TABLE_NOBASE</c> and carries no origin and no angles at all — the
    /// demo names the model and the pose, and <c>CBaseViewModel::CalcViewModelView</c> starts it at
    /// the eye:
    ///
    /// <code>
    /// QAngle vmangles = eyeAngles;
    /// Vector vmorigin = eyePosition;
    /// </code>
    ///
    /// **The bob, the lag and the shake that follow in the engine are deliberately not copied.**
    /// Every one of them is a function of movement and elapsed time rather than of anything the
    /// recording holds, so reproducing them would be this viewer inventing motion — which is the one
    /// thing it exists not to do. What is drawn is where the weapon was; how it swayed is not in the
    /// file.
    ///
    /// Mirrored, because a viewmodel is drawn mirrored and the cull flips with it. Getting that
    /// wrong does not fail, it draws the weapon inside out.
    /// </remarks>
    private void AddViewmodel(IReadOnlyList<ScenePlayer> players, in MomentInfo info)
    {
        // **Reported once when first person is on and the source is missing, because that pair is a
        // WIRING fault rather than an ordinary state.** A viewer with no demo open legitimately has
        // no source and must not throw — but it is also not in first person. When it is, an unset
        // source and a demo that genuinely carries no viewmodel look identical from the outside, and
        // the first of those shipped: nothing assigned `Viewmodels` when this moved out of the form,
        // so the weapon never drew and the suite stayed green (B193).
        // **`DrawViewmodel` is part of the condition, not just of the guard below** (B166). With
        // `r_drawviewmodel 0` nothing was going to be drawn, so an unset source is not a wiring
        // fault — warning about it would be the same mistake as warning in third person, which the
        // clause above already avoids.
        if (info.FirstPerson && info.DrawViewmodel && Viewmodels is null && !_reportedNoViewmodels)
        {
            _reportedNoViewmodels = true;

            _render.LogWarning(
                "{Message}",
                "no viewmodel source: first person is on but nothing set MomentScene.Viewmodels, " +
                "so no weapon will be drawn in hand");
        }

        // **`r_drawviewmodel` is checked here because this is where the engine checks it** (B166).
        // `ClientModeTFNormal::ShouldDrawViewModel` (`clientmode_tf.cpp:584`) gates the whole
        // viewmodel on it, and it ships `"1"` — so a viewer that never read it behaved correctly for
        // anyone who had not turned it off, and wrongly for the owner, who had.
        //
        // It joins the existing early return rather than getting one of its own: every clause here
        // means "draw no weapon in hand", and they must all drop the camera the same way.
        if (!info.DrawViewmodel ||
            !info.FirstPerson ||
            Viewmodels is not { } source ||
            info.Followed is not { } follower ||
            info.EyeCamera is not { } camera)
        {
            // **Dropping the camera is how "draw none" is said.** The instance list is owned by the
            // pose step and survives paused frames on purpose, so leaving it populated while first
            // person is off would keep a weapon on screen after V was pressed.
            ViewmodelCamera = null;
            return;
        }

        // **Resolved from the players this moment already sampled**, rather than being handed in.
        // The arms come from the class script exactly as the body does, and the weapon from the item
        // schema — both questions the scene can answer for itself now that it holds the roster.
        //
        // **This resolves the holder at a different tick than the original did, deliberately.**
        // `MainForm` looked them up at `_transport.CurrentTick` — a whole tick — while the props
        // being drawn were sampled at the FRACTIONAL tick. Reading them off the sampled roster makes
        // the weapon and the world agree on which moment they are showing, which is the off-by-one
        // that produces "the weapon is one frame stale". In practice the two resolve identically,
        // because the fields consulted here — `PlayerClass`, `WeaponItem`, `WeaponClass` — are
        // retained entity state rather than interpolated values, so both land on the same frame.
        ScenePlayer? held = null;

        foreach (ScenePlayer player in players)
        {
            if (player.EntityIndex == follower)
            {
                held = player;
                break;
            }
        }

        string? hands = held is { PlayerClass: { } playerClass }
            ? Appearance.Hands(playerClass)
            : null;

        // **What light the viewmodel is actually given, said once** (B170). Measured against TF2 in
        // the same room with `mat_hdr_level 0` and `mat_specular 0`: TF2's scattergun reads 81 to
        // 108 on its metal, ours 11 to 28 — about twenty times darker in linear terms, while world
        // surfaces beside it are within thirty percent. So the weapon is not washed out because its
        // reflection is too strong; the reflection is roughly right and the DIFFUSE under it has
        // almost nothing in it.
        //
        // This prints the cube the eye samples, which is the input that claim rests on.
        if (!_reportedViewmodelLight)
        {
            _reportedViewmodelLight = true;

            AmbientCube atEye = Lighting.ComputeLighting(
                camera.Origin.X, camera.Origin.Y, camera.Origin.Z);

            _render.LogInformation(
                "{Message}",
                $"viewmodel light at ({camera.Origin.X:0}, {camera.Origin.Y:0}, {camera.Origin.Z:0}): " +
                $"luminance {AmbientCube.Luminance(atEye):0.####}, " +
                $"+Z ({atEye.PositiveZ.Red:0.###}, {atEye.PositiveZ.Green:0.###}, {atEye.PositiveZ.Blue:0.###}), " +
                $"-Z ({atEye.NegativeZ.Red:0.###}, {atEye.NegativeZ.Green:0.###}, {atEye.NegativeZ.Blue:0.###}), " +
                $"sun {(Lighting.SunAt(camera.Origin.X, camera.Origin.Y, camera.Origin.Z) is null ? "none" : "reaching")}");
        }

        ViewmodelSceneResult scene = _viewmodels.Build(
            source,
            info.CurrentTick,
            follower,
            new ViewmodelPlacement(
                camera.Origin.X,
                camera.Origin.Y,
                camera.Origin.Z,
                camera.Angles.Pitch,
                camera.Angles.Yaw,
                camera.Angles.Roll),
            hands,
            held is { } holder ? Weapons.For(holder) : null);

        if (scene.Props.Count == 0)
        {
            _render.LogWarning(
                "{Message}",
                $"no viewmodel for entity {follower} at tick {info.CurrentTick}");

            ViewmodelCamera = null;
            return;
        }

        // **Whether the set grew, because packing is not uploading.** `Add` fills this process's
        // copy of the geometry; the renderer keeps its own on the GPU and only receives it when
        // `UploadModels` is called. The viewmodel's Add was once ignoring that signal, so the arms
        // were packed, posed, instanced, transformed correctly and submitted against geometry the
        // renderer did not have.
        if (_models.Add(scene.Props) && Upload is { } upload)
        {
            upload.UploadModels(_models);

            _render.LogDebug(
                "{Message}",
                $"viewmodel models uploaded: {_models.Count} packed, " +
                $"{_models.Vertices.Count} vertices");
        }

        if (scene.Changed)
        {
            // **Names each prop, because the count says two and cannot say two of WHAT.** The merged
            // arms model already carries a weapon part — c_soldier_arms pairs as hands, sleeves and
            // w_rocketlauncher — so a second prop naming the same geometry draws the gun twice.
            foreach (SceneProp shown in scene.Props)
            {
                _render.LogDebug(
                    "{Message}",
                    $"  viewmodel prop '{shown.ModelPath}' seq {shown.Pose.Sequence}");
            }
        }

        // **One call for all of them, because Instances CLEARS the list it is given.** Posing the
        // arms and then the weapon into the same list threw the arms away and drew the gun alone.
        _models.Instances(
            scene.Props,
            _viewmodelInstances,
            Lighting.ComputeLighting,
            Lighting.SunAt,
            info.Seconds);

        if (scene.Changed)
        {
            _render.LogDebug(
                "{Message}",
                $"viewmodel at tick {info.CurrentTick}: {scene.Props.Count} props, " +
                $"{_viewmodelInstances.Count} instances");
        }

        ViewmodelCamera = new FreeCamera
        {
            Origin = camera.Origin,
            Angles = camera.Angles,
            Aspect = camera.Aspect,
            FarZ = camera.FarZ,
            FieldOfView = info.ViewmodelFieldOfView,
            NearZ = ViewmodelPass.NearPlane,
        };
    }

    /// <summary>Says once when players are being drawn with nothing to dress them from.</summary>
    /// <remarks>
    /// **A null object is the right default and it hides a missed wiring, so it has to say so.**
    /// <see cref="NoAppearance"/> answers null to every question, which is correct for a machine
    /// with no TF2 — and identical, from the outside, to a scene nobody ever handed a real
    /// appearance to. The visible result of the second is every player drawn with a null
    /// <c>Pose.Slot</c>, so their weapon animations quietly fall back to the generic primary forms.
    /// Nothing throws, and no test that does not read <c>Slot</c> can tell.
    ///
    /// That is exactly the failure `docs/memory/a-null-object-default-hides-a-missed-wiring.md`
    /// records — 193 call sites converted, the suite green, and the log 202 lines shorter. It was
    /// nearly repeated here: moving `ShowMoment` out dropped the `EnsureWeaponRoles` call, and only
    /// an analyzer noticing the method had become unreachable caught it.
    ///
    /// **Warning rather than Debug, and once rather than per frame.** It reports a wiring fault
    /// rather than per-frame detail, so it must survive `developer 0` — and B191's lesson is about
    /// lines that REPEAT, not lines that happen once.
    /// </remarks>
    private void ReportUndressedPlayers(IReadOnlyList<ScenePlayer> players)
    {
        if (_reportedNoAppearance || players.Count == 0 || Appearance is not NoAppearance)
        {
            return;
        }

        _reportedNoAppearance = true;

        _render.LogWarning(
            "{Message}",
            $"no player appearance: {players.Count.ToString(CultureInfo.InvariantCulture)} " +
            "players will draw with no weapon animation slot. Either the game's archives are not " +
            "open, or nothing set MomentScene.Appearance.");
    }

    /// <summary>Whether the undressed-players warning has already been given.</summary>
    private bool _reportedNoAppearance;

    /// <summary>Whether the missing-viewmodel-source warning has already been given.</summary>
    private bool _reportedNoViewmodels;

    /// <summary>Whether the light the viewmodel receives has been reported (B170).</summary>
    private bool _reportedViewmodelLight;

    /// <summary>Whether the missing-upload warning has already been given.</summary>
    private bool _reportedNoUpload;

    /// <summary>Says where every carried weapon is, once a second.</summary>
    /// <remarks>
    /// **Nothing at all unless someone is listening** (B191). Everything below walks every instance,
    /// formats nine numbers per weapon and joins them — work CA1873 exists to keep out of a disabled
    /// log — and then writes a line, which is a disk flush. Measured 2026-08-25 on the moment
    /// ledger: `weapons 193.6` of a 198 ms scene rebuild, with every other column at a millisecond
    /// or less. The rate limit already held it to once a second; once a second is still enough to be
    /// the worst frame in that second.
    /// </remarks>
    private void ReportWeapons()
    {
        if (!_render.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();

        if (now - _weaponReportedAt < Stopwatch.Frequency)
        {
            return;
        }

        _weaponReportedAt = now;

        static bool IsWeapon(string path) =>
            path.Contains("/weapons/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("\\weapons\\", StringComparison.OrdinalIgnoreCase);

        int inScene = 0;
        int atOrigin = 0;
        int owned = 0;
        int attached = 0;
        string first = None;

        foreach (SceneProp prop in _drawn)
        {
            if (!IsWeapon(prop.ModelPath))
            {
                continue;
            }

            inScene++;

            if (prop.OwnedBy is not null)
            {
                owned++;
            }

            if (prop.AttachedTo is not null)
            {
                attached++;
            }

            // The signature of a carried weapon that never got a transform: an owner, no attachment,
            // and the world origin.
            if (prop.Pose is { X: 0f, Y: 0f, Z: 0f })
            {
                atOrigin++;

                if (first == None)
                {
                    first = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{System.IO.Path.GetFileNameWithoutExtension(prop.ModelPath)}" +
                        $" entity {prop.EntityIndex}" +
                        $" owner {prop.OwnedBy?.ToString(CultureInfo.InvariantCulture) ?? "-"}" +
                        $" attached {prop.AttachedTo?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
                }
            }
        }

        int instanced = 0;
        int drawnAtOrigin = 0;
        string drawn = None;

        foreach (ModelInstance instance in _instances)
        {
            if (!IsWeapon(instance.ModelPath))
            {
                continue;
            }

            instanced++;

            // **Where it actually DRAWS, which is its bones and not its matrix** (D88). A skinned
            // model's bones are in world space and its model matrix is deliberately identity, so
            // reading the matrix reports (0,0,0) for every correctly placed weapon in the game.
            //
            // This line said "9 AT THE ORIGIN" on a demo where all nine had merged onto their owners
            // correctly — 2 of 5 bones on weapon_bone, confirmed in the same log. An instrument that
            // reports a defect which is not there costs exactly what a missing one does.
            (float x, float y, float z) = instance.Bones is { Count: > 0 } bones
                ? (bones[0][3], bones[0][7], bones[0][11])
                : (instance.Matrix[12], instance.Matrix[13], instance.Matrix[14]);

            if (x == 0f && y == 0f && z == 0f)
            {
                drawnAtOrigin++;
            }

            if (drawn == None)
            {
                drawn = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{System.IO.Path.GetFileNameWithoutExtension(instance.ModelPath)}" +
                    $" at ({x:0}, {y:0}, {z:0})" +
                    $" [{(instance.Bones is { Count: > 0 } ? "bone" : "matrix")}]");
            }
        }

        _render.LogDebug(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"weapons: {inScene} in the scene, {instanced} instanced, " +
                $"{owned} owned, {attached} attached; " +
                $"{atOrigin} sent no origin of their own, which is what a bone merge looks like; " +
                $"{drawnAtOrigin} DRAWN AT THE ORIGIN; " +
                $"first without an origin {first}; first instanced {drawn}"));
    }

    /// <summary>What the weapon report says when it found none, in both halves.</summary>
    private const string None = "none";

    /// <summary>Says which props survive first-person filtering, once per followed entity.</summary>
    /// <remarks>
    /// **Says what it is deciding about, because three fixes have now been aimed at this from a
    /// screenshot.** The question is never "is something wrong" — it is which prop is still drawn
    /// and what it claims about its owner, and no count can answer that.
    /// </remarks>
    private void ReportFirstPersonKeeps(int looking)
    {
        if (_lastFirstPersonReport == looking || !_render.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        _lastFirstPersonReport = looking;

        foreach (SceneProp prop in _drawn)
        {
            if (prop.EntityIndex == looking ||
                prop.AttachedTo == looking ||
                prop.OwnedBy == looking)
            {
                continue;
            }

            _render.LogDebug(
                "{Message}",
                $"first person keeps entity {prop.EntityIndex} '{prop.ModelPath}' " +
                $"attachedTo={prop.AttachedTo?.ToString(CultureInfo.InvariantCulture) ?? "-"} " +
                $"ownedBy={prop.OwnedBy?.ToString(CultureInfo.InvariantCulture) ?? "-"} " +
                $"(following {looking})");
        }
    }

    /// <summary>Names what is drawn, when the set changes.</summary>
    private void ReportInstances()
    {
        if (_instances.Count == _lastInstanceCount || !_render.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        _lastInstanceCount = _instances.Count;

        // **Named and counted.** "Some models are missing" is a question about which, and a total
        // cannot answer it — a demo only carries what the recorder could see, so an absent pickup
        // may be correct rather than broken.
        string names = string.Join(
            ", ",
            _instances
                .GroupBy(instance => instance.ModelPath, StringComparer.Ordinal)
                .Select(group => $"{group.Count()}x{group.Key}"));

        // **How many were actually lit, not just how many were drawn.** A model with no cube draws
        // at full brightness and looks like a rendering fault; the count is what says whether the
        // leaf lookup found anything, without anyone having to judge by eye.
        int unlit = _instances.Count(instance => instance.Light == default(AmbientCube));

        // Debug, because "the instance count changed" happens whenever a prop enters or leaves the
        // visible set — measured at 13 lines in 10 seconds of ordinary play, which is per-frame
        // detail wearing a change guard (B191).
        _render.LogDebug(
            "{Message}", $"drawing {_instances.Count} posed models ({unlit} unlit): {names}");
    }

    /// <summary>How long a phase may take before it is worth reporting, in seconds.</summary>
    /// <remarks>
    /// **Its own threshold rather than the frame loop's**, even though the numbers agree: this one
    /// is applied to one step of a scene rebuild, and the viewer's is applied to a whole frame. A
    /// constant carries no scope.
    /// </remarks>
    public const double StallSeconds = 0.03;
}

/// <summary>A game nobody has installed: every player is undressed and nothing resolves.</summary>
/// <remarks>
/// **A real object rather than a null field** (D83). The scene asks the same question whether or not
/// TF2 is present, and a null here is the shape that hid a missed wiring across 193 call sites once.
/// </remarks>
internal sealed class NoAppearance : IPlayerAppearance
{
    /// <summary>The only instance, since it holds nothing.</summary>
    public static NoAppearance Instance { get; } = new();

    /// <inheritdoc/>
    public string? ModelOf(int playerClass) => null;

    /// <inheritdoc/>
    public string? WeaponSuffix(string? weaponClass, int? playerClass) => null;

    /// <inheritdoc/>
    /// <remarks>
    /// **True, matching <see cref="GameAppearance"/>'s answer when the install cannot say.**
    /// Air-walking is the general case and only the medic opts out, so defaulting to false would
    /// stop every class air-walking on a machine with no TF2 — a visible difference produced by an
    /// absent install rather than by the demo.
    /// </remarks>
    public bool Airwalks(int playerClass) => true;

    /// <inheritdoc/>
    public string? Hands(int playerClass) => null;
}
