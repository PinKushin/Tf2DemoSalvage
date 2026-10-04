using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>Where a rope end is and which way it faces — <c>CalculateEndPointAttachment</c>'s position and angles.</summary>
/// <param name="Position">The attachment, or the entity's <c>WorldSpaceCenter</c>.</param>
/// <param name="Forward">The forward of the attachment's angles, or of the entity's.</param>
public readonly record struct RopeEndPoint(Vector3 Position, Vector3 Forward);

/// <summary>
/// <c>CalculateEndPointAttachment</c> (`c_rope.cpp:1923-1967`) for one end: the player's weapon's <c>buff_attach</c> when
/// asked, else the attachment above zero, else the entity's <c>WorldSpaceCenter</c>.
/// </summary>
/// <param name="handle">The raw <c>EHANDLE</c>; the caller dereferences it as the client would.</param>
/// <param name="attachment">The attachment number; 0 is none.</param>
/// <param name="playerWeapon"><c>ROPE_PLAYER_WPN_ATTACH</c>.</param>
/// <returns>The end, or null for a handle that names nothing — which leaves the cached end where it was.</returns>
public delegate RopeEndPoint? RopeEndPointResolver(int handle, int attachment, bool playerWeapon);

/// <summary>What a rope node's ±2 hull sweep through the world hit — the parts of <c>trace_t</c> the collision reads.</summary>
/// <param name="Fraction">How far it got.</param>
/// <param name="Normal">The struck plane's normal.</param>
/// <param name="Distance">The struck plane's distance.</param>
/// <param name="Solid"><c>allsolid</c> or <c>startsolid</c>.</param>
public readonly record struct RopeHit(float Fraction, Vector3 Normal, float Distance, bool Solid);

/// <summary>
/// <c>UTIL_TraceHull( prev, pos, ( -2, -2, -2 ), ( 2, 2, 2 ), MASK_SOLID_BRUSHONLY, world only )</c>.
/// </summary>
/// <param name="from">The node's previous position.</param>
/// <param name="to">Its new one.</param>
/// <returns>What the sweep hit.</returns>
public delegate RopeHit RopeCollision(Vector3 from, Vector3 to);

/// <summary>The camera, as the rope build reads it: <c>CurrentViewOrigin</c>, <c>CurrentViewForward</c>, <c>ScreenWidth</c>.</summary>
/// <param name="Origin">The view origin, which is also <c>MainViewOrigin</c> for the wind distance.</param>
/// <param name="Forward">The view forward.</param>
/// <param name="ScreenWidth">The viewport's width in pixels.</param>
public readonly record struct RopeView(Vector3 Origin, Vector3 Forward, float ScreenWidth);

/// <summary>
/// Every <c>C_RopeKeyframe</c> in a moment, simulated and drawn as the client does: <c>ClientThink</c>, then
/// <c>DrawModel</c> and the rope manager's <c>BuildRope</c> and two passes (`c_rope.cpp`, B477).
/// </summary>
/// <remarks>
/// **A rope's shape is client state.** The wire carries the two ends, the length and slack, the node count and the
/// flags; the client hangs the nodes between the ends by simulating five seconds of gravity at creation, keeps them
/// swaying in random gusts while the camera is near, and smooths them with a Catmull-Rom spline when it draws. So this
/// class keeps a <see cref="RopePhysics"/> per rope across frames, the way the entity does.
///
/// **What is not here, and why.** <c>GetWindspeedAtTime</c> is always zero: no demo in either corpus carries an
/// <c>env_wind</c> (`entity-census`), so only the gust branch is reachable. The impulse from the rope's own entity
/// message, the <c>ShakeRopes</c> effect and the holiday lights are not ported (B478). The random gusts draw from a
/// stream seeded by the entity index rather than the client's global stream, whose state cannot be reproduced.
/// </remarks>
public sealed class EntityRopes
{
    /// <summary><c>ROPE_COLLIDE</c>.</summary>
    public const int CollideFlag = 1 << 2;

    /// <summary><c>ROPE_SIMULATE</c> — "Is the rope valid?"; a rope without it is neither simulated nor drawn.</summary>
    public const int SimulateFlag = 1 << 3;

    /// <summary><c>ROPE_NO_WIND</c>.</summary>
    public const int NoWindFlag = 1 << 5;

    /// <summary><c>ROPE_INITIAL_HANG</c>.</summary>
    public const int InitialHangFlag = 1 << 6;

    /// <summary><c>ROPE_PLAYER_WPN_ATTACH</c>.</summary>
    public const int PlayerWeaponFlag = 1 << 7;

    /// <summary><c>ROPE_NO_GRAVITY</c>.</summary>
    public const int NoGravityFlag = 1 << 8;

    /// <summary><c>ROPE_RESIZE</c>.</summary>
    public const int ResizeFlag = 1 << 0;

    /// <summary><c>ROPE_BARBED</c>.</summary>
    public const int BarbedFlag = 1 << 1;

