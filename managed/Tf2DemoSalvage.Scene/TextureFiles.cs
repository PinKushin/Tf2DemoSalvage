using System;
using System.Collections.Concurrent;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>One map load's texture files and decodes, by name, so every material naming a file shares one (finding 80).</summary>
/// <remarks>
/// **Keyed by name, as the engine keys a texture.** `IMaterialSystem::FindTexture( char const* pTextureName, ... )` and
/// `IsTextureLoaded( char const* pTextureName )` (`imaterialsystem.h:948-951`) find a texture by its name alone; the
/// creation flags are applied at the first load. A decode here depends on the file, the load's size cap and the frame,
/// and the frame is in the key; nothing a material decides changes it, so no case needs a wider key.
///
/// **Lives for one load.** <see cref="MapAssets.Load"/> makes one and drops it on return, so it never holds a texture past
/// <see cref="MapAssets.ReleaseUploaded"/>. Concurrent because the brushwork resolve is a <c>Parallel.For</c>; a factory
/// that runs twice in a race produces the same bytes, and only one is kept.
/// </remarks>
internal sealed class TextureFiles
{
    private readonly ConcurrentDictionary<string, Lazy<byte[]?>> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Path, int Frame), Lazy<VtfTexture>> _decodes = new();

    /// <summary>The key one file goes by, however its name is spelled.</summary>
    /// <remarks>
    /// **As `PakFile.Normalise` and the archives treat a path**: case does not matter and `\` is `/`. Both tables key by this,
    /// so two materials spelling one file two ways share one read and one decode (the review of 2026-10-18 found the
    /// decodes keyed case-sensitively and neither table folding the slashes). The reader is still given the path as asked.
    /// </remarks>
    private static string Key(string path) => path.Replace('\\', '/').ToUpperInvariant();

    /// <summary>A file's bytes, read once.</summary>
    /// <param name="path">The path.</param>
    /// <param name="read">How to read it the first time.</param>
    /// <returns>The bytes, or null when it does not exist.</returns>
    /// <remarks>
    /// **A lazy value, because `GetOrAdd`'s factory can run twice in a race**, and two materials of the brushwork's
    /// `Parallel.For` asking for one file at once is the common case: measured, two files were still read twice without it.
    /// A failed read is not kept, so the next asker tries again as before.
    /// </remarks>
    public byte[]? File(string path, Func<string, byte[]?> read)
    {
        string key = Key(path);
        Lazy<byte[]?> once = _files.GetOrAdd(key, _ => new Lazy<byte[]?>(() => read(path)));

        try
        {
            return once.Value;
        }
        catch (Exception)
        {
            _files.TryRemove(new(key, once));
            throw;
        }
    }

    /// <summary>A file's frame, decoded once; a decode that throws is not kept.</summary>
    /// <param name="path">The path, which names the decode.</param>
    /// <param name="frame">The frame.</param>
    /// <param name="decode">How to decode it the first time.</param>
    /// <returns>The decode.</returns>
    public VtfTexture Decode(string path, int frame, Func<VtfTexture> decode)
    {
        (string, int) key = (Key(path), frame);
        Lazy<VtfTexture> once = _decodes.GetOrAdd(key, _ => new Lazy<VtfTexture>(decode));

        try
        {
            return once.Value;
        }
        catch (Exception)
        {
            _decodes.TryRemove(new(key, once));
            throw;
        }
    }
}
