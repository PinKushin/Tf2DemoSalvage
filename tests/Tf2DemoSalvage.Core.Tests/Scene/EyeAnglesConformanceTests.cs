using System;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A player's eye angles against the client's ONE <c>m_angEyeAngles</c>, which two receive tables write (B442).
/// </summary>
/// <remarks>
/// **Two tables, one member** (<c>c_tf_player.cpp:3734-3767</c>):
///
/// <code>
/// // specific to the local player
/// BEGIN_RECV_TABLE_NOBASE( C_TFPlayer, DT_TFLocalPlayerExclusive )
///     ...
///     RecvPropFloat( RECVINFO( m_angEyeAngles[0] ) ),
///     RecvPropFloat( RECVINFO( m_angEyeAngles[1] ) ),
///     ...
/// // all players except the local player
/// BEGIN_RECV_TABLE_NOBASE( C_TFPlayer, DT_TFNonLocalPlayerExclusive )
///     ...
///     RecvPropFloat( RECVINFO( m_angEyeAngles[0] ) ),
///     RecvPropFloat( RECVINFO( m_angEyeAngles[1] ) ),
/// </code>
///
/// <c>RECVINFO( m_angEyeAngles[1] )</c> is the same offset of the same <c>C_TFPlayer</c> in both, so each
/// received component overwrites whatever either table put there before: the member holds the LAST write,
/// component by component. Which table a client is sent is the server's choice (<c>tf_player.cpp:800-804</c>,
/// "Data that only gets sent to the local player" / "Data that gets sent to all other players") — but a
/// point-of-view recorder's own ENTER carries BOTH, and every later update only the local one.
///
/// **B442 was this, read in a fixed order.** <c>EyeAngles()</c> took the non-local table whenever it held
/// anything. For the recorder of <c>movement-test-pov-cp_process</c> that table was written once, at the
/// tick-0 ENTER (<c>336.422</c>), and never again, while 2,033 local writes followed — so the recorder faced
/// 336.4 for the whole demo, and at tick 5541, running at 209.4, <c>move_x</c> read -0.674. The origin had
/// the same shape and was fixed the same way (c7d65f1b); the eye angles kept the fixed order.
/// </remarks>
public sealed class EyeAnglesConformanceTests
{
    private const string ClientPlayer = "src/game/client/tf/c_tf_player.cpp";
    private const string LocalTable = "DT_TFLocalPlayerExclusive";
    private const string NonLocalTable = "DT_TFNonLocalPlayerExclusive";
    private const string Pitch = "m_angEyeAngles[0]";
    private const string Yaw = "m_angEyeAngles[1]";

    [TestCase(LocalTable)]
    [TestCase(NonLocalTable)]
    public void RecvTable_EachExclusiveTable_WritesBothComponentsOfTheOneMember(string table)
    {
        // **The premise, read rather than assumed**: if either table wrote a member of its own, the two
        // would be separate values and last-write-wins across them would be wrong.
        SourceSdk.Require();

        string source = SourceSdk.Text(ClientPlayer).ShouldNotBeNull();
        string opening = $"BEGIN_RECV_TABLE_NOBASE( C_TFPlayer, {table} )";
        int start = source.IndexOf(opening, StringComparison.Ordinal);

        start.ShouldBeGreaterThanOrEqualTo(0, $"{opening} is not in {ClientPlayer}");

        int end = source.IndexOf("END_RECV_TABLE()", start, StringComparison.Ordinal);
        string block = source[start..end];

        block.ShouldContain($"RecvPropFloat( RECVINFO( {Pitch} ) )");
        block.ShouldContain($"RecvPropFloat( RECVINFO( {Yaw} ) )");
    }

    [Test]
    public void EyeAngles_ALocalWriteAfterTheEnter_IsWhatTheMemberHolds()
    {
        // **The recorder's own sequence, from the demo** (B442). The ENTER writes the local table and then
        // the non-local one — `tflocaldata` is declared before `tfnonlocaldata` (tf_player.cpp:801, :804) —
        // with the same angles; every update after it writes the local table alone.
        EntityState recorder = new(2, 0, 0, "CTFPlayer");

        Write(recorder, LocalTable, pitch: 3.882f, yaw: 336.422f);
        Write(recorder, NonLocalTable, pitch: 3.882f, yaw: 336.422f);
        recorder.Set($"{LocalTable}.{Yaw}", PropertyValue.FromFloat(209.384f));

        recorder.EyeAngles().ShouldBe((3.882f, 209.384f));
    }

    [Test]
    public void EyeAngles_ANonLocalWriteAfterTheEnter_IsWhatTheMemberHolds()
    {
        // **The control: every other player**, whose ENTER carries both tables too and whose updates come
        // through the non-local one. A fix that simply preferred the local table would break this.
        EntityState other = new(3, 0, 0, "CTFPlayer");

        Write(other, LocalTable, pitch: 3.882f, yaw: 336.422f);
        Write(other, NonLocalTable, pitch: 3.882f, yaw: 336.422f);
        other.Set($"{NonLocalTable}.{Yaw}", PropertyValue.FromFloat(209.384f));

        other.EyeAngles().ShouldBe((3.882f, 209.384f));
    }

    [Test]
    public void EyeAngles_EachComponent_IsItsOwnLastWrite()
    {
        // **Per COMPONENT, because each RecvProp writes one element.** The tables disagree on purpose so the
        // three readings part: the member is (40, 30); a rule choosing the newest TABLE gives (10, 30); the
        // fixed order this replaces gave (40, 50).
        EntityState player = new(2, 0, 0, "CTFPlayer");

        Write(player, LocalTable, pitch: 10f, yaw: 20f);
        Write(player, NonLocalTable, pitch: 40f, yaw: 50f);
        player.Set($"{LocalTable}.{Yaw}", PropertyValue.FromFloat(30f));

        player.EyeAngles().ShouldBe((40f, 30f));
    }

    private static void Write(EntityState player, string table, float pitch, float yaw)
    {
        player.Set($"{table}.{Pitch}", PropertyValue.FromFloat(pitch));
        player.Set($"{table}.{Yaw}", PropertyValue.FromFloat(yaw));
    }
}
