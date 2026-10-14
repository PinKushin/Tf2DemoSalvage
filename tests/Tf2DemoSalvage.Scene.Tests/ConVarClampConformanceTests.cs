namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A clamped ConVar's string, as the engine stores it (B543).</summary>
/// <remarks>
/// `ConVar::InternalSetValue` (`src/tier1/convar.cpp:794-798`): when `ClampValue` moves the value, the string is
/// regenerated with `Q_snprintf( tempVal, sizeof(tempVal), "%f", fNewValue )` — a float promoted to double, six
/// places — and otherwise the string is kept exactly as written (`:788`, `:806`). Bounds come from the declaration:
/// `viewmodel_fov` is `true, 0.1, true, 179.9` (`src/game/client/view.cpp:111`); `viewmodel_fov_demo`
/// (`clientmode_tf.cpp:570`) and `developer` (`util_shared.cpp:45`) declare none, so the engine never clamps them.
/// </remarks>
public sealed class ConVarClampConformanceTests
{
    [Test]
    public void ConVarText_ViewmodelFovAboveItsMax_IsTheMaxAsPercentF()
    {
        // 179.9f promoted to double is 179.89999389…, which "%f" rounds to 179.899994.
        new ViewerSettings().Set("viewmodel_fov", "500").ConVarText("viewmodel_fov").ShouldBe("179.899994");
    }

    [Test]
    public void ConVarText_ViewmodelFovBelowItsMin_IsTheMinAsPercentF()
    {
        new ViewerSettings().Set("viewmodel_fov", "0").ConVarText("viewmodel_fov").ShouldBe("0.100000");
    }

    [Test]
    public void ConVarText_ViewmodelFovInsideItsBounds_IsAsWritten()
    {
        new ViewerSettings().Set("viewmodel_fov", "90").ConVarText("viewmodel_fov").ShouldBe("90");
    }

    [Test]
    public void ConVarText_ViewmodelFovDemoAboveTheViewerClamp_IsAsWritten()
    {
        // No bounds declared, so the engine keeps "500" although this viewer draws at its clamp.
        new ViewerSettings().Set("viewmodel_fov_demo", "500").ConVarText("viewmodel_fov_demo").ShouldBe("500");
    }

    [Test]
    public void ConVarText_DeveloperAboveTheViewerClamp_IsAsWritten()
    {
        new ViewerSettings().Set("developer", "9").ConVarText("developer").ShouldBe("9");
    }
}
