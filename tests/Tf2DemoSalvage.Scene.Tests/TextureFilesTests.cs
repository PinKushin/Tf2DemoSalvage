namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A load's texture files are one file per name, however the name is spelled (finding 80).</summary>
/// <remarks>
/// **Both archives and the pakfile treat a path as one file whatever its case and slashes** (`PakFile.Normalise`), so the
/// table that shares reads between materials must too: two VMTs spelling `Water/TFWater001_normal` and
/// `water\tfwater001_normal` name one file, and the Fable review of 2026-10-18 found them read twice.
/// </remarks>
public sealed class TextureFilesTests
{
    [Test]
    public void File_TheSameNameInTwoCases_IsReadOnce()
    {
        TextureFiles files = new();
        int reads = 0;

        byte[]? first = files.File("materials/Water/TFWater001_normal.vtf", _ => { reads++; return [1, 2, 3]; });
        byte[]? second = files.File("materials/water/tfwater001_normal.vtf", _ => { reads++; return [9]; });

        reads.ShouldBe(1);
        second.ShouldBeSameAs(first);
    }

    [Test]
    public void File_TheSameNameWithBackslashes_IsReadOnce()
    {
        TextureFiles files = new();
        int reads = 0;

        byte[]? first = files.File("materials/water/tfwater001_normal.vtf", _ => { reads++; return [1]; });
        byte[]? second = files.File("materials\\water\\tfwater001_normal.vtf", _ => { reads++; return [2]; });

        reads.ShouldBe(1);
        second.ShouldBeSameAs(first);
    }

    [Test]
    public void File_TwoDifferentNames_AreTwoReads()
    {
        TextureFiles files = new();
        int reads = 0;

        files.File("materials/water/a.vtf", _ => { reads++; return [1]; });
        files.File("materials/water/b.vtf", _ => { reads++; return [2]; });

        reads.ShouldBe(2);
    }

    [Test]
    public void File_TheFirstAskersSpelling_IsWhatTheReaderGets()
    {
        TextureFiles files = new();
        string? asked = null;

        files.File("materials\\Water\\A.vtf", path => { asked = path; return [1]; });

        asked.ShouldBe("materials\\Water\\A.vtf", "the reader is given the path as asked, the table only keys by the normalised one");
    }
}
