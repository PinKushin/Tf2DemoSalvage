namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// What TF does to a viewmodel's position between the eye and the draw: the min-mode offset and the bob (B525).
/// </summary>
/// <remarks>
/// *Read from published source.* `CTFViewModel::CalcViewModelView` (`tf_viewmodel.cpp:130-227`) starts at the eye,
/// adds the weapon's `min_viewmodel_offset` along the eye's forward, right and up when `tf_use_min_viewmodels` is on
/// (`:196-221`), and calls the base (`baseviewmodel_shared.cpp:385-412`), which lets the weapon add its bob
/// (`CTFWeaponBase::AddViewmodelBob`, `tf_weaponbase.cpp:4845`). The bob is two helpers, `:4713-4822`:
///
/// <code>
/// // CalcViewModelBobHelper — nothing at all while frametime is 0 or there is no player
/// speed = clamp( Length2D( velocity ), last − ( now − lastTime ) · 320, last + ( now − lastTime ) · 320 ), then ±320
/// bobTime += ( now − lastTime ) · speed / 320
/// cycle = bobTime − (int)( bobTime / 0.8 ) · 0.8,  over 0.8;        below ½: π · cycle / ½, else π + π · ( cycle − ½ ) / ½
/// vertical = clamp( speed · 0.005 · ( 0.3 + 0.7 · sin( cycle ) ), −7, 4 )
/// cycle = bobTime − (int)( bobTime / 0.8 · 2 ) · 0.8 · 2,  over 1.6;  the same two halves
/// lateral  = clamp( speed · 0.005 · ( 0.3 + 0.7 · sin( cycle ) ), −7, 4 )
///
/// // AddViewModelBobHelper — forward and right from the angles as they come in
/// origin += forward · vertical · 0.4;  origin.z += vertical · 0.1
/// roll += vertical · 0.5;  pitch −= vertical · 0.4;  yaw −= lateral · 0.3
/// origin += right · lateral · 0.2
/// </code>
///
/// **The lateral cycle's arithmetic is Valve's as written**: `bobTime / cycle * 2` is twice the quotient, not the
/// quotient by twice the cycle, so the "cycle" it leaves is negative for any bob time past 0.4 — and the sine does
/// not care. Tidying it would draw a different sway.
/// </remarks>
public sealed class ViewmodelViewConformanceTests
{
    /// <remarks>
    /// Ten seconds at full speed from a fresh state: bob time 10; the vertical cycle is exactly half way (sine of π,
    /// nothing), the lateral one lands on a sine of one.
    /// </remarks>
    [Test]
    public void Calc_TenSecondsAtFullSpeed_IsTheHelpersArithmetic()
    {
        ViewmodelBob bob = new();

        bob.Calc(speed: 320f, seconds: 10f, frameAdvanced: true);

        bob.Vertical.ShouldBe(0.48f, 0.001f);
        bob.Lateral.ShouldBe(1.6f, 0.001f);
    }

    /// <remarks>The speed may change by 320 a second and no faster: half a second after standing still it is 160.</remarks>
    [Test]
    public void Calc_ASuddenSprint_IsSlewedAt320ASecond()
    {
        ViewmodelBob bob = new();

        bob.Calc(speed: 0f, seconds: 10f, frameAdvanced: true);
        bob.Calc(speed: 320f, seconds: 10.5f, frameAdvanced: true);

        bob.Vertical.ShouldBe(0.7574f, 0.001f);
        bob.Lateral.ShouldBe(0.7056f, 0.001f);
    }

    /// <remarks>`if ( ( !gpGlobals->frametime ) || ( player == NULL ) ) return 0.0f; // just use old value`.</remarks>
    [Test]
    public void Calc_WhilePausedOrWithNoPlayer_KeepsTheOldValues()
    {
        ViewmodelBob bob = new();

        bob.Calc(speed: 320f, seconds: 10f, frameAdvanced: true);
        bob.Calc(speed: 0f, seconds: 30f, frameAdvanced: false);
        bob.Calc(speed: null, seconds: 40f, frameAdvanced: true);

        bob.Vertical.ShouldBe(0.48f, 0.001f);
        bob.Lateral.ShouldBe(1.6f, 0.001f);
    }

    /// <remarks>Looking along +X: forward is +X and right is −Y, and both are taken before the angles move.</remarks>
    [Test]
    public void Add_ToAnEyeLookingAlongX_MovesAndTurnsItAsTheHelperDoes()
    {
        ViewmodelBob bob = new();

        bob.Calc(speed: 320f, seconds: 10f, frameAdvanced: true);

        ViewmodelPlacement placed = bob.Add(new ViewmodelPlacement(100f, 200f, 300f, 0f, 0f, 0f));

        placed.X.ShouldBe(100f + (0.48f * 0.4f), 0.001f);
        placed.Y.ShouldBe(200f - (1.6f * 0.2f), 0.001f);
        placed.Z.ShouldBe(300f + (0.48f * 0.1f), 0.001f);
        placed.Roll.ShouldBe(0.48f * 0.5f, 0.001f);
        placed.Pitch.ShouldBe(-0.48f * 0.4f, 0.001f);
        placed.Yaw.ShouldBe(-1.6f * 0.3f, 0.001f);
    }

    [Test]
    public void Add_FromAFreshState_LeavesTheEyeWhereItIs()
    {
        ViewmodelPlacement eye = new(1f, 2f, 3f, 4f, 5f, 6f);

        new ViewmodelBob().Add(eye).ShouldBe(eye);
    }

    /// <remarks>
    /// `UTIL_StringToVector` over the item's `min_viewmodel_offset` — the stock rocket launcher's is `"10 -3 -10"` —
    /// laid along the eye's forward, right and up. Looking along +X that is 10 forward, 3 to the LEFT (right is −Y)
    /// and 10 down.
    /// </remarks>
    [Test]
    public void Offset_WithMinViewmodelsOn_MovesTheEyeAlongItsOwnAxes()
    {
        ViewmodelPlacement placed = MinViewmodel.Offset(new ViewmodelPlacement(0f, 0f, 64f, 0f, 0f, 0f), on: true, "10 -3 -10");

        placed.ShouldBe(new ViewmodelPlacement(10f, 3f, 54f, 0f, 0f, 0f));
    }

    [TestCase(false, "10 -3 -10")]
    [TestCase(true, null)]
    [TestCase(true, "")]
    public void Offset_SwitchedOffOrWithNoAttribute_LeavesTheEye(bool on, string? attribute)
    {
        ViewmodelPlacement eye = new(1f, 2f, 3f, 10f, 20f, 0f);

        MinViewmodel.Offset(eye, on, attribute).ShouldBe(eye);
    }

    /// <remarks>Turned to face +Y, the same offset goes 10 along +Y and 3 along −X.</remarks>
    [Test]
    public void Offset_WithTheEyeTurned_FollowsTheEye()
    {
        ViewmodelPlacement placed = MinViewmodel.Offset(new ViewmodelPlacement(0f, 0f, 0f, 0f, 90f, 0f), on: true, "10 -3 0");

        placed.X.ShouldBe(-3f, 0.001f);
        placed.Y.ShouldBe(10f, 0.001f);
        placed.Z.ShouldBe(0f, 0.001f);
    }
}
