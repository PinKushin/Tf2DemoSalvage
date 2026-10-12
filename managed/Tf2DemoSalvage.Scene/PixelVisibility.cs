using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>What a glow asks the occlusion system — <c>pixelvis_queryparams_t</c> and the handle's owner.</summary>
/// <param name="Entity">The entity whose <c>m_queryHandle</c> this is.</param>
/// <param name="Position">The glow's origin.</param>
/// <param name="ProxySize">The proxy's radius, in world units.</param>
/// <param name="ProxyAspect">Its width over its height.</param>
public readonly record struct GlowQuery(int Entity, Vector3 Position, float ProxySize, float ProxyAspect);

/// <summary>One occlusion proxy to count pixels through: a quad, corners in order around it.</summary>
/// <param name="Key">The entity it answers for.</param>
/// <param name="First">Up and left.</param>
/// <param name="Second">Up and right.</param>
/// <param name="Third">Down and right.</param>
/// <param name="Fourth">Down and left.</param>
public readonly record struct PixelVisibilityProxy(int Key, Vector3 First, Vector3 Second, Vector3 Third, Vector3 Fourth);

/// <summary>The main view as the proxies are built in it.</summary>
/// <param name="Origin"><c>CurrentViewOrigin()</c>.</param>
/// <param name="Right"><c>CurrentViewRight()</c>.</param>
/// <param name="Up"><c>CurrentViewUp()</c>.</param>
/// <param name="Matrix">World to clip, row major, vectors as rows — <see cref="FreeCamera.ToMatrix"/>.</param>
/// <param name="Screen">The same view as <c>ComputePixelDiameterOfSphere</c> projects through it.</param>
public sealed record PixelVisibilityView(
    Vector3 Origin, Vector3 Right, Vector3 Up, IReadOnlyList<float> Matrix, ScreenFadeView Screen)
{
    /// <summary>The view a camera draws.</summary>
    /// <param name="camera">The camera.</param>
    /// <param name="viewportHeight">The viewport's height in pixels.</param>
    /// <returns>The view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="camera"/> is null.</exception>
    public static PixelVisibilityView From(FreeCamera camera, float viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(camera);

        (_, (float X, float Y, float Z) right, (float X, float Y, float Z) up) = camera.Basis();

        return new PixelVisibilityView(
            new Vector3(camera.Origin.X, camera.Origin.Y, camera.Origin.Z),
            new Vector3(right.X, right.Y, right.Z),
            new Vector3(up.X, up.Y, up.Z),
            camera.ToMatrix(),
            camera.ScreenView(viewportHeight));
    }
}

/// <summary>
/// <c>CPixelVisibilitySystem</c> (<c>game/client/c_pixel_visibility.cpp</c>): how much of a glow's proxy the scene's
/// depth lets through, per entity, a frame behind and approached at a rate (B378, B521).
/// </summary>
/// <remarks>
/// *Read from published source.* The engine draws each proxy twice inside occlusion queries at the end of the view —
/// once in a depth range nothing can hide, once against the scene — and the next frame's
/// <c>PixelVisibility_FractionVisible</c> reads the two counts. This is the half that needs no device: which proxies,
/// where, and what the counts become. The renderer draws <see cref="EndView"/>'s quads and hands the counts to
/// <see cref="BeginFrame"/>.
///
/// **One view.** The engine keeps a query per set per view ID; this viewer asks from the main view only, so a set
/// is its query.
///
/// **The frame time is the caller's, and the engine's is zero while paused** — a paused TF2 holds every glow where the
/// pause found it. A viewer whose camera flies while paused passes the frame's real duration instead; D-numbered
/// nowhere, named in `docs/RISKS.md` B378.
/// </remarks>
public sealed class PixelVisibility
{
    /// <summary><c>PIXELVIS_DEFAULT_PROXY_SIZE</c> (<c>c_pixel_visibility.h:14</c>).</summary>
    public const float DefaultProxySize = 2f;

    /// <summary><c>PIXELVIS_DEFAULT_FADE_TIME</c> (<c>c_pixel_visibility.h:15</c>).</summary>
    public const float DefaultFadeTime = 0.0625f;

    /// <summary><c>MIN_PROXY_PIXELS</c>, off the Xbox (<c>c_pixel_visibility.cpp:47</c>).</summary>
    private const float MinimumProxyPixels = 5f;

