using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A viewmodel wearable leaves every world list (B771, `econ_wearable.cpp:375-393`).</summary>
public sealed class ViewModelWearablesTests
{
    /// <remarks>
    /// `bUseViewModel` is false for every player a world list draws, so a `CTFWearableVM` goes and a `CTFWearable` stays,
    /// the control.
    /// </remarks>
    [Test]
    public void OutOfTheWorld_AViewmodelWearableBesideAWorldOne_KeepsOnlyTheWorldOne()
    {
        List<SceneProp> drawn = [Item(1, ViewModelWearables.ClassName), Item(2, "CTFWearable")];

        DrawList.KeepOnly(drawn, ViewModelWearables.OutOfTheWorld(drawn));

        drawn.ShouldHaveSingleItem().EntityIndex.ShouldBe(2);
    }

    [Test]
    public void OutOfTheWorld_NoViewmodelWearable_ReturnsTheListItself()
    {
        List<SceneProp> drawn = [Item(2, "CTFWearable")];

        ViewModelWearables.OutOfTheWorld(drawn).ShouldBeSameAs(drawn);
    }

    [Test]
    public void Of_AMoment_GathersOnlyItsViewmodelWearables()
    {
        List<SceneProp> found = [Item(9, "stale")];

        ViewModelWearables.Of([Item(1, ViewModelWearables.ClassName), Item(2, "CTFWearable")], found);

        found.ShouldHaveSingleItem().EntityIndex.ShouldBe(1);
    }

    private static SceneProp Item(int entity, string className) =>
        new(entity, "models/player/items/demo/hat.mdl", SceneModelKind.Studio, new ScenePose(), AttachedTo: 3, OwnedBy: 3)
        {
            ClassName = className,
        };
}
