using System;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What one process shares across every VGUI root: the surface, its font manager, and the localised strings.</summary>
/// <remarks>
/// vguimatsurface.dll has one `CFontManager` and one surface, so the client's HUD and the engine's tools panel draw
/// through the same glyph cache into the same frame; each root has only its own scheme. A new screen size makes a new
/// manager and draw list, and each root loads its scheme again, as the engine reloads every scheme on a resolution change.
/// The strings are the files the engine, GameUI and client add at start (`resource/%s_%%language%%.txt` with the game's
/// directory, then `gameui_`, `platform_`, `vgui_` and `chat_`); their relative order is not read from the binaries — a
/// token defined twice across them would take whichever this order loads last. `closecaption_` waits for captions.
/// </remarks>
/// <param name="read">Reads a game file by path.</param>
/// <param name="fullPath">A game path to the loose file on disk, for custom fonts.</param>
/// <param name="gdi">The GDI adapter fonts are made through.</param>
/// <param name="textureSize">A material's texture size, for the draw list.</param>
/// <param name="language">The game's language.</param>
public sealed class VguiSurfaceHost(
    Func<string, byte[]?> read, Func<string, string?> fullPath, IVguiGdi gdi, Func<string, (int Wide, int Tall)> textureSize, string language = "english")
{
    private static readonly string[] LanguageFiles =
    [
        "resource/valve_%language%.txt", "resource/tf_%language%.txt", "resource/gameui_%language%.txt",
        "resource/platform_%language%.txt", "resource/vgui_%language%.txt", "resource/chat_%language%.txt",
    ];

    private VguiLocalize? _localize;
    private VguiFontManager? _fonts;
    private VguiDrawList? _list;

    /// <summary>The screen's width.</summary>
    public int Wide { get; private set; }

    /// <summary>The screen's height.</summary>
    public int Tall { get; private set; }

    /// <summary>This frame's draw list.</summary>
    public VguiDrawList List => _list ?? throw new InvalidOperationException("BeginFrame makes the draw list.");

    /// <summary>The localised strings, loaded on first use.</summary>
    public VguiLocalize Localize
    {
        get
        {
            if (_localize is null)
            {
                _localize = new VguiLocalize(language);

                foreach (string file in LanguageFiles)
                {
                    _localize.AddFile(file, read);
                }
            }

            return _localize;
        }
    }

    /// <summary>Starts a frame: a new manager and list when the screen changed size, and the list emptied.</summary>
    /// <param name="wide">The screen's width.</param>
    /// <param name="tall">Its height.</param>
    public void BeginFrame(int wide, int tall)
    {
        if (_list is null || (Wide, Tall) != (wide, tall))
        {
            (Wide, Tall) = (wide, tall);
            _fonts = new VguiFontManager(gdi);
            _list = new VguiDrawList(textureSize, _fonts);
        }

        _list.Clear();
    }

    /// <summary>`LoadSchemeFromFile`: a scheme's colours, borders and fonts, at this screen size.</summary>
    /// <param name="path">The scheme file.</param>
    /// <returns>The context panels under that scheme are laid out and painted with.</returns>
    public VguiContext LoadScheme(string path)
    {
        VguiFontManager fonts = _fonts ?? throw new InvalidOperationException("BeginFrame makes the font manager.");
        KeyValuesTree root = KeyValuesTree.Load(read(path) ?? [], path, read);
        VguiScheme scheme = VguiScheme.Load(root);
        VguiSchemeFonts schemeFonts = VguiSchemeFonts.Load(root, fonts, fullPath, language, Tall);

        return new VguiContext(scheme, VguiBorders.Load(root, scheme, Tall), root.FindOrCreate("Fonts"), Wide, Tall, language, schemeFonts)
        {
            Surface = List,
            Localize = Localize.Find,
            Read = read,
        };
    }

    /// <summary>Reads a game file, for a root loading its own `.res`.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The bytes, or null.</returns>
    public byte[]? Read(string path) => read(path);
}