    /// <summary>The engine's own constant, which is the reciprocal of the root it is named for.</summary>
    private const float Sqrt2 = 0.707106781f;

    private readonly Dictionary<int, Set> _sets = [];
    private readonly List<int> _order = [];
    private readonly List<PixelVisibilityProxy> _proxies = [];
    private Func<int, (int Pixels, int Possible)?> _results = static _ => null;
    private Func<Vector3, bool> _sight = static _ => true;
    private float _frameTime;
    private int _frame;

    /// <summary><c>m_hwCanTestGlows</c>: <c>r_dopixelvisibility</c> as the level's load found it.</summary>
    private bool _hardwareCanTestGlows = true;

    /// <summary>What is keeping <see cref="Settled"/> false, for the log of a capture that is waiting on it.</summary>
    /// <returns>One clause a set, carried from the fields <see cref="Settled"/> reads; empty when settled.</returns>
    public string Unsettled()
    {
        System.Text.StringBuilder report = new();

        foreach ((int entity, Set set) in _sets)
        {
            if (set.Issued && (!set.HasResults || Differs(set.Brightness, set.Goal)))
            {
                report.Append(System.Globalization.CultureInfo.InvariantCulture, $"entity {entity}: brightness {set.Brightness:0.####} toward {set.Goal:0.####}, results {set.HasResults}, failed {set.Failed}, asked at frame {set.Asked} of {_frame}, frame time {_frameTime:0.#####}; ");
            }
        }

        return report.ToString();
    }

    /// <summary><c>r_dopixelvisibility</c> as it is now — <c>PixelVisibility_IsAvailable()</c>'s reading.</summary>
    public bool DoPixelVisibility { get; set; } = true;

    /// <summary><c>LevelShutdownPostEntity</c> then <c>LevelInitPreEntity</c> (`c_pixel_visibility.cpp:517-563`).</summary>
    /// <param name="doPixelVisibility"><c>r_dopixelvisibility</c> at the moment the level loads.</param>
    /// <remarks>
    /// The engine also requires fast queries and DirectX 8; this renderer has both. With the switch off every glow's
    /// fraction is <see cref="GlowSight"/>'s line of sight for the whole level, whatever the cvar does afterwards.
    /// </remarks>
    public void LevelInit(bool doPixelVisibility)
    {
        _sets.Clear();
        _order.Clear();
        _proxies.Clear();
        DoPixelVisibility = doPixelVisibility;
        _hardwareCanTestGlows = doPixelVisibility;
    }

