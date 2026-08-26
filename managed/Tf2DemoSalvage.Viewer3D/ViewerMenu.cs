using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>What the menu can ask the viewer to do.</summary>
/// <param name="OpenDemo">Open the file picker.</param>
/// <param name="Exit">Close the window.</param>
/// <param name="SetFullScreen">Enter or leave full screen.</param>
/// <param name="SetFullScreenMode">Choose borderless or exclusive.</param>
/// <param name="SetTextureQuality">Choose which mip level to load from.</param>
/// <param name="SetSurfaceColours">Colour surfaces by category.</param>
/// <param name="SetFrameRateMeter">Show Valve's frame rate meter.</param>
/// <param name="SetWireframe">Draw edges only.</param>
/// <param name="SetFullbright">Substitute the lighting or the texture.</param>
/// <param name="SetDrawWorld">Draw the level's brushwork.</param>
/// <param name="SetDrawEntities">Draw props and models.</param>
/// <param name="SetDebugMode">Turn one per-surface debug view on or off.</param>
/// <param name="SetSpecular">Add cubemap reflections.</param>
/// <param name="Screenshot">Write a picture of the viewport.</param>
/// <remarks>
/// **Delegates rather than a reference to the form, and that is the point of the split.** A menu
/// that holds a <c>MainForm</c> is a menu for that one window; a menu that holds fourteen actions
/// describes what a viewer frontend must be able to do, which is the list any replacement — ImGui,
/// Qt, anything — has to satisfy. The owner's reason for wanting the seam, 2026-08-26: *"that makes
/// the swap to ImGUI or QT or any other cross platform UI frontend much easier"*.
///
/// **It also keeps <c>MainForm</c>'s methods private** where they were private. An extraction that
/// widens a dozen members to <c>internal</c> so a neighbour can call them has moved the code and
/// kept the coupling.
/// </remarks>
internal readonly record struct ViewerMenuActions(
    Action OpenDemo,
    Action Exit,
    Action<bool> SetFullScreen,
    Action<FullScreenMode> SetFullScreenMode,
    Action<TextureQuality> SetTextureQuality,
    Action<bool> SetSurfaceColours,
    Action<bool> SetFrameRateMeter,
    Action<bool> SetWireframe,
    Action<Fullbright> SetFullbright,
    Action<bool> SetDrawWorld,
    Action<bool> SetDrawEntities,
    Action<Func<DebugModes, bool, DebugModes>, bool> SetDebugMode,
    Action<bool> SetSpecular,
    Action Screenshot);

/// <summary>The viewer's main menu: the strip, and the items whose state is read elsewhere.</summary>
/// <remarks>
/// **This was 363 lines inside <c>MainForm</c>'s constructor** (B188, D90) — about half of it, and
/// the largest single thing left in the file after the sampling and the frame accounting moved out.
///
/// **It is view code and it stays view code**, which is worth saying because the rest of the
/// thin-view work was about moving non-view code out. Menus are WinForms; nothing here belongs in
/// Presentation. What was wrong was not the layer, it was that one constructor built the window,
/// composed twenty collaborators, laid out the controls AND built the menu.
///
/// **The item identifiers stay on <c>MainForm</c>** because tests and the UI suite address them as
/// <c>MainForm.ViewMenuName</c> and <c>MainForm.ScreenshotItemName</c>. Moving them would be a
/// rename dressed as a refactor.
/// </remarks>
internal sealed class ViewerMenu : IDisposable
{
    /// <summary>The strip to dock on the form.</summary>
    public MenuStrip Strip { get; }

    /// <summary>Whether the viewport fills the screen. Checked state is read by the form.</summary>
    public ToolStripMenuItem FullScreen { get; }

    /// <summary>Borderless full screen.</summary>
    public ToolStripMenuItem Borderless { get; }

    /// <summary>Exclusive full screen.</summary>
    public ToolStripMenuItem Exclusive { get; }

    /// <summary>The surface-category view. Its checked state reaches the world build.</summary>
    public ToolStripMenuItem SurfaceColours { get; }

    /// <summary>Valve's frame rate meter.</summary>
    public ToolStripMenuItem FrameRate { get; }

