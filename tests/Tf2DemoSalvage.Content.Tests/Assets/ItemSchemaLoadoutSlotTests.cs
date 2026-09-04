using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>GetDefaultLoadoutSlot()</c> — which loadout slot an item occupies by default.
/// </summary>
/// <remarks>
/// <c>m_iDefaultLoadoutSlot = StringFieldToInt( pszLoadoutSlot, GetLoadoutStrings(m_eEquipType),
/// true )</c> (<c>tf_item_schema.cpp:951</c>), against <c>g_szLoadoutStrings</c>
/// (<c>tf_item_schema.cpp:1513-1533</c>). See <see cref="ItemSchema.DefaultLoadoutSlot"/>'s remarks
/// for the "head" → "misc" rewrite this class exists to pin down.
/// </remarks>
public sealed class ItemSchemaLoadoutSlotTests
{
    /// <summary>A schema small enough to reason about, shaped like the real file's item_slot use.</summary>
    private const string Schema = """
        "items_game"
        {
            "prefabs"
            {
                "base_primary"
                {
                    "item_slot" "primary"
                }
            }
            "items"
            {
                "200"
                {
                    "name" "A_PRIMARY_WEAPON"
                    "item_slot" "primary"
                }
                "201"
                {
                    "name" "A_MISC_COSMETIC"
                    "item_slot" "misc"
                }
                "202"
                {
                    "name" "A_LOWERCASE_HEAD_COSMETIC"
                    "item_slot" "head"
                }
                "203"
                {
                    "name" "A_CAPITALIZED_HEAD_COSMETIC"
                    "item_slot" "Head"
                }
                "204"
                {
                    "name" "NO_ITEM_SLOT_KEY"
                }
                "205"
                {
                    "name" "EXPLICIT_BLANK_SLOT"
                    "item_slot" ""
                }
                "206"
                {
                    "name" "AN_ACCOUNT_ONLY_SLOT_NAME"
                    "item_slot" "quest"
                }
                "207"
                {
                    "name" "INHERITS_FROM_PREFAB"
                    "prefab" "base_primary"
                }
                "208"
                {
                    "name" "UPPERCASE_MISC"
                    "item_slot" "MISC"
                }
            }
        }
        """;

    [Test]
    public void DefaultLoadoutSlot_AnItemInThePrimarySlot_IsPrimary()
    {
        Read().DefaultLoadoutSlot(200).ShouldBe(ItemSchema.LoadoutSlotPrimary);
    }

    [Test]
    public void DefaultLoadoutSlot_AnItemInTheMiscSlot_IsMisc()
    {
        Read().DefaultLoadoutSlot(201).ShouldBe(ItemSchema.LoadoutSlotMisc);
    }

    [Test]
    public void DefaultLoadoutSlot_ALowercaseHeadDeclaration_ResolvesToMiscNotHead()
    {
        // **The Valve quirk this file exists to pin down.** tf_item_schema.cpp:941-944 rewrites
        // the exact string "head" to "misc" BEFORE the table lookup ever runs:
        //
        //     if ( !V_strcmp( pszLoadoutSlot, "head" ) ) pszLoadoutSlot = "misc";
        //
        // So a schema declaring `item_slot "head"` can never come back as LOADOUT_POSITION_HEAD
        // (7) — it always resolves to LOADOUT_POSITION_MISC (8) instead. The real armory UI
        // agrees: charinfo_armory_subpanel.cpp:605 tests only `== LOADOUT_POSITION_MISC`.
        Read().DefaultLoadoutSlot(202).ShouldBe(ItemSchema.LoadoutSlotMisc);
    }

    [Test]
    public void DefaultLoadoutSlot_ACapitalizedHeadDeclaration_ResolvesToHead()
    {
        // **The control for the test above, and it is what proves the rewrite is exact-case.**
        // V_strcmp is plain, case-SENSITIVE strcmp (strtools.h:160) — unlike the case-insensitive
        // Q_stricmp the table lookup itself uses. So "Head" is not caught by the rewrite and
        // resolves to LOADOUT_POSITION_HEAD via the ordinary case-insensitive table match.
        //
        // Without this control, an implementation that always mapped "head" (any case) to MISC
        // would still pass the test above; without the test above, an implementation that never
        // rewrote anything would still pass this one. Only the pair proves the exact-case rule.
        Read().DefaultLoadoutSlot(203).ShouldBe(ItemSchema.LoadoutSlotHead);
    }

    [Test]
    public void DefaultLoadoutSlot_AnItemWithNoItemSlotKey_IsInvalid()
    {
        // tf_item_schema.cpp:892 constructs m_iDefaultLoadoutSlot as LOADOUT_POSITION_INVALID, and
        // the parse is skipped entirely for a blank/missing item_slot (`if ( *pszLoadoutSlot )`,
        // tf_item_schema.cpp:939) rather than run and fall back like drop_type's does — so this
        // default is unambiguous, unlike DropType's.
        Read().DefaultLoadoutSlot(204).ShouldBe(ItemSchema.LoadoutSlotInvalid);
    }

    [Test]
    public void DefaultLoadoutSlot_AnExplicitEmptySlot_IsAlsoInvalid()
    {
        // The control for the test above: Apply() treats an explicit "" the same as a missing
        // key, matching `if ( *pszLoadoutSlot )` being false for an empty C string too.
        Read().DefaultLoadoutSlot(205).ShouldBe(ItemSchema.LoadoutSlotInvalid);
    }

    [Test]
    public void DefaultLoadoutSlot_AnAccountOnlySlotName_IsInvalidUnderTheClassTable()
    {
        // "quest" is only in g_szAccountLoadoutStrings, never in the class table this method
        // uses (see its remarks for why: "class" is items_game.txt's default equip_type and the
        // account table has no head or misc position at all). Against the class table it is
        // simply unrecognized.
        Read().DefaultLoadoutSlot(206).ShouldBe(ItemSchema.LoadoutSlotInvalid);
    }

    [Test]
    public void DefaultLoadoutSlot_AnItemInheritingFromAPrefab_TakesThePrefabsSlot()
    {
        // item_slot is read off the same prefab-merged KeyValues tree drop_type is, so it
        // inherits through the chain the same way.
        Read().DefaultLoadoutSlot(207).ShouldBe(ItemSchema.LoadoutSlotPrimary);
    }

    [Test]
    public void DefaultLoadoutSlot_AnUppercaseMiscDeclaration_StillMatches()
    {
        // The table lookup itself (unlike the head-to-misc rewrite) is case-insensitive.
        Read().DefaultLoadoutSlot(208).ShouldBe(ItemSchema.LoadoutSlotMisc);
    }

    [Test]
    public void DefaultLoadoutSlot_AnItemTheSchemaDoesNotHave_IsInvalid()
    {
        Read().DefaultLoadoutSlot(99999).ShouldBe(ItemSchema.LoadoutSlotInvalid);
    }

    private static ItemSchema Read() => ItemSchema.Read(Encoding.UTF8.GetBytes(Schema));
}
