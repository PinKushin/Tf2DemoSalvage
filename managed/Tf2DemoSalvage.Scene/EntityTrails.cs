using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Every <c>CSpriteTrail</c> in a moment, sampled and drawn as the client does — <c>UpdateTrail</c> from
/// <c>ClientThink</c>, then <c>DrawModel</c> (`SpriteTrail.cpp:379-531`).
/// </summary>
/// <remarks>
/// **The trail is client state, built frame by frame from where the entity is drawn.** No point of it is on the wire:
/// each frame <c>UpdateTrail</c> appends the render origin when it has moved more than two units and the sample interval
/// (<c>lifetime / 256</c>) has passed, and <c>DrawModel</c> strings the points and the current head through
/// <c>CBeamSegDraw</c>, fading each by its remaining life. So this class keeps a ring of points per trail across calls,
/// the way <c>m_vecSteps</c> does, and a trail's shape depends on the frames it was sampled at — as TF2's does.
///
/// **Two rules are the viewer's, not the engine's, and both exist because a demo here can be seeked.** A trail not
/// offered in a frame is forgotten, which is what deleting the entity does in the engine; and a trail whose clock went
/// backwards, or jumped further than its lifetime, starts empty. The engine never sees either: its demo player only
/// plays forwards, one frame at a time, so every point it holds was sampled within a lifetime of now. Without the
/// second rule a forward seek would draw one frame of a strip fading from the old place to the new.
///
/// **A material the particle pass cannot draw is skipped, counted, and still sampled.** <c>effects/beam001_*</c> — the
/// most common trail in real matches — is a <c>Refract</c> material, which needs the frame behind it; this pass draws
/// <c>Sprite</c> and <c>UnlitGeneric</c> materials (B476).
/// </remarks>
public sealed class EntityTrails
{
    /// <summary><c>MAX_SPRITE_TRAIL_POINTS</c> (`SpriteTrail.h:85`), a power of two so the ring index masks.</summary>
    internal const int MaximumPoints = 256;

    /// <summary>The ring's index mask, <c>MAX_SPRITE_TRAIL_MASK</c>.</summary>
    private const int PointMask = MaximumPoints - 1;

    /// <summary>A new point needs the head to have moved MORE than this, squared: <c>DistToSqr( … ) &gt; 4.0f</c> (`:388`).</summary>
    private const float MinimumStepSquared = 4f;

    private readonly Dictionary<int, Trail> _trails = [];
    private readonly HashSet<int> _offered = [];
    private readonly List<int> _forgotten = [];
    private readonly UniformRandomStream _random = new();
    private readonly List<BeamSegment> _segments = [];
    private readonly List<DetailSpriteVertex> _corners = [];
    private readonly SpriteStripBatches _strips = new();

    /// <summary>How many trails the last build drew.</summary>
    public int Drawn { get; private set; }

    /// <summary>How many it sampled and did not draw — no material, one this pass cannot draw, or <c>kRenderNone</c>.</summary>
    public int Skipped { get; private set; }