    /// <summary><c>ROPE_LOCK_START_POINT</c>.</summary>
    public const int LockStartPoint = 0x1;

    /// <summary><c>ROPE_LOCK_END_POINT</c>.</summary>
    public const int LockEndPoint = 0x2;

    /// <summary><c>ROPE_LOCK_START_DIRECTION</c>.</summary>
    public const int LockStartDirection = 0x4;

    /// <summary><c>ROPE_LOCK_END_DIRECTION</c>.</summary>
    public const int LockEndDirection = 0x8;

    /// <summary><c>ROPESLACK_FUDGEFACTOR</c>: "so when a level designer enters a slack of zero … it doesn't dangle so low".</summary>
    internal const int SlackFudge = -100;

    /// <summary><c>MAX_ROPE_SUBDIVS</c> (`c_rope.h:25`).</summary>
    internal const int MaximumSubdivisions = 8;

    /// <summary><c>rope_subdiv</c>'s default, for an <c>m_Subdiv</c> of 255.</summary>
    internal const int DefaultSubdivisions = 2;

    /// <summary><c>ROPE_GRAVITY</c>, `rope_shared.h:21`.</summary>
    private static readonly Vector3 Gravity = new(0f, 0f, -1500f);

    /// <summary><c>g_RopeSubdivs</c>: per subdivision count, the <c>( t, t², t³ )</c> of each point between two nodes.</summary>
    private static readonly Vector3[][] Subdivisions = BuildSubdivisions();

    /// <summary><c>g_BarbedSubdivs</c>, "interesting barbed-wire-looking effect" (`c_rope.cpp:141-144`).</summary>
    private static readonly Vector3[] BarbedSubdivisions =
    [
        new(1.5f, 1.5f * 1.5f, 1.5f * 1.5f * 1.5f),
        new(-0.5f, -0.5f * -0.5f, -0.5f * -0.5f * -0.5f),
        new(0.5f, 0.5f * 0.5f, 0.5f * 0.5f * 0.5f),
    ];

    private readonly Dictionary<int, Rope> _ropes = [];
    private readonly HashSet<int> _offered = [];
    private readonly List<int> _forgotten = [];
    private readonly SpriteStripBatches _strips = new();
    private readonly List<BeamSegment> _segments = [];
    private readonly List<float> _backWidths = [];
    private readonly List<DetailSpriteVertex> _corners = [];

    /// <summary>How many ropes the last build drew.</summary>
    public int Drawn { get; private set; }

    /// <summary>How many it was offered and drew nothing for — no <c>ROPE_SIMULATE</c>, or no material.</summary>
    public int Skipped { get; private set; }

    /// <summary>The material a rope's translucent anti-aliasing pass is drawn with: its own name plus <c>_back</c>.</summary>
    /// <param name="modelPath">The rope's material path, as <c>modelprecache</c> names it.</param>
    /// <returns>The back material's path.</returns>
    /// <remarks>
    /// <c>FinishInit</c> cuts the name at <c>.vmt</c> — <c>Q_stristr</c>, so any case — and appends <c>_back</c>
    /// (`c_rope.cpp:1299-1325`).
    /// </remarks>
    public static string BackMaterialPath(string modelPath)
    {
        ArgumentNullException.ThrowIfNull(modelPath);

        int extension = modelPath.IndexOf(".vmt", StringComparison.OrdinalIgnoreCase);

        return string.Concat(extension >= 0 ? modelPath[..extension] : modelPath, "_back.vmt");
    }

