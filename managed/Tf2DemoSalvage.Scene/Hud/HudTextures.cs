using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CHudTexture` (game/client/hud.h:37, hud.cpp:585): one icon — a sub-rectangle of a texture, or one character of a font.</summary>
/// <remarks>
/// A font icon's size is measured once, when <see cref="HudTextures"/> sets it up (`CHud::SetupNewHudTexture`,
/// hud.cpp:708), and is never measured again — `RefreshHudTextures` updates only texture icons.
/// </remarks>
public sealed class HudTexture
{
    /// <summary>`szShortName`: the name `GetIcon` finds it by, with its file reference's prefix.</summary>
    public required string ShortName { get; init; }

    /// <summary>`szTextureFile`: the material, or the scheme font's name for a font icon.</summary>
    public required string TextureFile { get; init; }

    /// <summary>`bRenderUsingFont`.</summary>
    public bool RenderUsingFont { get; init; }

    /// <summary>`cCharacterInFont`: a signed `char`, so a byte above 127 reaches `DrawUnicodeChar` sign-extended.</summary>
    public char CharacterInFont { get; init; }

    /// <summary>`rc`: left, top, right, bottom in texels — for a font icon, 0, 0, the character's width and the font's tall.</summary>
    public (int Left, int Top, int Right, int Bottom) Rc { get; internal set; }

    /// <summary>`hFont`, resolved at setup; null is handle 0.</summary>
    public VguiFontAmalgam? Font { get; internal set; }

    /// <summary>`Width()`.</summary>
    public int Width => Rc.Right - Rc.Left;

    /// <summary>`Height()`.</summary>
    public int Height => Rc.Bottom - Rc.Top;

    /// <summary>`DrawSelf( x, y, clr )`: at its own size.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="x">Left.</param>
    /// <param name="y">Top.</param>
    /// <param name="color">The colour.</param>
    public void DrawSelf(IVguiSurface surface, int x, int y, (byte Red, byte Green, byte Blue, byte Alpha) color) =>
        DrawSelf(surface, x, y, Width, Height, color);

    /// <summary>`DrawSelf( x, y, w, h, clr )`: a font icon ignores the size and draws its character at x, y.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="x">Left.</param>
    /// <param name="y">Top.</param>
    /// <param name="wide">Wide.</param>
    /// <param name="tall">Tall.</param>
    /// <param name="color">The colour.</param>
    public void DrawSelf(IVguiSurface surface, int x, int y, int wide, int tall, (byte Red, byte Green, byte Blue, byte Alpha) color)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (RenderUsingFont)
        {
            if (Font is null)
            {
                return;
            }

            surface.DrawSetTextFont(Font);
            surface.DrawSetTextColor(color);
            surface.DrawSetTextPos(x, y);
            surface.DrawUnicodeChar(CharacterInFont);
            return;
        }

        (float s0, float t0, float s1, float t1) = TexCoords(surface);
        surface.DrawSetTexture(TextureFile);
        surface.DrawSetColor(color);
        surface.DrawTexturedSubRect(x, y, x + wide, y + tall, s0, t0, s1, t1);
    }

    /// <summary>`DrawSelfCropped` with a final size (hud.cpp:611), texture icons: the crop interpolated across the icon's coordinates.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="x">Left.</param>
    /// <param name="y">Top.</param>
    /// <param name="cropX">Crop left, in icon texels.</param>
    /// <param name="cropY">Crop top.</param>
    /// <param name="cropWide">Crop wide.</param>
    /// <param name="cropTall">Crop tall.</param>
    /// <param name="finalWide">Drawn wide.</param>
    /// <param name="finalTall">Drawn tall.</param>
    /// <param name="color">The colour.</param>
    /// <remarks>ponytail: a font icon's cropped path (`DrawGetUnicodeCharRenderInfo`) is not ported — no TF2 element crops a font icon; add it with the first that does.</remarks>
    public void DrawSelfCropped(
        IVguiSurface surface, int x, int y, int cropX, int cropY, int cropWide, int cropTall, int finalWide, int finalTall, (byte Red, byte Green, byte Blue, byte Alpha) color)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (RenderUsingFont)
        {
            return;
        }

        float wide = Width;
        float tall = Height;
        (float s0, float t0, float s1, float t1) = TexCoords(surface);
        float spanS = s1 - s0;
        float spanT = t1 - t0;

        surface.DrawSetTexture(TextureFile);
        surface.DrawSetColor(color);
        surface.DrawTexturedSubRect(
            x,
            y,
            x + finalWide,
            y + finalTall,
            s0 + (cropX / wide * spanS),
            t0 + (cropY / tall * spanT),
            s0 + ((cropX + cropWide) / wide * spanS),
            t0 + ((cropY + cropTall) / tall * spanT));
    }

    /// <summary>`EffectiveWidth`: scaled and truncated; a font icon answers its character's width, unscaled.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="scale">The scale.</param>
    /// <returns>The width.</returns>
    public int EffectiveWidth(IVguiSurface surface, float scale)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!RenderUsingFont)
        {
            return (int)(Width * scale);
        }

        return Font is null ? 0 : surface.GetCharacterWidth(Font, CharacterInFont);
    }

    /// <summary>`texCoords`: half a texel in from each edge of `rc` (hud.cpp:728).</summary>
    private (float S0, float T0, float S1, float T1) TexCoords(IVguiSurface surface)
    {
        (int wide, int tall) = surface.DrawGetTextureSize(TextureFile);

        return ((Rc.Left + 0.5f) / wide, (Rc.Top + 0.5f) / tall, (Rc.Right - 0.5f) / wide, (Rc.Bottom - 0.5f) / tall);
    }
}

/// <summary>`CHud::m_Icons`: every icon `scripts/hud_textures.txt` then `scripts/mod_textures.txt` define, found by name without case.</summary>
/// <remarks>
/// hud.cpp:58 `LoadHudTextures`: an entry with a `font` key is a font icon, its `character` the first byte. Anything else is
/// a texture icon once for each file reference it names — `file` with no prefix, then each of `TextureFileRefs` with its
/// `prefix` — so `scattergun` with `dfile` and `dnegfile` is two icons, `d_scattergun` and `dneg_scattergun`.
/// `CHud::Init` (hud.cpp:464) then adds each in load order through `AddSearchableHudIconToList`, which keeps the first of a
/// name: a later file cannot replace an icon.
/// </remarks>
public sealed class HudTextures
{
    private readonly Dictionary<string, HudTexture> _icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many icons are held.</summary>
    public int Count => _icons.Count;

    /// <summary>`CHud::Init`'s load, each font icon set up against the context's fonts.</summary>
    /// <param name="context">The HUD's scheme context; its <see cref="VguiContext.Read"/> is the filesystem.</param>
    /// <returns>The icons.</returns>
    public static HudTextures Load(VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        HudTextures textures = new();
        Func<string, byte[]?> read = context.Read ?? (_ => null);

        foreach (HudTexture texture in Read(read, "scripts/hud_textures"))
        {
            textures.AddSearchable(texture, context);
        }

        foreach (HudTexture texture in Read(read, "scripts/mod_textures"))
        {
            textures.AddSearchable(texture, context);
        }

        return textures;
    }

    /// <summary>`GetIcon`.</summary>
    /// <param name="name">The short name.</param>
    /// <returns>The icon, or null.</returns>
    public HudTexture? GetIcon(string name) => _icons.GetValueOrDefault(name);

    /// <summary>`LoadHudTextures` of one file, without the extension.</summary>
    private static List<HudTexture> Read(Func<string, byte[]?> read, string path)
    {
        List<HudTexture> list = [];

        if (read(path + ".txt") is not { } bytes)
        {
            return list;
        }

        KeyValuesTree root = KeyValuesTree.Load(bytes, path + ".txt", name => read(name));
        List<(string Key, string Prefix)> references = [("file", string.Empty)];

        foreach (KeyValuesTree reference in root.Find("TextureFileRefs")?.Children ?? [])
        {
            references.Add((reference.Name, reference.Find("prefix")?.Value ?? string.Empty));
        }

        foreach (KeyValuesTree entry in root.Find("TextureData")?.Children ?? [])
        {
            if (entry.Find("font")?.Value is { } font)
            {
                string character = entry.Find("character")?.Value ?? string.Empty;

                list.Add(new HudTexture
                {
                    ShortName = entry.Name,
                    TextureFile = font,
                    RenderUsingFont = true,
                    CharacterInFont = character.Length == 0 ? '\0' : (char)(sbyte)Encoding.UTF8.GetBytes(character)[0],
                });
                continue;
            }

            int left = PanelLayout.Atoi(entry.Find("x")?.Value ?? string.Empty);
            int top = PanelLayout.Atoi(entry.Find("y")?.Value ?? string.Empty);
            int right = PanelLayout.Atoi(entry.Find("width")?.Value ?? string.Empty) + left;
            int bottom = PanelLayout.Atoi(entry.Find("height")?.Value ?? string.Empty) + top;

            foreach ((string key, string prefix) in references)
            {
                if (entry.Find(key)?.Value is { } file)
                {
                    list.Add(new HudTexture { ShortName = prefix + entry.Name, TextureFile = file, Rc = (left, top, right, bottom) });
                }
            }
        }

        return list;
    }

    /// <summary>`AddSearchableHudIconToList`, with `SetupNewHudTexture`'s font measurement.</summary>
    private void AddSearchable(HudTexture texture, VguiContext context)
    {
        if (!_icons.TryAdd(texture.ShortName, texture) || !texture.RenderUsingFont)
        {
            return;
        }

        // `GetFont( szTextureFile, true )` from ClientScheme: always the proportional handle.
        texture.Font = context.GetFont(texture.TextureFile, proportional: true);
        texture.Rc = texture.Font is null || context.Surface is not { } surface
            ? (0, 0, 0, 0)
            : (0, 0, surface.GetCharacterWidth(texture.Font, texture.CharacterInFont), surface.GetFontTall(texture.Font));
    }
}
