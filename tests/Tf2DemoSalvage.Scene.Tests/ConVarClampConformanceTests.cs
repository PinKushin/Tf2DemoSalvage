namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A clamped ConVar's string, as the engine stores it (B543).</summary>
/// <remarks>
/// `ConVar::InternalSetValue` (`src/tier1/convar.cpp:794-798`): when `ClampValue` moves the value, the string is
/// regenerated with `Q_snprintf( tempVal, sizeof(tempVal), "%f", fNewValue )` — a float promoted to double, six
/// places — and otherwise the string is kept exactly as written (`:788`, `:806`). Bounds come from the declaration:
/// `viewmodel_fov` is `true, 0.1, true, 179.9` (`src/game/client/view.cpp:111`); `viewmodel_fov_demo`
/// (`clientmode_tf.cpp:570`) and `developer` (`util_shared.cpp:45`) declare none, so the engine never clamps them.
/// Of every ConVar the viewer implements, three declare bounds: `viewmodel_fov`, `cl_interp` and `mat_vsync`
/// (`EngineConVars`, each cited there) — one test each.
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
    public void ConVarText_InterpAboveItsMax_IsTheMaxAsPercentF()
    {
        // cdll_bounded_cvars.cpp:104, `true, 0.0f, true, 0.5f`.
        new ViewerSettings().Set("cl_interp", "1").ConVarText("cl_interp").ShouldBe("0.500000");
    }

    [Test]
    public void ConVarText_VsyncAboveItsMax_IsTheMaxAsPercentF()
    {
        // Engine-side; TF2's `help mat_vsync` prints "min. 0.000000 max. 1.000000" (convar.cpp:1432-1436).
        new ViewerSettings().Set("mat_vsync", "5").ConVarText("mat_vsync").ShouldBe("1.000000");
    }

    [Test]
    public void ConVarText_AFloatInsideItsBounds_IsAsWrittenNotPercentF()
    {
        // The representative as-written case: the engine keeps "0.0152", where `%f` would read "0.015200".
        new ViewerSettings().Set("cl_interp", "0.0152").ConVarText("cl_interp").ShouldBe("0.0152");
    }

    [Test]
    public void ConVarText_DeveloperAboveTheViewerClamp_IsAsWritten()
    {
        new ViewerSettings().Set("developer", "9").ConVarText("developer").ShouldBe("9");
    }
}
