using System.Collections.ObjectModel;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// `IMatRenderContext` as a model panel's paint uses it between `Begin3DPaint` and `End3DPaint`: the models drawn and the
/// particles rendered (`CBaseModelPanel::PostPaint3D`, basemodel_panel.cpp:904-912), under the panel's own camera.
/// </summary>
/// <param name="Eye">The panel camera's position, which a sprite trail turns to face.</param>
/// <param name="Right">The panel camera's right vector, for billboards.</param>
/// <param name="Up">The panel camera's up vector.</param>
public sealed record VguiRenderContext(Vector3 Eye, Vector3 Right, Vector3 Up)
{
    /// <summary>The models drawn, in draw order.</summary>
    public Collection<ModelInstance> Models { get; } = [];

    /// <summary>The particle batches rendered after them.</summary>
    public Collection<ParticleBatch> Particles { get; } = [];
}
