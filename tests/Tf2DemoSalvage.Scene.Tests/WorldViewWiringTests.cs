using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The world pass publishes the views it collated by, which the particle effects and the beams are culled with.
/// </summary>
/// <remarks>
/// **The other half of the beams' wiring.** `EffectEntities` reads <see cref="IPosedEntities.WorldView"/> off the same
/// model set the frame posed, so the only way the beam cull can be dropped is this property never being set — a null
/// view culls nothing, with a green suite. The control is the set before any pass: null.
/// </remarks>
public sealed class WorldViewWiringTests
{
    [Test]
    public void Instances_TheWorldPass_PublishesItsCollationView()
    {
        EntityModelSet models = new() { ViewOrigin = (0f, 0f, 0f) };
        SceneProp[] props = [new(2, "models/props/crate.mdl", ScenePropTrack.Classify("models/props/crate.mdl"), new ScenePose { X = 300f }, null)];

        models.Add(props, ModelFramesFixture.OneTriangle);
        ((IPosedEntities)models).WorldView.ShouldBeNull("the control: no pass has collated yet");

        models.Instances(props, new List<ModelInstance>());

        ((IPosedEntities)models).WorldView.ShouldNotBeNull();
    }
}
