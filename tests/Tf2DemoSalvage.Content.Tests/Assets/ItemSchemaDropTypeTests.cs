using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>m_iDropType</c> — whether an item stays attached to the body on death, drops off it, or
/// breaks.
/// </summary>
/// <remarks>
/// <c>m_iDropType = StringFieldToInt( m_pKVItem->GetString("drop_type"), g_szDropTypeStrings, 4 )</c>
/// (<c>econ_item_schema.cpp:3173</c>), against the table <c>{ "", "none", "drop", "break" }</c>
/// (<c>econ_item_schema.cpp:69-74</c>). See <see cref="ItemSchema.DropTypeNone"/>'s remarks for why
/// a missing key resolves to <see cref="ItemSchema.DropTypeNone"/> here rather than to the engine's
/// literal runtime sentinel of -1 — the two are interchangeable everywhere the SDK reads the value.
/// </remarks>
public sealed class ItemSchemaDropTypeTests
{
    /// <summary>A schema small enough to reason about, shaped like the real file's drop_type use.</summary>
    private const string Schema = """
        "items_game"
        {
            "prefabs"
            {
                "base_dropping"
                {
                    "drop_type" "drop"
                }
                "base_breaking"
                {
                    "drop_type" "break"
                }
            }
            "items"
            {
                "100"
                {
                    "name" "EXPLICIT_NONE"
                    "drop_type" "none"
                }
                "101"
                {
                    "name" "EXPLICIT_DROP"
                    "drop_type" "drop"
                }
                "102"
                {
                    "name" "EXPLICIT_BREAK"
                    "drop_type" "break"
                }
                "103"
                {
                    "name" "NO_DROP_TYPE_KEY"
                }
                "104"
                {
                    "name" "EXPLICIT_BLANK"
                    "drop_type" ""
                }
                "105"
                {
                    "name" "MIXED_CASE_DROP"
                    "drop_type" "DrOp"
                }
                "106"
                {
                    "name" "UNRECOGNIZED_VALUE"
                    "drop_type" "sparkles"
                }
                "107"
                {
                    "name" "INHERITS_FROM_PREFAB"
                    "prefab" "base_dropping"
                }
                "108"
                {
                    "name" "OVERRIDES_ITS_PREFAB"
                    "prefab" "base_breaking"
                    "drop_type" "none"
                }
            }
        }
        """;

    [Test]
    public void DropType_AnItemDeclaringNone_IsNone()
    {
        Read().DropType(100).ShouldBe(ItemSchema.DropTypeNone);
    }

    [Test]
    public void DropType_AnItemDeclaringDrop_IsDrop()
    {
        Read().DropType(101).ShouldBe(ItemSchema.DropTypeDrop);
    }

    [Test]
    public void DropType_AnItemDeclaringBreak_IsBreak()
    {
        // Valve's own comment calls "break" unimplemented (econ_item_schema.cpp:74), but the
        // string table still parses it — the schema can declare it even if nothing consumes it.
        Read().DropType(102).ShouldBe(ItemSchema.DropTypeBreak);
    }

    [Test]
    public void DropType_AnItemWithNoDropTypeKey_DefaultsToNone()
    {
        // The behaviour the task exists to pin down: no key means "stays attached", exposed as
        // the named NONE ordinal rather than the engine's raw parse artefact (see the class
        // remarks and ItemSchema.DropTypeNone for why the two are interchangeable).
        Read().DropType(103).ShouldBe(ItemSchema.DropTypeNone);
    }

    [Test]
    public void DropType_AnExplicitEmptyString_IsAlsoNone()
    {
        // The control for the test above: an explicit "" must resolve the same way a missing key
        // does, because Apply() (and the engine's own GetString default) treat the two alike.
        Read().DropType(104).ShouldBe(ItemSchema.DropTypeNone);
    }

    [Test]
    public void DropType_AMixedCaseValue_StillMatches()
    {
        // StringFieldToInt compares with Q_stricmp (econ_item.cpp:37), case-insensitively.
        Read().DropType(105).ShouldBe(ItemSchema.DropTypeDrop);
    }

    [Test]
    public void DropType_AnUnrecognizedValue_FallsBackToNone()
    {
        // StringFieldToInt's loop finds no match and falls through to its own -1 fallback; this
        // wrapper answers with the same named default it uses for a missing key.
        Read().DropType(106).ShouldBe(ItemSchema.DropTypeNone);
    }

    [Test]
    public void DropType_AnItemInheritingFromAPrefab_TakesThePrefabsDropType()
    {
        // drop_type is read off m_pKVItem AFTER MergeDefinitionPrefab has folded the prefab chain
        // into it (econ_item_schema.cpp:3023-3024), so it is inherited exactly like model_player.
        Read().DropType(107).ShouldBe(ItemSchema.DropTypeDrop);
    }

    [Test]
    public void DropType_AnItemNamingItsOwnValue_OverridesItsPrefab()
    {
        // **The control for the inheritance test above.** Item 108 uses a "break" prefab but
        // names "none" itself; nearest-declaration-wins must take the item's own value, or this
        // and the previous test would both pass for a reader that always took the prefab.
        Read().DropType(108).ShouldBe(ItemSchema.DropTypeNone);
    }

    [Test]
    public void DropType_AnItemTheSchemaDoesNotHave_IsNone()
    {
        Read().DropType(99999).ShouldBe(ItemSchema.DropTypeNone);
    }

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));
}
