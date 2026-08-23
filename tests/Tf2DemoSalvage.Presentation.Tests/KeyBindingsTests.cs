using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// Rebindable actions, with defaults taken from what TF2 actually ships.
/// </summary>
/// <remarks>
/// **The defaults are read from the game rather than remembered.** TF2's `tf_english.txt` carries
/// `"TF_Spectator_SwitchCamModeKey" "[%jump%]"` beside `"Switch Camera Mode"`, and
/// `"TF_Spectator_CycleTargetFwdKey" "[%attack%]"` beside the target cycling — so the game binds
/// the ACTION and prints whichever key the player chose. That is the shape copied here.
/// </remarks>
public sealed class KeyBindingsTests
{
    [Test]
    public void Defaults_SwitchCameraMode_IsSpaceAsTf2Binds()
    {
        // TF2 prints [%jump%] for "Switch Camera Mode", and jump is Space unless rebound. Anyone
        // who has spectated in TF2 will press it without being told, which is the argument for
        // matching a convention rather than inventing one.
        new KeyBindings().KeyFor(ViewerAction.SwitchCameraMode).ShouldBe("Space");
    }

    [Test]
    public void Defaults_TargetCycling_IsOnTheTwoAttackButtons()
    {
        KeyBindings bindings = new();

        bindings.KeyFor(ViewerAction.CycleTargetForward).ShouldBe("MouseLeft");
        bindings.KeyFor(ViewerAction.CycleTargetReverse).ShouldBe("MouseRight");
    }

    [Test]
    public void Defaults_EveryAction_IsBoundToSomething()
    {
        // **An unbound action is a feature the user cannot reach**, and it fails silently: the key
        // does nothing and there is nothing to see. Enumerating the enum rather than listing the
        // actions means adding one to the enum and forgetting the default reddens this.
        KeyBindings bindings = new();

        foreach (ViewerAction action in Enum.GetValues<ViewerAction>())
        {
            bindings.KeyFor(action).ShouldNotBeNullOrWhiteSpace($"{action} has no default binding");
        }
    }

    [Test]
    public void Bind_ReplacesADefault()
    {
        KeyBindings bindings = new();

        bindings.Bind(ViewerAction.SwitchCameraMode, "Tab");

        bindings.KeyFor(ViewerAction.SwitchCameraMode).ShouldBe("Tab");
        bindings.ActionsFor("Space").ShouldNotContain(ViewerAction.SwitchCameraMode);
    }

    [Test]
    public void Constructor_PartialBindings_KeepTheDefaultsForEverythingElse()
    {
        // A settings file that names two bindings must not silently unbind the other ten. That is
        // the failure where a user rebinds one key and loses the rest of the controls.
        KeyBindings bindings = new(new Dictionary<ViewerAction, string>
        {
            [ViewerAction.PlayPause] = "P",
        });

        bindings.KeyFor(ViewerAction.PlayPause).ShouldBe("P");
        bindings.KeyFor(ViewerAction.FlyForward).ShouldBe("W", "untouched actions keep their default");
    }

    [Test]
    public void ActionsFor_AKeyBoundTwice_ReturnsBothRatherThanPickingOne()
    {
        // **A table that silently picked one would make a collision look like a lost binding**, so
        // this reports every match and leaves the choice to whoever handles the key.
        //
        // Deliberately constructed rather than taken from the defaults, because the defaults no
        // longer collide — and that is itself a decision worth not undoing by accident. `FlyUp` was
        // on Space alongside `SwitchCameraMode` for one commit; `ProcessCmdKey` checks flight keys
        // first, swallowed the press, and three UI tests failed by timing out on a key that did
        // nothing. Source separates the two as well: `+moveup` drives the roaming camera's vertical
        // and `+jump` cycles the observer mode.
        KeyBindings collided = new(new Dictionary<ViewerAction, string>
        {
            [ViewerAction.FlyUp] = "Space",
        });

        IReadOnlyList<ViewerAction> actions = collided.ActionsFor("Space");

        actions.ShouldContain(ViewerAction.SwitchCameraMode);
        actions.ShouldContain(ViewerAction.FlyUp);
        actions.Count.ShouldBe(2);
    }

    [Test]
    public void Defaults_NoTwoActions_ShareAKey()
    {
        // **The control for the test above, and the one that would have caught the collision.**
        // Sharing a key is legal and reportable, but a DEFAULT that shares one means a control the
        // user cannot reach out of the box — and it fails by doing nothing, which is the hardest
        // failure to attribute.
        List<string> keys = [.. KeyBindings.Defaults.Values];

        keys.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(
            keys.Count, "two actions share a default key, so one of them is unreachable");
    }

    [Test]
    public void ActionsFor_IsCaseInsensitive()
    {
        // Bindings come from a text file a person edits by hand, and "space" is what they will
        // type. Rejecting it would look like the setting being ignored.
        new KeyBindings().ActionsFor("space").ShouldContain(ViewerAction.SwitchCameraMode);
        new KeyBindings().ActionsFor("SPACE").ShouldContain(ViewerAction.SwitchCameraMode);
    }

    [Test]
    public void ActionsFor_AnUnboundKey_IsEmptyRatherThanThrowing()
    {
        new KeyBindings().ActionsFor("Z").ShouldBeEmpty();
    }

    [Test]
    public void All_IsOrderedSoASettingsFileCanBeDiffed()
    {
        // A settings file that reorders itself between runs is one nobody can diff, and every
        // save looks like a change.
        IReadOnlyList<(ViewerAction Action, string Key)> first = new KeyBindings().All();
        IReadOnlyList<(ViewerAction Action, string Key)> second = new KeyBindings().All();

        first.ShouldBe(second);
        first.Count.ShouldBe(Enum.GetValues<ViewerAction>().Length);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Bind_AnEmptyKey_IsRefused(string key)
    {
        // Binding an action to nothing removes it from the user's reach while looking like a
        // successful setting.
        Should.Throw<ArgumentException>(() => new KeyBindings().Bind(ViewerAction.PlayPause, key));
    }
}
