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

    private string? _lastPath;
    private SpritePass _lastPass;
    private List<DetailSpriteVertex>? _lastInto;

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
        _placed.Begin();
        Corners = 0;
    }

    /// <summary>Each placed strip's own batches (B734).</summary>
    private readonly PlacedBatches _placed = new();

    /// <summary>Corners into each of the material's passes, coloured as its shader would take them.</summary>
    /// <param name="path">The material's model path.</param>
    /// <param name="sprite">The material as loaded.</param>
    /// <param name="renderMode">The render mode it is drawn at.</param>
    /// <param name="corners">The strip's corners, with the colour <see cref="BeamSegDraw"/> packed into them.</param>
    /// <param name="placement">
    /// The strip's own place in the view's translucent walk, which keeps it a renderable of its own (B734); null merges it
    /// with every other strip on its material, drawn after the walk.
    /// </param>
    public void Add(
        string path, EngineSprite sprite, int renderMode, IReadOnlyList<DetailSpriteVertex> corners,
        TranslucentPlacement? placement = null)
    {
        // Indexed, because both lists are interface-typed and a `foreach` over one boxes its enumerator — per strip and per
        // pass, every frame (B599).
        IReadOnlyList<SpritePass> passes = EntitySprites.PassesFor(sprite, renderMode);

        for (int p = 0; p < passes.Count; p++)
        {
            SpritePass pass = passes[p];
            List<DetailSpriteVertex>? into;

            if (placement is { } renderable)
            {
                into = _placed.Next(renderable, sprite.Material with { Blend = pass.Blend, Depth = pass.Depth });
            }
            else
            {
                // The last list asked for, by reference: consecutive strips of one material skip hashing the path (f12, 211
                // beams: 0.133 -> 0.098 ms a frame, docs/measurements/2026-11-06-detail-sprites-visible-leaves.md).
                into = ReferenceEquals(path, _lastPath) && pass == _lastPass ? _lastInto : null;

                if (into is null && !_byMaterial.TryGetValue((path, pass), out into))
                {
                    into = [];
                    _byMaterial[(path, pass)] = into;
                }

                (_lastPath, _lastPass, _lastInto) = (path, pass, into);
            }

            for (int c = 0; c < corners.Count; c++)
            {
                DetailSpriteVertex corner = corners[c];
                (Vector3 colour, float alpha) = EntitySprites.ShaderInput(
                    sprite, renderMode, new Vector3(corner.Red, corner.Green, corner.Blue), corner.Alpha);

                into.Add(corner with { Red = colour.X, Green = colour.Y, Blue = colour.Z, Alpha = alpha });
                Corners++;
            }
        }
    }

    /// <summary>One batch per material and pass that has corners, then each placed strip's own.</summary>
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

        _placed.AppendTo(_batches);

        return _batches;
    }
}
