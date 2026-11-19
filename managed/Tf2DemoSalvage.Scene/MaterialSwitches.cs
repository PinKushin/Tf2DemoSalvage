using System;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The two material switches whose change reloads every model in TF2 (B720): `mat_bumpmap` and `mat_phong`.
/// `CMaterialSystem::OverrideConfig` (`materialsystem.dll` `FUN_180018fd0`) reloads the materials on either, with
/// `MATERIAL_RESTORE_VERTEX_FORMAT_CHANGED`, on which the datacache (`FUN_18000fbe0`) loads every model's hardware data again.
/// </summary>
/// <param name="BumpMapping">`mat_bumpmap`.</param>
/// <param name="Phong">`mat_phong`.</param>
public readonly record struct MaterialSwitches(bool BumpMapping, bool Phong)
{
    /// <summary>TF2's defaults, both on.</summary>
    public static MaterialSwitches Default { get; } = new(true, true);

    /// <summary>This state's bit in a four-state mask.</summary>
    public int Bit => 1 << ((BumpMapping ? 1 : 0) | (Phong ? 2 : 0));

    /// <summary>A question's answer under each of the four states, as a mask of <see cref="Bit"/>.</summary>
    /// <param name="marks">The question, by `mat_bumpmap` and `mat_phong`.</param>
    /// <returns>The mask.</returns>
    public static int MarksOf(Func<bool, bool, bool> marks)
    {
        ArgumentNullException.ThrowIfNull(marks);

        int mask = 0;

        foreach (bool bump in (ReadOnlySpan<bool>)[false, true])
        {
            foreach (bool phong in (ReadOnlySpan<bool>)[false, true])
            {
                if (marks(bump, phong))
                {
                    mask |= new MaterialSwitches(bump, phong).Bit;
                }
            }
        }

        return mask;
    }
}
