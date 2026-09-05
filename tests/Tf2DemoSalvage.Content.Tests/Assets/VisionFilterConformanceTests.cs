using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The vision an item needs before anyone can see it — <c>vision_filter_flags</c> (B354).
/// </summary>
/// <remarks>
/// **One key, read straight off the item definition** (<c>econ_item_schema.cpp:3156</c>):
///
/// <code>
///   m_nVisionFilterFlags = m_pKVItem->GetInt( "vision_filter_flags", 0 );
/// </code>
///
/// and consumed by `CEconEntity::ShouldHideForVisionFilterFlags` (<c>econ_entity.cpp:1820</c>),
/// which hides the item from any viewer lacking that vision.
///
/// **23 shipped items declare it** — four at `TF_VISION_FILTER_PYRO` and nineteen at
/// `TF_VISION_FILTER_ROME` (<c>shareddefs.h:977</c>). Zero declare `TF_VISION_FILTER_HALLOWEEN`,
/// which is why the engine's holiday arm cannot change any drawing decision.
///
/// **It sits on the item, not inside `visuals`** — unlike `wm_bodygroup_override` — so a reader that
/// filed it with the other visual keys would find nothing. Checked against the shipped file rather
/// than assumed from the neighbours.
/// </remarks>
public sealed class VisionFilterConformanceTests
{
    [Test]
    public void VisionFilterFlags_ForAPyrolandItem_AreThePyroBit()
    {
        Read().VisionFilterFlagsFor(738).ShouldBe(1);
    }

    /// <remarks>
    /// The control, and the common case by a wide margin: all but 23 shipped items declare nothing,
    /// and `GetInt( …, 0 )` makes that a zero the consumer's `!= 0` guard turns into "never hidden".
    /// </remarks>
    [Test]
    public void VisionFilterFlags_ForAnOrdinaryItem_AreNone()
    {
        Read().VisionFilterFlagsFor(999).ShouldBe(0);
    }

    /// <remarks>
    /// Inherited like every other definition key, because the engine merges the prefab chain into
    /// one KeyValues block before reading any of it.
    /// </remarks>
    [Test]
    public void VisionFilterFlags_AreInheritedFromAPrefab()
    {
        Read().VisionFilterFlagsFor(602).ShouldBe(4);
    }

    /// <remarks>
    /// **The nearest definition wins, and an item can turn its prefab's filter OFF.** This is what
    /// separates "the item states 0" from "the item states nothing", and reading them alike would
    /// make a prefab's filter unremovable — the same tri-state that
    /// <c>hide_bodygroups_deployed_only</c> needed.
    /// </remarks>
    [Test]
    public void VisionFilterFlags_WhenAnItemOverridesItsPrefabWithZero_AreNone()
    {
        Read().VisionFilterFlagsFor(603).ShouldBe(0);
    }

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));

    private const string Schema = """
        "items_game"
        {
            "prefabs"
            {
                "robot_skin"
                {
                    "vision_filter_flags" "4"
                }
            }
            "items"
            {
                "738"
                {
                    "name" "Pet Balloonicorn"
                    "vision_filter_flags" "1"
                }
                "602"
                {
                    "name" "an MvM robot skin"
                    "prefab" "robot_skin"
                }
                "603"
                {
                    "name" "a robot skin everyone can see"
                    "prefab" "robot_skin"
                    "vision_filter_flags" "0"
                }
                "999"
                {
                    "name" "an ordinary hat"
                }
            }
        }
        """;
}
