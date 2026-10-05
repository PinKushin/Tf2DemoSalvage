using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// `CTFParticlePanel` (game/client/tf/vgui/tf_particlepanel.cpp): particle systems painted flat into a panel — the
/// match doors' slam (`FrontParticlePanel`, `HudMatchStatus.res`).
/// </summary>
/// <remarks>
/// Each effect is simulated on `engine->Time()` from `OnTick` (:236) and rendered under the panel camera's view — origin
/// and angles zero, so looking down +X — with an orthographic projection translated to the effect's position and scaled
/// (:586-619). The particle simulation and batching are the world's (<see cref="ParticleEffect"/>,
/// <see cref="ParticleEffects"/>), as the model panels use them. **Not ported:** `IsValidHierarchy` (:219), whose invalid
/// case is a collection whose definition changed under it.
/// </remarks>
public sealed class TfParticlePanel : VguiEditablePanel
{
    /// <summary>`MAX_PARTICLE_CONTROL_POINTS` (particles.h).</summary>
    public const int MaxControlPoints = 64;

    private const float OrthoDepth = 9999f;

    private readonly List<Effect> _effects = [];
    private readonly ParticleEffects _renderer = new();
    private KeyValuesTree? _particles;

    /// <summary>`CTFParticlePanel( parent, name )` (:54).</summary>
    /// <param name="parent">The parent.</param>
    /// <param name="name">The name.</param>
    public TfParticlePanel(VguiPanel? parent, string? name)
        : base(parent, name)
    {
    }

    /// <inheritdoc/>
    public override string ClassName => "CTFParticlePanel";

    /// <summary>`m_vecParticleEffects`, for a test.</summary>
    public IReadOnlyList<Effect> Effects => _effects;

    /// <inheritdoc/>
    /// <remarks>`ApplySettings` (:81): a `ParticleEffects` block replaces the effects.</remarks>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(context);

        base.ApplySettings(block, context);

