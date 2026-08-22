namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Which faces of a model are thrown away before rasterising.
/// </summary>
/// <remarks>
/// **Named rather than passed as a raw Direct3D value, so the choice can be tested.** The decision
/// is three-way and the inputs are two booleans, which is the shape that usually loses a case —
/// and the case it loses here draws a weapon inside out rather than failing.
/// </remarks>
public enum ModelCull
{
    /// <summary>Back faces go, which is the engine's default and every ordinary model's.</summary>
    /// <remarks>
    /// <c>MATERIAL_CULLMODE_CCW</c>, front faces wound clockwise
    /// (<c>imaterialsystem.h:180</c>).
    /// </remarks>
    Back,

    /// <summary>Front faces go, for a model drawn mirrored.</summary>
    /// <remarks>
    /// <c>MATERIAL_CULLMODE_CW</c>, which <c>C_BaseViewModel::InternalDrawModel</c> sets around a
    /// flipped viewmodel and puts back immediately afterwards.
    /// </remarks>
    Front,

    /// <summary>Nothing goes, for a material that asked to be seen from both sides.</summary>
    /// <remarks>
    /// <c>$nocull</c> sets <c>MATERIAL_VAR_NOCULL</c> and shaders test it per material
    /// (<c>imaterial.h:369</c>). It outranks the mirror flip: a two-sided material is two-sided
    /// whichever way the model carrying it is wound.
    /// </remarks>
    None,
}
