using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>Every `CTETFParticleEffect`, as the `"ParticleEffect"` dispatch the client turns it into (B415).</summary>
/// <remarks>
/// <code>
/// // tf_fx_particleeffect.cpp:79 — C_TETFParticleEffect::PostDataUpdate
/// data.m_nHitBox = m_iParticleSystemIndex;  data.m_vOrigin, m_vStart, m_vAngles as sent
/// if ( m_hEntity != INVALID_EHANDLE ) { data.m_hEntity = m_hEntity; data.m_fFlags |= PARTICLE_DISPATCH_FROM_ENTITY }
/// data.m_nDamageType = m_iAttachType;  data.m_nAttachmentIndex = m_iAttachmentPointIndex
/// if ( m_bResetParticles ) data.m_fFlags |= PARTICLE_DISPATCH_RESET_PARTICLES
/// colours and control point 1 as sent;  DispatchEffect( "ParticleEffect", data )
/// </code>
/// `entindex` goes through `RecvProxy_ParticleSystemEntIndex`: 2047, and −1 from old demos, are no entity; anything
/// else — zero included, which is the world — is one. An unsent field is zero through its proxy.
/// </remarks>
public sealed class TfParticleEffectFeed
{
    /// <summary>The server class this reads.</summary>
    public const string EventClassName = "CTETFParticleEffect";

    /// <summary>`PARTICLE_DISPATCH_FROM_ENTITY`.</summary>
    public const int FromEntity = 1 << 0;

    /// <summary>`PARTICLE_DISPATCH_RESET_PARTICLES`.</summary>
    public const int ResetParticles = 1 << 1;

    /// <summary>`kInvalidEHandleParticleEffect`.</summary>
    private const int NoEntity = 2047;

    private readonly List<SceneEffectDispatch> _effects = [];

    /// <summary>Every effect, in fire order, as its dispatch.</summary>
    public IReadOnlyList<SceneEffectDispatch> All => _effects;

    /// <summary>Takes one decoded temp entity if it is a TF particle effect.</summary>
    /// <param name="className">The class the effect's id resolved to.</param>
    /// <param name="effect">The decoded effect.</param>
    /// <param name="tick">The tick its packet arrived on.</param>
    /// <returns><c>true</c> when it was one and was recorded.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public bool Record(string className, DecodedTempEntity effect, int tick)
    {
        ArgumentNullException.ThrowIfNull(className);
        ArgumentNullException.ThrowIfNull(effect);

        if (!string.Equals(className, EventClassName, StringComparison.Ordinal))
        {
            return false;
        }

        (float X, float Y, float Z) origin = default, start = default, angles = default;
        (float X, float Y, float Z) colourOne = default, colourTwo = default, controlPoint1 = default;
        int system = 0, entity = 0, attachType = 0, attachment = 0;
        bool reset = false, colours = false, hasControlPoint1 = false;

        foreach (DecodedProperty property in effect.State)
        {
            PropertyValue value = property.Value;

            switch (property.Definition.Property.Name)
            {
                case "m_vecOrigin[0]": origin.X = value.AsFloat; break;
                case "m_vecOrigin[1]": origin.Y = value.AsFloat; break;
                case "m_vecOrigin[2]": origin.Z = value.AsFloat; break;
                case "m_vecStart[0]": start.X = value.AsFloat; break;
                case "m_vecStart[1]": start.Y = value.AsFloat; break;
                case "m_vecStart[2]": start.Z = value.AsFloat; break;
                case "m_vecAngles": angles = value.AsVector; break;
                case "m_iParticleSystemIndex": system = (int)value.AsInt; break;
                case "entindex": entity = (int)value.AsInt; break;
                case "m_iAttachType": attachType = (int)value.AsInt; break;
                case "m_iAttachmentPointIndex": attachment = (int)value.AsInt; break;
                case "m_bResetParticles": reset = value.AsInt != 0; break;
                case "m_bCustomColors": colours = value.AsInt != 0; break;
                case "m_CustomColors.m_vecColor1": colourOne = value.AsVector; break;
                case "m_CustomColors.m_vecColor2": colourTwo = value.AsVector; break;
                case "m_bControlPoint1": hasControlPoint1 = value.AsInt != 0; break;
                case "m_ControlPoint1.m_vecOffset[0]": controlPoint1.X = value.AsFloat; break;
                case "m_ControlPoint1.m_vecOffset[1]": controlPoint1.Y = value.AsFloat; break;
                case "m_ControlPoint1.m_vecOffset[2]": controlPoint1.Z = value.AsFloat; break;
                default: break;
            }
        }

        bool fromEntity = entity is not NoEntity and not -1;
        int flags = (fromEntity ? FromEntity : 0) | (reset ? ResetParticles : 0);

        _effects.Add(new SceneEffectDispatch(
            tick, -1, origin, start, default, angles, flags, 0f, attachment, -1, 0, attachType, system,
            fromEntity ? entity : -1, 0, colours, colourOne, colourTwo, hasControlPoint1, controlPoint1));

        return true;
    }
}