        if (block.Find("ParticleEffects") is { } effects)
        {
            _particles = effects;
            UpdateParticlesFromKv(context);
        }
    }

    /// <summary>`OnCommand` (:111): "start[n]" starts one or every effect, "stop[n]" stops emitting.</summary>
    /// <param name="command">The command.</param>
    public override void OnCommand(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.StartsWith("start", StringComparison.OrdinalIgnoreCase))
        {
            string number = command[5..];
            int only = number.Length > 0 ? PanelLayout.Atoi(number) : -1;

            for (int index = 0; index < _effects.Count; index++)
            {
                if (only != -1 && index != only)
                {
                    continue;
                }

                Effect effect = _effects[index];

                if (effect.System is null)
                {
                    effect.SetParticleSystem(effect.Name, Systems(), Sheets);
                }

                if (effect.System is null)
                {
                    continue;
                }

                effect.Startup();
                effect.Emitting = true;
                effect.ForceStopped = false;
            }
        }
        else if (command.StartsWith("stop", StringComparison.OrdinalIgnoreCase))
        {
            // `command + ARRAYSIZE( "start" ) - 1`: five characters in, one past "stop" (:148).
            string number = command.Length > 5 ? command[5..] : string.Empty;

            for (int index = 0; index < _effects.Count; index++)
            {
                if ((number.Length == 0 || index == PanelLayout.Atoi(number)) && _effects[index].System is not null)
                {
                    _effects[index].Emitting = false;
                    _effects[index].ForceStopped = true;
                }
            }
        }
    }

    /// <summary>`FireParticleEffect` (:183): a looping or one-shot effect at a screen position, removed when it ends.</summary>
    /// <remarks>`flEndTime` defaults to `FLT_MAX` seconds, which is null here: no end.</remarks>
    /// <param name="name">The system.</param>
    /// <param name="x">Screen x.</param>
    /// <param name="y">Screen y.</param>
    /// <param name="scale">Its scale.</param>
    /// <param name="loop">Whether it restarts when finished.</param>
    /// <param name="endTime">Seconds from now it ends.</param>
    public void AddParticleEffect(string name, int x, int y, float scale, bool loop, float? endTime = null)
    {
        HudViewport? viewport = HudViewport.Of(this);
        (int parentX, int parentY) = Parent is { } parent ? AbsolutePosition(parent) : (0, 0);
        Effect effect = new(name)
        {
            X = x - parentX,
            Y = Tall - y - parentY,
            Scale = Proportional && viewport?.Context is { } context ? scale * (context.ScreenTall / 480f) : scale,
            Loop = loop,
            AutoDelete = true,
            StartActivated = true,
            EndTime = endTime is { } seconds ? (viewport?.State.CurTime ?? 0f) + seconds : float.MaxValue,
        };

        for (int point = 0; point < MaxControlPoints; point++)
        {
            effect.ControlPoints[point] = new Vector3(0f, 0f, 10f * point);
        }

        _effects.Add(effect);
        effect.SetParticleSystem(name, Systems(), Sheets);
    }

    /// <summary>`OnTick` (:236): every effect advanced on real time, a finished one-shot removed.</summary>
    protected override void OnThink()
    {
        if (HudViewport.Of(this) is not { } viewport)
        {
            return;
        }

        HudState state = viewport.State;

        for (int index = _effects.Count - 1; index >= 0; index--)
        {
            _effects[index].Update(state.RealTime, state.CurTime, Systems(), Sheets);

            // "If this effect is done and should auto-delete, then now is when we delete".
            if (_effects[index].System is null && _effects[index].AutoDelete)
            {
                _effects.RemoveAt(index);
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>`Paint` (:262) and each effect's (:586): an orthographic view translated to it and scaled.</remarks>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(context);

        if (HudViewport.Of(this)?.ParticleMaterials is not { } materials)
        {
            return;
        }

        int wide = Math.Min(Wide, context.ScreenWide);
        int tall = Math.Min(Tall, context.ScreenTall);

        if (wide <= 0 || tall <= 0)
        {
            return;
        }

        foreach (Effect effect in _effects)
        {
            if (effect.System is not { } system || !effect.Started)
            {
                continue;
            }

            // The camera at the origin looking down +X: the view's right is world −Y and its up world +Z.
            List<ParticleBatch> batches = [];

            foreach (ParticleBatch batch in _renderer.Build([system], Vector3.Zero, -Vector3.UnitY, Vector3.UnitZ, materials))
            {
                batches.Add(batch with { Corners = [.. batch.Corners] });
            }

            if (batches.Count > 0)
            {
                surface.Paint3D(0, 0, wide, tall, OrthoCamera(effect.X, effect.Y, effect.Scale, wide, tall), [], batches);
            }
        }
    }

    /// <summary>
    /// `ComputeViewMatrix` (view x right = −world y, view y = world z, view z = −world x) then `Ortho( 0, 0, w, h, −9999,
    /// 9999 )` after `Translate( x, y )` and `Scale( s )`, as a row-major world-to-clip matrix with y up.
    /// </summary>
    /// <param name="x">The effect's x, from the panel's left.</param>
    /// <param name="y">Its y, from the panel's bottom.</param>
    /// <param name="scale">Its scale.</param>
    /// <param name="wide">The paint's width.</param>
    /// <param name="tall">Its height.</param>
    /// <returns>The matrix.</returns>
    public static float[] OrthoCamera(int x, int y, float scale, int wide, int tall) =>
    [
        0f, 0f, -scale / (2f * OrthoDepth), 0f,
        -2f * scale / wide, 0f, 0f, 0f,
        0f, 2f * scale / tall, 0f, 0f,
        (2f * x / wide) - 1f, (2f * y / tall) - 1f, 0.5f, 1f,
    ];

    /// <summary>`UpdateParticlesFromKV` (:335): each block's position, scale, flags, system, angles and control points.</summary>
    private void UpdateParticlesFromKv(VguiContext context)
    {
        if (_particles is not { } block)
        {
            return;
        }

        _effects.Clear();

        foreach (KeyValuesTree entry in block.Children)
        {
            int x = Position(entry.Find("particle_xpos")?.Value, X, Wide, context);
            int y = Position(entry.Find("particle_ypos")?.Value, Y, Tall, context);
            float scale = entry.Find("particle_scale")?.Value is { } scaleText ? PanelLayout.Atof(scaleText) : 1f;
            Effect effect = new(entry.Find("particleName")?.Value ?? string.Empty)
            {
                X = x,
                Y = Tall - y,

                // "Scale the scale factor the same way we do the XY position coordinates".
                Scale = Proportional ? scale * (context.ScreenTall / 480f) : scale,
                Loop = Bool(entry, "loop", true),
                StartActivated = Bool(entry, "start_activated", true),
            };

            _effects.Add(effect);
            effect.SetParticleSystem(effect.Name, Systems(), Sheets);

            if (entry.Find("angles")?.Value is { Length: > 0 } angles && effect.System is not null && Vector(angles) is { } parsed)
            {
                effect.Angles = parsed;
            }

            // "Read all control point values": control_point0, 1, … until one is absent.
            effect.ControlPoints[0] = Vector3.Zero;

            for (int point = 0; point < MaxControlPoints && entry.Find($"control_point{point}")?.Value is { Length: > 0 } value; point++)
            {
                if (Vector(value) is { } position)
                {
                    effect.ControlPoints[point] = position;
                }
            }
        }
    }

    /// <summary>The `r`/`c` alignment prefix and proportional scaling of a `particle_xpos`/`particle_ypos` (:355-419).</summary>
    private int Position(string? text, int fallback, int extent, VguiContext context)
    {
        if (text is null)
        {
            return fallback;
        }

        bool right = text.Length > 0 && char.ToLowerInvariant(text[0]) == 'r';
        bool centre = text.Length > 0 && char.ToLowerInvariant(text[0]) == 'c';
        int value = PanelLayout.Atoi(right || centre ? text[1..] : text);

        if (Proportional)
        {
            value = context.Scale(value);
        }

        if (right)
        {
            return extent - value;
        }

        return centre ? (extent / 2) + value : value;
    }

    private static bool Bool(KeyValuesTree entry, string name, bool fallback) =>
        entry.Find(name)?.Value is { } value ? PanelLayout.Atoi(value) != 0 : fallback;

    private static Vector3? Vector(string text)
    {
        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 3
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float a)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float b)
            && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float c)
            ? new Vector3(a, b, c)
            : null;
    }

    private static (int X, int Y) AbsolutePosition(VguiPanel panel)
    {
        (int x, int y) = (0, 0);

        for (VguiPanel? at = panel; at is not null; at = at.Parent)
        {
            x += at.X;
            y += at.Y;
        }

        return (x, y);
    }

    private IReadOnlyDictionary<string, ParticleSystem>? Systems() => HudViewport.Of(this)?.ParticleSystems;

    private IReadOnlyList<SheetSequence>? Sheets(ParticleSystem system) =>
        HudViewport.Of(this)?.ParticleMaterials is { } materials && materials.TryGetValue(ParticleEffects.MaterialOf(system), out ParticleMaterial material)
            && material.Sequences.Count > 0
            ? material.Sequences
            : null;

    /// <summary>`ParticleEffect_t` (tf_particlepanel.h:38).</summary>
    /// <param name="name">`m_ParticleSystemName`.</param>
    public sealed class Effect(string name)
    {
        /// <summary>`m_ParticleSystemName`.</summary>
        public string Name { get; private set; } = name;

        /// <summary>`m_pParticleSystem`, or null.</summary>
        public ParticleEffect? System { get; private set; }

        /// <summary>`m_pControlPointValue`.</summary>
        public Vector3[] ControlPoints { get; } = new Vector3[MaxControlPoints];

        /// <summary>`m_Angles`.</summary>
        public Vector3 Angles { get; set; }

        /// <summary>`m_nXPos`.</summary>
        public int X { get; set; }

        /// <summary>`m_nYPos`, from the bottom.</summary>
        public int Y { get; set; }

        /// <summary>`m_flScale`.</summary>
        public float Scale { get; set; } = 1f;

        /// <summary>`m_flEndTime`.</summary>
        public float EndTime { get; set; } = float.MaxValue;

        /// <summary>`m_bLoop`.</summary>
        public bool Loop { get; set; } = true;

        /// <summary>`m_bStartActivated`.</summary>
        public bool StartActivated { get; set; } = true;

        /// <summary>`m_bForceStopped`.</summary>
        public bool ForceStopped { get; set; }

        /// <summary>`m_bAutoDelete`.</summary>
        public bool AutoDelete { get; set; }

        /// <summary>`m_bStarted`.</summary>
        public bool Started { get; private set; }

        /// <summary>Whether `StartEmission` holds; `StopEmission` lets it fade.</summary>
        public bool Emitting { get; set; } = true;

        /// <summary>`m_flLastTime`, null for `FLT_MAX`.</summary>
        private float? _lastTime;

        /// <summary>`SetParticleSystem` (:565): an undefined name leaves none.</summary>
        /// <param name="systemName">The system.</param>
        /// <param name="systems">The definitions.</param>
        /// <param name="sheets">Each system's sheet.</param>
        public void SetParticleSystem(string systemName, IReadOnlyDictionary<string, ParticleSystem>? systems, Func<ParticleSystem, IReadOnlyList<SheetSequence>?> sheets)
        {
            Shutdown();

            if (systems is null || !systems.TryGetValue(systemName, out ParticleSystem? system))
            {
                return;
            }

            System = new ParticleEffect(system, systems, sheets, ParticleEffects.NextCreatedSeed());
            Name = systemName;

            if (StartActivated)
            {
                Startup();
            }
        }

        /// <summary>`StartupParticleCollection` (:542).</summary>
        public void Startup()
        {
            _lastTime = null;
            Started = true;
        }

        /// <summary>`ShutdownParticleCollection` (:552).</summary>
        public void Shutdown()
        {
            System = null;
            Started = false;
        }

        /// <summary>`Update( flTime )` (:480): a real-time step, and a finished or expired one ended — or remade, looping.</summary>
        /// <param name="time">`engine->Time()`.</param>
        /// <param name="curTime">`gpGlobals->curtime`, which the end time is on.</param>
        /// <param name="systems">The definitions.</param>
        /// <param name="sheets">Each system's sheet.</param>
        /// <returns>Whether it still has a system.</returns>
        public bool Update(float time, float curTime, IReadOnlyDictionary<string, ParticleSystem>? systems, Func<ParticleSystem, IReadOnlyList<SheetSequence>?> sheets)
        {
            if (System is not { } system || !Started)
            {
                return false;
            }

            float step = time - (_lastTime ?? time);

            _lastTime = time;

            // Every control point at its value, all oriented by `m_Angles`.
            FreeCamera basis = new() { Angles = (Angles.X, Angles.Y, Angles.Z) };
            ((float X, float Y, float Z) forward, (float X, float Y, float Z) right, (float X, float Y, float Z) up) = basis.Basis();

            for (int point = MaxControlPoints - 1; point >= 0; point--)
            {
                system.SetControlPoint(point, new ParticleControlPoint(
                    ControlPoints[point], new Vector3(forward.X, forward.Y, forward.Z), new Vector3(right.X, right.Y, right.Z), new Vector3(up.X, up.Y, up.Z)));
            }

            if (Emitting)
            {
                system.Step(system.ControlPoint(0), step);
            }
            else
            {
                system.Fade(step);
            }

            if (system.Finished || curTime >= EndTime)
            {
                System = null;

                // "Loop if we're supposed to".
                if (Loop && Name.Length > 0 && !ForceStopped && systems is not null && systems.TryGetValue(Name, out ParticleSystem? again))
                {
                    System = new ParticleEffect(again, systems, sheets, ParticleEffects.NextCreatedSeed());
                    Emitting = true;
                }

                _lastTime = null;
            }

            return System is not null;
        }
    }
}
