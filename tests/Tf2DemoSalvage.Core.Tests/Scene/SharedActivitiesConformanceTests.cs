using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The engine's shared activity list — `ActivityList_RegisterSharedActivities` over the `Activity` enum (B437).
/// </summary>
/// <remarks>
/// **An activity travels as its LIST INDEX**: a voice command's `m_nData` is the server's
/// `ActivityList_IndexForName( … )`, and `REGISTER_SHARED_ACTIVITY( ACT_X )` registers each shared name at its enum
/// value (`activitylist.cpp:108-137`, asserting each is the last plus one). So the list is the enum, in order, from
/// `ACT_RESET` at 0. Compared with `ai_activity.h` both ways.
/// </remarks>
public sealed class SharedActivitiesConformanceTests
{
    [Test]
    public void SharedActivities_EveryIndex_MatchesTheSdkEnum()
    {
        if (SourceSdk.Text("src/game/shared/ai_activity.h") is not { } header)
        {
            Assert.Ignore("the Source SDK is not available");
            return;
        }

        string body = header[header.IndexOf("ACT_RESET", System.StringComparison.Ordinal)..
            header.IndexOf("LAST_SHARED_ACTIVITY", System.StringComparison.Ordinal)];
        List<string> sdk = [.. Regex.Matches(body, @"(?m)^\s*(ACT_[A-Za-z0-9_]+)").Select(match => match.Groups[1].Value)];

        sdk.Count.ShouldBeGreaterThan(1000, "the control: the parse must find the enum");

        for (int index = 0; index < sdk.Count; index++)
        {
            SharedActivities.NameOf(index).ShouldBe(sdk[index], $"index {index}");
        }

        SharedActivities.NameOf(sdk.Count).ShouldBeNull("past LAST_SHARED_ACTIVITY is a private activity");
        SharedActivities.NameOf(-1).ShouldBeNull("ACT_INVALID");
    }

    [Test]
    public void IsShared_ANameInTheList_IsAndAnInventedOneIsNot()
    {
        SharedActivities.IsShared("ACT_MP_GESTURE_VC_HANDMOUTH").ShouldBeTrue();
        SharedActivities.IsShared("ACT_ITEM2_VM_FIRE").ShouldBeFalse("a viewmodel's private name, not in the enum");
    }

    /// <remarks>
    /// **A voice command names its activity by that index** (`tf_playeranimstate.cpp:1053-1058`,
    /// `RestartGesture( GESTURE_SLOT_ATTACK_AND_RELOAD, (Activity)nData )`). A shared index resolves to its name; one
    /// past the shared list is a private activity whose number the server assigned at load, and stays a number.
    /// </remarks>
    [Test]
    public void Map_AVoiceCommandGesture_NamesItsSharedActivity()
    {
        int handMouth = Enumerable.Range(0, 3000).First(index => SharedActivities.NameOf(index) == "ACT_MP_GESTURE_VC_HANDMOUTH");

        GestureTrigger shared = PlayerGestureEvent.Map(PlayerAnimEvent.VoiceCommandGesture, new GestureContext(NData: handMouth))
            .ShouldNotBeNull();
        shared.ActivityName.ShouldBe("ACT_MP_GESTURE_VC_HANDMOUTH");
        shared.ActivityNumber.ShouldBeNull();

        GestureTrigger custom = PlayerGestureEvent.Map(PlayerAnimEvent.CustomGesture, new GestureContext(NData: handMouth))
            .ShouldNotBeNull();
        custom.ActivityName.ShouldBe("ACT_MP_GESTURE_VC_HANDMOUTH");

        GestureTrigger privateOne = PlayerGestureEvent.Map(PlayerAnimEvent.VoiceCommandGesture, new GestureContext(NData: 5000))
            .ShouldNotBeNull();
        privateOne.ActivityName.ShouldBeNull();
        privateOne.ActivityNumber.ShouldBe(5000);
    }
}
