using System;
using System.Collections.Generic;

namespace HelixEngine.Rendering
{
    /// <summary>A value of a volume component that a volume overrides (overrideState) and the stack blends.</summary>
    [Serializable]
    public abstract class VolumeParameter : ICloneable
    {
        [SerializeField] protected internal bool m_OverrideState;

        public virtual bool overrideState
        {
            get => m_OverrideState;
            set => m_OverrideState = value;
        }

        internal abstract void Interp(VolumeParameter from, VolumeParameter to, float t);

        public T GetValue<T>() => ((VolumeParameter<T>)this).value;

        public abstract void SetValue(VolumeParameter parameter);

        protected internal virtual void OnEnable() { }
        protected internal virtual void OnDisable() { }

        public static bool IsObjectParameter(Type type)
        {
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ObjectParameter<>))
                    return true;
            return false;
        }

        public virtual void Release() { }

        public abstract object Clone();
    }

    /// <summary>A parameter of a type: blends by taking the overriding value as soon as its weight is above zero (the typed parameters lerp).</summary>
    [Serializable]
    public class VolumeParameter<T> : VolumeParameter, IEquatable<VolumeParameter<T>>
    {
        [SerializeField] protected T m_Value;

        public virtual T value
        {
            get => m_Value;
            set => m_Value = value;
        }

        public VolumeParameter() : this(default, false) { }

        protected VolumeParameter(T value, bool overrideState = false)
        {
            m_Value = value;
            m_OverrideState = overrideState;
        }

        internal override void Interp(VolumeParameter from, VolumeParameter to, float t) => Interp(from.GetValue<T>(), to.GetValue<T>(), t);

        public virtual void Interp(T from, T to, float t) => m_Value = t > 0f ? to : from;

        public void Override(T x)
        {
            overrideState = true;
            m_Value = x;
        }

        public override void SetValue(VolumeParameter parameter) => m_Value = parameter.GetValue<T>();

        public override string ToString() => $"{value} ({overrideState})";

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + overrideState.GetHashCode();
                if (!EqualityComparer<T>.Default.Equals(value, default))
                    hash = hash * 23 + value.GetHashCode();
                return hash;
            }
        }

        public bool Equals(VolumeParameter<T> other) =>
            other is not null && (ReferenceEquals(this, other) || EqualityComparer<T>.Default.Equals(m_Value, other.m_Value));

        public override bool Equals(object obj) => obj is VolumeParameter<T> other && Equals(other);

        public static bool operator ==(VolumeParameter<T> lhs, T rhs) => lhs != null && lhs.value != null && lhs.value.Equals(rhs);
        public static bool operator !=(VolumeParameter<T> lhs, T rhs) => !(lhs == rhs);

        public static explicit operator T(VolumeParameter<T> prop) => prop.m_Value;

        public override object Clone() => MemberwiseClone();
    }

    // ----------------------------------------------------------------------------------------------- the typed ones

    [Serializable]
    public class BoolParameter : VolumeParameter<bool>
    {
        public BoolParameter(bool value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public class LayerMaskParameter : VolumeParameter<LayerMask>
    {
        public LayerMaskParameter(LayerMask value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public class IntParameter : VolumeParameter<int>
    {
        public IntParameter(int value, bool overrideState = false) : base(value, overrideState) { }
        public sealed override void Interp(int from, int to, float t) => m_Value = (int)(from + (to - from) * t);
    }

    [Serializable]
    public class NoInterpIntParameter : VolumeParameter<int>
    {
        public NoInterpIntParameter(int value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public class MinIntParameter : IntParameter
    {
        [NonSerialized] public int min;
        public MinIntParameter(int value, int min, bool overrideState = false) : base(value, overrideState) => this.min = min;
        public override int value { get => m_Value; set => m_Value = Mathf.Max(value, min); }
    }

    [Serializable]
    public class NoInterpMinIntParameter : VolumeParameter<int>
    {
        [NonSerialized] public int min;
        public NoInterpMinIntParameter(int value, int min, bool overrideState = false) : base(value, overrideState) => this.min = min;
        public override int value { get => m_Value; set => m_Value = Mathf.Max(value, min); }
    }

    [Serializable]
    public class MaxIntParameter : IntParameter
    {
        [NonSerialized] public int max;
        public MaxIntParameter(int value, int max, bool overrideState = false) : base(value, overrideState) => this.max = max;
        public override int value { get => m_Value; set => m_Value = Mathf.Min(value, max); }
    }

    [Serializable]
    public class NoInterpMaxIntParameter : VolumeParameter<int>
    {
        [NonSerialized] public int max;
        public NoInterpMaxIntParameter(int value, int max, bool overrideState = false) : base(value, overrideState) => this.max = max;
        public override int value { get => m_Value; set => m_Value = Mathf.Min(value, max); }
    }

    [Serializable]
    public class ClampedIntParameter : IntParameter
    {
        [NonSerialized] public int min;
        [NonSerialized] public int max;

        public ClampedIntParameter(int value, int min, int max, bool overrideState = false) : base(value, overrideState)
        {
            this.min = min;
            this.max = max;
        }

        public override int value { get => m_Value; set => m_Value = Mathf.Clamp(value, min, max); }
    }

    [Serializable]
    public class NoInterpClampedIntParameter : VolumeParameter<int>
    {
        [NonSerialized] public int min;
        [NonSerialized] public int max;

        public NoInterpClampedIntParameter(int value, int min, int max, bool overrideState = false) : base(value, overrideState)
        {
            this.min = min;
            this.max = max;
        }

        public override int value { get => m_Value; set => m_Value = Mathf.Clamp(value, min, max); }
    }

    [Serializable]
    public class FloatParameter : VolumeParameter<float>
    {
        public FloatParameter(float value, bool overrideState = false) : base(value, overrideState) { }
        public sealed override void Interp(float from, float to, float t) => m_Value = from + (to - from) * t;
    }

    [Serializable]
    public class NoInterpFloatParameter : VolumeParameter<float>
    {
        public NoInterpFloatParameter(float value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public class MinFloatParameter : FloatParameter
    {
        [NonSerialized] public float min;
        public MinFloatParameter(float value, float min, bool overrideState = false) : base(value, overrideState) => this.min = min;
        public override float value { get => m_Value; set => m_Value = Mathf.Max(value, min); }
    }

    [Serializable]
    public class NoInterpMinFloatParameter : VolumeParameter<float>
    {
        [NonSerialized] public float min;
        public NoInterpMinFloatParameter(float value, float min, bool overrideState = false) : base(value, overrideState) => this.min = min;
        public override float value { get => m_Value; set => m_Value = Mathf.Max(value, min); }
    }

    [Serializable]
    public class MaxFloatParameter : FloatParameter
    {
        [NonSerialized] public float max;
        public MaxFloatParameter(float value, float max, bool overrideState = false) : base(value, overrideState) => this.max = max;
        public override float value { get => m_Value; set => m_Value = Mathf.Min(value, max); }
    }

    [Serializable]
    public class NoInterpMaxFloatParameter : VolumeParameter<float>
    {
        [NonSerialized] public float max;
        public NoInterpMaxFloatParameter(float value, float max, bool overrideState = false) : base(value, overrideState) => this.max = max;
        public override float value { get => m_Value; set => m_Value = Mathf.Min(value, max); }
    }

    [Serializable]
    public class ClampedFloatParameter : FloatParameter
    {
        [NonSerialized] public float min;
        [NonSerialized] public float max;

        public ClampedFloatParameter(float value, float min, float max, bool overrideState = false) : base(value, overrideState)
        {
            this.min = min;
            this.max = max;
        }

        public override float value { get => m_Value; set => m_Value = Mathf.Clamp(value, min, max); }
    }

    [Serializable]
    public class NoInterpClampedFloatParameter : VolumeParameter<float>
    {
        [NonSerialized] public float min;
        [NonSerialized] public float max;

        public NoInterpClampedFloatParameter(float value, float min, float max, bool overrideState = false) : base(value, overrideState)
        {
            this.min = min;
            this.max = max;
        }

        public override float value { get => m_Value; set => m_Value = Mathf.Clamp(value, min, max); }
    }

    /// <summary>A range (x min, y max) within limits.</summary>
    [Serializable]
    public class FloatRangeParameter : VolumeParameter<Vector2>
    {
        [NonSerialized] public float min;
        [NonSerialized] public float max;

        public FloatRangeParameter(Vector2 value, float min, float max, bool overrideState = false) : base(value, overrideState)
        {
            this.min = min;
            this.max = max;
        }

        public override Vector2 value
        {
            get => m_Value;
            set
            {
                m_Value.x = Mathf.Max(value.x, min);
                m_Value.y = Mathf.Min(value.y, max);
            }
        }

        public override void Interp(Vector2 from, Vector2 to, float t)
        {
            m_Value.x = from.x + (to.x - from.x) * t;
            m_Value.y = from.y + (to.y - from.y) * t;
        }
    }

    [Serializable]
    public class NoInterpFloatRangeParameter : VolumeParameter<Vector2>
    {
        [NonSerialized] public float min;
        [NonSerialized] public float max;

        public NoInterpFloatRangeParameter(Vector2 value, float min, float max, bool overrideState = false) : base(value, overrideState)
        {
            this.min = min;
            this.max = max;
        }

        public override Vector2 value
        {
            get => m_Value;
            set
            {
                m_Value.x = Mathf.Max(value.x, min);
                m_Value.y = Mathf.Min(value.y, max);
            }
        }
    }

    [Serializable]
    public class ColorParameter : VolumeParameter<Color>
    {
        [NonSerialized] public bool hdr;
        [NonSerialized] public bool showAlpha = true;
        [NonSerialized] public bool showEyeDropper = true;

        public ColorParameter(Color value, bool overrideState = false) : base(value, overrideState) { }

        public ColorParameter(Color value, bool hdr, bool showAlpha, bool showEyeDropper, bool overrideState = false) : base(value, overrideState)
        {
            this.hdr = hdr;
            this.showAlpha = showAlpha;
            this.showEyeDropper = showEyeDropper;
        }

        public override void Interp(Color from, Color to, float t)
        {
            m_Value.r = from.r + (to.r - from.r) * t;
            m_Value.g = from.g + (to.g - from.g) * t;
            m_Value.b = from.b + (to.b - from.b) * t;
            m_Value.a = from.a + (to.a - from.a) * t;
        }
    }

    [Serializable]
    public class NoInterpColorParameter : VolumeParameter<Color>
    {
        [NonSerialized] public bool hdr;
        [NonSerialized] public bool showAlpha = true;
        [NonSerialized] public bool showEyeDropper = true;

        public NoInterpColorParameter(Color value, bool overrideState = false) : base(value, overrideState) { }

        public NoInterpColorParameter(Color value, bool hdr, bool showAlpha, bool showEyeDropper, bool overrideState = false) : base(value, overrideState)
        {
            this.hdr = hdr;
            this.showAlpha = showAlpha;
            this.showEyeDropper = showEyeDropper;
        }
    }

    [Serializable]
    public class Vector2Parameter : VolumeParameter<Vector2>
    {
        public Vector2Parameter(Vector2 value, bool overrideState = false) : base(value, overrideState) { }
        public override void Interp(Vector2 from, Vector2 to, float t) => m_Value = from + (to - from) * t;
    }

    [Serializable]
    public class NoInterpVector2Parameter : VolumeParameter<Vector2>
    {
        public NoInterpVector2Parameter(Vector2 value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public class Vector3Parameter : VolumeParameter<Vector3>
    {
        public Vector3Parameter(Vector3 value, bool overrideState = false) : base(value, overrideState) { }
        public override void Interp(Vector3 from, Vector3 to, float t) => m_Value = from + (to - from) * t;
    }

    [Serializable]
    public class NoInterpVector3Parameter : VolumeParameter<Vector3>
    {
        public NoInterpVector3Parameter(Vector3 value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public class Vector4Parameter : VolumeParameter<Vector4>
    {
        public Vector4Parameter(Vector4 value, bool overrideState = false) : base(value, overrideState) { }
        public override void Interp(Vector4 from, Vector4 to, float t) => m_Value = from + (to - from) * t;
    }

    [Serializable]
    public class NoInterpVector4Parameter : VolumeParameter<Vector4>
    {
        public NoInterpVector4Parameter(Vector4 value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public class TextureParameter : VolumeParameter<Texture>
    {
        public TextureDimension dimension;
        public TextureParameter(Texture value, bool overrideState = false) : this(value, TextureDimension.Any, overrideState) { }
        public TextureParameter(Texture value, TextureDimension dimension, bool overrideState = false) : base(value, overrideState) => this.dimension = dimension;
    }

    [Serializable]
    public class NoInterpTextureParameter : VolumeParameter<Texture>
    {
        public NoInterpTextureParameter(Texture value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public class Texture2DParameter : VolumeParameter<Texture>
    {
        public Texture2DParameter(Texture value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public class RenderTextureParameter : VolumeParameter<RenderTexture>
    {
        public RenderTextureParameter(RenderTexture value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public class NoInterpRenderTextureParameter : VolumeParameter<RenderTexture>
    {
        public NoInterpRenderTextureParameter(RenderTexture value, bool overrideState = false) : base(value, overrideState) { }
    }

    /// <summary>A parameter whose value is an object holding parameters of its own.</summary>
    [Serializable]
    public class ObjectParameter<T> : VolumeParameter<T>
    {
        public ObjectParameter(T value) : base(value, true) { }
        public sealed override bool overrideState { get => true; set => m_OverrideState = true; }
        public sealed override void Interp(T from, T to, float t) { }
    }

    [Serializable]
    public class EnumParameter<T> : VolumeParameter<T> where T : Enum
    {
        public EnumParameter(T value, bool overrideState = false) : base(value, overrideState) { }
    }
}
