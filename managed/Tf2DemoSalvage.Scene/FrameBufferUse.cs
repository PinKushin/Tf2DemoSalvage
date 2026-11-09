using System;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Whether a model material needs the frame-buffer copy apart from its cloak pass: the shader's
/// <c>NeedsPowerOfTwoFrameBufferTexture</c> with the cloak branch left out (B711).
/// </summary>
/// <remarks>
/// <c>C_BaseAnimating::UsesPowerOfTwoFrameBufferTexture</c> asks it over the model's materials, and
/// <c>DrawTranslucentRenderables</c> skips a renderable that answers true in a water target (`viewrender.cpp:4612-4617`).
/// VertexLitGeneric answers true for <c>$sheenpassenabled</c> with no per-frame test (`vertexlitgeneric_dx9.cpp:286-301`).
/// Every override otherwise answers <c>MATERIAL_VAR2_NEEDS_POWER_OF_TWO_FRAME_BUFFER_TEXTURE</c>, which only these shaders
/// set, in their init: Refract, Cloak, Predator, Aftershock and Portal_Refract always; Core and Portal when they declare no
/// <c>$basetexture</c>. *Interpolated:* a shader whose override returns true without the flag was not searched, and
/// <c>$flags2</c> in a VMT is not read. The cloak pass is per frame and stays the instance's (<c>CloakBind.Cloaking</c>).
/// </remarks>
public static class FrameBufferUse
{
    /// <summary>Whether the material needs the frame-buffer copy whatever this frame's cloak.</summary>
    /// <param name="material">The material.</param>
    /// <returns>True for a sheen pass or a shader that sets the flag.</returns>
    public static bool Needs(VmtMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);

        // `params[SHEENPASSENABLED]->GetIntValue()`: the integer part.
        if (material.Value("$sheenpassenabled") is { } sheen &&
            int.TryParse(sheen.Trim().Split('.')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int on) && on != 0)
        {
            return true;
        }

        string shader = material.Shader;

        if (Is(shader, "Refract") || Is(shader, "Cloak") || Is(shader, "Predator") || Is(shader, "Aftershock") ||
            Is(shader, "Portal_Refract"))
        {
            return true;
        }

        return (Is(shader, "Core") || Is(shader, "Portal")) && material.Value("$basetexture") is null;
    }

    private static bool Is(string shader, string name) => string.Equals(shader, name, StringComparison.OrdinalIgnoreCase);
}
