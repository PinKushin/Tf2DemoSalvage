using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`C_BaseObject`/`CObjectSentrygun` (tf_obj.cpp, tf_obj_sentrygun.cpp), per frame.</summary>
public sealed class SceneBuildingTests
{
    [Test]
    public void BuildingsAt_ASentrygun_ReadsEveryField()
    {
        SceneBuilding building = DemoTimeline.Build(SyntheticPlayer.DemoWithBuilding()).BuildingsAt(100).ShouldHaveSingleItem();

        building.ShouldBe(new SceneBuilding(55)
        {
            Health = 111,
            MaxHealth = 150,
            ObjectType = SceneBuilding.Sentrygun,
            ObjectMode = SceneBuilding.TeleporterExit,
            Team = null,
            BuilderEntityIndex = 1,
            Sapped = true,
            Disabled = true,
            Building = true,
            Placing = false,
            Carried = true,
            MiniBuilding = false,
            DisposableBuilding = true,
            UpgradeLevel = 3,
            UpgradeMetal = 197,
            UpgradeMetalRequired = 200,
            PercentageConstructed = 0.75f,
            SentryAmmoShells = 140,
            SentryAmmoRockets = 6,
            DispenserAmmoMetal = null,
            TeleporterState = null,
            Position = (128.5f, -64.25f, 32.75f),
            Mins = (-20f, -20f, 0f),
            Maxs = (20f, 20f, 66f),
            SolidType = 2,
            SolidFlags = 4,
        });
    }
}