    /// <summary>Edges only.</summary>
    public ToolStripMenuItem Wireframe { get; }

    /// <summary>Cubemap reflections.</summary>
    public ToolStripMenuItem Specular { get; }

    /// <summary>The lighting submenu, whose three items are checked as a group.</summary>
    public ToolStripMenuItem FullbrightMenu { get; }

    /// <summary>Draw the level's brushwork.</summary>
    public ToolStripMenuItem DrawWorld { get; }

    /// <summary>Draw props and models.</summary>
    public ToolStripMenuItem DrawEntities { get; }

    /// <summary>The per-surface debug submenu.</summary>
    public ToolStripMenuItem DebugMenu { get; }

    /// <summary>The texture-quality items, by level, so one can be checked and the rest cleared.</summary>
    public IReadOnlyDictionary<TextureQuality, ToolStripMenuItem> TextureQualityItems { get; }

    /// <summary>Builds the whole menu.</summary>
    /// <param name="actions">What each item asks the viewer to do.</param>
    /// <param name="settings">The saved settings, for the items that open already checked.</param>
    /// <param name="bindings">Which key performs which action, so no shortcut is written in here.</param>
    /// <remarks>
    /// **Initial checked state comes from settings, not from the form**, so the menu can be built
    /// before anything else exists. Three items open checked because their feature is on by
    /// default — drawing the world, drawing entities and reflections — and those are literals rather
    /// than settings because they are not saved.
    /// </remarks>
    public ViewerMenu(ViewerMenuActions actions, ViewerSettings settings, KeyBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        Keys Shortcut(ViewerAction action) => KeyNames.Resolve(bindings.KeyFor(action));

        Dictionary<TextureQuality, ToolStripMenuItem> textureQualityItems = [];

        TextureQualityItems = textureQualityItems;

        Strip = new MenuStrip { Name = "MainMenu", AccessibleName = "Main menu" };

        ToolStripMenuItem file = new("&File")
        {
            Name = MainForm.FileMenuId,
            AccessibleName = "File menu",
        };

        ToolStripMenuItem open = new("&Open demo...")
        {
            Name = MainForm.OpenDemoItemId,
            AccessibleName = "Open demo",
            ShortcutKeys = Shortcut(ViewerAction.OpenDemo),
        };
        open.Click += (_, _) => actions.OpenDemo();

        ToolStripMenuItem exit = new("E&xit")
        {
            Name = MainForm.ExitItemId,
            AccessibleName = "Exit",
        };
        exit.Click += (_, _) => actions.Exit();

        FullScreen = new ToolStripMenuItem("&Full screen")
        {
            Name = MainForm.FullScreenItemId,
            AccessibleName = MainForm.FullScreenItemName,
            ShortcutKeys = Shortcut(ViewerAction.FullScreen),
            CheckOnClick = true,
        };
        FullScreen.CheckedChanged += (_, _) => actions.SetFullScreen(FullScreen.Checked);

        // **Both modes offered, because neither is right for everyone.** Borderless always works
        // and alt-tabs instantly; exclusive is the lower-latency path and can be refused by DXGI.
        Borderless = new ToolStripMenuItem("&Borderless")
        {
            Name = MainForm.BorderlessItemId,
            AccessibleName = "Borderless full screen",
            Checked = settings.FullScreenMode == FullScreenMode.Borderless,
        };
        Borderless.Click += (_, _) => actions.SetFullScreenMode(FullScreenMode.Borderless);

        Exclusive = new ToolStripMenuItem("&Exclusive")
        {
            Name = MainForm.ExclusiveItemId,
            AccessibleName = "Exclusive full screen",
            Checked = settings.FullScreenMode == FullScreenMode.Exclusive,
        };
        Exclusive.Click += (_, _) => actions.SetFullScreenMode(FullScreenMode.Exclusive);

        ToolStripMenuItem fullScreenMode = new("Full screen &mode")
        {
            Name = "FullScreenModeMenu",
            AccessibleName = "Full screen mode",
        };
        fullScreenMode.DropDownItems.Add(Borderless);
        fullScreenMode.DropDownItems.Add(Exclusive);

        // **Texture detail, chosen from the game's own mip chain.** Not a quality slider over
        // something resampled here: each level is an image Valve generated when the texture was
        // made, so a lower setting is a smaller read and a smaller upload rather than extra work.
        ToolStripMenuItem textureQuality = new("&Texture quality")
        {
            Name = MainForm.TextureQualityMenuId,
            AccessibleName = "Texture quality",
        };

        foreach (TextureQuality quality in new[]
        {
            TextureQuality.Full, TextureQuality.High, TextureQuality.Medium, TextureQuality.Low,
        })
        {
            TextureQuality chosen = quality;
            int pixels = (int)quality;

            ToolStripMenuItem item = new(
                pixels == 0
                    ? "&Full"
                    : string.Create(CultureInfo.InvariantCulture, $"{quality} ({pixels} px)"))
            {
                Name = "TextureQuality" + quality,
                AccessibleName = "Texture quality " + quality,
                Checked = settings.TextureQuality == quality,
            };

            item.Click += (_, _) => actions.SetTextureQuality(chosen);
            textureQualityItems.Add(quality, item);
            textureQuality.DropDownItems.Add(item);
        }

        ToolStripMenuItem view = new("&View")
        {
            Name = MainForm.ViewMenuId,
            AccessibleName = MainForm.ViewMenuName,
        };

        // **A diagnostic view, kept in the product deliberately.** It answers "is anything here,
        // and what kind of thing is it", which a textured picture cannot - and which cost hours
        // this session when terrain, a material and a prop each went missing while the map still
        // looked like a map.
        SurfaceColours = new ToolStripMenuItem("Surface &colours")
        {
            Name = MainForm.SurfaceColoursItemId,
            CheckOnClick = true,
            ShortcutKeys = Shortcut(ViewerAction.SurfaceColours),
        };

        SurfaceColours.CheckedChanged += (_, _) => actions.SetSurfaceColours(SurfaceColours.Checked);

        // **A menu item as well as the cvar, because a cvar nobody can find is a cvar nobody uses.**
        // The owner's words are the requirement: "we need a fps overlay too, we dont have one so i
        // have no idea what fps we are rendering at and cant tell stutter in the demo from stutter
        // in the decode, from stutter in fps" — and later, "we might have a fps overlay i just dont
        // normally turn on, which you launching for me to check the sounds would have allowed me to
        // check". Something reached for mid-investigation has to be one keypress away.
        //
        // **It sets `cl_showfps 2`, not 1**, because the smoothed meter is the one that answers his
        // question. Mode 1 is an instantaneous rate that jumps every frame; mode 2 carries the worst
        // and best single frame beside the average, and an occasional long frame against a healthy
        // average is exactly what stutter looks like.
        //
        // **F8, which is free.** F9 is surface colours, F10 wireframe, F11 full screen and F12 the
        // screenshot — and F11 colliding with full screen silently broke it for days (B165), so a
        // new shortcut gets checked against the four already here rather than assumed spare.
        FrameRate = new ToolStripMenuItem("&Frame rate")
        {
            Name = MainForm.FrameRateItemId,
            CheckOnClick = true,
            Checked = settings.ShowFrameRate != 0,
            ShortcutKeys = Shortcut(ViewerAction.FrameRate),
            AccessibleName = "Frame rate",
            AccessibleDescription =
                "Draws TF2's own frame rate meter in the top right: the average, the worst and best " +
                "single frame in brackets, and how long this frame took.",
        };

        FrameRate.CheckedChanged += (_, _) => actions.SetFrameRateMeter(FrameRate.Checked);

        // **Valve's `mat_wireframe`, replacing the brush outline that used to sit on F10.** The
        // outline drew precomputed BSP edge segments as an overlay — 60,764 of them, built for the
        // overhead view and, as the owner put it, "like an ortho overlay". It could not answer the
        // question a wireframe is for, because it drew edges from the map file rather than the
        // triangles actually submitted: no props, no models, nothing about what reached the GPU.
        //
        // This one is a rasteriser fill mode over every pass, so an edge on screen means that
        // triangle was drawn. That is the difference between "not submitted" and "submitted and
        // invisible", which nothing else in this viewer can distinguish.
        Wireframe = new ToolStripMenuItem("&Wireframe")
        {
            Name = MainForm.WireframeItemId,
            CheckOnClick = true,
            Checked = false,
            ShortcutKeys = Shortcut(ViewerAction.Wireframe),
            AccessibleName = "Wireframe",
            AccessibleDescription =
                "Draws every surface as edges only, so geometry that never reached the screen can " +
                "be told apart from geometry that is drawn but invisible.",
        };

        Wireframe.CheckedChanged += (_, _) => actions.SetWireframe(Wireframe.Checked);

        // **`mat_specular`, and it is a diagnostic before it is a preference.** A cubemap
        // reflection is ADDED to an opaque surface, so a prop whose envmap term dominates draws in
        // the colour of whatever its cubemap holds — against a sky, that is the sky, and the prop
        // reads as geometry that was never drawn. Surface colours returns from the shader before
        // the reflection is added, which is why a surface can be invisible in the textured view
        // and present in the category view: the same triangles, coloured differently.
        //
        // **No shortcut, because F8 was already the frame rate's and every function key is taken.**
        // This carried ShortcutKeys = Keys.F8 alongside the frame-rate item, so two menu items
        // claimed one key and one of them silently did nothing — the same defect as F12, found in
        // the same audit, and the third instance of it in this file after B165's F11.
        //
        // The frame rate keeps F8 because it has a stated reason: it mirrors TF2's own cl_showfps
        // (B174). Reflections is a debug toggle with no such claim, and inventing Ctrl+F8 for it
        // would be an arbitrary answer to a question nobody asked. The menu still reaches it.
        Specular = new ToolStripMenuItem("&Reflections")
        {
            Name = MainForm.SpecularItemId,
            CheckOnClick = true,
            Checked = true,
            AccessibleName = "Reflections",
            AccessibleDescription =
                "Adds cubemap reflections to surfaces that ask for them. Turn off to see whether " +
                "a reflection is hiding a surface.",
        };

        Specular.CheckedChanged += (_, _) => actions.SetSpecular(Specular.Checked);

        // **A submenu of three, because `mat_fullbright` has three states.** Offering it as a
        // checkbox would be the same mistake as reading the cvar's name and assuming a boolean —
        // and it is the more useful state, lighting-only, that a checkbox would drop.
        FullbrightMenu = new ToolStripMenuItem("&Lighting")
        {
            Name = MainForm.FullbrightItemId,
            AccessibleName = "Lighting",
            AccessibleDescription =
                "Substitutes the lighting or the texture, to tell a shadow apart from a dark " +
                "texture and a painted shape apart from a lit one.",
        };

        // **The keys come from the binding table, and the three actions are numbered after
        // `mat_fullbright`'s own argument** (B214, D101). They were F5, F6 and F7 — all three of
        // which TF2 binds to something else, and F5 is its SCREENSHOT key.
        foreach ((Fullbright mode, string label, ViewerAction action) in new[]
        {
            (Fullbright.Off, "&Normal", ViewerAction.FullbrightOff),
            (Fullbright.NoLighting, "&No lighting (mat_fullbright 1)", ViewerAction.FullbrightNoLighting),
            (Fullbright.LightingOnly, "Lighting &only (mat_fullbright 2)", ViewerAction.FullbrightLightingOnly),
        })
        {
            Fullbright chosen = mode;

            ToolStripMenuItem item = new(label)
            {
                Name = MainForm.FullbrightItemId + chosen,
                ShortcutKeys = Shortcut(action),
                Checked = chosen == Fullbright.Off,
            };

            item.Click += (_, _) => actions.SetFullbright(chosen);

            FullbrightMenu.DropDownItems.Add(item);
        }

        // **`r_drawworld` and `r_drawentities`, which answer "which pass owns this".** The question
        // comes up the moment something is drawn twice, in the wrong order, or by code nobody
        // expected — and it took a day to answer by hand when static props turned out to be
        // inheriting the overlay pass's blend state (B154).
        DrawWorld = new ToolStripMenuItem("Draw &world")
        {
            Name = MainForm.DrawWorldItemId,
            CheckOnClick = true,
            Checked = true,
            AccessibleName = "Draw world",
            AccessibleDescription = "Draws map brushwork and its overlays. Turn off to see only entities.",
        };

        DrawWorld.CheckedChanged += (_, _) => actions.SetDrawWorld(DrawWorld.Checked);

        DrawEntities = new ToolStripMenuItem("Draw &entities")
        {
            Name = MainForm.DrawEntitiesItemId,
            CheckOnClick = true,
            Checked = true,
            AccessibleName = "Draw entities",
            AccessibleDescription = "Draws static props and models. Turn off to see only the map.",
        };

        DrawEntities.CheckedChanged += (_, _) => actions.SetDrawEntities(DrawEntities.Checked);

        // **A submenu of independent switches, because Valve's are independent cvars.** Grouping
        // them as radio items would be tidier and would misrepresent the engine: mat_drawflat and
        // mat_luxels compose, and seeing a luxel grid on flat-shaded geometry is a legitimate thing
        // to want when a shadow looks wrong and you cannot tell whether the texture is confusing
        // you.
        DebugMenu = new ToolStripMenuItem("&Debug views")
        {
            Name = MainForm.DebugMenuItemId,
            AccessibleName = "Debug views",
            AccessibleDescription =
                "Valve's per-surface debug visualisations: flat shading, the luxel grid, and " +
                "normal maps shown as colour.",
        };

        // **Each entry carries the flag it sets, and that is a bug fix rather than a tidy-up**
        // (B210). This was a name and a `switch` on that name — five arms with a default that set
        // `LeafVis`, against a list of SIX entries. So `mat_showlowresimage` could not be reached
        // from the UI at all, and Ctrl+T silently toggled the leaf box, which Ctrl+L then fought
        // over. Everything around it was tested: the flag, the renderer that reads it, a render
        // test, and a shortcut-collision test proving Ctrl+T claims a key nothing else has.
        //
        // The mapping sits beside the label now, so a seventh mode cannot be added without saying
        // what it sets — rather than being a second list, elsewhere, that has to agree with this one.
        // **Every key here comes from the binding table now** (B214, D101). F1 and F2 were TF2's
        // `+showroundinfo` and `show_quest_log`, so a pasted config took them; F3 and F4 stayed,
        // because those are two of the five function keys TF2 leaves alone.
        foreach ((string label, string cvar, ViewerAction bound, Func<DebugModes, bool, DebugModes> set)
            in new (string, string, ViewerAction, Func<DebugModes, bool, DebugModes>)[]
        {
            ("Flat &shading (mat_drawflat)", nameof(DebugModes.DrawFlat), ViewerAction.DrawFlat,
                static (modes, on) => modes with { DrawFlat = on }),

            ("&Luxel grid (mat_luxels)", nameof(DebugModes.Luxels), ViewerAction.Luxels,
                static (modes, on) => modes with { Luxels = on }),

            ("&Normal maps (mat_normalmaps)", nameof(DebugModes.NormalMaps), ViewerAction.NormalMaps,
                static (modes, on) => modes with { NormalMaps = on }),

            ("Bump &basis (mat_bumpbasis)", nameof(DebugModes.BumpBasis), ViewerAction.BumpBasis,
                static (modes, on) => modes with { BumpBasis = on }),


            // **Not F11, which is full screen — this collided and full screen lost.** The debug
            // group runs F1..F4 and every remaining function key was already taken (F5..F7
            // lighting, F8 reflections, F9 surface colours, F10 wireframe, F11 full screen, F12
            // capture), so this one reached for F11 without checking. WinForms dispatches a
            // duplicate shortcut to one item, and the later registration won: pressing F11 toggled
            // the leaf box and the window never went full screen.
            //
            // **Three UI tests went red the moment it landed and stayed red**, which is the part
            // worth keeping. The owner spotted it by eye — "the app never went full screen, it did
            // seem to try to start the leaf debug though" — and that sentence names both halves of
            // a collision that no single test could describe.
            //
            // Off the function-key run rather than onto Shift+F11, deliberately: a modified twin of
            // the full-screen key is a mis-press away from the bug this fixes. Ctrl+L is mnemonic
            // for leaf, and the menu shows the binding.
            ("Leaf &box (mat_leafvis)", nameof(DebugModes.LeafVis), ViewerAction.LeafVis,
                static (modes, on) => modes with { LeafVis = on }),

            // **Ctrl+T, and for the same reason as Ctrl+L above: the function keys are full.** The
            // last of B153's set, and the only one that needed the asset rather than a shader
            // branch — every VTF's thumbnail had been skipped on the way past until now.
            ("Low-res &image (mat_showlowresimage)",
                nameof(DebugModes.ShowLowResImage),
                ViewerAction.LowResImage,
                static (modes, on) => modes with { ShowLowResImage = on }),
        })
        {
            string which = cvar;
            Func<DebugModes, bool, DebugModes> apply = set;

            ToolStripMenuItem item = new(label)
            {
                Name = MainForm.DebugMenuItemId + which,
                CheckOnClick = true,
                ShortcutKeys = Shortcut(bound),
            };

            item.CheckedChanged += (sender, _) =>
            {
                if (sender is ToolStripMenuItem toggled)
                {
                    actions.SetDebugMode(apply, toggled.Checked);
                }
            };

            DebugMenu.DropDownItems.Add(item);
        }

        // **F12 is bound ONCE, in ProcessCmdKey, and this item only DISPLAYS it.** It carried
        // ShortcutKeys = Keys.F12 as well, so the key was registered twice — by the menu and by the
        // form — and pressing it did nothing at all: no file, no log line, no error. The owner spotted
        // the shape immediately: "if f12 is double bound it wont work".
        //
        // This is the second time in this file. B165 was the same mistake on F11, which silently
        // broke full screen for days. A shortcut belongs to one owner; the other one says so in
        // text.
        ToolStripMenuItem screenshot = new("Save a &screenshot")
        {
            Name = MainForm.ScreenshotItemId,
            ShortcutKeyDisplayString = "F12",
            AccessibleName = MainForm.ScreenshotItemName,
            AccessibleDescription = "Writes a picture of the viewport beside the viewer's log.",
        };

        screenshot.Click += (_, _) => actions.Screenshot();

        view.DropDownItems.Add(screenshot);
        view.DropDownItems.Add(Wireframe);
        view.DropDownItems.Add(Specular);
        view.DropDownItems.Add(FullbrightMenu);
        view.DropDownItems.Add(DrawWorld);
        view.DropDownItems.Add(DrawEntities);
        view.DropDownItems.Add(DebugMenu);
        view.DropDownItems.Add(SurfaceColours);
        view.DropDownItems.Add(FrameRate);
        view.DropDownItems.Add(FullScreen);
        view.DropDownItems.Add(fullScreenMode);
        view.DropDownItems.Add(textureQuality);

        file.DropDownItems.Add(open);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(exit);

        Strip.Items.Add(file);
        Strip.Items.Add(view);
    }

