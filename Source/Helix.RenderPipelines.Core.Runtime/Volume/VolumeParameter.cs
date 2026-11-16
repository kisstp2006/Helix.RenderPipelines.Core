using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HelixEngine.Rendering.Internal;

namespace HelixEngine.Rendering
{
    /// <summary>
    /// A value of a volume component that a volume overrides (overrideState) and the stack blends. A C# component's
    /// parameters keep their values here (the native stack blends them through calls back); a parameter of a C++
    /// component is read and written in the native core (m_Native).
    /// </summary>
    [Serializable]
    public abstract class VolumeParameter : ICloneable
    {
        [SerializeField] protected internal bool m_OverrideState;
        [NonSerialized] internal ulong m_Native;

        public virtual bool overrideState
        {
            get => m_Native != 0 ? VolumesNative.OverrideState(m_Native) : m_OverrideState;
            set
            {
                if (m_Native != 0)
                    VolumesNative.SetOverrideState(m_Native, value);
                else
                    m_OverrideState = value;
            }
        }

        // Generated fields of C++ components (the parameter members) answer these wrappers.
        static VolumeParameter() => global::Helix.Interop.Handles.Register(typeof(VolumeParameter), (id, type) => Native(id, type));

        // The C# classes of C++ parameters: the class of each native kind (a C++ component's parameter list) and how a
        // wrapper of a class is made (a C++ component's member of that class).
        static readonly Type[] s_KindClasses =
        {
            typeof(BoolParameter), typeof(IntParameter), typeof(FloatParameter), typeof(Vector2Parameter), typeof(Vector3Parameter),
            typeof(Vector4Parameter), typeof(ColorParameter), typeof(LayerMaskParameter), typeof(TextureParameter),
        };

        static readonly Dictionary<Type, Func<VolumeParameter>> s_Makers = new Dictionary<Type, Func<VolumeParameter>>
        {
            [typeof(BoolParameter)] = () => new BoolParameter(false),
            [typeof(LayerMaskParameter)] = () => new LayerMaskParameter(default),
            [typeof(IntParameter)] = () => new IntParameter(0),
            [typeof(NoInterpIntParameter)] = () => new NoInterpIntParameter(0),
            [typeof(MinIntParameter)] = () => new MinIntParameter(0, 0),
            [typeof(ClampedIntParameter)] = () => new ClampedIntParameter(0, 0, 0),
            [typeof(FloatParameter)] = () => new FloatParameter(0),
            [typeof(NoInterpFloatParameter)] = () => new NoInterpFloatParameter(0),
            [typeof(MinFloatParameter)] = () => new MinFloatParameter(0, 0),
            [typeof(MaxFloatParameter)] = () => new MaxFloatParameter(0, 0),
            [typeof(ClampedFloatParameter)] = () => new ClampedFloatParameter(0, 0, 0),
            [typeof(NoInterpClampedFloatParameter)] = () => new NoInterpClampedFloatParameter(0, 0, 0),
            [typeof(ColorParameter)] = () => new ColorParameter(default),
            [typeof(Vector2Parameter)] = () => new Vector2Parameter(default),
            [typeof(NoInterpVector2Parameter)] = () => new NoInterpVector2Parameter(default),
            [typeof(Vector3Parameter)] = () => new Vector3Parameter(default),
            [typeof(NoInterpVector3Parameter)] = () => new NoInterpVector3Parameter(default),
            [typeof(Vector4Parameter)] = () => new Vector4Parameter(default),
            [typeof(NoInterpVector4Parameter)] = () => new NoInterpVector4Parameter(default),
            [typeof(TextureParameter)] = () => new TextureParameter(null),
        };

        // One wrapper per native parameter.
        static readonly Dictionary<ulong, VolumeParameter> s_Wrappers = new Dictionary<ulong, VolumeParameter>();
        static int s_PruneAt = 256;

