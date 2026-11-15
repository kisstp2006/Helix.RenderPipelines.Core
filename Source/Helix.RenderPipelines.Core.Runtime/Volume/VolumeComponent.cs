using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using HelixEngine.Rendering.Internal;

namespace HelixEngine.Rendering
{
    /// <summary>
    /// Settings of an effect as volume parameters: profiles hold the ones volumes override, the stack holds the blended
    /// result. The native core's component: a C++ component's parameters are its members, a C# component's its fields of
    /// parameter types in the order they are declared (handed to the native component when it is enabled).
    /// </summary>
    public partial class VolumeComponent
    {
        /// <summary>The name of the component in the user interface.</summary>
        public string displayName
        {
            get => VolumesNative.DisplayName(this);
            protected set => VolumesNative.SetDisplayName(this, value ?? "");
        }

        VolumeParameter[] m_ParameterList;
        ReadOnlyCollection<VolumeParameter> m_Parameters;

        internal VolumeParameter[] parameterList
        {
            get
            {
                if (m_ParameterList == null)
                    FindParameters();
                return m_ParameterList;
            }
        }

        public ReadOnlyCollection<VolumeParameter> parameters
        {
            get
            {
                if (m_Parameters == null)
                    m_Parameters = new ReadOnlyCollection<VolumeParameter>(parameterList);
                return m_Parameters;
            }
        }

        static readonly Dictionary<Type, FieldInfo[]> s_Fields = new Dictionary<Type, FieldInfo[]>();

        static FieldInfo[] FieldsOf(Type type)
        {
            if (s_Fields.TryGetValue(type, out FieldInfo[] fields))
                return fields;
            var list = new List<FieldInfo>();
            var chain = new List<Type>();
            for (Type t = type; t != null && t != typeof(VolumeComponent) && !global::Helix.Interop.Types.IsGenerated(t); t = t.BaseType)
                chain.Add(t);
            // Base classes first, each in declaration order.
            for (int i = chain.Count - 1; i >= 0; i--)
                list.AddRange(chain[i].GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                  .Where(f => typeof(VolumeParameter).IsAssignableFrom(f.FieldType)).OrderBy(f => f.MetadataToken));
            fields = list.ToArray();
            s_Fields[type] = fields;
            return fields;
        }

        // The C++ members' wrappers, then the C# fields (handed to the native component of a C# component).
        void FindParameters()
        {
            var list = new List<VolumeParameter>();
            int native = VolumesNative.NativeParameterCount(this);
            for (int i = 0; i < native; i++)
                list.Add(VolumeParameter.Native(VolumesNative.ParameterId(this, i)));
            var own = new List<VolumeParameter>();
            foreach (FieldInfo f in FieldsOf(GetType()))
                if (f.GetValue(this) is VolumeParameter p)
                    own.Add(p);
            list.AddRange(own);
            // An object of a C# class: its native component blends these through GC handles it frees.
            if (((global::Helix.Interop.INativeObject)this).HxDerived)
            {
                ManagedParameters.Ensure();
                VolumesNative.SetManagedParameters(this, own.Select(ManagedParameters.Handle).ToArray());
            }
            m_ParameterList = list.ToArray();
            m_Parameters = null;
        }

        internal void Refresh() => FindParameters();

        protected virtual void OnEnable()
        {
            FindParameters();
            foreach (VolumeParameter p in m_ParameterList)
                p?.OnEnable();
        }

