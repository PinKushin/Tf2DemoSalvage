using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>On a real match, each decapitation's body is drawn headless where its blood looks for its head (B762).</summary>
/// <remarks>
/// **Output level, through the production pass.** `BurstEffects` asks for the `head` attachment of
/// `RagdollProps.FirstCorpseEntityIndex` plus the corpse's place in the list, and `DecapitationSounds` emits from the
/// same corpses. The first version asked for the server's ragdoll slot, which nothing poses, and every unit test passed.
/// This reads the scene `RagdollProps.Fill` hands the renderer. `20171113_2240_cp_badlands_red_blu` holds seven
/// decapitations, none gibbed, measured with the `corpses` probe; the count is the control that the instrument found them.
/// </remarks>
public sealed class CorpusDecapitationTests
{
    [Test]
    public void Fill_DecapitationsOnBadlands_DrawAHeadlessBodyWhereTheBloodLooks()
    {
        DemoTimeline timeline = TimelineCache.For(Corpus.Demo("20171113_2240_cp_badlands_red_blu"));

        List<int> decapitated =
        [
            .. Enumerable.Range(0, timeline.Corpses.Count)
                .Where(index => !timeline.Corpses[index].Gib && RagdollProps.IsDecapitation(timeline.Corpses[index])),
        ];

        decapitated.Count.ShouldBe(7);

        foreach (int index in decapitated)
        {
            SceneRagdoll corpse = timeline.Corpses[index];
            List<SceneProp> scene = [];

            RagdollProps.Fill(timeline.Corpses, corpse.FirstTick + 13, playerClass => $"models/b762/{playerClass}.mdl", scene);

            SceneProp body = scene.Single(prop =>
                prop.ClassName == RagdollProps.RagdollClassName && prop.EntityIndex == RagdollProps.FirstCorpseEntityIndex + index);

            body.Pose.Decapitated.ShouldBeTrue(body.ModelPath);
            body.EntityIndex.ShouldNotBe(corpse.EntityIndex, "the server's slot, which nothing poses");
        }
    }
}
