using System;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// One key of a script's top block, as `pKeyValuesData->GetString( key )` reads it.
/// </summary>
/// <remarks>
/// **It was a text scan and it is now the KeyValues loader** (B523, B524). The scan — the first quoted value after the
/// quoted key, anywhere in the file — found keys inside comments, which is how the shipped `soldier.ctx`'s
/// `//"DontDoNewJump" "1"` put every soldier's jump in the reference pose; and it would have answered with a key from a
/// nested block, or one tagged for another platform. `tf_classdata.cpp` asks the file's ROOT for a direct sub-key, and
/// <see cref="KeyValuesTree"/> is that file loaded as the engine loads it.
///
/// **Only the first match is taken**, which is `FindKey`'s answer for a key stated twice.
/// </remarks>
internal static class ScriptKeyValue
{
    /// <summary>The value of the root block's first sub-key of this name.</summary>
    /// <param name="script">The script's bytes, already decrypted if it was encrypted.</param>
    /// <param name="key">The key to look for, without quotes; matched without case, as the key table is.</param>
    /// <returns>The value, or <c>null</c> when the root does not state it or states it as a block.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public static string? First(ReadOnlySpan<byte> script, string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        // No file is opened beside a class script: none of the shipped ones names a `#base`.
        return KeyValuesTree.Load(script, string.Empty, static _ => null).Find(key) is { Children.Count: 0 } found
            ? found.Value
            : null;
    }
}