    /// <summary>Samples and draws this moment's trails.</summary>
    /// <param name="props">What the scene is drawing; only props carrying a <see cref="SceneSpriteTrail"/> are read.</param>
    /// <param name="camera">Where the camera is — <c>CBeamSegDraw</c> turns the strip to face it.</param>
    /// <param name="sprites">Each sprite material as loaded, keyed by model path.</param>
    /// <param name="renderOrigin">
    /// <c>CSpriteTrail::GetRenderOrigin</c> for a prop: its attached entity's attachment, else that entity's origin,
    /// else the trail's own place (`SpriteTrail.cpp:537-553`) — or null when the entity it hangs from is not in this
    /// moment, which holds the head where it last was (see the remarks).
    /// </param>
    /// <param name="currentTime"><c>gpGlobals-&gt;curtime</c>, in seconds.</param>
    /// <returns>One batch per material and pass.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public IReadOnlyList<ParticleBatch> Build(
        IReadOnlyList<SceneProp> props,
        Vector3 camera,
        IReadOnlyDictionary<string, EngineSprite> sprites,
        Func<SceneProp, Vector3?> renderOrigin,
        float currentTime)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentNullException.ThrowIfNull(renderOrigin);

        _strips.Clear();
        _offered.Clear();
        Drawn = 0;
        Skipped = 0;

        foreach (SceneProp prop in props)
        {
            if (prop.Pose.SpriteTrail is not { } parameters)
            {
                continue;
            }

            _offered.Add(prop.EntityIndex);

            Trail trail = TrailFor(prop, parameters, currentTime);

            // A head with nothing to hang from stays where it was: the client still holds the entity the trail named —
            // dormant, or unlinked at its last place — so its origin stops moving and the ring fades out behind it.
            if ((renderOrigin(prop) ?? trail.Head) is not { } head)
            {
                Skipped++;
                continue;
            }

            trail.Head = head;

            // `ClientThink` runs whether or not the entity draws, so the sample comes before every reason not to.
            Update(trail, parameters, head, currentTime);

            if (Draw(trail, prop, parameters, head, camera, sprites, currentTime))
            {
                Drawn++;
            }
            else
            {
                Skipped++;
            }
        }

        Forget();

        return _strips.Batches(sprites);
    }

    /// <summary>This entity's ring, new when it was not offered last frame, changed material, or its clock jumped.</summary>
    private Trail TrailFor(SceneProp prop, SceneSpriteTrail parameters, float currentTime)
    {
        if (!_trails.TryGetValue(prop.EntityIndex, out Trail? trail) ||
            !string.Equals(trail.ModelPath, prop.ModelPath, StringComparison.Ordinal))
        {
            trail = new Trail(prop.ModelPath);
            _trails[prop.EntityIndex] = trail;
        }
        else if (currentTime < trail.LastTime || currentTime - trail.LastTime > parameters.LifeTime)
        {
            // A seek: see the remarks. Every point the old ring held is either in the future or dead.
            trail.Clear();
        }

        trail.LastTime = currentTime;

        return trail;
    }

    /// <summary>Drops every ring whose entity was not offered this frame.</summary>
    private void Forget()
    {
        _forgotten.Clear();

        foreach (int entity in _trails.Keys)
        {
            if (!_offered.Contains(entity))
            {
                _forgotten.Add(entity);
            }
        }

        foreach (int entity in _forgotten)
        {
            _trails.Remove(entity);
        }
    }

    /// <summary><c>CSpriteTrail::UpdateTrail</c> (`SpriteTrail.cpp:379-416`).</summary>
    private void Update(Trail trail, SceneSpriteTrail parameters, Vector3 head, float currentTime)
    {
        // "Can't update too quickly".
        if (trail.UpdateTime > currentTime)
        {
            return;
        }

        TrailPoint? last = trail.StepCount != 0 ? trail.Point(trail.StepCount - 1) : null;

        if (last is not { } previous || Vector3.DistanceSquared(previous.Position, head) > MinimumStepSquared)
        {
            // "If we're over our limit, steal the last point and put it up front".
            if (trail.StepCount >= MaximumPoints)
            {
                --trail.StepCount;
                ++trail.FirstStep;
            }

            float variance = _random.RandomFloat(-parameters.StartWidthVariance, parameters.StartWidthVariance);

            trail.Set(
                trail.StepCount,
                new TrailPoint(
                    head,
                    currentTime + parameters.LifeTime,
                    last is { } before
                        ? before.TexCoord + (Vector3.Distance(before.Position, head) * parameters.TextureRes)
                        : 0f,
                    variance));

            ++trail.StepCount;
        }

        // "Don't update again for a bit".
        trail.UpdateTime = currentTime + (parameters.LifeTime / MaximumPoints);
    }

    /// <summary><c>CSpriteTrail::DrawModel</c> (`SpriteTrail.cpp:422-531`), dying points and all.</summary>
    /// <returns>Whether anything was emitted.</returns>
    private bool Draw(
        Trail trail,
        SceneProp prop,
        SceneSpriteTrail parameters,
        Vector3 head,
        Vector3 camera,
        IReadOnlyDictionary<string, EngineSprite> sprites,
        float currentTime)
    {
        // "Must have at least one point".
        if (trail.StepCount < 1)
        {
            return false;
        }

        // `IsVisible()` — `ShouldDraw` refuses `kRenderNone` — then `Draw_SetSpriteTexture`, which finds no sprite for a
        // model that never loaded.
        if (prop.Pose.RenderMode == RenderModes.None ||
            !sprites.TryGetValue(prop.ModelPath, out EngineSprite sprite) ||
            sprite.Material.Sheet is null ||
            !Drawable(sprite))
        {
            return false;
        }

        // "Setup the first point, always emanating from the attachment point".
        TrailPoint newest = trail.Point(trail.StepCount - 1);
        TrailPoint current = new(
            head,
            currentTime + parameters.LifeTime,
            newest.TexCoord + (Vector3.Distance(head, newest.Position) * parameters.TextureRes),
            0f);

        (byte red, byte green, byte blue) = prop.Pose.RenderColor;
        Vector3 colour = new(red / 255f, green / 255f, blue / 255f);
        float brightness = (prop.Pose.Sprite ?? SceneSprite.Default).Brightness / 255f;

        _segments.Clear();

        TrailPoint? previous = null;
        float tailAlphaDistance = parameters.MinFadeLength;

        // The engine's `for` decrements its own counter to stay on a removed point's index; a `while` that steps only past
        // a live point is the same walk.
        int index = 0;

        while (index <= trail.StepCount)
        {
            // "This makes it so that we're always drawing to the current location".
            TrailPoint point = index != trail.StepCount ? trail.Point(index) : current;

            float lifePercent = Math.Clamp((point.DieTime - currentTime) / parameters.LifeTime, 0f, 1f);
            float alphaFade = lifePercent;

            if (tailAlphaDistance > 0f)
            {
                if (previous is { } prior)
                {
                    tailAlphaDistance -= Vector3.Distance(point.Position, prior.Position);
                }

                if (tailAlphaDistance > 0f)
                {
                    float tailFade = Lerp(
                        (parameters.MinFadeLength - tailAlphaDistance) / parameters.MinFadeLength, 0f, 1f);

                    if (tailFade < alphaFade)
                    {
                        alphaFade = tailFade;
                    }
                }
            }

            float width = parameters.EndWidth >= 0f
                ? Lerp(lifePercent, parameters.EndWidth, parameters.StartWidth)
                : parameters.StartWidth;

            width += point.WidthVariance;

            if (width < 0f)
            {
                width = 0f;
            }

            _segments.Add(new BeamSegment(point.Position, colour, point.TexCoord, width, brightness * alphaFade));

            // "See if we're done with this bad boy": drawn, then pushed off the front of the ring.
            if (point.DieTime <= currentTime)
            {
                ++trail.FirstStep;
                --trail.StepCount;
            }
            else
            {
                ++index;
            }

            previous = point;
        }

        _corners.Clear();
        BeamSegDraw.Draw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_segments), camera, _corners);

        int before = _strips.Corners;

        _strips.Add(prop.ModelPath, sprite, prop.Pose.RenderMode, _corners);

        return _strips.Corners > before;
    }

    /// <summary>Whether the particle pass can draw this material's shader: <c>Sprite</c> and <c>UnlitGeneric</c>.</summary>
    private static bool Drawable(EngineSprite sprite) =>
        sprite.IsSpriteShader || sprite.Shader.StartsWith("UnlitGeneric", StringComparison.OrdinalIgnoreCase);

    /// <summary>Source's <c>Lerp( t, a, b )</c>: <c>a + ( b − a ) · t</c>.</summary>
    private static float Lerp(float t, float from, float to) => from + ((to - from) * t);

    /// <summary><c>TrailPoint_t</c> (`SpriteTrail.h:23-31`).</summary>
    private readonly record struct TrailPoint(Vector3 Position, float DieTime, float TexCoord, float WidthVariance);

    /// <summary>One trail's ring — <c>m_vecSteps</c>, <c>m_nFirstStep</c>, <c>m_nStepCount</c> and <c>m_flUpdateTime</c>.</summary>
    /// <param name="modelPath">The material the ring was sampled for.</param>
    private sealed class Trail(string modelPath)
    {
        private readonly TrailPoint[] _steps = new TrailPoint[MaximumPoints];

        public string ModelPath { get; } = modelPath;

        public int FirstStep { get; set; }

        public int StepCount { get; set; }

        /// <summary><c>m_flUpdateTime</c>; zero for a new entity, whose memory the client allocator clears.</summary>
        public float UpdateTime { get; set; }

        /// <summary>The clock at the last build that offered this trail — the seek rule's, not the engine's.</summary>
        public float LastTime { get; set; } = float.NegativeInfinity;

        /// <summary>Where the head was last placed, held while the entity it hangs from is missing.</summary>
        public Vector3? Head { get; set; }

        /// <summary><c>GetTrailPoint( n )</c>: <c>( n + m_nFirstStep ) &amp; MAX_SPRITE_TRAIL_MASK</c> (`:322`).</summary>
        public TrailPoint Point(int n) => _steps[(n + FirstStep) & PointMask];

        public void Set(int n, TrailPoint point) => _steps[(n + FirstStep) & PointMask] = point;

        public void Clear()
        {
            FirstStep = 0;
            StepCount = 0;
            UpdateTime = 0f;
            Head = null;
        }
    }
}
