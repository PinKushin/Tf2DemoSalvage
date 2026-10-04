using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Where a beam end is when it hangs off an entity — <c>ComputeBeamEntPosition</c> (`view_beams.cpp:218`).
/// </summary>
/// <param name="handle">The raw <c>EHANDLE</c>, serial included; the caller dereferences it as the client would.</param>
/// <param name="attachment">The attachment number, or a hitbox number when <paramref name="hitboxes"/> is set.</param>
/// <param name="hitboxes"><c>FBEAM_USE_HITBOXES</c>.</param>
/// <returns>The point, or null when the handle names nothing — the engine's <c>return false</c>.</returns>
/// <remarks>
/// **Three answers in the engine's order**: the attachment (or the point on the hitbox nearest the main view), then a
/// player's <c>WorldSpaceCenter()</c> — *"Player origins are at their feet"* — then anything else's render origin. An
/// attachment that does not exist falls through to the last two; only a missing entity answers null.
/// </remarks>
public delegate Vector3? BeamEntityPosition(int handle, int attachment, bool hitboxes);

/// <summary>
/// Every <c>CBeam</c> in a moment, as the batches the renderer draws — <c>CViewRenderBeams::DrawBeam( C_Beam* )</c> and
/// what it calls.
/// </summary>
/// <remarks>
/// **A fresh <c>Beam_t</c> per beam per frame, as the engine builds one** (`view_beams.cpp:2126-2358`): the entity's
/// fields are copied into a <c>BeamInfo_t</c>, <c>SetupBeam</c> and <c>SetBeamAttributes</c> fill the beam, the entity
/// type is mapped onto a temp-entity type, <c>UpdateBeam</c> places its ends and generates its noise, and
/// <c>DrawBeam( Beam_t* )</c> draws it. Nothing carries from one frame to the next except <c>beamRandom</c>'s stream.
///
/// **What the corpus exercises**: every one of its 18,942 beams is a <c>point_spotlight</c> shaft — a
/// <c>BEAM_POINTS</c> with a halo, so <see cref="DrawBeamWithHalo"/> draws it as a two-segment strip of constant width
/// plus a glow at the lamp. The laser, spline and entity-end branches are ported for the beams the corpus does not
/// carry.
///
/// **One thing the engine has and this does not: the halo's occlusion query.** <c>PixelVisibility_FractionVisible</c>
/// is a GPU query that fades the glow as the lamp goes behind something; this asks the same line-of-sight trace the
/// entity sprites do (<see cref="GlowSight"/>) and so pops where TF2 dissolves — B378's open half, shared.
/// </remarks>
public sealed class EntityBeams
{
    /// <summary><c>BEAM_POINTS</c>.</summary>
    public const int BeamPoints = 0;

    /// <summary><c>BEAM_ENTPOINT</c>.</summary>
    public const int BeamEntPoint = 1;

    /// <summary><c>BEAM_ENTS</c>.</summary>
    public const int BeamEnts = 2;

    /// <summary><c>BEAM_HOSE</c>.</summary>
    public const int BeamHose = 3;

    /// <summary><c>BEAM_SPLINE</c>.</summary>
    public const int BeamSpline = 4;

    /// <summary><c>BEAM_LASER</c>.</summary>
    public const int BeamLaser = 5;

    /// <summary><c>FBEAM_STARTENTITY</c>.</summary>
    public const int StartEntityFlag = 0x1;

    /// <summary><c>FBEAM_ENDENTITY</c>.</summary>
    public const int EndEntityFlag = 0x2;

    /// <summary><c>FBEAM_FADEIN</c>.</summary>
    public const int FadeInFlag = 0x4;

    /// <summary><c>FBEAM_FADEOUT</c>.</summary>
    public const int FadeOutFlag = 0x8;

    /// <summary><c>FBEAM_SOLID</c>.</summary>
    public const int SolidFlag = 0x20;

