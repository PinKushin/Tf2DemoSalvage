using System;
using System.Collections.Concurrent;

namespace Tf2DemoSalvage.Presentation;

/// <summary>VGUI textures decoded ahead of their first draw, held until the renderer takes them (B595 part 6).</summary>
/// <typeparam name="T">The decoded texture.</typeparam>
/// <param name="resolve">Decodes a material from the files the HUD reads; null when it does not resolve.</param>
/// <param name="files">Those files as they stand now; a change drops what was held.</param>
/// <remarks>
/// `CHud::Init` runs each icon through `SetupNewHudTexture` (`hud.cpp:708-733`), which loads the texture with
/// `DrawSetTextureFile` and draws from it later. Our size query decoded the sheet at HUD load and kept only its size, so the
/// first draw decoded it again on the frame. Now the size query's decode is kept and the first draw takes it, so the
/// texture is decoded once and holds no second copy after the upload. Thread-safe: the renderer may take on its own thread.
/// </remarks>
public sealed class VguiMaterialLoads<T>(Func<string, T?> resolve, Func<object?> files)
    where T : struct
{
    private readonly ConcurrentDictionary<string, T?> _held = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Lock _gate = new();
    private object? _of;

    /// <summary>Decodes the material now and holds it for <see cref="Take"/>.</summary>
    /// <param name="material">The VGUI material path.</param>
    /// <returns>The texture, or null when it does not resolve.</returns>
    public T? Load(string material)
    {
        Current();

        return _held.GetOrAdd(material, resolve);
    }

    /// <summary>The held texture, given up to the caller, or a fresh decode when none is held.</summary>
    /// <param name="material">The VGUI material path.</param>
    /// <returns>The texture, or null when it does not resolve.</returns>
    public T? Take(string material)
    {
        Current();

        return _held.TryRemove(material, out T? held) ? held : resolve(material);
    }

    /// <summary>Drops what was held when the files changed; locked because the renderer may take on its own thread.</summary>
    private void Current()
    {
        object? now = files();

        lock (_gate)
        {
            if (!ReferenceEquals(now, _of))
            {
                _held.Clear();
                _of = now;
            }
        }
    }
}
