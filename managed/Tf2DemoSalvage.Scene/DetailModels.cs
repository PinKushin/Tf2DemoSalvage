using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>One detail prop that is a studio MODEL, placed for one view (B363).</summary>
/// <param name="Model">Index into the map's detail model dictionary.</param>
/// <param name="Origin">Where it stands, in world units.</param>
/// <param name="Angles">Its orientation, after <c>ComputeAngles</c> has had its say.</param>
/// <param name="Alpha">
/// <c>m_Alpha</c> — the distance fade, 0 to 255. A model at 0 is not drawn at all.
/// </param>
/// <param name="Lighting">
/// The lump's own <c>m_Lighting</c> for this object — <c>CDetailModel::m_Color</c>. A detail model
/// is required to be UNLIT (`UnserializeModelDict` substitutes `models/error.mdl` for a vertex-lit
/// one), so this colour is all the light it has.
/// </param>
public readonly record struct DetailModelInstance(
    int Model,
    (float X, float Y, float Z) Origin,
    (float Pitch, float Yaw, float Roll) Angles,
    byte Alpha,
    (float Red, float Green, float Blue) Lighting);

/// <summary>
/// The detail props a map scatters that are MODELS rather than sprites (B363).
/// </summary>
/// <remarks>
/// **A `DETAIL_PROP_TYPE_MODEL` is an ordinary studio model the map scatters like grass**, and it
/// is drawn through a completely different path from the sprites — which is why it needs a type of
/// its own here rather than a branch inside <see cref="DetailSprites"/>.
///
/// <code>
///   int CDetailModel::DrawModel( int flags )
///   {
///       if ((m_Alpha == 0) || (!m_pModel))
///           return 0;
///
///       return modelrender->DrawModel( flags, this,
///           MODEL_INSTANCE_INVALID, -1, m_pModel, m_Origin, m_Angles, 0, 0, 0 );
///   }
/// </code>
///
/// `detailobjectsystem.cpp:694`. **Skin 0, body 0, hitboxset 0 and entity index −1**, so none of the
/// econ, bodygroup or skin resolution any other model needs applies to one of these.
///
/// **Its alpha is the same fade the sprites get, computed by the same code.**
/// `CDetailObjectSystem::EnumerateLeaf` (`detailobjectsystem.cpp:2742`) walks every
/// `CDetailModel` in a leaf without asking its type:
///
/// <code>
///   model.SetAlpha( 255 );
///   if ( sqDist &lt; m_flCurMaxSqDist )
///   {
///       if ( sqDist &gt; m_flCurFadeSqDist )
///           model.SetAlpha( m_flCurFalloffFactor * ( m_flCurMaxSqDist - sqDist ) );
///       else
///           model.SetAlpha( 255 );
///
///       model.ComputeAngles();
///   }
///   else
///   {
///       model.SetAlpha( 0 );
///   }
/// </code>
///
/// so <see cref="DetailFade"/> serves both, and **`ComputeAngles` is called for models too** — a
/// screen-aligned detail model turns to face the eye.
///
/// **An alpha of zero is not drawn, and that is enforced twice.** `DrawModel` returns 0 for it, and
/// before that `CClientLeafSystem::CollateRenderablesInLeaf` only adds a transparent renderable
/// `if( pRenderable->GetFxBlend() > 0 )` (`clientleafsystem.cpp:1734`). So this builder emits
/// nothing for a faded-out model rather than emitting a transparent one.
///
/// **`cl_detail_multiplier` does NOT apply to models.** The sprite branches of `UnserializeModels`
/// loop `SPRITE_MULTIPLIER` times and jitter each copy by `RandomVector( -50, 50 )`; the
/// `DETAIL_PROP_TYPE_MODEL` branch (`detailobjectsystem.cpp:1829`) creates exactly one object and
/// never reads the macro. A viewer that multiplied both would put four times as many models on a
/// map as the engine does, at a setting that is `FCVAR_CHEAT` and defaults to 1 — invisible until
/// somebody set it.
///
/// **Opaque or translucent is decided per instance, not per material.**
/// `CDetailModel::IsTransparent` is `(m_Alpha &lt; 255) || modelinfo->IsTranslucent(m_pModel)`
/// (`detailobjectsystem.cpp:583`), and `CollateRenderablesInLeaf` sorts on that — so a detail model
/// joins the translucent pass only while it is mid-fade, and is an opaque renderable the rest of
/// the time.
/// </remarks>
public static class DetailModels
{
    /// <summary>What one view's build produced.</summary>
    /// <param name="Built">Models placed for this view.</param>
    /// <param name="Faded">Models the distance fade dropped entirely.</param>
    /// <param name="Aligned">Of those built, how many were turned to face the eye.</param>
    /// <param name="Translucent">Of those built, how many are mid-fade and so not opaque.</param>
    public readonly record struct Frame(int Built, int Faded, int Aligned, int Translucent);

    /// <summary>Places every detail model this view can see.</summary>
    /// <param name="objects">The detail props, from <see cref="BspDetailProps"/>.</param>
    /// <param name="eye">Where the view is, in world units.</param>
    /// <param name="fade">The distance fade for this view.</param>
    /// <param name="into">Where the placed models are appended.</param>
    /// <returns>What was built, for the report.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **The distance is measured to the ORIGIN and is "calculated badly" in Valve's own words**
    /// (`detailobjectsystem.cpp:2756`) — the same squared distance the sprites use, with no regard
    /// for the model's size, so a large detail model pops rather than fading from its far edge.
    /// Transcribed as written.
    ///
    /// **Sprites are skipped by TYPE rather than by the dictionary they index.** A sprite's
    /// <see cref="BspDetailProp.DetailModel"/> indexes the sprite dictionary and a model's indexes
    /// the model dictionary, so reading one as the other silently places grass at a rectangle's
    /// index — which on `cp_granary` would be 19,189 models where the map has 324.
    /// </remarks>
    public static Frame Build(
        IReadOnlyList<BspDetailProp> objects,
        (float X, float Y, float Z) eye,
        DetailFade fade,
        IList<DetailModelInstance> into)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(into);

        int built = 0;
        int faded = 0;
        int aligned = 0;
        int translucent = 0;

        for (int index = 0; index < objects.Count; index++)
        {
            BspDetailProp prop = objects[index];

            if (prop.Type != DetailPropType.Model)
            {
                continue;
            }

            float dx = prop.Origin.X - eye.X;
            float dy = prop.Origin.Y - eye.Y;
            float dz = prop.Origin.Z - eye.Z;

            byte alpha = fade.Alpha((dx * dx) + (dy * dy) + (dz * dz));

            if (alpha == 0)
            {
                faded++;
                continue;
            }

            (float Pitch, float Yaw, float Roll) angles = DetailSprites.Facing(prop, eye);

            if (prop.Orientation is 1 or 2)
            {
                aligned++;
            }

            if (alpha < 255)
            {
                translucent++;
            }

            into.Add(new DetailModelInstance(
                prop.DetailModel, prop.Origin, angles, alpha, prop.Lighting));
            built++;
        }

        return new Frame(built, faded, aligned, translucent);
    }
}
