using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>`CTFDroppedWeapon` (tf_dropped_weapon.cpp:40), `CTFReviveMarker` (tf_revive.cpp:42) and the respawn waves, per frame.</summary>
public sealed class SceneIdEntityTests
{
    [Test]
    public void IdEntitiesAt_ADroppedWeaponAndAReviveMarker_ReadEveryField()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithIdEntities());
        IReadOnlyList<SceneIdEntity> found = timeline.IdEntitiesAt(100);

        found.Count.ShouldBe(2);
        found[0].ShouldBe(new SceneIdEntity(60, SceneIdEntityKind.DroppedWeapon)
        {
            Model = SyntheticPlayer.DroppedWeaponModel,
            ItemValid = true,
            ItemDefinition = 211,
            ItemQuality = 11,
            AccountId = 123456789u,
            ChargeLevel = 0.625f,
            Position = (10.5f, -20.25f, 4.75f),
            Angles = (0f, 90f, 0f),
            Mins = (-30f, -4f, -2f),
            Maxs = (30f, 4f, 6f),
            SolidType = 6,
            SolidFlags = 0x200,
        });
        found[1].ShouldBe(new SceneIdEntity(61, SceneIdEntityKind.ReviveMarker)
        {
            Health = 37,
            MaxHealth = 85,
            OwnerEntityIndex = 1,
            Position = (-100f, 50f, 8f),
            Mins = (-12f, -12f, 0f),
            Maxs = (12f, 12f, 48f),
            SolidType = 2,
            SolidFlags = 8,
        });
        found[1].IsSolid.ShouldBeTrue("FSOLID_TRIGGER alone does not make it pass-through");
    }

    [Test]
    public void RulesAt_TheRespawnWaveArraysAndRobotLogic_AreReadPerTeam()
    {
        SceneGameRules rules = DemoTimeline.Build(SyntheticPlayer.DemoWithIdEntities()).RulesAt(100);

        rules.NextRespawnWave.ShouldBe((120.5f, 131.25f));
        rules.TeamRespawnWaveTimes.ShouldBe((6f, -1f));
        rules.RobotDestructionRespawnScale.ShouldBe((0.25f, 0.5f));
    }
}
