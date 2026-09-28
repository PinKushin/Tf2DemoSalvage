using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// `DT_SceneEntity` as `C_SceneEntity` receives it (`c_sceneentity.cpp:33`): the scene's string index, whether it is
/// playing and paused, and `m_hActorList`, a `SendPropUtlVector` of handles.
/// </summary>
public sealed class EntitySceneStateTests
{
    private const string Table = "DT_SceneEntity";

    /// <summary>The count's key, reached through the `lengthproxy` sub-table and so keyed by path.</summary>
    private const string Length = "DT_SceneEntity.m_hActorList.lengthproxy.lengthprop16";

    [Test]
    public void SceneFields_NeverSent_AreNull()
    {
        EntityState scene = Scene();

        scene.SceneStringIndex().ShouldBeNull();
        scene.ScenePlayingBack().ShouldBeNull();
        scene.ScenePaused().ShouldBeNull();
        scene.SceneActors().ShouldBeEmpty();
    }

    [TestCase(1, true)]
    [TestCase(0, false)]
    public void ScenePlayingBackAndPaused_TheSentBit_IsItsTruth(int sent, bool expected)
    {
        EntityState scene = Scene();
        scene.Set($"{Table}.m_bIsPlayingBack", PropertyValue.FromInt(sent));
        scene.Set($"{Table}.m_bPaused", PropertyValue.FromInt(1 - sent));
        scene.Set($"{Table}.m_nSceneStringIndex", PropertyValue.FromInt(17));

        scene.ScenePlayingBack().ShouldBe(expected);
        scene.ScenePaused().ShouldBe(!expected, "the other bit, so the two are read from their own keys");
        scene.SceneStringIndex().ShouldBe(17);
    }

    [Test]
    public void SceneActors_HandlesWithinTheLength_AreTheirEntityIndices()
    {
        // A handle is the index in its low eleven bits under a serial number (`MAX_EDICT_BITS`, basehandle.h).
        EntityState scene = Scene();
        scene.Set(Length, PropertyValue.FromInt(2));
        scene.Set("_ST_m_hActorList_16.001", PropertyValue.FromInt((5 << 11) | 9));
        scene.Set("_ST_m_hActorList_16.000", PropertyValue.FromInt((3 << 11) | 4));

        scene.SceneActors().ShouldBe([4, 9], "in element order, not the order the keys arrived in");
    }

    [Test]
    public void SceneActors_AnElementPastTheLength_IsAStaleOneAndDropped()
    {
        // The vector shrank: the client's `CUtlVector` is resized to the count, so the element left behind is gone.
        EntityState scene = Scene();
        scene.Set(Length, PropertyValue.FromInt(1));
        scene.Set("_ST_m_hActorList_16.000", PropertyValue.FromInt(4));
        scene.Set("_ST_m_hActorList_16.001", PropertyValue.FromInt(9));

        scene.SceneActors().ShouldBe([4]);
    }

    [Test]
    public void SceneActors_NoLengthSent_KeepsEveryElement()
    {
        EntityState scene = Scene();
        scene.Set("_ST_m_hActorList_16.000", PropertyValue.FromInt(4));
        scene.Set("_ST_m_hActorList_16.001", PropertyValue.FromInt(9));

        scene.SceneActors().ShouldBe([4, 9]);
    }

    [Test]
    public void SceneActors_AnInvalidHandleOrAKeyWithNoIndex_NamesNoActor()
    {
        EntityState scene = Scene();
        scene.Set(Length, PropertyValue.FromInt(3));
        scene.Set("_ST_m_hActorList_16.000", PropertyValue.FromInt(EntityState.NoHandle));
        scene.Set("_ST_m_hActorList_16.001", PropertyValue.FromInt(9));
        scene.Set("_ST_m_hActorList_16.x", PropertyValue.FromInt(6));

        scene.SceneActors().ShouldBe([9]);
    }

    private static EntityState Scene() => new(40, 0, 0, "CSceneEntity");
}