        protected virtual void OnDisable()
        {
            if (m_ParameterList == null)
                return;
            foreach (VolumeParameter p in m_ParameterList)
                p?.OnDisable();
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                foreach (VolumeParameter p in parameterList)
                    hash = hash * 23 + (p != null ? p.GetHashCode() : 0);
                return hash;
            }
        }
    }

    /// <summary>The volume components a volume overrides (the native core's profile; components is a view of its list).</summary>
    public sealed partial class VolumeProfile
    {
        readonly List<VolumeComponent> m_Components = new List<VolumeComponent>();
        uint m_Version = uint.MaxValue;

        /// <summary>The profile's components: add and remove them with Add and Remove.</summary>
        public List<VolumeComponent> components
        {
            get
            {
                uint version = VolumesNative.Version(this);
                if (version != m_Version)
                {
                    m_Version = version;
                    m_Components.Clear();
                    int count = VolumesNative.ComponentCount(this);
                    for (int i = 0; i < count; i++)
                        m_Components.Add(VolumesNative.ComponentAt(this, i));
                }
                return m_Components;
            }
        }

        /// <summary>Gives each component its parameters again and marks the profile dirty.</summary>
        public void Reset()
        {
            isDirty = true;
            foreach (VolumeComponent c in components)
                if (c != null)
                    c.Refresh();
        }

        public T Add<T>(bool overrides = false) where T : VolumeComponent => (T)Add(typeof(T), overrides);

        public VolumeComponent Add(Type type, bool overrides = false)
        {
            if (Has(type))
                throw new InvalidOperationException("Component already exists in the volume");
            VolumeComponent component = VolumesNative.Add(this, type, overrides);
            if (component == null)
                throw new ArgumentException($"{type?.Name} is no volume component type", nameof(type));
            return component;
        }

        public void Remove<T>() where T : VolumeComponent => Remove(typeof(T));

        public void Remove(Type type) => VolumesNative.Remove(this, type);

        public bool Has<T>() where T : VolumeComponent => Has(typeof(T));

        public bool Has(Type type) => components.Exists(c => c != null && c.GetType() == type);

        public bool HasSubclassOf(Type type) => components.Exists(c => c != null && c.GetType().IsSubclassOf(type));

        public bool TryGet<T>(out T component) where T : VolumeComponent => TryGet(typeof(T), out component);

        public bool TryGet<T>(Type type, out T component) where T : VolumeComponent
        {
            component = null;
            foreach (VolumeComponent c in components)
                if (c != null && c.GetType() == type)
                {
                    component = (T)c;
                    return true;
                }
            return false;
        }

        public bool TryGetSubclassOf<T>(Type type, out T component) where T : VolumeComponent
        {
            component = null;
            foreach (VolumeComponent c in components)
                if (c != null && c.GetType().IsSubclassOf(type))
                {
                    component = (T)c;
                    return true;
                }
            return false;
        }

        public bool TryGetAllSubclassOf<T>(Type type, List<T> result) where T : VolumeComponent
        {
            int count = result.Count;
            foreach (VolumeComponent c in components)
                if (c != null && c.GetType().IsSubclassOf(type))
                    result.Add((T)c);
            return result.Count != count;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                foreach (VolumeComponent c in components)
                    hash = hash * 23 + (c != null ? c.GetHashCode() : 0);
                return hash;
            }
        }
    }

    /// <summary>The blended state of every volume component type, for a camera (or any point): the native core's stack by id.</summary>
    public sealed class VolumeStack : IDisposable
    {
        internal readonly ulong m_Id;
        static readonly Dictionary<ulong, VolumeStack> s_Stacks = new Dictionary<ulong, VolumeStack>();

        VolumeStack(ulong id) => m_Id = id;

        internal static VolumeStack Of(ulong id)
        {
            if (id == 0)
                return null;
            if (!s_Stacks.TryGetValue(id, out VolumeStack s))
                s_Stacks[id] = s = new VolumeStack(id);
            return s;
        }

        internal static void Forget(ulong id) => s_Stacks.Remove(id);

        public bool isValid => VolumesNative.StackValid(m_Id);

        public T GetComponent<T>() where T : VolumeComponent => (T)GetComponent(typeof(T));

        public VolumeComponent GetComponent(Type type) => VolumesNative.StackComponent(m_Id, type);

        // Every component of the stack, in the order of the component types.
        internal IEnumerable<VolumeComponent> components
        {
            get
            {
                int count = VolumesNative.StackComponentCount(m_Id);
                for (int i = 0; i < count; i++)
                    if (VolumesNative.StackComponentAt(m_Id, i) is VolumeComponent c)
                        yield return c;
            }
        }

        public void Dispose() => VolumesNative.StackDispose(m_Id);
    }
}
