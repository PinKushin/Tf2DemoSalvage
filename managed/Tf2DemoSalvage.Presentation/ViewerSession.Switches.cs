using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Logging;
using Tf2DemoSalvage.Render;

namespace Tf2DemoSalvage.Presentation;

public sealed partial class ViewerSession
{
    /// <summary>Which <c>mat_fullbright</c> substitution is showing.</summary>
    public Fullbright Fullbright { get; private set; } = Fullbright.Off;

    /// <summary>Whether surfaces are coloured by category; the menu item is its tick, and reports every change here.</summary>
    private bool _surfaceColours;

    /// <summary>Which of Valve's per-surface debug views are showing.</summary>
    private DebugModes _debug = DebugModes.None;

    /// <summary>Chooses a lighting substitution and ticks the matching menu item.</summary>
    /// <param name="mode">Which substitution to show.</param>
    /// <remarks>
    /// Public so a test can drive it without synthesising a menu click. The menu items call this
    /// too, so there is one path rather than a UI path and a test path that can disagree.
    /// </remarks>
    public void SetFullbright(Fullbright mode)
    {
        Fullbright = mode;

        _window.ShowFullbright((int)mode);

        _renderLog.LogInformation("{Message}", $"mat_fullbright {(int)mode}");

        if (_device is { } device)
        {
            device.Fullbright = mode;
        }

        _window.Redraw();
    }

    /// <summary>How full screen is entered.</summary>
    // The stray second summary removed here on 2026-08-26 — "Whether the viewport is filling the
    // screen" — belongs to `IsFullScreen`, sixty lines below, which has its own.
    public bool FullScreenExclusive => _settingsOwner.FullScreenExclusive;

