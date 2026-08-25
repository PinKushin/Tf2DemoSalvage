using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Narrowing the draw list to what a visibility rule kept.
/// </summary>
/// <remarks>
/// **Six lines that were written twice, and the copy in the middle is load-bearing.** Both
/// <see cref="FirstPersonVisibility"/> and <see cref="WeaponVisibility"/> answer with a list and the
/// caller then replaced the draw list with it — the same dance in both places, including the
/// intermediate copy that stops the answer being destroyed by the clear that is about to read it.
/// </remarks>
public sealed class DrawListTests
{
    [Test]
    public void KeepOnly_WithFewerVisible_ReplacesTheList()
    {
        List<SceneProp> drawn = [Prop(1), Prop(2), Prop(3)];

        DrawList.KeepOnly(drawn, [Prop(1), Prop(3)]);

        drawn.Select(prop => prop.EntityIndex).ShouldBe([1, 3]);
    }

    [Test]
    public void KeepOnly_WithEverythingVisible_LeavesTheListAlone()
    {
        // The ordinary case — no camera filter and every weapon drawn — and it must not pay for a
        // copy per frame to discover that nothing changed.
        List<SceneProp> drawn = [Prop(1), Prop(2)];

        DrawList.KeepOnly(drawn, [Prop(1), Prop(2)]);

        drawn.Select(prop => prop.EntityIndex).ShouldBe([1, 2]);
    }

    [Test]
    public void KeepOnly_WhenTheVisibleListIsLazyOverTheDrawnList_KeepsTheRightProps()
    {
        // **The hazard the intermediate copy exists for, and it is invisible in the ordinary case.**
        // A filter that answers lazily — `Where(...)` without materialising — is still reading the
        // draw list when the swap begins. Clearing first would therefore hand `AddRange` an empty
        // sequence and delete every prop in the scene, and the failure would look like "nothing
        // renders" rather than like an aliasing bug.
        //
        // Both callers today answer with a materialised list, so this cannot bite right now. It is
        // pinned because the next person to write a visibility rule has no reason to know, and the
        // copy looks exactly like a line worth deleting.
        List<SceneProp> drawn = [Prop(1), Prop(2), Prop(3)];

        IReadOnlyList<SceneProp> lazy = new LazyView(drawn, keep: 2);

        DrawList.KeepOnly(drawn, lazy);

        drawn.Select(prop => prop.EntityIndex).ShouldBe([1, 3]);
    }

    /// <summary>A visibility answer that reads the draw list on demand rather than up front.</summary>
    private sealed class LazyView(List<SceneProp> source, int keep) : IReadOnlyList<SceneProp>
    {
        private IEnumerable<SceneProp> Kept => source.Where(prop => prop.EntityIndex != keep);

        public int Count => source.Count - 1;

        public SceneProp this[int index] => Kept.ElementAt(index);

        public IEnumerator<SceneProp> GetEnumerator() => Kept.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private static SceneProp Prop(int entity) =>
        new(entity, "models/props/crate.mdl", SceneModelKind.Studio, new ScenePose { Scale = 1f });
}
