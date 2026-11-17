using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// What `CWeaponSkinProxy::OnBind` (c_tf_player.cpp:3396-3707) asks the compositor for, per item (B754): the recipe, the
/// team and seed it is composited at, and whether at the base texture's full size.
/// </summary>
/// <param name="Name">`"%d_%d_wear_%02d"` of the item's own index, its kit and its wear category (:3657).</param>
/// <param name="Recipe">`GetItemPaintKitDefinitionKV( GetRemappedItemDefIndex(), nWear )` (:3629).</param>
/// <param name="Team">`pItem->GetTeamNumber()`, red for `TEAM_UNASSIGNED` (:3654).</param>
/// <param name="Seed">`GetOriginalID()`, or the item's custom seed (:3636-3651).</param>
/// <param name="FullSize">
/// Whether the composite is the base texture's size: `ShouldWeaponSkinUseHighRes()` or the local player's (:3500-3505);
/// otherwise two mips smaller.
/// </param>
public sealed record WeaponSkin(string Name, PaintKitNode Recipe, int Team, ulong Seed, bool FullSize)
{
    /// <summary>The composite's size for a base texture of the given actual size (:3488-3508).</summary>
    /// <param name="width">`m_pBaseTextureOrig->GetActualWidth()`.</param>
    /// <param name="height">`GetActualHeight()`.</param>
    /// <returns>The size, dropped two mips unless <see cref="FullSize"/>, never below one.</returns>
    public (int Width, int Height) SizeFor(int width, int height) =>
        FullSize ? (width, height) : (System.Math.Max(1, width >> 2), System.Math.Max(1, height >> 2));
}