    /// <summary>Chooses how full screen is entered, and remembers it.</summary>
    /// <param name="exclusive">The exclusive mode; borderless otherwise.</param>
    /// <remarks>
    /// Applied immediately when already full screen, so the choice can be judged by making it
    /// rather than by restarting. What is remembered, saved and said is the settings owner's
    /// (`SettingsPresenter.SetFullScreenExclusive`); the check marks and the device are this window's.
    /// </remarks>
    public void SetFullScreenExclusive(bool exclusive)
    {
        _window.ShowFullScreenExclusive(exclusive);

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_settingsOwner.SetFullScreenExclusive(exclusive, _window.IsFullScreen && _device is { } device ? device.SetExclusiveFullScreen : null)
            is { } said)
        {
            _window.Status = said;
        }
    }

    /// <summary>How much texture detail is loaded, by level name.</summary>
    public string TextureQuality => _settingsOwner.TextureQuality;

    /// <summary>Chooses how much texture detail to load, and remembers it.</summary>
    /// <param name="quality">The detail level's name, as the menu's item carries it.</param>
    public void SetTextureQuality(string quality)
    {
        _window.ShowTextureQuality(quality);

        _window.Status = _settingsOwner.SetTextureQuality(quality);
    }

    /// <summary>Sets `volume` from the menu, and remembers it; reaches the mix on the next frame.</summary>
    /// <param name="percent">The level's key, as the menu's item carries it.</param>
    public void SetVolume(string percent)
    {
        if (_settingsOwner.SetVolume(percent) is { } status)
        {
            _window.Status = status;
        }
    }

    /// <summary>Sets `snd_mute_losefocus` from the menu, and remembers it.</summary>
    /// <param name="on">Whether to go silent while another window has focus.</param>
    public void SetMuteLoseFocus(bool on)
    {
        if (_settingsOwner.SetMuteLoseFocus(on) is { } status)
        {
            _window.Status = status;
        }
    }

    /// <summary>Turns the surface-category view on or off.</summary>
    /// <param name="on">Whether to colour surfaces by category.</param>
    /// <remarks>
    /// **A world rebuild rather than a repaint, unlike its neighbours below.** The category colour
    /// is baked into the vertex data, so the geometry has to be built again; the others are shader
    /// constants and a repaint is enough.
    /// </remarks>
    public void SetSurfaceColours(bool on)
    {
        _surfaceColours = on;

        // **The legend goes in the log, because a colour nobody can name is not an answer.**
        // Violet was read as "the sign" and white as "an uncoloured surface" during the B154
        // hunt, both wrong, and there was nowhere to look it up.
        _renderLog.LogInformation(
            "{Message}",
            on
                ? "surface colours on — grey-blue brushwork, green terrain, orange props, " +
                  "violet overlays, Valve's magenta chequer where a material resolved to " +
                  "nothing; brush entities take their own FGD colour, magenta where the class " +
                  "states none, as Hammer draws them"
                : "surface colours off");

        // **`ClearWorld` throws away the models too, and only the map came back** (B219). It
        // disposes the world and empties `_packedModels` with the buffers they feed; `Invalidate`
        // re-projects and re-uploads the brushwork, and nothing re-ran `UploadModels`, because
        // `MomentScene` asks for one only when the model set GREW — which after a clear it has not.
        //
        // The owner: *"setting surface colors on removes the viewmodel and doesnt bring it back, so
        // i need you to restart the program"*. Every model, not just the viewmodel, and the log had
        // been saying so 444,242 times in seven seconds.
        //
        // **A repaint, not a rebuild, and the owner asked for exactly that**: *"it shouldnt, which
        // is why its super slow compared to any other view"*. It rebuilt because the colours were
        // baked into vertices — flat colours by surface kind in `MapWorldBuilder`, Valve's per-class
        // colours in `BrushModels` — so switching the view meant rebuilding every vertex in the map.
        //
        // Neither is baked now. A batch carries its `SurfaceCategory` and an instance carries its
        // class colour, so the view is a shader constant like `mat_specular`, and the flag reaches
        // the draw through `SetCamera` on the next frame.
        //
        // **B219 goes with it.** `ClearWorld` was here, and it discarded every model's GPU buffer
        // while only the map was re-uploaded — the viewmodel and every prop gone until a restart.
        // The fix that pairs it with `MomentScene.Uploaded = false` still stands for the other
        // callers; this one no longer needs either.
        _window.Redraw();
    }

    /// <summary>Shows or hides Valve's frame rate meter.</summary>
    /// <param name="on">Whether to draw it.</param>
    /// <remarks>
    /// **Two, not one**, because `cl_showfps 2` is the form that names the worst and best frame as
    /// well as the average, and the smoothed single number is the one this project has repeatedly
    /// found hides a stall.
    /// </remarks>
    public void SetFrameRateMeter(bool on) => _settingsOwner.SetFrameRateMeter(on);

    /// <summary>Shows or hides Valve's position readout.</summary>
    /// <param name="on">Whether to draw it.</param>
    /// <remarks>
    /// **One, not two.** `cl_showpos 1` reports the VIEW's own origin and angles, which is the
    /// question a screenshot is taken to answer — where the camera is. Mode 2 reports the watched
    /// player instead, and for a free-camera shot of a piece of geometry that says nothing about
    /// what is being looked at. Someone who wants the player can still write `cl_showpos 2` in a
    /// config; the menu offers the useful one.
    /// </remarks>
    public void SetPositionReadout(bool on)
    {
        _settingsOwner.SetPositionReadout(on);

        // **Invalidated, unlike the frame-rate meter beside it.** That one is only ever switched on
        // while frames are already being produced, because it exists to measure them. This can be
        // switched on while playback is paused, and a paused viewer redraws on demand — without
        // this the readout would not appear until something else caused a frame.
        _window.Redraw();
    }

    /// <summary>Valve's <c>mat_wireframe</c>.</summary>
    /// <param name="on">Whether to draw edges only.</param>
    public void SetWireframe(bool on) =>
        SetRenderToggle("mat_wireframe", on, static (device, value) => device.Wireframe = value);

    /// <summary>Valve's <c>r_drawworld</c>.</summary>
    /// <param name="on">Whether to draw the level's brushwork.</param>
    public void SetDrawWorld(bool on) =>
        SetRenderToggle("r_drawworld", on, static (device, value) => device.DrawWorld = value);

    /// <summary>Valve's <c>r_drawentities</c>.</summary>
    /// <param name="on">Whether to draw props and models.</param>
    public void SetDrawEntities(bool on) =>
        SetRenderToggle("r_drawentities", on, static (device, value) => device.DrawEntities = value);

    /// <summary>Valve's <c>mat_specular</c>.</summary>
    /// <param name="on">Whether to draw specular reflections.</param>
    /// <remarks>
    /// **A repaint, not a world rebuild: this is a shader constant and the geometry is untouched.**
    /// The rebuild was why reflections appeared instantly while every other debug view waited — it
    /// was doing far more work to get the same repaint.
    /// </remarks>
    public void SetSpecular(bool on) =>
        SetRenderToggle("mat_specular", on, static (device, value) => device.Specular = value);

    /// <summary>Valve's <c>mat_phong</c>.</summary>
    /// <param name="on">Whether materials asking for <c>$phong</c> get their highlight.</param>
    /// <remarks>
    /// **A repaint, like `mat_specular`** — a shader constant on the world camera, no geometry
    /// touched. Added for B170, where the question "is the highlight what washes out a modern
    /// weapon" had no instrument: this viewer had `mat_specular` for cubemap reflections and
    /// nothing at all for phong, so the one manipulation that would settle it could not be made.
    /// </remarks>
    public void SetPhong(bool on)
    {
        // Recorded for this run, as a bind's `mat_phong` is, so the menu's check re-reads what the menu set (D215).
        _settingsOwner.SetConVar("mat_phong", on ? "1" : "0");
        SetRenderToggle("mat_phong", on, static (device, value) => device.Phong = value);
    }

    /// <summary>Turns one of Valve's per-surface debug views on or off.</summary>
    /// <param name="apply">Which flag the menu item sets, supplied by the item itself.</param>
    /// <param name="on">Whether to turn it on.</param>
    /// <remarks>
    /// **The flag arrives as a function rather than a name** (B210). It used to be a name matched by
    /// a `switch` with fewer arms than there were menu items, so one view was unreachable and its
    /// shortcut toggled a different one.
    /// </remarks>
    public void SetDebugMode(Func<DebugModes, bool, DebugModes> apply, bool on)
    {
        ArgumentNullException.ThrowIfNull(apply);

        _debug = apply(_debug, on);

        _renderLog.LogInformation("{Message}", $"debug views: {_debug}");

        if (_device is { } device)
        {
            device.Debug = _debug;
        }

        // **Ask for a repaint.** The viewport draws on demand rather than continuously, so updating
        // a shader constant is not enough on its own — the change reaches the GPU and then waits for
        // an unrelated event to show it. That is what made these appear only when the camera moved.
        _window.Redraw();
    }

    /// <summary>Logs a render switch, tells the device, and asks for a repaint.</summary>
    /// <param name="cvar">Valve's name for the switch, for the log.</param>
    /// <param name="on">The new state.</param>
    /// <param name="apply">How the device is told.</param>
    /// <remarks>
    /// **Four handlers were this same three-line body** — wireframe, draw-world, draw-entities and
    /// specular — each written out separately with its own `if (_device is { } …)`. They are one
    /// method because the fourth copy is where a difference starts hiding: B210 was exactly that,
    /// a list of near-identical cases where one did something else.
    ///
    /// **A repaint rather than a rebuild.** Every switch here is a shader constant, so the geometry
    /// on the device is still correct and only the picture is stale.
    /// </remarks>
    private void SetRenderToggle(string cvar, bool on, Action<IRenderDevice, bool> apply)
    {
        _renderLog.LogInformation("{Message}", $"{cvar} {(on ? 1 : 0)}");

        if (_device is { } device)
        {
            apply(device, on);
        }

        _window.Redraw();
    }

    /// <summary>Changes the HUD and remembers the choice, hot-swapped with no restart.</summary>
    /// <param name="hud">A folder or `.vpk` under <see cref="ProgramCustomFolder"/>, or null for TF2's stock HUD.</param>
    /// <remarks>
    /// **The rebuild is not done here.** The presenter already compares the stored choice against what it last built
    /// and remakes its archives when they differ, so setting it is the whole swap; the next frame's HUD reads through
    /// the new archives. `--hud` sets it at launch and the remembered choice from settings is the fallback (D193).
    /// </remarks>
    public void SetChosenHud(string? hud)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_presenter.SetChosenHud(hud) is { } said)
        {
            _window.Status = said;
        }
    }
}
