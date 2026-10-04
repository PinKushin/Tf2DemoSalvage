using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Strip corners grouped by material and pass — what the beam and trail passes hand the renderer.
/// </summary>
/// <remarks>
/// **One place for both**, because a beam and a trail are the same draw once <see cref="BeamSegDraw"/> has made their
/// corners: a sprite material, the passes its render mode selects, and each corner's colour as that material's shader
/// takes it (<see cref="EntitySprites.ShaderInput"/>).
/// </remarks>
internal sealed class SpriteStripBatches
{
    private readonly Dictionary<(string Path, SpritePass Pass), List<DetailSpriteVertex>> _byMaterial = [];

    private readonly List<ParticleBatch> _batches = [];

    /// <summary>How many corners have been added since <see cref="Clear"/>.</summary>
    public int Corners { get; private set; }

    /// <summary>Empties every material's corners, keeping the lists.</summary>
    public void Clear()
    {
        foreach (List<DetailSpriteVertex> corners in _byMaterial.Values)
        {
            corners.Clear();
        }

        _batches.Clear();
        Corners = 0;
    }

    /// <summary>Corners into each of the material's passes, coloured as its shader would take them.</summary>
    /// <param name="path">The material's model path.</param>
    /// <param name="sprite">The material as loaded.</param>
    /// <param name="renderMode">The render mode it is drawn at.</param>
    /// <param name="corners">The strip's corners, with the colour <see cref="BeamSegDraw"/> packed into them.</param>
    public void Add(string path, EngineSprite sprite, int renderMode, IReadOnlyList<DetailSpriteVertex> corners)
    {
        foreach (SpritePass pass in EntitySprites.PassesFor(sprite, renderMode))
        {
            if (!_byMaterial.TryGetValue((path, pass), out List<DetailSpriteVertex>? into))
            {
                into = [];
                _byMaterial[(path, pass)] = into;
            }

            foreach (DetailSpriteVertex corner in corners)
            {
                (Vector3 colour, float alpha) = EntitySprites.ShaderInput(
                    sprite, renderMode, new Vector3(corner.Red, corner.Green, corner.Blue), corner.Alpha);

                into.Add(corner with { Red = colour.X, Green = colour.Y, Blue = colour.Z, Alpha = alpha });
                Corners++;
            }
        }
    }

    /// <summary>One batch per material and pass that has corners.</summary>
    /// <param name="sprites">Each sprite material as loaded, keyed by model path.</param>
    /// <returns>The batches, valid until the next <see cref="Clear"/>.</returns>
    public IReadOnlyList<ParticleBatch> Batches(IReadOnlyDictionary<string, EngineSprite> sprites)
    {
        _batches.Clear();

        foreach (((string path, SpritePass pass), List<DetailSpriteVertex> corners) in _byMaterial)
        {
            if (corners.Count > 0 && sprites.TryGetValue(path, out EngineSprite sprite))
            {
                _batches.Add(new ParticleBatch(
                    corners, sprite.Material with { Blend = pass.Blend, Depth = pass.Depth }));
            }
        }

        return _batches;
    }
}