        // The wrapper of a C++ component's parameter: of the class asked for, else of its kind's class.
        internal static VolumeParameter Native(ulong id, Type type = null)
        {
            if (id == 0)
                return null;
            if (s_Wrappers.TryGetValue(id, out VolumeParameter known) && (type == null || type.IsInstanceOfType(known)))
                return known;
            int kind = VolumesNative.Kind(id);
            if (kind < 0)
                return null;
            if (type == null || type == typeof(VolumeParameter))
                type = kind < s_KindClasses.Length ? s_KindClasses[kind] : null;
            if (type == null)
                return null;
            if (!s_Makers.TryGetValue(type, out Func<VolumeParameter> make))
            {
                make = MakerOf(type);
                if (make == null)
                    return null;
                s_Makers[type] = make;
            }
            VolumeParameter p = make();
            p.m_Native = id;
            ParameterValue limits = VolumesNative.Limits(id);
            if (limits.i != 0)
                p.TakeLimits(limits.x, limits.y);
            if (s_Wrappers.Count >= s_PruneAt)
            {
                var dead = new List<ulong>();
                foreach (ulong key in s_Wrappers.Keys)
                    if (VolumesNative.Kind(key) < 0)
                        dead.Add(key);
                foreach (ulong key in dead)
                    s_Wrappers.Remove(key);
                s_PruneAt = Math.Max(256, s_Wrappers.Count * 2);
            }
            s_Wrappers[id] = p;
            return p;
        }

