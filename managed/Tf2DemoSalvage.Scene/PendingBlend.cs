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
    public IReadOnlyList<PoseLayer> Transitions { get; set; } = [];

    /// <summary>Records this frame's inputs and marks them unconsumed.</summary>
    public void Set(SceneProp prop, PropModels.SkinnedModel skinned, int sequence, float phase, float[] parameters, double seconds) =>
        (Prop, Skinned, Sequence, Phase, Parameters, Seconds, Ready) = (prop, skinned, sequence, phase, parameters, seconds, true);
}
