using System.Windows.Forms;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>The panel the scene is presented into, which — unlike a plain one — can hold focus.</summary>
/// <remarks>
/// **A `Panel` cannot take focus, and that single fact caused two defects** (B212, B216). `TabStop`
/// was already set to `true` on the plain panel and did nothing: WinForms decides focusability with
/// `ControlStyles.Selectable`, which `Panel` clears, and no amount of `TabStop` overrides it. The
/// file even said so beside the wheel handler — *"A Panel does not take focus, so its own wheel event
/// may never fire"* — as a workaround rather than as a problem to fix.
///
/// **What follows from it is worse than a missing wheel event: focus never describes what the user
/// is doing.** Clicking the 3D view leaves focus wherever it was, which is the playlist, so the
/// window's idea of "the focused control" is the list even while somebody is flying the camera
/// across a map.
///
/// - **B212** is the direct consequence: `ProcessCmdKey` reached over whatever held focus, because
///   nothing else could hold it.
/// - **B216** is where it became load-bearing. The shortcut guard asks what the focused widget uses,
///   which is only meaningful if focus tracks intent. Adding list type-ahead against a permanently
///   focused playlist swallowed `SPACE` and every letter **globally** — the camera stopped switching
///   and `w`/`a`/`s`/`d` stopped flying, and four UI tests said so at once.
///
/// So this exists to make focus mean something. `Selectable` is the whole of it; the panel is
/// otherwise a plain one, and it is still never painted by WinForms.
///
/// **It shows no focus rectangle**, deliberately: the scene fills it edge to edge and a dotted
/// outline over a 3D view would read as a rendering fault rather than as a focus cue.
/// </remarks>
internal sealed class ViewportPanel : Panel
{
    public ViewportPanel() => SetStyle(ControlStyles.Selectable, true);

    /// <inheritdoc />
    protected override bool ShowFocusCues => false;
}
