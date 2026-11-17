using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>A decapitated corpse's sound, which no demo carries (B762).</summary>
/// <remarks>
/// `C_TFRagdoll::OnDataChanged` creates a ragdoll rather than gibs, then on `IsDecapitation()` runs `CreateTFHeadGib()` and
/// `EmitSound( "TFPlayer.Decapitated" )` (`c_tf_player.cpp:1239-1245`). That is the ragdoll entity's own sound, in the
/// data-changed phase of the frame the corpse arrives. *Interpolated:* it sits where the corpse was made and does not
/// follow it, as <see cref="MedigunSounds"/> does for its loop.
/// </remarks>
public static class DecapitationSounds
{
    /// <summary>The script the ragdoll emits.</summary>
    public const string Name = "TFPlayer.Decapitated";

    /// <summary>Every decapitated corpse's sound, in tick order.</summary>
    /// <param name="corpses">The demo's corpses.</param>
    /// <param name="scripts">The sound scripts.</param>
    /// <returns>The sounds.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<SceneSound> For(IReadOnlyList<SceneRagdoll> corpses, IReadOnlyDictionary<string, SoundScriptEntry> scripts)
    {
        ArgumentNullException.ThrowIfNull(corpses);
        ArgumentNullException.ThrowIfNull(scripts);

        List<SceneSound> sounds = [];

        foreach (SceneRagdoll corpse in corpses)
        {
            // `if ( m_bGib ) CreateTFGibs( … ); else { CreateTFRagdoll(); if ( IsDecapitation() ) … }`.
            if (corpse.Gib || !RagdollProps.IsDecapitation(corpse))
            {
                continue;
            }

            if (EntitySounds.Emit(
                    corpse.FirstTick, corpse.EntityIndex, Name, (corpse.X, corpse.Y, corpse.Z), scripts, emitted: true,
                    ClientSoundPhase.DataChanged) is { } sound)
            {
                sounds.Add(sound);
            }
        }

        return [.. sounds.OrderBy(static sound => sound.Tick)];
    }
}