    /// <summary><c>FBEAM_ONLYNOISEONCE</c>.</summary>
    public const int OnlyNoiseOnceFlag = 0x100;

    /// <summary><c>FBEAM_USE_HITBOXES</c>.</summary>
    public const int UseHitboxesFlag = 0x400;

    /// <summary><c>FBEAM_STARTVISIBLE</c>.</summary>
    public const int StartVisibleFlag = 0x800;

    /// <summary><c>FBEAM_ENDVISIBLE</c>.</summary>
    public const int EndVisibleFlag = 0x1000;

    /// <summary><c>FBEAM_FOREVER</c>.</summary>
    public const int ForeverFlag = 0x4000;

    /// <summary><c>FBEAM_HALOBEAM</c>.</summary>
    public const int HaloBeamFlag = 0x8000;

    /// <summary>
    /// The entity flags a <c>C_Beam</c> passes to its <c>Beam_t</c> — <c>SINENOISE | SOLID | SHADEIN | SHADEOUT | NOTILE</c>
    /// (`view_beams.cpp:2340`). Everything else on <c>m_nBeamFlags</c> is dropped, <c>FBEAM_HALOBEAM</c> and the fades
    /// among them.
    /// </summary>
    public const int EntityFlagsPassed =
        BeamDraw.SineNoiseFlag | SolidFlag | BeamDraw.ShadeInFlag | BeamDraw.ShadeOutFlag | BeamDraw.NoTileFlag;

    /// <summary>The DirectX support level TF2 runs at on Direct3D 9 hardware — <c>mat_dxlevel 95</c>.</summary>
    /// <remarks>
    /// <c>C_Beam::ShouldDraw</c> refuses a beam whose <c>m_nMinDXLevel</c> is above it (`beam_shared.cpp:1046`). The
    /// corpus's beams all state 90.
    /// </remarks>
    public const int HardwareDxLevel = 95;

    /// <summary><c>TE_BEAMPOINTS</c>.</summary>
    private const int TePoints = 0;

    /// <summary><c>TE_BEAMSPLINE</c>.</summary>
    private const int TeSpline = 6;

    /// <summary><c>TE_BEAMLASER</c>.</summary>
    private const int TeLaser = 8;

    /// <summary><c>beamRandom</c>, the one stream every beam's noise is drawn from (`view_beams.cpp:179`).</summary>
    private readonly UniformRandomStream _random = new();

    /// <summary>One beam's noise table, reused: a beam is rebuilt from nothing every frame.</summary>
    private readonly float[] _noise = new float[BeamDraw.NoiseDivisions + 1];

    private readonly List<BeamSegment> _segments = [];
    private readonly List<DetailSpriteVertex> _corners = [];
    private readonly SpriteStripBatches _strips = new();

    /// <summary>How many beams the last build drew.</summary>
    public int Drawn { get; private set; }

    /// <summary>How many it was offered and drew nothing for, for a reason the engine would also have.</summary>
    public int Skipped { get; private set; }