    /// <summary>Simulates and draws this moment's ropes.</summary>
    /// <param name="props">What the scene holds; only props carrying a <see cref="SceneRope"/> are read.</param>
    /// <param name="view">The camera.</param>
    /// <param name="sprites">Each material as loaded, keyed by path.</param>
    /// <param name="endPoints">Where each end is; see <see cref="RopeEndPointResolver"/>.</param>
    /// <param name="lighting"><c>engine-&gt;ComputeLighting</c> at a point, averaged over the cube and clamped.</param>
    /// <param name="collide">The world sweep a <c>ROPE_COLLIDE</c> rope's nodes take.</param>
    /// <param name="frameTime"><c>gpGlobals-&gt;frametime</c>; zero while paused.</param>
    /// <returns>One batch per material and pass: every back pass, then every solid one.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public IReadOnlyList<ParticleBatch> Build(
        IReadOnlyList<SceneProp> props,
        in RopeView view,
        IReadOnlyDictionary<string, EngineSprite> sprites,
        RopeEndPointResolver endPoints,
        Func<Vector3, Vector3> lighting,
        RopeCollision collide,
        float frameTime)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentNullException.ThrowIfNull(endPoints);
        ArgumentNullException.ThrowIfNull(lighting);
        ArgumentNullException.ThrowIfNull(collide);

        _strips.Clear();
        _offered.Clear();
        Drawn = 0;
        Skipped = 0;

        List<(Rope Rope, SceneProp Prop)> drawn = [];

        foreach (SceneProp prop in props)
        {
            if (prop.Pose.Rope is not { } parameters)
            {
                continue;
            }

            _offered.Add(prop.EntityIndex);

            Rope rope = RopeFor(prop, parameters);

            rope.Context = new Context(endPoints, lighting, collide, view);
            rope.Think(frameTime);

            if (rope.ReadyToDraw(sprites))
            {
                drawn.Add((rope, prop));
            }
            else
            {
                Skipped++;
            }
        }

        Forget();

        // `DrawRenderCache_NonQueued`: every rope's back pass, then every rope's solid pass, per material pair.
        foreach ((Rope rope, SceneProp prop) in drawn)
        {
            bool back = Draw(rope, prop, sprites, view, solid: false);
            bool solid = Draw(rope, prop, sprites, view, solid: true);

            if (back || solid)
            {
                Drawn++;
            }
        }

        return _strips.Batches(sprites);
    }

    /// <summary>This entity's simulation, new when it was not offered last frame or changed material.</summary>
    private Rope RopeFor(SceneProp prop, SceneRope parameters)
    {
        if (!_ropes.TryGetValue(prop.EntityIndex, out Rope? rope) ||
            !string.Equals(rope.ModelPath, prop.ModelPath, StringComparison.Ordinal))
        {
            rope = new Rope(prop.EntityIndex, prop.ModelPath, parameters);
            _ropes[prop.EntityIndex] = rope;
        }

        rope.Receive(parameters, new Vector3(prop.Pose.X, prop.Pose.Y, prop.Pose.Z));

        return rope;
    }

    /// <summary>Drops every rope whose entity was not offered this frame — the entity was deleted.</summary>
    private void Forget()
    {
        _forgotten.Clear();

        foreach (int entity in _ropes.Keys)
        {
            if (!_offered.Contains(entity))
            {
                _forgotten.Add(entity);
            }
        }

        foreach (int entity in _forgotten)
        {
            _ropes.Remove(entity);
        }
    }

    /// <summary>One pass of one rope: <c>BuildRope</c>, then <c>RenderNonSolidRopes</c> or <c>RenderSolidRopes</c>.</summary>
    /// <returns>Whether anything was emitted.</returns>
    private bool Draw(Rope rope, SceneProp prop, IReadOnlyDictionary<string, EngineSprite> sprites, in RopeView view, bool solid)
    {
        EngineSprite material = sprites[prop.ModelPath];
        string backPath = BackMaterialPath(prop.ModelPath);
        bool fakeAntialiasing = sprites.TryGetValue(backPath, out EngineSprite back);

        if (!solid && !fakeAntialiasing)
        {
            return false;
        }

        float maximumBackWidth = rope.Build(_segments, _backWidths, view, fakeAntialiasing, material.Height);

        if (solid && fakeAntialiasing)
        {
            // "If it's all going to be 0 alpha, then just skip drawing this one."
            if (RopeSolidMinimumAlpha <= 0f && maximumBackWidth <= RopeSolidMinimumWidth)
            {
                return false;
            }

            for (int index = 0; index < _segments.Count; index++)
            {
                float width = _backWidths[index];
                float alpha = Math.Clamp(
                    BeamDraw.RemapVal(width, RopeSolidMinimumWidth, RopeSolidMaximumWidth, RopeSolidMinimumAlpha, RopeSolidMaximumAlpha),
                    0f,
                    1f);

                _segments[index] = _segments[index] with { Width = width, Alpha = alpha };
            }
        }

        _corners.Clear();
        BeamSegDraw.Draw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_segments), view.Origin, _corners);

        int before = _strips.Corners;

        if (solid)
        {
            _strips.Add(prop.ModelPath, material, RenderModes.Normal, _corners);
        }
        else
        {
            _strips.Add(backPath, back, RenderModes.Normal, _corners);
        }

        return _strips.Corners > before;
    }

    /// <summary><c>rope_solid_minwidth</c>.</summary>
    private const float RopeSolidMinimumWidth = 0.3f;

    /// <summary><c>rope_solid_maxwidth</c>.</summary>
    private const float RopeSolidMaximumWidth = 1f;

    /// <summary><c>rope_solid_minalpha</c>.</summary>
    private const float RopeSolidMinimumAlpha = 0f;

    /// <summary><c>rope_solid_maxalpha</c>.</summary>
    private const float RopeSolidMaximumAlpha = 1f;

    private static Vector3[][] BuildSubdivisions()
    {
        // `CSubdivInit` (`c_rope.cpp:124-138`).
        Vector3[][] table = new Vector3[MaximumSubdivisions][];

        for (int subdivisions = 0; subdivisions < MaximumSubdivisions; subdivisions++)
        {
            table[subdivisions] = new Vector3[MaximumSubdivisions];

            for (int index = 0; index <= subdivisions; index++)
            {
                float t = (float)(index + 1) / (subdivisions + 1);
                table[subdivisions][index] = new Vector3(t, t * t, t * t * t);
            }
        }

        return table;
    }

    /// <summary>What one frame's think and draw read from outside the rope.</summary>
    private readonly record struct Context(
        RopeEndPointResolver EndPoints, Func<Vector3, Vector3> Lighting, RopeCollision Collide, RopeView View);

    /// <summary>One <c>C_RopeKeyframe</c>'s client state, and its <c>ClientThink</c>, <c>DrawModel</c> and <c>BuildRope</c>.</summary>
    private sealed class Rope
    {
        /// <summary><c>rope_wind_dist</c>: no gusts past this far from the camera.</summary>
        private const float WindDistance = 1000f;

        /// <summary><c>ROPE_IMPULSE_SCALE</c>.</summary>
        private const float ImpulseScale = 20f;

        /// <summary><c>ROPE_IMPULSE_DECAY</c>.</summary>
        private const float ImpulseDecay = 0.95f;

        /// <summary><c>g_flLockAmount</c>.</summary>
        private const float LockAmount = 0.1f;

        /// <summary><c>g_flLockFalloff</c>.</summary>
        private const float LockFalloff = 0.3f;

        private readonly RopePhysics _physics = new();
        private readonly Vector3[] _lightValues = new Vector3[RopePhysics.MaximumNodes];
        private readonly bool[] _linksTouching = new bool[RopePhysics.MaximumNodes];
        private readonly bool[] _previousEndValid = new bool[2];
        private readonly Vector3[] _previousEnd = new Vector3[2];
        private readonly Vector3[] _cachedEnd = new Vector3[2];
        private readonly Vector3[] _cachedForward = new Vector3[2];
        private readonly UniformRandomStream _random = new();

        private SceneRope _parameters;
        private Vector3 _origin;
        private int _linksTouchingCount;
        private bool _applyWind;
        private int _previousLockedPoints;
        private int _forcePointMoveCounter;
        private bool _endPointsDirty = true;
        private bool _newDataThisFrame;
        private bool _physicsInitialised;
        private Vector3 _impulse;
        private Vector3 _previousImpulse;
        private float _gustTimer;
        private float _gustLifetime;
        private float _timeToNextGust;
        private Vector3 _windDirection;

        public Rope(int entity, string modelPath, SceneRope parameters)
        {
            ModelPath = modelPath;
            _parameters = parameters;
            _random.SetSeed(entity);

            // `FinishInit`: the node count is clamped and fixed when the entity is created.
            _physics.SetNumNodes(Math.Clamp(parameters.Segments, 2, RopePhysics.MaximumNodes));
            RecomputeSprings();
            _newDataThisFrame = true;
        }

        public string ModelPath { get; }

        public Context Context { get; set; }

        /// <summary><c>OnDataChanged</c>: new data this frame when anything received changed, and the springs on a new length or slack.</summary>
        public void Receive(SceneRope parameters, Vector3 origin)
        {
            if (parameters != _parameters || origin != _origin)
            {
                _newDataThisFrame = true;
            }

            bool resprung = parameters.Slack != _parameters.Slack || parameters.Length != _parameters.Length;

            _parameters = parameters;
            _origin = origin;

            // `RecvProxy_RecomputeSprings` on `m_Slack` and `m_RopeLength`.
            if (resprung)
            {
                RecomputeSprings();
            }
        }

        /// <summary><c>C_RopeKeyframe::ClientThink</c> (`c_rope.cpp:1400-1443`).</summary>
        public void Think(float frameTime)
        {
            // "Only recalculate the endpoint attachments once per frame."
            _endPointsDirty = true;

            if (!InitRopePhysics())
            {
                return;
            }

            if (!DetectRestingState())
            {
                RunRopeSimulation(frameTime);

                _newDataThisFrame = false;

                // "Setup a new wind gust?"
                _gustTimer += frameTime;
                _timeToNextGust -= frameTime;

                if (_timeToNextGust <= 0f)
                {
                    _windDirection = Vector3.Normalize(new Vector3(
                        _random.RandomFloat(-1f, 1f), _random.RandomFloat(-1f, 1f), _random.RandomFloat(-1f, 1f)));
                    _windDirection *= 50f;
                    _windDirection *= _random.RandomFloat(-1f, 1f);

                    _gustTimer = 0f;
                    _gustLifetime = _random.RandomFloat(2f, 3f);
                    _timeToNextGust = _random.RandomFloat(3f, 4f);
                }
            }
        }

        /// <summary>
        /// <c>DrawModel</c>'s gates (`c_rope.cpp:1446-1473`) and <c>AddToRenderCache</c>'s: initialised, simulating, and
        /// a solid material to draw with.
        /// </summary>
        public bool ReadyToDraw(IReadOnlyDictionary<string, EngineSprite> sprites)
        {
            // `ShouldDraw` refuses a rope without ROPE_SIMULATE, and `InitRopePhysics` with it.
            if (!InitRopePhysics() ||
                !sprites.TryGetValue(ModelPath, out EngineSprite material) ||
                material.Material.Sheet is null)
            {
                return false;
            }

            // "Resize the rope".
            if ((_parameters.Flags & ResizeFlag) != 0)
            {
                RecomputeSprings();
            }

            ConstrainNodesBetweenEndpoints();

            return true;
        }

        /// <summary><c>BuildRope</c> (`c_rope.cpp:1680-1844`): the spline-subdivided strip and its widths and alphas.</summary>
        /// <returns><c>m_flMaxBackWidth</c>.</returns>
        public float Build(
            List<BeamSegment> segments, List<float> backWidths, in RopeView view, bool fakeAntialiasing, int textureHeight)
        {
            segments.Clear();
            backWidths.Clear();

            Vector3[] subdivisions = SubdivisionVectors(out int subdivisionCount);
            float subdivisionScale = 1f / (subdivisionCount + 1);
            int nodeCount = _physics.NodeCount;
            int lastNode = nodeCount - 1;
            int previousNode = 0;
            RopeNode[] nodes = _physics.Nodes;

            for (int node = 0; node < nodeCount; node++)
            {
                Vector3 colour = LinearToGamma(_lightValues[node]);
                segments.Add(new BeamSegment(nodes[node].Predicted, colour, 0f, 0f, 0f));

                if (node < lastNode)
                {
                    // "Draw a midpoint to the next segment."
                    int next = node + 1;
                    int nextNext = node + 2;

                    if (next >= nodeCount)
                    {
                        next = nextNext = lastNode;
                    }
                    else if (nextNext >= nodeCount)
                    {
                        nextNext = lastNode;
                    }

                    Vector3 colourIncrement = subdivisionScale * (LinearToGamma(_lightValues[node + 1]) - colour);

                    for (int step = 0; step < subdivisionCount; step++)
                    {
                        Vector3 position = CatmullRom(
                            nodes[previousNode].Predicted, nodes[node].Predicted, nodes[next].Predicted,
                            nodes[nextNext].Predicted, subdivisions[step]);

                        segments.Add(new BeamSegment(position, segments[^1].Colour + colourIncrement, 0f, 0f, 0f));
                    }

                    previousNode = node;
                }
            }

            // "Figure out texture scale."
            float pixelsPerInch = 4f / _parameters.TextureScale;
            float totalTexCoord = pixelsPerInch * (_parameters.Length + _parameters.Slack + SlackFudge);
            int totalPoints = ((nodeCount - 1) * subdivisionCount) + 1;
            float increment = totalTexCoord / totalPoints / textureHeight;
            float texCoord = 0f;
            float maximumBackWidth = 0f;

            for (int index = 0; index < segments.Count; index++)
            {
                BeamSegment segment = segments[index];

                if (fakeAntialiasing)
                {
                    (float width, float alpha, float backWidth) = Smoothed(segment.Position, view);

                    segments[index] = segment with { TexCoord = texCoord, Width = width, Alpha = alpha };
                    backWidths.Add(backWidth);

                    if (backWidth > 0f)
                    {
                        maximumBackWidth = Math.Max(maximumBackWidth, backWidth);
                    }
                }
                else
                {
                    // "Build the data with no smoothing."
                    segments[index] = segment with { TexCoord = texCoord, Width = _parameters.Width, Alpha = 0.3f };
                    backWidths.Add(-1f);
                }

                texCoord += increment;
            }

            return maximumBackWidth;
        }

        /// <summary>
        /// The fake anti-aliasing widths (`c_rope.cpp:1771-1827`): a translucent rope at least
        /// <c>rope_smooth_minwidth</c> pixels wide under a solid one 1.4 pixels narrower.
        /// </summary>
        private (float Width, float Alpha, float BackWidth) Smoothed(Vector3 position, in RopeView view)
        {
            const float Enlarge = 1.4f;
            const float MinimumAlpha = 0.2f;
            const float MaximumAlpha = 0.5f;
            const float MinimumScreenWidth = 0.3f;
            const float MaximumAlphaScreenWidth = 1.75f;

            float halfScreenWidth = view.ScreenWidth / 2f;
            float z = Math.Max(Vector3.Dot(view.Forward, position - view.Origin), 0.1f);
            float screenSpaceWidth = _parameters.Width * halfScreenWidth / z;

            if (screenSpaceWidth < MinimumScreenWidth)
            {
                return (MinimumScreenWidth * z / halfScreenWidth, MinimumAlpha, 0f);
            }

            float alpha = screenSpaceWidth > MaximumAlphaScreenWidth
                ? MaximumAlpha
                : BeamDraw.RemapVal(screenSpaceWidth, MinimumScreenWidth, MaximumAlphaScreenWidth, MinimumAlpha, MaximumAlpha);

            float backWidth = _parameters.Width - (z * Enlarge / view.ScreenWidth);

            return (_parameters.Width, alpha, Math.Max(backWidth, 0f));
        }

        /// <summary><c>GetRopeSubdivVectors</c> (`c_rope.cpp:2022-2043`).</summary>
        private Vector3[] SubdivisionVectors(out int count)
        {
            if ((_parameters.Flags & BarbedFlag) != 0)
            {
                count = BarbedSubdivisions.Length;
                return BarbedSubdivisions;
            }

            int subdivisions = _parameters.Subdiv == 255 ? DefaultSubdivisions : _parameters.Subdiv;

            if (subdivisions >= MaximumSubdivisions)
            {
                subdivisions = MaximumSubdivisions - 1;
            }

            count = subdivisions;
            return Subdivisions[subdivisions];
        }

        /// <summary><c>InitRopePhysics</c> (`c_rope.cpp:1868-1920`).</summary>
        private bool InitRopePhysics()
        {
            if ((_parameters.Flags & SimulateFlag) == 0)
            {
                return false;
            }

            if (_physicsInitialised)
            {
                return true;
            }

            // "Must have both entities to work" — `GetEndPointPos` answers true whatever it found, so neither test can
            // refuse: a missing end keeps its cached position, zero until one has been seen.
            _previousEndValid[0] = EndPointPosition(0, out _previousEnd[0]);
            _previousEndValid[1] = EndPointPosition(1, out _previousEnd[1]);

            Vector3 start = _previousEnd[0];
            Vector3 attached = _previousEnd[1];

            _physics.ResetSpringLength(0f);
            RecomputeSprings();
            _physics.Restart();

            for (int index = 0; index < _physics.NodeCount; index++)
            {
                float t = (float)index / (_physics.NodeCount - 1);
                ref RopeNode node = ref _physics.Nodes[index];

                node.Position = Vector3.Lerp(start, attached, t);
                node.Previous = node.Position;
            }

            // "Simulate for a bit to let it sag."
            if ((_parameters.Flags & InitialHangFlag) != 0)
            {
                RunRopeSimulation(5f);
            }

            CalcLightValues();

            _timeToNextGust = _random.RandomFloat(1f, 3f);
            _physicsInitialised = true;

            return true;
        }

        /// <summary><c>RecomputeSprings</c>: <c>( length + slack − 100 ) / ( nodes − 1 )</c>, in integers as the engine divides.</summary>
        private void RecomputeSprings()
        {
            // Every operand is an `int` in the engine, so the quotient is truncated before it becomes the float length.
            int springLength = (_parameters.Length + _parameters.Slack + SlackFudge) / (_physics.NodeCount - 1);

            _physics.ResetSpringLength(springLength);
        }

        /// <summary><c>RunRopeSimulation</c>: forget what touched, simulate, count what touched.</summary>
        private void RunRopeSimulation(float seconds)
        {
            Array.Clear(_linksTouching);

            _physics.Simulate(seconds, NodeForces, ApplyConstraints);

            _linksTouchingCount = 0;

            for (int index = 0; index < _physics.NodeCount; index++)
            {
                if (_linksTouching[index])
                {
                    ++_linksTouchingCount;
                }
            }
        }

        /// <summary><c>CPhysicsDelegate::GetNodeForces</c> (`c_rope.cpp:881-920`).</summary>
        private Vector3 NodeForces(int node)
        {
            // **ROPE_NO_GRAVITY leaves the engine's vector uninitialised**; zero here, which is what it is meant to be.
            Vector3 acceleration = (_parameters.Flags & NoGravityFlag) == 0 ? Gravity : Vector3.Zero;

            // `GetWindspeedAtTime` is zero without an `env_wind`, so the gust is the only wind (see the remarks).
            if (!_linksTouching[node] && _applyWind && _gustTimer < _gustLifetime)
            {
                float fraction = _gustTimer / _gustLifetime;
                float scale = 1f - MathF.Cos(fraction * MathF.PI);

                acceleration += _windDirection * scale;
            }

            // "Apply any instananeous forces and reset."
            acceleration += ImpulseScale * _impulse;
            _impulse *= ImpulseDecay;

            return acceleration;
        }

        /// <summary><c>CPhysicsDelegate::ApplyConstraints</c> (`c_rope.cpp:952-1031`): collide, then lock the ends.</summary>
        private void ApplyConstraints(RopeNode[] nodes, int count)
        {
            // `rope_collide` is 1, so only a ROPE_COLLIDE rope sweeps its nodes.
            if ((_parameters.Flags & CollideFlag) != 0)
            {
                Collide(nodes, count);
            }

            if ((_parameters.LockedPoints & LockStartPoint) != 0)
            {
                EndPointAttachment(0, out nodes[0].Position, out Vector3 forward);

                if ((_parameters.LockedPoints & LockStartDirection) != 0 && count > 3)
                {
                    LockNodeDirection(nodes, 0, 1, Math.Min(2, count - 2), forward);
                }
            }

            if ((_parameters.LockedPoints & LockEndPoint) != 0)
            {
                EndPointAttachment(1, out nodes[count - 1].Position, out Vector3 forward);

                if ((_parameters.LockedPoints & LockEndDirection) != 0 && count > 3)
                {
                    LockNodeDirection(nodes, count - 1, -1, Math.Min(2, count - 2), forward);
                }
            }
        }

        /// <summary>The world collision branch (`c_rope.cpp:958-1000`), ten sweeps a node at most.</summary>
        private void Collide(RopeNode[] nodes, int count)
        {
            const int Iterations = 10;
            const float SlowFactor = 0.3f;

            for (int index = 0; index < count; index++)
            {
                ref RopeNode node = ref nodes[index];
                int iteration;

                for (iteration = 0; iteration < Iterations; iteration++)
                {
                    RopeHit hit = Context.Collide(node.Previous, node.Position);

                    if (hit.Fraction >= 1f)
                    {
                        break;
                    }

                    if (hit.Fraction <= 0f || hit.Solid)
                    {
                        _linksTouching[index] = true;
                        node.Position = node.Previous;
                        break;
                    }

                    // "Apply some friction."
                    node.Position -= (node.Position - node.Previous) * SlowFactor;

                    // "Move it out along the face normal."
                    float behind = Vector3.Dot(hit.Normal, node.Position) - hit.Distance;
                    node.Position += hit.Normal * (-behind + 2.2f);
                    _linksTouching[index] = true;
                }

                if (iteration == Iterations)
                {
                    node.Position = node.Previous;
                }
            }
        }

        /// <summary><c>LockNodeDirection</c> (`c_rope.cpp:923-949`), walking from an end by <paramref name="parity"/>.</summary>
        private static void LockNodeDirection(RopeNode[] nodes, int from, int parity, int falloffNodes, Vector3 ideal)
        {
            float amount = LockAmount;

            for (int index = 0; index < falloffNodes; index++)
            {
                Vector3 first = nodes[from + (index * parity)].Position;
                ref Vector3 second = ref nodes[from + ((index + 1) * parity)].Position;

                Vector3 direction = second - first;
                float length = direction.Length();

                if (length > 0.0001f)
                {
                    direction /= length;
                    second = first + (Vector3.Lerp(direction, ideal, amount) * length);
                    amount *= LockFalloff;
                }
            }
        }

        /// <summary><c>ConstrainNodesBetweenEndpoints</c> (`c_rope.cpp:1363-1398`).</summary>
        private void ConstrainNodesBetweenEndpoints()
        {
            if (!_parameters.ConstrainBetweenEndpoints)
            {
                return;
            }

            Vector3 midpoint = (_cachedEnd[0] + _cachedEnd[1]) / 2f;
            Vector3 normal = midpoint - _cachedEnd[0];
            float normalLength = normal.Length();
            normal = normalLength > 0f ? normal / normalLength : Vector3.Zero;

            for (int index = 1; index < _physics.NodeCount - 1; index++)
            {
                ref RopeNode node = ref _physics.Nodes[index];

                node.Position = ConstrainNode(normal, node.Position, midpoint, normalLength);
                node.Predicted = ConstrainNode(normal, node.Predicted, midpoint, normalLength);
            }
        }

        /// <summary><c>ConstrainNode</c>: a node past an end is pulled back onto the plane through it.</summary>
        private static Vector3 ConstrainNode(Vector3 normal, Vector3 position, Vector3 midpoint, float normalLength)
        {
            Vector3 toNode = position - midpoint;
            Vector3 projected = Vector3.Dot(toNode, normal) * normal;
            float toNodeLength = toNode.Length();
            float projectedLength = projected.Length();

            toNode = toNodeLength > 0f ? toNode / toNodeLength : Vector3.Zero;

            // "See if it's past an endpoint".
            if (projectedLength < normalLength + 1f)
            {
                return position;
            }

            return midpoint + (toNode * toNodeLength * (normalLength / projectedLength));
        }

        /// <summary><c>DetectRestingState</c> (`c_rope.cpp:1603-1646`), which also decides whether the wind applies.</summary>
        private bool DetectRestingState()
        {
            _applyWind = false;

            if (_previousLockedPoints != _parameters.LockedPoints)
            {
                // "Force it to move the points for some number of frames when they get detached or after we get new data."
                _forcePointMoveCounter = 10;
                _previousLockedPoints = _parameters.LockedPoints;
                return false;
            }

            if (_newDataThisFrame)
            {
                return false;
            }

            if (DidEndPointMove(0) || DidEndPointMove(1))
            {
                return false;
            }

            Vector3 first = _physics.Nodes[0].Position;
            Vector3 last = _physics.Nodes[_physics.NodeCount - 1].Position;

            if ((_parameters.Flags & NoWindFlag) == 0)
            {
                // "Don't apply wind if more than half of the nodes are touching something."
                float distance = DistanceToSegment(Context.View.Origin, first, last);

                if (_linksTouchingCount < (_physics.NodeCount >> 1))
                {
                    _applyWind = distance < WindDistance;
                }
            }

            if (_previousImpulse != _impulse)
            {
                _previousImpulse = _impulse;
                return false;
            }

            return !AnyPointsMoved() && !_applyWind;
        }

        /// <summary><c>AnyPointsMoved</c>: a node moved more than √0.03, or the forced count has not run out.</summary>
        private bool AnyPointsMoved()
        {
            for (int index = 0; index < _physics.NodeCount; index++)
            {
                RopeNode node = _physics.Nodes[index];

                if ((node.Position - node.Previous).LengthSquared() > 0.03f)
                {
                    return true;
                }
            }

            return --_forcePointMoveCounter > 0;
        }

        /// <summary><c>DidEndPointMove</c> (`c_rope.cpp:1580-1600`).</summary>
        private bool DidEndPointMove(int end)
        {
            // "If this point isn't locked anyway, just break out."
            if ((_parameters.LockedPoints & (1 << end)) == 0)
            {
                return false;
            }

            bool wasValid = _previousEndValid[end];
            Vector3 was = _previousEnd[end];

            _previousEndValid[end] = EndPointPosition(end, out _previousEnd[end]);

            if (!wasValid && !_previousEndValid[end])
            {
                return true;
            }

            // `VectorsAreEqual( vOld, vNew, 0.1 )`: each component within a tenth.
            Vector3 change = Vector3.Abs(was - _previousEnd[end]);

            return change.X > 0.1f || change.Y > 0.1f || change.Z > 0.1f;
        }

        /// <summary><c>GetEndPointPos</c>: the cached ends, recomputed once a frame; always true.</summary>
        private bool EndPointPosition(int end, out Vector3 position)
        {
            Refresh();
            position = _cachedEnd[end];
            return true;
        }

        /// <summary><c>GetEndPointAttachment</c>: the cached end and the forward of its angles.</summary>
        private void EndPointAttachment(int end, out Vector3 position, out Vector3 forward)
        {
            Refresh();
            position = _cachedEnd[end];
            forward = _cachedForward[end];
        }

        /// <summary>Both ends through <c>CalculateEndPointAttachment</c>, once per frame; a missing entity keeps its last.</summary>
        private void Refresh()
        {
            if (!_endPointsDirty)
            {
                return;
            }

            bool weapon = (_parameters.Flags & PlayerWeaponFlag) != 0;

            if (Context.EndPoints(_parameters.StartPoint, _parameters.StartAttachment, weapon) is { } start)
            {
                (_cachedEnd[0], _cachedForward[0]) = (start.Position, start.Forward);
            }

            if (Context.EndPoints(_parameters.EndPoint, _parameters.EndAttachment, weapon) is { } end)
            {
                (_cachedEnd[1], _cachedForward[1]) = (end.Position, end.Forward);
            }

            _endPointsDirty = false;
        }

        /// <summary><c>CalcLightValues</c> with <c>rope_averagelight</c> 1: the engine's cube average at each node.</summary>
        private void CalcLightValues()
        {
            for (int index = 0; index < _physics.NodeCount; index++)
            {
                _lightValues[index] = Context.Lighting(_physics.Nodes[index].Predicted);
            }
        }

        /// <summary>
        /// <c>Catmull_Rom_Spline_Matrix</c> then <c>Catmull_Rom_Eval</c> (`c_rope.cpp:1658-1674`): the basis baked from four
        /// points, evaluated at a precomputed <c>( t, t², t³ )</c>.
        /// </summary>
        internal static Vector3 CatmullRom(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4, Vector3 t)
        {
            Vector3 cubic = 0.5f * ((-1f * p1) + (3f * p2) + (-3f * p3) + p4);
            Vector3 square = 0.5f * ((2f * p1) + (-5f * p2) + (4f * p3) - p4);
            Vector3 linear = 0.5f * ((-1f * p1) + p3);

            return p2 + (t.X * linear) + (t.Y * square) + (t.Z * cubic);
        }

        /// <summary><c>CalcDistanceToLineSegment</c>: from a point to the nearest point of a segment.</summary>
        private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 along = end - start;
            float lengthSquared = along.LengthSquared();
            float t = lengthSquared < 1e-10f ? 0f : Math.Clamp(Vector3.Dot(point - start, along) / lengthSquared, 0f, 1f);

            return Vector3.Distance(point, start + (along * t));
        }

        /// <summary>
        /// A linear light value as the Cable shader writes it: it reads its texture as sRGB and writes sRGB
        /// (`cable_dx9.cpp:68`, `:93`), so a light multiplied in linear space is the gamma of that light here.
        /// </summary>
        private static Vector3 LinearToGamma(Vector3 linear) => new(
            MathF.Pow(Math.Max(linear.X, 0f), 1f / 2.2f),
            MathF.Pow(Math.Max(linear.Y, 0f), 1f / 2.2f),
            MathF.Pow(Math.Max(linear.Z, 0f), 1f / 2.2f));
    }
}
