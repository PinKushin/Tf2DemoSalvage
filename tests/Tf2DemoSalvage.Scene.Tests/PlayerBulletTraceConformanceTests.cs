using System;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Where a bullet stops once players are in the way — TF2's `UTIL_PlayerBulletTrace` (B415).</summary>
/// <remarks>
/// `tf_player_shared.cpp:10240`: one trace with `CONTENTS_HITBOX`, whose entity pass only reaches players the
/// partition finds along the ray clipped to the world; then, when that hit no entity, `UTIL_ClipTraceToPlayers` down a
/// ray 40 units longer, keeping the nearest player within 60 units of it — accepted only when a world trace from the
/// hit to the player's origin, at the hit's height, reaches the player.
/// </remarks>
public sealed class PlayerBulletTraceConformanceTests
{
    private static readonly Vector3 Start = new(0f, 0f, 50f);
    private static readonly Vector3 End = new(1000f, 0f, 50f);

    /// <summary>An open world.</summary>
    private static readonly Func<Vector3, Vector3, float> Open = static (_, _) => 1f;

    [Test]
    public void Clip_APlayerInFrontOfTheWall_StopsTheBulletAtTheirHitbox()
    {
        BulletTarget player = new(7, new Vector3(500f, 0f, 0f), Ducked: false);

        (Vector3 end, int? struck) = PlayerBulletTrace.Clip(
            Start, End, worldFraction: 0.9f, [player], Hitbox(7, 0.48f), Open);

        struck.ShouldBe(7);
        end.X.ShouldBe(480f, 0.001f);
    }

    [Test]
    public void Clip_NoPlayers_EndsAtTheWorld()
    {
        (Vector3 end, int? struck) = PlayerBulletTrace.Clip(Start, End, 0.9f, [], Hitbox(7, 0.48f), Open);

        struck.ShouldBeNull();
        end.X.ShouldBe(900f, 0.001f);
    }

    /// <remarks>
    /// **A player behind the wall is not in the main pass**, because the partition is walked along the ray clipped to
    /// the world. Their hull starts at x = 476 and the wall is at 300.
    /// </remarks>
    [Test]
    public void Clip_APlayerBehindTheWall_IsNotHit()
    {
        BulletTarget player = new(7, new Vector3(500f, 0f, 0f), Ducked: false);

        (Vector3 end, int? struck) = PlayerBulletTrace.Clip(
            Start, End, worldFraction: 0.3f, [player], Hitbox(7, 0.48f), static (_, _) => 0.5f);

        struck.ShouldBeNull();
        end.X.ShouldBe(300f, 0.001f);
    }

    /// <remarks>
    /// **The extension is what catches a hitbox outside the hull**: a player off to the side, whose hull the ray
    /// misses but whose arm it crosses, is found by `UTIL_ClipTraceToPlayers` — the ray passes 40 units from their
    /// centre — and kept because nothing stands between the arm and their origin. The fraction is along the
    /// EXTENDED ray, 1040 units, which is Valve's own mismatch with the world's.
    /// </remarks>
    [Test]
    public void Clip_AnArmOutsideTheHull_IsHitByTheExtension()
    {
        BulletTarget player = new(7, new Vector3(500f, 40f, 9f), Ducked: false);

        (Vector3 end, int? struck) = PlayerBulletTrace.Clip(
            Start, End, worldFraction: 0.9f, [player], Hitbox(7, 0.5f), Open);

        struck.ShouldBe(7);
        end.X.ShouldBe(520f, 0.001f);
    }

    /// <remarks>The validation trace from the arm to the player's origin hits a wall first, so the hit is thrown away.</remarks>
    [Test]
    public void Clip_AnArmThroughAThinWall_IsRejected()
    {
        BulletTarget player = new(7, new Vector3(500f, 40f, 9f), Ducked: false);

        (Vector3 end, int? struck) = PlayerBulletTrace.Clip(
            Start, End, worldFraction: 0.9f, [player], Hitbox(7, 0.5f), static (_, _) => 0.1f);

        struck.ShouldBeNull();
        end.X.ShouldBe(900f, 0.001f);
    }

    /// <remarks>
    /// **The case Josh's comment names, end to end.** A wall at x = 470; a player whose hull starts at 476, behind
    /// it; their arm pokes through to x = 460, in front of it. The main pass never tries them — the world-clipped ray
    /// stops at 470, short of the hull — so the arm is found only by the extension, and the validation trace from the
    /// arm towards their origin meets the wall at 470 before their hull at 476. The bullet stops at the wall.
    /// Tested all players in the main pass instead, the arm would be struck.
    /// </remarks>
    [Test]
    public void Clip_AnArmPokingThroughAThinWallFromBehindIt_LeavesTheBulletAtTheWall()
    {
        const float Wall = 470f;
        const float Arm = 460f;

        BulletTarget player = new(7, new Vector3(500f, 0f, 0f), Ducked: false);

        (Vector3 end, int? struck) = PlayerBulletTrace.Clip(
            Start,
            End,
            worldFraction: Wall / 1000f,
            [player],
            static (_, from, delta) => (Arm - from.X) / delta.X,
            static (from, to) => from.X < Wall && to.X > Wall ? (Wall - from.X) / (to.X - from.X) : 1f);

        struck.ShouldBeNull();
        end.X.ShouldBe(Wall, 0.001f);
    }

    [Test]
    public void Clip_APlayerMoreThanSixtyUnitsFromTheRay_IsNotTried()
    {
        BulletTarget player = new(7, new Vector3(500f, 90f, 9f), Ducked: false);

        (_, int? struck) = PlayerBulletTrace.Clip(Start, End, 0.9f, [player], Hitbox(7, 0.5f), Open);

        struck.ShouldBeNull();
    }

    /// <summary>A world where only one entity has hitboxes, struck at a fixed fraction of whatever ray is asked.</summary>
    private static Func<int, Vector3, Vector3, float?> Hitbox(int entity, float fraction) =>
        (asked, _, _) => asked == entity ? fraction : null;
}
