using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Render;

/// <summary>Whether a renderable's materials are translucent, kept on the renderable (B262).</summary>
/// <remarks>
/// **The engine asks once and stores the answer on the handle**: <c>CreateRenderableHandle</c> calls
/// <c>IsTransparent()</c> (<c>clientleafsystem.cpp:651</c>), <c>NewRenderable</c> stores the group (<c>:631</c>), and
/// collation reads the stored group (<c>:1607</c>, <c>:1678</c>). What changes per frame is the alpha
/// (<c>ComputeFxBlend</c>), which <c>RenderGroups</c> still applies every frame on top of this.
///
/// **Asked again when what selects the materials changes** — the frame's batches, the skin, the body groups. The
/// engine re-registers an entity whose model changes; that trigger is INTERPOLATED to include skin and body, which
/// choose materials here without a model change. Keyed per renderable (entity and model), not per model, as the
/// engine keys it per handle.
/// </remarks>
public sealed class TranslucencyCache
{
    private readonly Dictionary<(int Entity, string Model), Entry> _entries = [];

    private readonly record struct Entry(
        int Frame,
        IReadOnlyDictionary<int, int>? SkinSwap,
        IReadOnlyList<(int Base, int Count)>? BodyParts,
        int Body,
        bool Translucent);

    /// <summary>The renderable's translucency, asked only when it is new or its materials may have changed.</summary>
    /// <param name="renderable">Its entity and model.</param>
    /// <param name="frame">The frame whose batches it draws.</param>
    /// <param name="skinSwap">Its skin's material swaps.</param>
    /// <param name="bodyParts">Its body-part table.</param>
    /// <param name="body">Its body-group value.</param>
    /// <param name="ask">Asks the materials.</param>
    /// <returns>Whether it has a translucent material.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ask"/> is null.</exception>
    public bool For(
        (int Entity, string Model) renderable,
        int frame,
        IReadOnlyDictionary<int, int>? skinSwap,
        IReadOnlyList<(int Base, int Count)>? bodyParts,
        int body,
        Func<bool> ask)
    {
        ArgumentNullException.ThrowIfNull(ask);

        if (_entries.TryGetValue(renderable, out Entry kept) &&
            kept.Frame == frame &&
            ReferenceEquals(kept.SkinSwap, skinSwap) &&
            ReferenceEquals(kept.BodyParts, bodyParts) &&
            kept.Body == body)
        {
            return kept.Translucent;
        }

        bool translucent = ask();

        _entries[renderable] = new Entry(frame, skinSwap, bodyParts, body, translucent);

        return translucent;
    }

    /// <summary>Forgets every renderable — a new map.</summary>
    public void Clear() => _entries.Clear();
}