    /// <summary>Builds this moment's beams.</summary>
    /// <param name="props">What the scene is drawing; only props carrying a <see cref="SceneBeam"/> are read.</param>
    /// <param name="view">The camera.</param>
    /// <param name="sprites">Each sprite material as loaded, keyed by model path.</param>
    /// <param name="absolute">A prop's pose in the world — <c>GetAbsOrigin()</c> through its move parents.</param>
    /// <param name="entityPosition">Where an entity end is; see <see cref="BeamEntityPosition"/>.</param>
    /// <param name="visible">Whether a halo's centre can be seen — the occlusion fraction, all or nothing.</param>
    /// <param name="currentTime"><c>gpGlobals-&gt;curtime</c>, in seconds.</param>
    /// <param name="frameTime"><c>gpGlobals-&gt;frametime</c>; zero while paused, which re-seeds the noise each frame.</param>
    /// <returns>One batch per material and pass.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public IReadOnlyList<ParticleBatch> Build(
        IReadOnlyList<SceneProp> props,
        in BeamView view,
        IReadOnlyDictionary<string, EngineSprite> sprites,
        Func<SceneProp, ScenePose> absolute,
        BeamEntityPosition entityPosition,
        Func<Vector3, bool> visible,
        float currentTime,
        float frameTime)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(sprites);
        ArgumentNullException.ThrowIfNull(absolute);
        ArgumentNullException.ThrowIfNull(entityPosition);
        ArgumentNullException.ThrowIfNull(visible);

        _strips.Clear();
        Drawn = 0;
        Skipped = 0;

        foreach (SceneProp prop in props)
        {
            if (prop.Pose.Beam is not { } beam)
            {
                continue;
            }

            if (Draw(prop, beam, view, sprites, absolute, entityPosition, visible, currentTime, frameTime))
            {
                Drawn++;
            }
            else
            {
                Skipped++;
            }
        }

        return _strips.Batches(sprites);
    }

    /// <summary>One beam: <c>C_Beam::DrawModel</c>, <c>DrawBeam( C_Beam* )</c>, <c>UpdateBeam</c> and <c>DrawBeam( Beam_t* )</c>.</summary>
    /// <returns>Whether anything was emitted.</returns>
    private bool Draw(
        SceneProp prop,
        SceneBeam beam,
        in BeamView view,
        IReadOnlyDictionary<string, EngineSprite> sprites,
        Func<SceneProp, ScenePose> absolute,
        BeamEntityPosition entityPosition,
        Func<Vector3, bool> visible,
        float currentTime,
        float frameTime)
    {
        // `C_Beam::ShouldDraw`: a beam asking for more than the hardware gives is not drawn at all.
        if (beam.MinDxLevel != 0 && beam.MinDxLevel > HardwareDxLevel)
        {
            return false;
        }

        // `SetupBeam` returns before filling anything when the model will not load, and `DrawBeam` then finds no
        // sprite — a beam with no material draws nothing.
        if (!sprites.TryGetValue(prop.ModelPath, out EngineSprite sprite))
        {
            return false;
        }

        ScenePose placed = absolute(prop);
        Vector3 origin = new(placed.X, placed.Y, placed.Z);

        Beam frame = new()
        {
            Start = AbsoluteStart(beam, origin, entityPosition),
            End = AbsoluteEnd(beam, prop, placed, entityPosition),
            Ends = [],
        };

        Setup(ref frame, beam, prop, currentTime);
        MapType(ref frame, beam, entityPosition);

        frame.Flags |= beam.Flags & EntityFlagsPassed;

        // `UpdateBeam` can return early, and `DrawBeam` is called regardless — it never tests how the update went, so a
        // beam whose end entity vanished draws with whatever the early return left behind.
        Update(ref frame, entityPosition, currentTime, frameTime);

        int before = _strips.Corners;

        DrawBeam(frame, prop.ModelPath, sprite, beam.HaloPath, sprites, view, visible);

        return _strips.Corners > before;
    }

    /// <summary>
    /// <c>C_Beam::GetAbsStartPos</c> (`beam_shared.cpp:515`): an entity end through <c>ComputeBeamEntPosition</c> for
    /// every type but points and hose, else the entity's own origin.
    /// </summary>
    private static Vector3 AbsoluteStart(SceneBeam beam, Vector3 origin, BeamEntityPosition entityPosition)
    {
        if (beam.Type is not (BeamPoints or BeamHose) &&
            entityPosition(beam.Ends[0].Handle, beam.Ends[0].Attachment, false) is { } end)
        {
            return end;
        }

        return origin;
    }

    /// <summary>
    /// <c>C_Beam::GetAbsEndPos</c> (`beam_shared.cpp:528`): the last entity end for the entity types, else
    /// <c>m_vecEndPos</c> — absolute when the beam has no move parent, and carried through the beam's own transform
    /// when it has one.
    /// </summary>
    private static Vector3 AbsoluteEnd(
        SceneBeam beam, SceneProp prop, ScenePose placed, BeamEntityPosition entityPosition)
    {
        int last = Math.Clamp(beam.EntityCount - 1, 0, SceneBeam.MaximumEnds - 1);

        if (beam.Type is not (BeamPoints or BeamHose) &&
            entityPosition(beam.Ends[last].Handle, beam.Ends[last].Attachment, false) is { } end)
        {
            return end;
        }

        (float x, float y, float z) = beam.EndPosition;

        if (prop.AttachedTo is null)
        {
            return new Vector3(x, y, z);
        }

        // `VectorTransform( m_vecEndPos, EntityToWorldTransform(), vecEndAbsPosition )`.
        (float wx, float wy, float wz) = new PropTransform(
                placed.X, placed.Y, placed.Z, placed.Pitch, placed.Yaw, placed.Roll, 1f)
            .Apply(x, y, z);

        return new Vector3(wx, wy, wz);
    }

    /// <summary><c>SetupBeam</c> and <c>SetBeamAttributes</c>, with the <c>BeamInfo_t</c> <c>DrawBeam( C_Beam* )</c> fills.</summary>
    private static void Setup(ref Beam frame, SceneBeam beam, SceneProp prop, float currentTime)
    {
        // `beamInfo.m_nType` is the constructor's TE_BEAMPOINTS, and `m_nSegments` its -1.
        frame.Type = TePoints;
        frame.HaloScale = beam.HaloScale;
        frame.Frequency = currentTime * beam.Speed;
        frame.Die = currentTime;
        frame.Width = beam.Width;
        frame.EndWidth = beam.EndWidth;
        frame.FadeLength = beam.FadeLength;
        frame.Amplitude = beam.Amplitude;

        // `beamInfo.m_flBrightness = pbeam->GetFxBlend()` — the render alpha through its effect.
        frame.Brightness = FxBlend.Compute(
            prop.Pose.RenderFx, prop.Pose.RenderMode, prop.Pose.RenderAlpha, prop.EntityIndex, currentTime).Blend;

        frame.Speed = beam.Speed;
        frame.Flags = 0;
        frame.Delta = frame.End - frame.Start;
        frame.Segments = SegmentsFor(frame.Delta, frame.Amplitude);

        // `SetBeamAttributes` also copies the start frame (truncated to an int), the frame rate and — after — the HDR
        // colour scale. The first two only choose an animation frame, which this project's sprites do not draw (B378);
        // the HDR scale is one on every corpus beam and the sprite pass applies none either.
        (byte red, byte green, byte blue) = prop.Pose.RenderColor;
        frame.Colour = new Vector3(red, green, blue);
    }

    /// <summary>
    /// The relinking switch (`view_beams.cpp:2280-2338`): each entity beam type as the temp-entity type that draws it.
    /// </summary>
    private static void MapType(ref Beam frame, SceneBeam beam, BeamEntityPosition entityPosition)
    {
        switch (beam.Type)
        {
            case BeamEnts:
                frame.Type = TePoints;
                frame.Flags = StartEntityFlag | EndEntityFlag;
                frame.Ends = [beam.Ends[0], beam.Ends[1]];
                frame.EntityCount = beam.EntityCount;
                break;

            case BeamLaser:
                frame.Type = TeLaser;
                frame.Flags = StartEntityFlag | EndEntityFlag;
                frame.Ends = [beam.Ends[0], beam.Ends[1]];
                frame.EntityCount = beam.EntityCount;
                break;

            case BeamSpline:
                frame.Type = TeSpline;
                frame.Flags = StartEntityFlag | EndEntityFlag;
                frame.EntityCount = beam.EntityCount;
                frame.Ends = [.. beam.Ends];
                break;

            case BeamEntPoint:
                frame.Type = TePoints;
                frame.Flags = 0;
                frame.Ends = [beam.Ends[0], beam.Ends[1]];

                // `if ( beam.entity[0].Get() )` — set only for an end that dereferences to an entity now.
                if (entityPosition(beam.Ends[0].Handle, beam.Ends[0].Attachment, false) is not null)
                {
                    frame.Flags |= StartEntityFlag;
                }

                if (entityPosition(beam.Ends[1].Handle, beam.Ends[1].Attachment, false) is not null)
                {
                    frame.Flags |= EndEntityFlag;
                }

                frame.EntityCount = beam.EntityCount;
                break;

            default:
                // BEAM_POINTS is "already set up", and BEAM_HOSE has no case: both stay TE_BEAMPOINTS.
                break;
        }
    }

    /// <summary><c>UpdateBeam</c> (`view_beams.cpp:1469-1611`), early returns and all.</summary>
    private void Update(ref Beam frame, BeamEntityPosition entityPosition, float currentTime, float frameTime)
    {
        // "If we are paused, force random numbers used by noise to generate the same value every frame."
        if (frameTime == 0f)
        {
            _random.SetSeed((int)currentTime);
        }

        frame.Frequency += (frame.Flags & OnlyNoiseOnceFlag) == 0
            ? frameTime
            : frameTime * _random.RandomFloat(1f, 2f);

        // A fresh beam has never calculated its noise, so FBEAM_ONLYNOISEONCE changes nothing here.
        Array.Clear(_noise);

        if (frame.Amplitude != 0f)
        {
            if ((frame.Flags & BeamDraw.SineNoiseFlag) != 0)
            {
                BeamDraw.SineNoise(_noise, BeamDraw.NoiseDivisions);
            }
            else
            {
                BeamDraw.Noise(_noise, BeamDraw.NoiseDivisions, 1f, _random);
            }
        }

        if ((frame.Flags & (StartEntityFlag | EndEntityFlag)) != 0)
        {
            if (!RecomputeEndpoints(ref frame, entityPosition, currentTime))
            {
                return;
            }

            frame.Delta = frame.End - frame.Start;
            frame.Segments = SegmentsFor(frame.Delta, frame.Amplitude);
        }

        if (frame.Type == TeSpline)
        {
            // "Why isn't attachment[0] being computed?" — Valve's own question; the walk starts at one.
            frame.Points = new Vector3[Math.Max(frame.EntityCount, 2)];
            frame.Points[0] = frame.Start;

            for (int index = 1; index < frame.Points.Length; index++)
            {
                frame.Points[index] = index < frame.Ends.Length &&
                    entityPosition(
                        frame.Ends[index].Handle,
                        frame.Ends[index].Attachment,
                        (frame.Flags & UseHitboxesFlag) != 0) is { } point
                    ? point
                    : frame.Points[index - 1];
            }
        }

        // `t` is the beam's place in its life. An entity beam lives for zero seconds, so this is one unless the frame
        // did not advance — and nothing an entity beam can carry reads it.
        frame.T = frame.Frequency + (frame.Die - currentTime);
        frame.T = frame.T != 0f ? frame.Frequency / frame.T : 1f;

        // "check for zero fadeLength (means no fade)".
        if (frame.FadeLength == 0f)
        {
            frame.FadeLength = frame.Delta.Length();
        }
    }

    /// <summary><c>RecomputeBeamEndpoints</c> (`view_beams.cpp:2071-2117`).</summary>
    private static bool RecomputeEndpoints(ref Beam frame, BeamEntityPosition entityPosition, float currentTime)
    {
        bool hitboxes = (frame.Flags & UseHitboxesFlag) != 0;

        if ((frame.Flags & StartEntityFlag) != 0)
        {
            if (entityPosition(frame.Ends[0].Handle, frame.Ends[0].Attachment, hitboxes) is { } start)
            {
                frame.Start = start;
                frame.Flags |= StartVisibleFlag;
            }
            else if ((frame.Flags & ForeverFlag) == 0)
            {
                frame.Flags &= ~StartEntityFlag;
            }

            // "If we've never seen the start entity, don't display."
            if ((frame.Flags & StartVisibleFlag) == 0)
            {
                return false;
            }
        }

        if ((frame.Flags & EndEntityFlag) != 0)
        {
            if (entityPosition(frame.Ends[1].Handle, frame.Ends[1].Attachment, hitboxes) is { } end)
            {
                frame.End = end;
                frame.Flags |= EndVisibleFlag;
            }
            else if ((frame.Flags & ForeverFlag) == 0)
            {
                frame.Flags &= ~EndEntityFlag;
                frame.Die = currentTime;
                return false;
            }
            else
            {
                return false;
            }

            if ((frame.Flags & EndVisibleFlag) == 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary><c>DrawBeam( Beam_t* )</c> (`view_beams.cpp:1936-2051`), for the three types an entity beam reaches.</summary>
    private void DrawBeam(
        Beam frame,
        string modelPath,
        EngineSprite sprite,
        string? haloPath,
        IReadOnlyDictionary<string, EngineSprite> sprites,
        in BeamView view,
        Func<Vector3, bool> visible)
    {
        // "Don't draw really short beams."
        if (frame.Delta.Length() < 0.1f)
        {
            return;
        }

        // `halosprite = modelinfo->GetModel( pbeam->haloIndex )` — no model for index zero or one that never loaded.
        (string Path, EngineSprite Sprite)? halo =
            !string.IsNullOrEmpty(haloPath) && sprites.TryGetValue(haloPath, out EngineSprite haloSprite)
                ? (haloPath, haloSprite)
                : null;

        // `frame = ( (int)( pbeam->frame + gpGlobals->curtime * pbeam->frameRate) % pbeam->frameCount )` picks the
        // sprite's animation frame. Not taken here: this project draws a sprite's first frame only (B378), so a frame
        // index would have nowhere to go — the beam shares the sprites' open half rather than inventing its own.
        int renderMode = (frame.Flags & SolidFlag) != 0 ? RenderModes.Normal : RenderModes.TransAdd;

        Vector3 colour = frame.Colour;

        if ((frame.Flags & FadeInFlag) != 0)
        {
            colour *= frame.T;
        }
        else if ((frame.Flags & FadeOutFlag) != 0)
        {
            colour *= 1f - frame.T;
        }

        colour *= (float)(1 / 255.0);
        Vector3 sourceColour = colour;
        colour *= (float)(frame.Brightness / 255.0);

        _segments.Clear();

        switch (frame.Type)
        {
            case TePoints:
                if (halo is { } withHalo)
                {
                    DrawBeamWithHalo(frame, colour, sourceColour, view, visible);
                    Emit(modelPath, sprite, renderMode, view);
                    EmitHalo(withHalo, view);
                }
                else
                {
                    BeamDraw.DrawSegs(
                        _noise, frame.Start, frame.Delta, frame.Width, frame.EndWidth, frame.Amplitude,
                        frame.Frequency, frame.Speed, frame.Segments, frame.Flags, colour, frame.FadeLength, view,
                        _segments);
                    Emit(modelPath, sprite, renderMode, view);
                }

                break;

            case TeSpline:
                (Vector3 haloAt, Vector3 haloColour) = BeamDraw.DrawSplineSegs(
                    _noise, frame.Points ?? [frame.Start, frame.End], frame.Width, frame.EndWidth, frame.Amplitude,
                    frame.Frequency, frame.Speed, frame.Segments, frame.Flags, colour, view, _segments);
                Emit(modelPath, sprite, renderMode, view);

                // "Draw halo at end of beam", with the halo sprite's GLOW material, at the halo scale.
                if (halo is { } splineHalo)
                {
                    _pendingHalo = (haloAt, frame.HaloScale, haloColour);
                    EmitHalo(splineHalo, view);
                }

                break;

            case TeLaser:
                DrawLaser(frame, colour, view);
                Emit(modelPath, sprite, renderMode, view);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// <c>DrawBeamWithHalo</c> (`view_beams.cpp:1777-1865`): the shaft dimmed as the camera nears its axis, and a glow
    /// at the lamp seen from in front.
    /// </summary>
    /// <remarks>
    /// **Not <c>FBEAM_HALOBEAM</c>, which is every beam here, draws the shaft at its START width at both ends, in two
    /// segments** — <c>DrawSegs( …, pbeam-&gt;width, pbeam-&gt;width, …, 2, … )</c>, "just shy of its end so it doesn't
    /// clip". The end width a spotlight networks never reaches the screen, and the flag that would let it is one the
    /// entity path strips.
    ///
    /// **The halo's colour is the SOURCE colour**, before the entity's brightness: a spotlight's halo is as bright as its
    /// render colour however dim its shaft is drawn, scaled only by how directly the camera faces down the beam.
    /// </remarks>
    private void DrawBeamWithHalo(Beam frame, Vector3 colour, Vector3 sourceColour, in BeamView view, Func<Vector3, bool> visible)
    {
        Vector3 beamDirection = BeamDraw.SafeNormalize(frame.End - frame.Start);
        Vector3 localDirection = BeamDraw.SafeNormalize(view.Origin - frame.Start);

        float dot = Vector3.Dot(beamDirection, localDirection);
        float fade = dot < 0f ? 0f : dot * 2f;

        // `CalcClosestPointOnLine( CurrentViewOrigin(), start, start + ( beamDir * 2 ), out, &distToLine )`, then the
        // distance recomputed from the closest point — the line's `t` it wrote is discarded.
        Vector3 closest = ClosestPointOnLine(view.Origin, frame.Start, frame.Start + (beamDirection * 2f));
        float distanceToLine = (view.Origin - closest).Length();

        float dotScale = 1f;

        // "Use beam width."
        float threshold = frame.Width * 4f;

        if (distanceToLine < threshold)
        {
            dotScale = Math.Clamp(BeamDraw.RemapVal(distanceToLine, threshold, frame.Width, 1f, 0f), 0f, 1f);
        }

        Vector3 scaled = colour * dotScale;

        if ((frame.Flags & HaloBeamFlag) != 0)
        {
            BeamDraw.DrawSegs(
                _noise, frame.Start, frame.Delta, frame.Width, frame.EndWidth, frame.Amplitude, frame.Frequency,
                frame.Speed, frame.Segments, frame.Flags, scaled, frame.FadeLength, view, _segments);
        }
        else
        {
            BeamDraw.DrawSegs(
                _noise, frame.Start, frame.Delta, frame.Width, frame.Width, frame.Amplitude, frame.Frequency,
                frame.Speed, 2, frame.Flags, scaled, frame.FadeLength, view, _segments);
        }

        _pendingHalo = null;

        float haloFractionVisible = visible(frame.Start) ? 1f : 0f;

        if (fade != 0f && haloFractionVisible > 0f)
        {
            // "NOTENOTE: This is kinda funky when moving away and to the backside -- jdw".
            float haloScale = Math.Clamp(
                BeamDraw.RemapVal(distanceToLine, threshold, frame.Width * 0.5f, 1f, 2f), 1f, 2f);

            haloScale *= frame.HaloScale;

            float colourFade = Math.Clamp(fade * fade, 0f, 1f);

            _pendingHalo = (frame.Start, haloScale, sourceColour * (colourFade * haloFractionVisible));
        }
    }

    /// <summary><c>DrawLaser</c> (`view_beams.cpp:1871-1921`): a beam that fades unless looked at from near its source.</summary>
    private void DrawLaser(Beam frame, Vector3 colour, in BeamView view)
    {
        Vector3 beamDirection = BeamDraw.SafeNormalize(frame.End - frame.Start);
        float dot = Vector3.Dot(beamDirection, view.Forward);

        // "abort if the player's looking along it away from the source".
        if (dot > 0f)
        {
            return;
        }

        // "Fade the beam if the player's not looking at the source."
        float fade = (float)Math.Pow(dot, 10);

        // "Fade the beam based on the player's proximity to the beam."
        Vector3 local = view.Origin - frame.Start;
        dot = Vector3.Dot(beamDirection, local);
        float distance = (local - (dot * beamDirection)).Length();

        if (distance > 30f)
        {
            distance = 1f - ((distance - 30f) / 64f);

            fade = distance <= 0f ? 0f : fade * (float)Math.Pow(distance, 3);
        }

        if (fade < 1f / 255f)
        {
            return;
        }

        BeamDraw.DrawSegs(
            _noise, frame.Start, frame.Delta, frame.Width, frame.EndWidth, frame.Amplitude, frame.Frequency,
            frame.Speed, frame.Segments, frame.Flags, colour * fade, frame.FadeLength, view, _segments);
    }

    /// <summary>The halo waiting to be drawn once its shaft is, or null.</summary>
    private (Vector3 At, float Scale, Vector3 Colour)? _pendingHalo;

    /// <summary>
    /// The strip built so far, through <see cref="BeamSegDraw"/>, into the beam material's passes at its render mode.
    /// </summary>
    private void Emit(string modelPath, EngineSprite sprite, int renderMode, in BeamView view)
    {
        if (_segments.Count < 2)
        {
            return;
        }

        _corners.Clear();
        BeamSegDraw.Draw(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_segments), view.Origin, _corners);

        _strips.Add(modelPath, sprite, renderMode, _corners);
    }

    /// <summary><c>BeamDrawHalo</c>: the pending halo, with the halo sprite drawn at <c>kRenderGlow</c>.</summary>
    private void EmitHalo((string Path, EngineSprite Sprite) halo, in BeamView view)
    {
        if (_pendingHalo is not { } pending)
        {
            return;
        }

        _pendingHalo = null;
        _corners.Clear();

        BeamDraw.DrawHalo(pending.At, pending.Scale, pending.Colour, view, _corners);

        _strips.Add(halo.Path, halo.Sprite, RenderModes.Glow, _corners);
    }

    /// <summary>
    /// <c>pbeam-&gt;segments</c> from a delta: "one per 4 pixels" for a noisy beam, "one per 16 pixels" otherwise
    /// (`view_beams.cpp:727-734`), truncated as the <c>int</c> assignment truncates.
    /// </summary>
    internal static int SegmentsFor(Vector3 delta, float amplitude) =>
        amplitude >= 0.50
            ? (int)((delta.Length() * 0.25) + 3)
            : (int)((delta.Length() * 0.075) + 3);

    /// <summary><c>CalcClosestPointOnLine</c>: the point on the infinite line through two points nearest a third.</summary>
    internal static Vector3 ClosestPointOnLine(Vector3 point, Vector3 lineA, Vector3 lineB)
    {
        Vector3 direction = lineB - lineA;
        float div = Vector3.Dot(direction, direction);

        float t = div < 0.00001f ? 0f : (Vector3.Dot(direction, point) - Vector3.Dot(direction, lineA)) / div;

        return lineA + (direction * t);
    }

    /// <summary>One frame's <c>Beam_t</c> — only the fields an entity beam's draw reads.</summary>
    private struct Beam
    {
        public int Type;
        public int Flags;
        public Vector3 Start;
        public Vector3 End;
        public Vector3 Delta;
        public float T;
        public float Frequency;
        public float Die;
        public float Width;
        public float EndWidth;
        public float FadeLength;
        public float Amplitude;
        public Vector3 Colour;
        public int Brightness;
        public float Speed;
        public int Segments;
        public float HaloScale;
        public int EntityCount;
        public (int Handle, int Attachment)[] Ends;
        public Vector3[]? Points;
    }
}
