namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Whether a key drives something the console holds down.</summary>
/// <remarks>
/// **This was a loop inside `FreeFlight`, a WinForms helper in the viewer project** (B188, D90). It
/// walked `ConfigConsole.HeldActions` comparing each against `KeyBindings.KeyFor` — two Presentation
/// tables, joined in a view. What genuinely belongs on that side is turning a `Keys` value into a
/// key NAME; deciding what that name means is a question about bindings.
///
/// **The window asks it to know whether to swallow a key.** A key swallowed but never pressed into
/// the console, or pressed but never swallowed, produces a camera that moves once and stops — which
/// is the failure the old comment described and the reason the two answers must come from one list.
/// </remarks>
public sealed class HeldKeyTests
{
    /// <summary>The viewer's own bindings, as it ships before any config is read.</summary>
    private static readonly KeyBindings Bound = new();

    [Test]
    public void IsHeldKey_ForAFlightBind_IsTrue()
    {
        ConfigConsole.IsHeldKey("w", Bound).ShouldBeTrue();
    }

    [Test]
    public void IsHeldKey_ForAKeyBoundToNothingHeld_IsFalse()
    {
        // **The control.** A predicate that answered true for everything would swallow every key
        // the window sees while the free camera is on, including the ones that open menus.
        ConfigConsole.IsHeldKey("F12", Bound).ShouldBeFalse();
    }

    [Test]
    public void IsHeldKey_ForAnUnboundKey_IsFalse()
    {
        ConfigConsole.IsHeldKey("F", Bound).ShouldBeFalse();
    }

    [Test]
    public void IsHeldKey_ForNothing_IsFalse()
    {
        // A `Keys` value with no Source name resolves to an empty string, which must not be treated
        // as a key bound to nothing-in-particular.
        ConfigConsole.IsHeldKey(string.Empty, Bound).ShouldBeFalse();
    }

    [Test]
    public void IsHeldKey_ForAKeyThatIsBothHeldAndNot_IsTrue()
    {
        // **One held action is enough, not all of them** — a predicate that required every action to
        // be held would stop the free camera rising the moment somebody shared a key.
        //
        // **The double-bind is BUILT here, not taken from the defaults, and finding that out is why
        // this test is written this way.** Its first version asserted `SPACE` was double-bound
        // because `ActionsFor`'s own documentation says so — *"Space is both 'switch camera mode'
        // and 'fly up' by default"*. It is not: `FlyUp` moved to `'` when the bindings were made to
        // follow TF2's `+moveup` (D69), and no key in `Defaults` carries two actions any more.
        //
        // The precondition failed rather than the assertion, which is the whole reason it was there:
        // without it this would have passed while measuring the ordinary single-action case, and the
        // interesting one would have gone uncovered in silence.
        KeyBindings shared = new();

        shared.Bind(ViewerAction.SwitchCameraMode, "j");
        shared.Bind(ViewerAction.FlyUp, "j");

        shared.ActionsFor("j").Count.ShouldBe(2, "the input this test needs is a shared key");

        ConfigConsole.IsHeldKey("j", shared).ShouldBeTrue();
    }

    [Test]
    public void IsHeldKey_ForASharedKeyWhereNothingIsHeld_IsFalse()
    {
        // **The control for the case above.** Two actions on one key must not make it held by
        // arithmetic; it is held because one of them is.
        KeyBindings shared = new();

        shared.Bind(ViewerAction.SwitchCameraMode, "j");
        shared.Bind(ViewerAction.ResetCamera, "j");

        ConfigConsole.IsHeldKey("j", shared).ShouldBeFalse();
    }

    [Test]
    public void IsHeldKey_IsCaseInsensitive()
    {
        // Config files spell keys however the person typed them; `bind W` and `bind w` are the same
        // bind to the engine.
        ConfigConsole.IsHeldKey("W", Bound).ShouldBeTrue();
    }
}
