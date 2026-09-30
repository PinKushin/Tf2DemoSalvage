using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The weapon in a player's hands is a HANDLE, found through its serial as `GetActiveWeapon()` finds it (B105).</summary>
/// <remarks>
/// **`m_hActiveWeapon` is an index and a serial**, and `RecvProxy_IntToEHandle` keeps both (client/recvproxy.cpp:80):
/// dereferencing compares the serial with the slot's current occupant, so a handle into a slot that has changed hands
/// names nothing rather than whoever moved in (B231, `docs/memory/a-handle-resolves-through-its-serial.md`).
///
/// **It matters more now than it did**: the item this handle names is what `GetActivityWeaponRole` reads the
/// `anim_slot` of (tf_weaponbase.cpp:4189-4197), so a stale handle resolved by its slot alone would animate a player
/// with whatever weapon — and whatever item — took the slot over. The three fields are one fact and go together.
/// </remarks>
public sealed class ActiveWeaponHandleTests
{
    [TestCase(null, 5, "CTFScatterGun", 200)]
    [TestCase(2, null, null, null)]
    public void PlayersAt_AnActiveWeaponHandle_NamesTheWeaponOnlyWhenItsSerialIsTheOccupants(
        int? serial, int? weapon, string? weaponClass, int? item)
    {
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticPlayer.DemoWithLoadout([(5, 200)], [], activeWeaponSerial: serial));

        ScenePlayer recorder = timeline.PlayersAt(100).Single(player => player.EntityIndex == 1);

        (recorder.ActiveWeapon, recorder.WeaponClass, recorder.WeaponItem).ShouldBe((weapon, weaponClass, item));
    }
}
