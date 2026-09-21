using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One `CTEEffectDispatch`: a named client effect and its `CEffectData` (B415).</summary>
/// <param name="Tick">The tick its packet arrived on.</param>
/// <param name="Name">`m_iEffectName`, into the <c>EffectDispatch</c> string table.</param>
/// <param name="Origin">`m_vOrigin`.</param>
/// <param name="Start">`m_vStart`.</param>
/// <param name="Normal">`m_vNormal`.</param>
/// <param name="Angles">`m_vAngles`, degrees.</param>
/// <param name="Flags">`m_fFlags`.</param>
/// <param name="Scale">`m_flScale`, 1 unless sent.</param>
/// <param name="Attachment">`m_nAttachmentIndex`.</param>
/// <param name="SurfaceProp">`m_nSurfaceProp`, a `short`: the sent value less one (`RecvProxy_ShortSubOne`); 0 unless sent.</param>
/// <param name="Material">`m_nMaterial`.</param>
/// <param name="DamageType">`m_nDamageType`.</param>
/// <param name="HitBox">`m_nHitBox`.</param>
/// <param name="Entity">`entindex` through `RecvProxy_EntIndex`; −1 for none, which is also the default.</param>
/// <param name="Colour">`m_nColor`.</param>
public readonly record struct SceneEffectDispatch(
    int Tick,
    int Name,
    (float X, float Y, float Z) Origin,
    (float X, float Y, float Z) Start,
    (float X, float Y, float Z) Normal,
    (float X, float Y, float Z) Angles,
    int Flags,
    float Scale,
    int Attachment,
    int SurfaceProp,
    int Material,
    int DamageType,
    int HitBox,
    int Entity,
    int Colour);

/// <summary>Every `CTEEffectDispatch` a demo carries, in fire order, and the table naming them (B415).</summary>
/// <remarks>
/// `DT_TEEffectDispatch` is one data table, `DT_EffectData` (`effect_dispatch_data.cpp:36`). **An unsent field is
/// `CEffectData`'s constructor default**, which is not zero for two of them: `m_flScale` is 1 and `m_hEntity` is
/// `INVALID_EHANDLE`.
/// </remarks>
public sealed class EffectDispatchFeed
{
    /// <summary>The server class this reads.</summary>
    public const string EventClassName = "CTEEffectDispatch";

    /// <summary>The string table <c>m_iEffectName</c> points into.</summary>
    public const string TableName = "EffectDispatch";

    private readonly List<SceneEffectDispatch> _effects = [];

    /// <summary>Every dispatch, in tick order.</summary>
    public IReadOnlyList<SceneEffectDispatch> All => _effects;

    /// <summary>The <c>EffectDispatch</c> table: an effect's name by its index.</summary>
    public NameTable Names { get; } = new();

    /// <summary>Takes one decoded temp entity if it is an effect dispatch.</summary>
    /// <param name="className">The class the effect's id resolved to.</param>
    /// <param name="effect">The decoded effect.</param>
    /// <param name="tick">The tick its packet arrived on.</param>
    /// <returns><c>true</c> when it was a dispatch and was recorded.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public bool Record(string className, DecodedTempEntity effect, int tick)
    {
        ArgumentNullException.ThrowIfNull(className);
        ArgumentNullException.ThrowIfNull(effect);

        if (!string.Equals(className, EventClassName, StringComparison.Ordinal))
        {
            return false;
        }

        Fields fields = new();

        foreach (DecodedProperty property in effect.Properties)
        {
            fields.Read(property.Definition.Property.Name, property.Value);
        }

        _effects.Add(fields.At(tick));

        return true;
    }

    /// <summary>The fields as they arrive, starting from `CEffectData`'s constructor.</summary>
    private struct Fields()
    {
        private (float X, float Y, float Z) _origin;
        private (float X, float Y, float Z) _start;
        private (float X, float Y, float Z) _normal;
        private (float X, float Y, float Z) _angles;
        private int _name;
        private int _flags;
        private float _scale = 1f;
        private int _attachment;
        private int _surfaceProp;
        private int _material;
        private int _damageType;
        private int _hitBox;
        private int _entity = -1;
        private int _colour;

        public void Read(string name, PropertyValue value)
        {
            switch (name)
            {
                case "m_vOrigin[0]": _origin.X = value.AsFloat; break;
                case "m_vOrigin[1]": _origin.Y = value.AsFloat; break;
                case "m_vOrigin[2]": _origin.Z = value.AsFloat; break;
                case "m_vStart[0]": _start.X = value.AsFloat; break;
                case "m_vStart[1]": _start.Y = value.AsFloat; break;
                case "m_vStart[2]": _start.Z = value.AsFloat; break;
                case "m_vNormal": _normal = value.AsVector; break;
                case "m_vAngles": _angles = value.AsVector; break;
                case "m_iEffectName": _name = (int)value.AsInt; break;
                case "m_fFlags": _flags = (int)value.AsInt; break;
                case "m_flScale": _scale = value.AsFloat; break;
                case "m_nAttachmentIndex": _attachment = (int)value.AsInt; break;
                case "m_nSurfaceProp": _surfaceProp = unchecked((short)((int)value.AsInt - 1)); break;
                case "m_nMaterial": _material = (int)value.AsInt; break;
                case "m_nDamageType": _damageType = (int)value.AsInt; break;
                case "m_nHitBox": _hitBox = (int)value.AsInt; break;
                case "entindex": _entity = Math.Max(-1, (int)value.AsInt); break;
                case "m_nColor": _colour = (int)value.AsInt; break;
                default: break;
            }
        }

        public readonly SceneEffectDispatch At(int tick) =>
            new(tick, _name, _origin, _start, _normal, _angles, _flags, _scale, _attachment, _surfaceProp, _material,
                _damageType, _hitBox, _entity, _colour);
    }
}
