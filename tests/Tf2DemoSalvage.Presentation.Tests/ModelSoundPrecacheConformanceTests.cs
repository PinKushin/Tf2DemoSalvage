using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// **The SERVER precaches a model's animation-event sounds with the model**, and this list stands in for that until the
/// client's own route — the demo's `soundprecache` table — can be precached here (finding 87, B568).
/// `CBaseEntity::PrecacheModelComponents` (`game/server/baseentity.cpp:5250-5412`) asks `g_ModelSoundsCache` for the
/// model's list on PC; the client's `PrecacheScriptSound` only looks an index up (`SoundEmitterSystem.cpp:1427-1434`), and
/// the client receives the waves through the table. The list is built by `CModelSoundsCache::BuildAnimationEventSoundList` (`game/shared/ModelSoundsCache.cpp:131-218`)
/// builds it by walking every sequence's events:
/// <code>
/// case CL_EVENT_SOUND:   // Old-style client .dll animation event
///     FindOrAddScriptSound( sounds, pEvent-&gt;pszOptions() );
/// </code>
/// and `PrecacheSoundList` precaches each script, which is every wave (B503). `FindOrAddScriptSound` (`:112-124`) keeps
/// only a name `GetSoundIndex` finds, so a raw wave or an empty option adds nothing, silently.
/// </summary>
/// <remarks>
/// Measured (finding 87): with these left out, a viewmodel's draw and reload sounds decoded on the render thread the first
/// time they played, and on a busy machine each of those reads blocked the frame for about 395 ms.
/// </remarks>
[TestFixture]
public sealed class ModelSoundPrecacheConformanceTests
{
    /// <summary>`CL_EVENT_SOUND`, the event `SoundPresenter` plays.</summary>
    private const int ClientSound = SoundPresenter.PlaySoundEvent;

    [Test]
    public void ModelWaves_ClientSoundEvents_NameEveryWaveOfTheirScriptOnce()
    {
        StudioEvent[] events =
        [
            new(0f, ClientSound, 0, "Weapon_Pipe.DrumOpen"),
            new(0.5f, ClientSound, 0, "Weapon_Pipe.DrumClose"),

            // The same script in a second sequence is one set of waves.
            new(0.1f, ClientSound, 0, "Weapon_Pipe.DrumOpen"),

            // The controls: a footstep and a particle event are not sounds this list names, an empty option names
            // nothing, a raw wave is not a script, and a script the install does not have resolves to nothing.
            // Each names a script of its own, so leaking one in would show.
            new(0.2f, SoundPresenter.FootstepEvent, 0, "Other.Footstep"),
            new(0.2f, ClientSound + 1, 0, "Other.Particle"),
            new(0.2f, ClientSound - 1, 0, "Other.Below"),

            // A new-system event numbered 5004 without `AE_TYPE_CLIENT` is the server's: the client never fires it
            // (`StudioEvent.FiresOnTheClient`), so nothing here would play its waves.
            new(0.2f, ClientSound, StudioEvent.NewSystemType, "Other.Server"),
            new(0.3f, ClientSound, 0, string.Empty),
            new(0.4f, ClientSound, 0, "Missing.Script"),
            new(0.4f, ClientSound, 0, "weapons/raw.wav"),
        ];

        Dictionary<string, SoundScriptEntry> scripts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Weapon_Pipe.DrumOpen"] = Script("Weapon_Pipe.DrumOpen", "weapons/drum_open1.wav", "weapons/drum_open2.wav"),
            ["Weapon_Pipe.DrumClose"] = Script("Weapon_Pipe.DrumClose", "weapons/drum_close.wav"),
            ["Other.Footstep"] = Script("Other.Footstep", "player/footstep.wav"),
            ["Other.Particle"] = Script("Other.Particle", "other/particle.wav"),
            ["Other.Below"] = Script("Other.Below", "other/below.wav"),
            ["Other.Server"] = Script("Other.Server", "other/server.wav"),
        };

        DemoSounds.ModelWaves(events, scripts).ShouldBe(["weapons/drum_open1.wav", "weapons/drum_open2.wav", "weapons/drum_close.wav"]);
    }

    [Test]
    public void ModelWaves_NoScripts_NamesNothing()
    {
        DemoSounds.ModelWaves([new StudioEvent(0f, ClientSound, 0, "Weapon_Pipe.DrumOpen")], null).ShouldBeEmpty();
    }

    private static SoundScriptEntry Script(string name, params string[] waves) =>
        new(name, 4, new SoundRange(1f, 1f), new SoundRange(100f, 100f), new SoundRange(75f, 75f), waves);
}