    /// <summary>Disposes the strip and every item it built.</summary>
    /// <remarks>
    /// **Ownership moved with the items** (B188, D90). `MainForm.Dispose` used to name thirteen of
    /// these one at a time, under a comment admitting why: *"Both are in Controls, which
    /// base.Dispose already walks — but the analyzer cannot see that ownership, and stating it costs
    /// nothing and is true."* The same reasoning applies here and the same explicitness answers it,
    /// but now it sits beside the code that constructed them.
    ///
    /// **Disposing the strip already cascades**, since a `ToolStrip` owns its items and a submenu
    /// owns its drop-down. The named calls are belt and braces for the analyzer; `Dispose` is
    /// idempotent on WinForms components, so the repetition is free.
    /// </remarks>
    public void Dispose()
    {
        Borderless.Dispose();
        Exclusive.Dispose();

        foreach (ToolStripMenuItem item in TextureQualityItems.Values)
        {
            item.Dispose();
        }

        Wireframe.Dispose();
        FrameRate.Dispose();
        Specular.Dispose();

        // Disposing the submenu disposes the three items it owns.
        FullbrightMenu.Dispose();
        DrawWorld.Dispose();
        DrawEntities.Dispose();
        DebugMenu.Dispose();
        SurfaceColours.Dispose();
        FullScreen.Dispose();

        Strip.Dispose();
    }
}
