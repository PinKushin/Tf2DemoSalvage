namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>Which frame the one-shot `opaque draw order` line reports (B426).</summary>
/// <remarks>
/// Static props are models from map load, so the seek's capture frame — drawing a stale pose of three
/// cobwebs — became "the first frame with a model" and the UI wiring tests read it as static props
/// missing. The line waits for a networked entity, as it did before static props were models.
/// </remarks>
public sealed class DrawOrderReportTests
{
    private static ModelInstance At(int entity) =>
        new("models/props/crate.mdl", new float[16], null, null, EntityIndex: entity);

    [Test]
    public void ReportsDrawOrder_StaticPropsAlone_IsFalse() =>
        Device3D.ReportsDrawOrder(
                [At(PropModels.FirstStaticPropEntityIndex), At(PropModels.FirstStaticPropEntityIndex + 5)])
            .ShouldBeFalse();

    /// <remarks>The control: the same list with one networked entity in it is reported.</remarks>
    [Test]
    public void ReportsDrawOrder_StaticPropsAndAnEntity_IsTrue() =>
        Device3D.ReportsDrawOrder([At(PropModels.FirstStaticPropEntityIndex), At(12)])
            .ShouldBeTrue();
}