        // A parameter class without a list entry (an enum's parameter of a pipeline): made by its (value, overrideState)
        // constructor with the type's default value.
        static Func<VolumeParameter> MakerOf(Type type)
        {
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(VolumeParameter<>))
                    continue;
                Type value = t.GetGenericArguments()[0];
                System.Reflection.ConstructorInfo ctor = type.GetConstructor(new[] { value, typeof(bool) });
                if (ctor == null)
                    return null;
                object initial = value.IsValueType ? Activator.CreateInstance(value) : null;
                return () => (VolumeParameter)ctor.Invoke(new[] { initial, false });
            }
            return null;
        }

        // A wrapper's limits are its native parameter's (read when it is made).
        internal virtual void TakeLimits(float min, float max) { }

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
            get => m_Native != 0 ? NativeValue<T>.Get(m_Native) : m_Value;
            set
            {
                if (m_Native != 0)
                    NativeValue<T>.Set(m_Native, value);
                else
                    m_Value = value;
            }
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
            if (m_Native != 0)
                NativeValue<T>.Set(m_Native, x);
            else
                m_Value = x;
        }

        public override void SetValue(VolumeParameter parameter)
        {
            if (m_Native != 0)
                value = parameter.GetValue<T>();
            else
                m_Value = parameter.GetValue<T>();
        }

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

        public static explicit operator T(VolumeParameter<T> prop) => prop.value;

        public override object Clone() => MemberwiseClone();
    }

    // A native parameter's value as ParameterValue carries it.
    static class NativeValue<T>
    {
        public static T Get(ulong id)
        {
            ParameterValue v = VolumesNative.GetValue(id);
            object o;
            if (typeof(T) == typeof(bool))
                o = v.i != 0;
            else if (typeof(T) == typeof(int))
                o = v.i;
            else if (typeof(T) == typeof(LayerMask))
                o = (LayerMask)v.i;
            else if (typeof(T) == typeof(float))
                o = v.x;
            else if (typeof(T) == typeof(Vector2))
                o = new Vector2(v.x, v.y);
            else if (typeof(T) == typeof(Vector3))
                o = new Vector3(v.x, v.y, v.z);
            else if (typeof(T) == typeof(Vector4))
                o = new Vector4(v.x, v.y, v.z, v.w);
            else if (typeof(T) == typeof(Color))
                o = new Color(v.x, v.y, v.z, v.w);
            else if (typeof(T).IsEnum)
                o = Enum.ToObject(typeof(T), v.i);
            else
                o = VolumesNative.GetTexture(id);
            return o is T t ? t : default;
        }

        public static void Set(ulong id, T value)
        {
            var v = new ParameterValue();
            switch (value)
            {
                case bool b:
                    v.i = b ? 1 : 0;
                    break;
                case int i:
                    v.i = i;
                    break;
                case LayerMask m:
                    v.i = m.value;
                    break;
                case float f:
                    v.x = f;
                    break;
                case Vector2 a:
                    (v.x, v.y) = (a.x, a.y);
                    break;
                case Vector3 a:
                    (v.x, v.y, v.z) = (a.x, a.y, a.z);
                    break;
                case Vector4 a:
                    (v.x, v.y, v.z, v.w) = (a.x, a.y, a.z, a.w);
                    break;
                case Color c:
                    (v.x, v.y, v.z, v.w) = (c.r, c.g, c.b, c.a);
                    break;
                case Enum e:
                    v.i = Convert.ToInt32(e);
                    break;
                default:
                    VolumesNative.SetTexture(id, value as Texture);
                    return;
            }
            VolumesNative.SetValue(id, v);
        }
    }

    // The C# parameters of components as the native core blends them: GC handles of the parameter objects.
    static unsafe class ManagedParameters
    {
        static bool s_Set;

        internal static void Ensure()
        {
            if (s_Set)
                return;
            s_Set = true;
            VolumesNative.SetManagedCallbacks((ulong)(delegate* unmanaged[Cdecl]<ulong, byte>)&OverrideState,
                                              (ulong)(delegate* unmanaged[Cdecl]<ulong, byte, void>)&SetOverrideState,
                                              (ulong)(delegate* unmanaged[Cdecl]<ulong, ulong, ulong, float, void>)&Interp,
                                              (ulong)(delegate* unmanaged[Cdecl]<ulong, ulong, void>)&SetValue,
                                              (ulong)(delegate* unmanaged[Cdecl]<ulong, ulong, float, void>)&OverrideFrom,
                                              (ulong)(delegate* unmanaged[Cdecl]<ulong, ulong, void>)&ResetTo,
                                              (ulong)(delegate* unmanaged[Cdecl]<ulong, void>)&Release,
                                              (ulong)(delegate* unmanaged[Cdecl]<ulong, void>)&Free);
        }

        internal static ulong Handle(VolumeParameter p) => (ulong)GCHandle.ToIntPtr(GCHandle.Alloc(p));

        static VolumeParameter P(ulong h) => (VolumeParameter)GCHandle.FromIntPtr((IntPtr)h).Target;

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static byte OverrideState(ulong h)
        {
            try
            {
                return P(h).overrideState ? (byte)1 : (byte)0;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return 0;
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void SetOverrideState(ulong h, byte value)
        {
            try
            {
                P(h).overrideState = value != 0;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void Interp(ulong h, ulong from, ulong to, float t)
        {
            try
            {
                P(h).Interp(P(from), P(to), t);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void SetValue(ulong h, ulong from)
        {
            try
            {
                P(h).SetValue(P(from));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // The stack's parameter takes a profile's: its override state, its value blended by t.
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void OverrideFrom(ulong h, ulong to, float t)
        {
            try
            {
                VolumeParameter into = P(h), from = P(to);
                if (!from.overrideState)
                    return;
                into.overrideState = true;
                into.Interp(into, from, t);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void ResetTo(ulong h, ulong defaults)
        {
            try
            {
                VolumeParameter into = P(h);
                into.SetValue(P(defaults));
                into.overrideState = false;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void Release(ulong h)
        {
            try
            {
                P(h).Release();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void Free(ulong h) => GCHandle.FromIntPtr((IntPtr)h).Free();
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
        internal override void TakeLimits(float lo, float hi) { min = (int)lo; }

        [NonSerialized] public int min;
        public MinIntParameter(int value, int min, bool overrideState = false) : base(value, overrideState) => this.min = min;
        public override int value { get => m_Native != 0 ? base.value : m_Value; set { if (m_Native != 0) base.value = value; else m_Value = Mathf.Max(value, min); } }
    }

    [Serializable]
    public class NoInterpMinIntParameter : VolumeParameter<int>
    {
        [NonSerialized] public int min;
        public NoInterpMinIntParameter(int value, int min, bool overrideState = false) : base(value, overrideState) => this.min = min;
        public override int value { get => m_Native != 0 ? base.value : m_Value; set { if (m_Native != 0) base.value = value; else m_Value = Mathf.Max(value, min); } }
    }

    [Serializable]
    public class MaxIntParameter : IntParameter
    {
        [NonSerialized] public int max;
        public MaxIntParameter(int value, int max, bool overrideState = false) : base(value, overrideState) => this.max = max;
        public override int value { get => m_Native != 0 ? base.value : m_Value; set { if (m_Native != 0) base.value = value; else m_Value = Mathf.Min(value, max); } }
    }

    [Serializable]
    public class NoInterpMaxIntParameter : VolumeParameter<int>
    {
        [NonSerialized] public int max;
        public NoInterpMaxIntParameter(int value, int max, bool overrideState = false) : base(value, overrideState) => this.max = max;
        public override int value { get => m_Native != 0 ? base.value : m_Value; set { if (m_Native != 0) base.value = value; else m_Value = Mathf.Min(value, max); } }
    }

    [Serializable]
    public class ClampedIntParameter : IntParameter
    {
        internal override void TakeLimits(float lo, float hi) { min = (int)lo; max = (int)hi; }

        [NonSerialized] public int min;
        [NonSerialized] public int max;

        public ClampedIntParameter(int value, int min, int max, bool overrideState = false) : base(value, overrideState)
        {
            this.min = min;
            this.max = max;
        }

        public override int value { get => m_Native != 0 ? base.value : m_Value; set { if (m_Native != 0) base.value = value; else m_Value = Mathf.Clamp(value, min, max); } }
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

        public override int value { get => m_Native != 0 ? base.value : m_Value; set { if (m_Native != 0) base.value = value; else m_Value = Mathf.Clamp(value, min, max); } }
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
        internal override void TakeLimits(float lo, float hi) { min = lo; }

        [NonSerialized] public float min;
        public MinFloatParameter(float value, float min, bool overrideState = false) : base(value, overrideState) => this.min = min;
        public override float value { get => m_Native != 0 ? base.value : m_Value; set { if (m_Native != 0) base.value = value; else m_Value = Mathf.Max(value, min); } }
    }

    [Serializable]
    public class NoInterpMinFloatParameter : VolumeParameter<float>
    {
        [NonSerialized] public float min;
        public NoInterpMinFloatParameter(float value, float min, bool overrideState = false) : base(value, overrideState) => this.min = min;
        public override float value { get => m_Native != 0 ? base.value : m_Value; set { if (m_Native != 0) base.value = value; else m_Value = Mathf.Max(value, min); } }
    }

    [Serializable]
    public class MaxFloatParameter : FloatParameter
    {
        internal override void TakeLimits(float lo, float hi) { max = hi; }

        [NonSerialized] public float max;
        public MaxFloatParameter(float value, float max, bool overrideState = false) : base(value, overrideState) => this.max = max;
        public override float value { get => m_Native != 0 ? base.value : m_Value; set { if (m_Native != 0) base.value = value; else m_Value = Mathf.Min(value, max); } }
    }

    [Serializable]
    public class NoInterpMaxFloatParameter : VolumeParameter<float>
    {
        [NonSerialized] public float max;
        public NoInterpMaxFloatParameter(float value, float max, bool overrideState = false) : base(value, overrideState) => this.max = max;
        public override float value { get => m_Native != 0 ? base.value : m_Value; set { if (m_Native != 0) base.value = value; else m_Value = Mathf.Min(value, max); } }
    }

    [Serializable]
    public class ClampedFloatParameter : FloatParameter
    {
        internal override void TakeLimits(float lo, float hi) { min = lo; max = hi; }

        [NonSerialized] public float min;
        [NonSerialized] public float max;

        public ClampedFloatParameter(float value, float min, float max, bool overrideState = false) : base(value, overrideState)
        {
            this.min = min;
            this.max = max;
        }

        public override float value { get => m_Native != 0 ? base.value : m_Value; set { if (m_Native != 0) base.value = value; else m_Value = Mathf.Clamp(value, min, max); } }
    }

    [Serializable]
    public class NoInterpClampedFloatParameter : VolumeParameter<float>
    {
        internal override void TakeLimits(float lo, float hi) { min = lo; max = hi; }

        [NonSerialized] public float min;
        [NonSerialized] public float max;

        public NoInterpClampedFloatParameter(float value, float min, float max, bool overrideState = false) : base(value, overrideState)
        {
            this.min = min;
            this.max = max;
        }

        public override float value { get => m_Native != 0 ? base.value : m_Value; set { if (m_Native != 0) base.value = value; else m_Value = Mathf.Clamp(value, min, max); } }
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
