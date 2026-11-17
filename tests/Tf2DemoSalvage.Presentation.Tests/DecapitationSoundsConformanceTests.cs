using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A decapitated corpse's `TFPlayer.Decapitated` (B762).</summary>
/// <remarks>
/// **Read from published source.** `C_TFRagdoll::OnDataChanged`, when it creates a ragdoll rather than gibs, follows
/// `CreateTFHeadGib` with `EmitSound( "TFPlayer.Decapitated" )` when `IsDecapitation()` (`c_tf_player.cpp:1239-1245`):
/// from the ragdoll entity, in the data-changed phase, on the tick the corpse arrived.
/// </remarks>
public sealed class DecapitationSoundsConformanceTests
{
    private static readonly Dictionary<string, SoundScriptEntry> Scripts = new()
    {
        [DecapitationSounds.Name] = new SoundScriptEntry(
            DecapitationSounds.Name, 1, new SoundRange(1f, 1f), new SoundRange(100f, 100f), new SoundRange(75f, 75f), ["player/flow.wav"]),
    };

    [Test]
    public void For_ADecapitatedCorpse_EmitsTheSoundFromItWhenItArrives()
    {
        SceneSound sound = DecapitationSounds.For([Corpse(20)], Scripts).ShouldHaveSingleItem();

        (sound.Name, sound.Tick, sound.EntityIndex).ShouldBe(("player/flow.wav", 300, 77));
        (sound.OriginX, sound.OriginY, sound.OriginZ).ShouldBe((1f, 2f, 3f));
        sound.Order.ShouldBe(new ClientSoundOrder(ClientSoundPhase.DataChanged, 0, 0));
    }

    /// <remarks>
    /// The controls: an ordinary kill is not a decapitation, and a gibbed corpse takes the `CreateTFGibs` branch, which has
    /// no sound (`:1235-1237`).
    /// </remarks>
    [Test]
    public void For_AnOrdinaryOrGibbedCorpse_IsSilent()
    {
        DecapitationSounds.For([Corpse(null), Corpse(20) with { Gib = true }], Scripts).ShouldBeEmpty();
    }

    [Test]
    public void For_TwoDecapitations_AreInTickOrder()
    {
        IReadOnlyList<SceneSound> sounds = DecapitationSounds.For(
            [Corpse(20) with { FirstTick = 500 }, Corpse(51) with { FirstTick = 400 }], Scripts);

        sounds.Count.ShouldBe(2);
        (sounds[0].Tick, sounds[1].Tick).ShouldBe((400, 500));
    }

    /// <remarks>The wiring: the schedule's client-made sounds carry a decapitated corpse's, and keep the others.</remarks>
    [Test]
    public void ClientMade_ADemoWithADecapitation_SchedulesItsSound()
    {
        DemoTimeline timeline = DemoTimeline.ForTracks([], corpses: [Corpse(20)]);
        SceneSound medigun = new(100, "medigun.wav", 0, 3, 0, 1f, 75, 100, 0f, 0f, 0f, 0f);

        IReadOnlyList<SceneSound> sounds = DemoSystems.ClientMade(timeline, Scripts, [], [], [medigun]);

        sounds.Select(sound => (sound.Tick, sound.Name)).ShouldBe([(100, "medigun.wav"), (300, "player/flow.wav")]);
    }

    private static SceneRagdoll Corpse(int? damageCustom) =>
        new(77, 1, 5, 2, 1f, 2f, 3f, Gib: false, Burning: false, FeignDeath: false, WasDisguised: false, FirstTick: 300,
            LastTick: 400, DamageCustom: damageCustom);
}
