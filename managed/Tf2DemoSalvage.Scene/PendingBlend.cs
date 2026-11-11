using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>One entity's blend inputs between its simulate and its next bone setup (finding 102).</summary>
/// <param name="entity">The entity whose next build reads them.</param>
internal sealed class PendingBlend(AnimatingEntity entity)
{
    /// <summary>The entity whose next build reads these.</summary>
    public AnimatingEntity Entity { get; } = entity;

    /// <summary>Whether this frame's simulate set inputs its build has not yet consumed.</summary>
    public bool Ready { get; set; }

    /// <summary>The prop as simulated.</summary>
    public SceneProp? Prop { get; private set; }

    /// <summary>Its model.</summary>
    public PropModels.SkinnedModel? Skinned { get; private set; }

    /// <summary>The main sequence.</summary>
    public int Sequence { get; private set; }

    /// <summary>The main sequence's cycle.</summary>
    public float Phase { get; private set; }

    /// <summary>The pose parameters.</summary>
    public float[]? Parameters { get; private set; }

    /// <summary>Demo time.</summary>
    public double Seconds { get; private set; }

    /// <summary>The sequences fading out, from this frame's `MaintainSequenceTransitions` queue.</summary>
    /// <remarks>
    /// **Roots, not layers** (B624): each is one `AccumulatePose` whose autolayers the bone build expands, so the
    /// expansion is bone-setup work and lands in its spans rather than in simulate's (finding 102).
    /// </remarks>
    public IReadOnlyList<FadingRoot> Transitions { get; set; } = [];

    /// <summary>A static prop's: the main sequence alone, at its cycle, with no layer, autoplay or IK (B560).</summary>
    public bool Still { get; private set; }

    /// <summary>Records this frame's inputs and marks them unconsumed.</summary>
    public void Set(SceneProp prop, PropModels.SkinnedModel skinned, int sequence, float phase, float[] parameters, double seconds, bool still = false) =>
        (Prop, Skinned, Sequence, Phase, Parameters, Seconds, Still, Ready) = (prop, skinned, sequence, phase, parameters, seconds, still, true);
}

/// <summary>One fading sequence's `AccumulatePose( pos, q, blend->m_nSequence, flCycle, blend->m_flWeight, … )` arguments.</summary>
/// <param name="Sequence">The sequence fading out.</param>
/// <param name="Cycle">`flCycle`, clamped — what its `AddSequenceLayers` windows (`c_baseanimating.cpp:1853-1854`).</param>
/// <param name="Frame">Where it is sampled, which a `STUDIO_REALTIME` sequence takes from the clock instead.</param>
/// <param name="Fraction">How far past <paramref name="Frame"/>.</param>
/// <param name="Weight">`m_flWeight`, from `GetFadeout`.</param>
internal readonly record struct FadingRoot(int Sequence, float Cycle, int Frame, float Fraction, float Weight);
