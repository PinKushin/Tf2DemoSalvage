using System.Collections.Generic;
using System.Text;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`TFPlayerClassData_t::m_nMaxHealth` (tf_classdata.cpp) and `CObjectInfo` from `scripts/objects.txt` (tf_shareddefs.cpp:1452).</summary>
public sealed class TfWeaponDataObjectAndClassTests
{
    private static readonly Dictionary<string, byte[]> Files = new()
    {
        ["scripts/playerclasses/scout.txt"] = Encoding.UTF8.GetBytes("\"PlayerClass\"\n{\n\t\"health\"\t\"125\"\n}\n"),
        ["scripts/objects.txt"] = Encoding.UTF8.GetBytes("""
            "Objects"
            {
                "OBJ_DISPENSER" { "StatusName" "#TF_Object_Dispenser" }
                "OBJ_TELEPORTER"
                {
                    "StatusName" "#TF_Object_Tele"
                    "AltModes"
                    {
                        "AltMode0" { "StatusName" "#TF_Object_Tele_Entrance" "ModeName" "#TF_Teleporter_Mode_Entrance" }
                        "AltMode1" { "StatusName" "#TF_Object_Tele_Exit" "ModeName" "#TF_Teleporter_Mode_Exit" }
                    }
                }
                "OBJ_SENTRYGUN" { "StatusName" "#TF_Object_Sentry" }
            }
            """),
    };

    [Test]
    public void ClassMaxHealth_AScriptSayingHealth125_Is125()
    {
        TfWeaponData data = new(Files.GetValueOrDefault);

        (data.ClassMaxHealth(1), data.ClassMaxHealth(2)).ShouldBe(((int?)125, (int?)null));
    }

    [Test]
    public void ObjectInfo_TeleporterExit_ReadsAltModeOnesNamesAndCountsOneAltMode()
    {
        TfWeaponData data = new(Files.GetValueOrDefault);

        // `m_iNumAltModes = iIndex - 1` (:1533): two AltMode blocks count as one.
        data.ObjectInfo(1, 1).ShouldBe(("#TF_Object_Tele_Exit", "#TF_Teleporter_Mode_Exit", 1));
    }

    [Test]
    public void ObjectInfo_TeleporterEntrance_TakesTheDefaultStatusNameButItsOwnModeName()
    {
        TfWeaponData data = new(Files.GetValueOrDefault);

        // "Alternate mode 0 always matches the defaults" (:1536) — the status name only; the mode name is AltMode0's.
        data.ObjectInfo(1, 0).ShouldBe(("#TF_Object_Tele", "#TF_Teleporter_Mode_Entrance", 1));
    }

    [Test]
    public void ObjectInfo_Dispenser_HasNoAltModes()
    {
        TfWeaponData data = new(Files.GetValueOrDefault);

        data.ObjectInfo(0, 0).ShouldBe(("#TF_Object_Dispenser", (string?)null, 0));
    }
}
