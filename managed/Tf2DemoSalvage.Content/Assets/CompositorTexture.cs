using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>A source texture for <see cref="TextureCompositor"/>: its RGBA8 mip chain, largest first, as the GPU samples it.</summary>
/// <param name="levels">Each level's width, height and RGBA8 texels, largest first.</param>
public sealed class CompositorTexture(IReadOnlyList<(int Width, int Height, byte[] Pixels)> levels)
{
    /// <summary>The levels, largest first.</summary>
    public IReadOnlyList<(int Width, int Height, byte[] Pixels)> Levels { get; } =
        levels is { Count: > 0 } ? levels : throw new ArgumentException("a texture has at least one level", nameof(levels));

    /// <summary>The top level's width.</summary>
    public int Width => Levels[0].Width;

    /// <summary>The top level's height.</summary>
    public int Height => Levels[0].Height;
}