    /// <summary>Whether every glow asked about has reached what its query last counted — a still is ready.</summary>
    public bool Settled
    {
        get
        {
            foreach (Set set in _sets.Values)
            {
                if (set.Issued && (!set.HasResults || Differs(set.Brightness, set.Goal)))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>A beam halo's proxy size — "HACKHACK: heuristic to estimate proxy size" (<c>view_beams.cpp:2266-2271</c>).</summary>
    /// <param name="haloScale">The beam entity's <c>m_fHaloScale</c>.</param>
    /// <param name="width">Its <c>m_fWidth</c>.</param>
    /// <param name="endWidth">Its <c>m_fEndWidth</c>.</param>
    /// <returns><c>clamp( 1 + haloScale * width / endWidth, 1, 8 )</c>.</returns>
    public static float HaloProxySize(float haloScale, float width, float endWidth) =>
        Math.Clamp(1f + (haloScale * width / endWidth), 1f, 8f);

    /// <summary>What the two counts ask the brightness to become (<c>c_pixel_visibility.cpp:349-360</c>).</summary>
    /// <param name="pixels">Proxy pixels that passed the depth test.</param>
    /// <param name="possible">Proxy pixels on screen.</param>
    /// <returns>Their ratio, 1 from 0.95, 0 when nothing was possible.</returns>
    public static float Target(int pixels, int possible)
    {
        if (possible <= 0)
        {
            return 0f;
        }

        float target = pixels / (float)possible;

        if (target >= 0.95f)
        {
            return 1f;
        }

        return target < 0f ? 0f : target;
    }

    /// <summary><c>PixelVisibility_DrawProxy</c>'s geometry and its return (<c>c_pixel_visibility.cpp:52-166</c>).</summary>
    /// <param name="query">The glow.</param>
    /// <param name="view">The view.</param>
    /// <returns>The quad and the share of it inside the frustum; null where a corner is behind the eye.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is null.</exception>
    /// <remarks>
    /// The engine draws a pyramid whose apex it has just collapsed onto its base — "collapses the pyramid to a plane -
    /// so this could be a quad instead" — so the quad is the whole of it. <c>bSizeInScreenspace</c> is not modeled:
    /// neither a sprite nor a beam sets it.
    /// </remarks>
    public static (PixelVisibilityProxy Proxy, float Clip)? Proxy(in GlowQuery query, PixelVisibilityView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        Vector3 origin = query.Position;
        float scale = query.ProxySize;

        // "don't expand this with distance to fit pixels or the sprite will poke through".
        float forwardScale = scale;

        float pixelsPerUnit = Math.Max(
            view.Screen.PixelWidthOfSphere((origin.X, origin.Y, origin.Z), 1f) * 0.5f, 1e-4f);

        if (scale * pixelsPerUnit < MinimumProxyPixels)
        {
            scale = MinimumProxyPixels / pixelsPerUnit;
        }

        origin -= SafeNormalize(origin - view.Origin) * forwardScale;

        scale *= Sqrt2;

        Vector3 across = view.Right * scale;
        Vector3 along = view.Up * (scale / query.ProxyAspect);

        PixelVisibilityProxy proxy = new(
            query.Entity, origin + along - across, origin + along + across, origin - along + across, origin - along - across);

        if (Screen(proxy.First, view.Matrix) is not { } first ||
            Screen(proxy.Second, view.Matrix) is not { } second ||
            Screen(proxy.Third, view.Matrix) is null ||
            Screen(proxy.Fourth, view.Matrix) is not { } fourth)
        {
            return null;
        }

        float width = second.X - first.X;
        float height = first.Y - fourth.Y;
        float widthOnScreen = Math.Min(1f, second.X) - Math.Max(-1f, first.X);
        float heightOnScreen = Math.Min(1f, first.Y) - Math.Max(-1f, fourth.Y);
        float area = width * height;

        // "area can be zero when we ALT-TAB".
        float ratio = area != 0f ? Math.Clamp(widthOnScreen * heightOnScreen / area, 0f, 1f) : 0f;

        return (proxy, ratio);
    }

    /// <summary>Starts a frame: the counts the last frame's queries came back with, and how long this one is.</summary>
    /// <param name="frameTime"><c>gpGlobals-&gt;frametime</c>.</param>
    /// <param name="results">The two counts for an entity's proxy, or null where the renderer has none.</param>
    /// <param name="sight">
    /// Whether the view reaches a point — <c>GlowSightDistance( position, true ) &gt; 0</c>, asked only when the level
    /// loaded with <c>r_dopixelvisibility 0</c>. Everything is in sight when omitted.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="results"/> is null.</exception>
    /// <remarks>
    /// Also the last frame's <c>PixelVisibility_EndScene</c>: a set nobody asked about for more than a frame is freed
    /// (<c>CPixelVisSet::IsActive</c>, <c>DeleteUnusedSets</c>), so its glow starts from zero when it is next seen.
    /// </remarks>
    public void BeginFrame(
        float frameTime, Func<int, (int Pixels, int Possible)?> results, Func<Vector3, bool>? sight = null)
    {
        ArgumentNullException.ThrowIfNull(results);

        _sight = sight ?? (static _ => true);

        for (int at = _order.Count - 1; at >= 0; at--)
        {
            if (_frame - _sets[_order[at]].Asked > 1)
            {
                _sets.Remove(_order[at]);
                _order.RemoveAt(at);
            }
        }

        _frame++;
        _frameTime = frameTime;
        _results = results;
    }

    /// <summary><c>PixelVisibility_FractionVisible</c> with a query handle.</summary>
    /// <param name="query">The glow.</param>
    /// <param name="fadeTime">Seconds from hidden to seen; zero or less is the engine's eighth of a second.</param>
    /// <returns><c>m_brightnessTarget * m_clipFraction</c>.</returns>
    public float FractionVisible(in GlowQuery query, float fadeTime = DefaultFadeTime)
    {
        // `if ( !m_hwCanTestGlows || building_cubemaps.GetBool() ) return GlowSightDistance( … ) > 0 ? 1.0f : 0.0f;`
        if (!_hardwareCanTestGlows)
        {
            return _sight(query.Position) ? 1f : 0f;
        }

        if (!_sets.TryGetValue(query.Entity, out Set? set) ||
            Differs(set.ProxySize, query.ProxySize) || Differs(set.ProxyAspect, query.ProxyAspect))
        {
            // `CPixelVisSet::Init`. A different size under the same index is another entity in a reused slot.
            if (set is null)
            {
                _order.Add(query.Entity);
            }

            set = new Set
            {
                ProxySize = query.ProxySize,
                ProxyAspect = query.ProxyAspect,
                FadeTimeInverse = fadeTime > 0f ? 1f / fadeTime : 1f / 0.125f,
            };

            _sets[query.Entity] = set;
        }

        set.Origin = query.Position;
        set.Asked = _frame;

        if (!set.Queried)
        {
            set.Queried = true;

            // `pixels < 0 || pixelsPossible < 0`: never issued, or the renderer has nothing for it.
            if (!set.Issued || _results(query.Entity) is not { } counted)
            {
                set.Failed = set.Issued;

                return set.Brightness * set.Clip;
            }

            set.HasResults = true;
            set.Goal = Target(counted.Pixels, counted.Possible);

            // "fade in / out". Nothing possible is not approached: `m_brightnessTarget = 0.0f`.
            set.Brightness = counted.Possible > 0
                ? Approach(set.Goal, set.Brightness, _frameTime * set.FadeTimeInverse)
                : 0f;
        }

        return set.Brightness * set.Clip;
    }

    /// <summary><c>PixelVisibility_EndCurrentView</c>: the proxies to count through, for every set still alive.</summary>
    /// <param name="view">The view just drawn.</param>
    /// <returns>The quads; the list is reused by the next call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is null.</exception>
    public IReadOnlyList<PixelVisibilityProxy> EndView(PixelVisibilityView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        _proxies.Clear();

        // `if ( !PixelVisibility_IsAvailable() && CurrentViewID() >= 0 ) return;` — the cvar as it is NOW.
        if (!DoPixelVisibility || !_hardwareCanTestGlows)
        {
            return _proxies;
        }

        foreach (int entity in _order)
        {
            Set set = _sets[entity];

            // `IssueQuery`: a query that failed last frame is not drawn this one, and is then tried again.
            if (!set.Failed)
            {
                if (Proxy(new GlowQuery(entity, set.Origin, set.ProxySize, set.ProxyAspect), view) is not { } built)
                {
                    // "In this case, the proxy wasn't issued cause it was offscreen".
                    set.Clip = 0f;
                    set.Queried = false;

                    continue;
                }

                set.Clip = built.Clip;
                _proxies.Add(built.Proxy);
            }

            set.Issued = true;
            set.Queried = false;
            set.Failed = false;
        }

        return _proxies;
    }

    /// <summary><c>Approach</c> (<c>mathlib/mathlib_base.cpp</c>): toward the target by at most the speed.</summary>
    private static float Approach(float target, float value, float speed)
    {
        float delta = target - value;

        if (delta > speed)
        {
            return value + speed;
        }

        return delta < -speed ? value - speed : target;
    }

    /// <summary><c>ScreenTransform</c>: clip x and y over w, or nothing when w is under 0.001 — "behind".</summary>
    private static Vector2? Screen(Vector3 point, IReadOnlyList<float> matrix)
    {
        float w = (point.X * matrix[3]) + (point.Y * matrix[7]) + (point.Z * matrix[11]) + matrix[15];

        if (w < 0.001f)
        {
            return null;
        }

        return new Vector2(
            ((point.X * matrix[0]) + (point.Y * matrix[4]) + (point.Z * matrix[8]) + matrix[12]) / w,
            ((point.X * matrix[1]) + (point.Y * matrix[5]) + (point.Z * matrix[9]) + matrix[13]) / w);
    }

    /// <summary>Exactly unequal: an approach lands ON its target, and a proxy's size is copied, never computed.</summary>
    private static bool Differs(float first, float second) => MathF.Abs(first - second) > 0f;

    private static Vector3 SafeNormalize(Vector3 vector)
    {
        float length = vector.Length();

        return length > 0f ? vector / length : vector;
    }

    /// <summary><c>CPixelVisSet</c> and its one <c>CPixelVisibilityQuery</c>.</summary>
    private sealed class Set
    {
        public float ProxySize;
        public float ProxyAspect;
        public float FadeTimeInverse;
        public Vector3 Origin;
        public int Asked;
        public float Brightness;
        public float Goal;
        public float Clip = 1f;
        public bool Issued;
        public bool Queried;
        public bool Failed;
        public bool HasResults;
    }
}
