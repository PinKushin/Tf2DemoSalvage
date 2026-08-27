namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Which convars decide whether a viewmodel is drawn, and at what field of view.
/// </summary>
/// <remarks>
/// **Written from the SDK before the implementation, per `docs/CONFORMANCE.md`** (B166). The
/// diagnosis was already in the risk register and every citation below was re-read against
/// `source-sdk-2013` rather than trusted — two register entries turned out stale earlier the same
/// day, so a claim being written down is not evidence that it still holds.
///
/// ## The gate
///
/// `ClientModeTFNormal::ShouldDrawViewModel`, `game/client/tf/clientmode_tf.cpp:584`:
///
/// <code>
/// C_TFPlayer *pPlayer = C_TFPlayer::GetLocalTFPlayer();
/// if ( pPlayer )
/// {
///     if ( pPlayer->m_Shared.InCond( TF_COND_ZOOMED ) )
///         return false;
/// }
/// if ( !r_drawviewmodel.GetBool() )
///     return false;
/// return true;
/// </code>
///
/// `ConVar r_drawviewmodel( "r_drawviewmodel","1", FCVAR_DONTRECORD )` — `viewrender.cpp:116`.
///
/// ## The field of view, and the trap
///
/// `ClientModeTFNormal::GetViewModelFOV`, `clientmode_tf.cpp:571`:
///
/// <code>
/// if ( engine->IsPlayingDemo() )
///     return v_viewmodel_fov_demo.GetFloat();
/// return v_viewmodel_fov.GetFloat();
/// </code>
///
/// `ConVar v_viewmodel_fov_demo( "viewmodel_fov_demo", "54", FCVAR_ARCHIVE )` — `:570`.
///
/// **A demo viewer is always on the first branch**, so `viewmodel_fov` never applies here at all.
/// This project reads `viewmodel_fov` and clamps it to 54..70; the owner's stored
/// `viewmodel_fov "0.100000"` therefore becomes 54 — which is exactly what `viewmodel_fov_demo`
/// defaults to. **The output is right and the reasoning is wrong**, which is the failure this suite
/// exists to make visible: nothing looks broken, so nothing gets checked.
///
/// **And the clamp is wrong on its own terms.** `viewmodel_fov` carries FOUR bounds —
/// `true, 0.1, true, 179.9, true, 54, true, 70` (`view.cpp:111`). The first pair is the hard range
/// the convar accepts; the second is the COMPETITIVE range. Applying the competitive pair as though
/// it were the only one is why 0.1 — a legal value the owner actually stores — cannot survive.
///
/// **Evidence class: read from published source.**
/// </remarks>
public sealed class ViewmodelCvarConformanceTests
{
    /// <summary>`r_drawviewmodel`, shipped `"1"` — `viewrender.cpp:116`.</summary>
    private const bool ValveDrawsViewmodelByDefault = true;

    /// <summary>`viewmodel_fov_demo`, shipped `"54"` — `clientmode_tf.cpp:570`.</summary>
    private const float ValveDemoViewmodelFov = 54f;

    /// <summary>`viewmodel_fov`'s hard lower bound — `view.cpp:111`, first of four.</summary>
    private const float ValveHardFloor = 0.1f;

    /// <summary>`viewmodel_fov`'s hard upper bound — `view.cpp:111`.</summary>
    private const float ValveHardCeiling = 179.9f;

    [Test]
    public void ViewmodelFov_DuringDemoPlayback_ComesFromTheDemoConvar()
    {
        // The whole point of B166. `GetViewModelFOV` returns `viewmodel_fov_demo` whenever
        // `engine->IsPlayingDemo()`, and this program is never in the other case.
        ViewerSettings.DemoViewmodelFieldOfViewCommand.ShouldBe("viewmodel_fov_demo");
        ViewerSettings.DefaultDemoViewmodelFieldOfView.ShouldBe(ValveDemoViewmodelFov);
    }

    [Test]
    public void ViewmodelFov_SetByTheDemoConvar_IsNotClampedToTheCompetitiveRange()
    {
        // **0.1 is a legal value and the owner's config holds it.** The competitive pair 54..70 is
        // the second of four bounds, applied by the game only in that mode; the convar's own range
        // is 0.1..179.9. Clamping to the narrow pair turns a stored 0.1 into 54 and looks correct
        // because 54 is also the demo default.
        ViewerSettings settings = ViewerSettings.Parse("viewmodel_fov_demo 0.1");

        settings.ViewmodelFieldOfView.ShouldBe(ValveHardFloor, 0.001f);
    }

    [Test]
    public void ViewmodelFov_BeyondTheHardBounds_IsClampedToThem()
    {
        // The bounds that DO apply. A config may say anything; the convar would refuse these.
        ViewerSettings low = ViewerSettings.Parse("viewmodel_fov_demo -5");
        ViewerSettings high = ViewerSettings.Parse("viewmodel_fov_demo 500");

        low.ViewmodelFieldOfView.ShouldBe(ValveHardFloor, 0.001f);
        high.ViewmodelFieldOfView.ShouldBe(ValveHardCeiling, 0.001f);
    }

    [Test]
    public void DrawViewmodel_ByDefault_IsOnBecauseValveShipsItOn()
    {
        // `r_drawviewmodel` is `"1"`, and `FCVAR_DONTRECORD` means a demo never carries a change to
        // it — so playback always starts from the default whatever the recorder had set.
        ViewerSettings.DrawViewmodelCommand.ShouldBe("r_drawviewmodel");
        new ViewerSettings().DrawViewmodel.ShouldBe(ValveDrawsViewmodelByDefault);
    }

    [Test]
    public void DrawViewmodel_TurnedOffByAConfig_IsOff()
    {
        // **The switch the owner actually uses.** Their config never sets it directly — the `vm_off`
        // alias in `viewmodels.cfg` does, invoked from class-selection scripts that do not run while
        // spectating a demo. So the viewmodel is drawn here even though they never see one in game,
        // and that is correct rather than a bug; this is what makes it controllable.
        ViewerSettings off = ViewerSettings.Parse("r_drawviewmodel 0");

        off.DrawViewmodel.ShouldBeFalse();
    }
}
