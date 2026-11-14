using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace HelixEngine.Rendering
{
    /// <summary>
    /// Settings of an effect as volume parameters: profiles hold the ones volumes override, the stack holds the blended
    /// result. Its parameters are its fields of parameter types, in the order they are declared.
    /// </summary>
    [Serializable]
    public class VolumeComponent : ScriptableObject
    {
        /// <summary>A component that is not active is left out of the blending.</summary>
        public bool active = true;

        /// <summary>The name of the component in the user interface.</summary>
        public string displayName { get; protected set; } = "";

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
            for (Type t = type; t != null && t != typeof(VolumeComponent); t = t.BaseType)
                chain.Add(t);
            // Base classes first, each in declaration order.
            for (int i = chain.Count - 1; i >= 0; i--)
                list.AddRange(chain[i].GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                  .Where(f => typeof(VolumeParameter).IsAssignableFrom(f.FieldType)).OrderBy(f => f.MetadataToken));
            fields = list.ToArray();
            s_Fields[type] = fields;
            return fields;
        }

        void FindParameters()
        {
            FieldInfo[] fields = FieldsOf(GetType());
            var list = new List<VolumeParameter>(fields.Length);
            foreach (FieldInfo f in fields)
                if (f.GetValue(this) is VolumeParameter p)
                    list.Add(p);
            m_ParameterList = list.ToArray();
            m_Parameters = null;
        }

        internal void Refresh() => FindParameters();

        protected virtual void OnEnable()
        {
            FindParameters();
            foreach (VolumeParameter p in m_ParameterList)
                p.OnEnable();
        }

        protected virtual void OnDisable()
        {
            if (m_ParameterList == null)
                return;
            foreach (VolumeParameter p in m_ParameterList)
                p.OnDisable();
        }

        /// <summary>The overridden parameters of this component blended into the state's by interpFactor.</summary>
        public virtual void Override(VolumeComponent state, float interpFactor)
        {
            VolumeParameter[] from = parameterList, into = state.parameterList;
            int count = Math.Min(from.Length, into.Length);
            for (int i = 0; i < count; i++)
            {
                VolumeParameter toParam = from[i];
                if (!toParam.overrideState)
                    continue;
                VolumeParameter stateParam = into[i];
                stateParam.overrideState = true;
                stateParam.Interp(stateParam, toParam, interpFactor);
            }
        }

        public void SetAllOverridesTo(bool state)
        {
            foreach (VolumeParameter p in parameterList)
                p.overrideState = state;
        }

        public bool AnyPropertiesIsOverridden()
        {
            foreach (VolumeParameter p in parameterList)
                if (p.overrideState)
                    return true;
            return false;
        }

        public virtual void Release()
        {
            if (m_ParameterList == null)
                return;
            foreach (VolumeParameter p in m_ParameterList)
                p?.Release();
        }

        // The values and override states of another component of the same type.
        internal void CopyFrom(VolumeComponent other)
        {
            active = other.active;
            VolumeParameter[] from = other.parameterList, into = parameterList;
            for (int i = 0; i < Math.Min(from.Length, into.Length); i++)
            {
                into[i].SetValue(from[i]);
                into[i].overrideState = from[i].overrideState;
            }
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                foreach (VolumeParameter p in parameterList)
                    hash = hash * 23 + p.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>The volume components a volume overrides.</summary>
    public sealed class VolumeProfile : ScriptableObject
    {
        public List<VolumeComponent> components = new List<VolumeComponent>();

        /// <summary>Set when components are added or removed; the manager reads it.</summary>
        [NonSerialized] public bool isDirty = true;

        void OnEnable() => components.RemoveAll(c => c == null);

        internal void OnDestroy()
        {
            foreach (VolumeComponent c in components)
                CoreUtils.Destroy(c);
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
            var component = (VolumeComponent)CreateInstance(type);
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            component.name = type.Name;
            component.SetAllOverridesTo(overrides);
            components.Add(component);
            isDirty = true;
            return component;
        }

        public void Remove<T>() where T : VolumeComponent => Remove(typeof(T));

        public void Remove(Type type)
        {
            int index = components.FindIndex(c => c != null && c.GetType() == type);
            if (index < 0)
                return;
            components.RemoveAt(index);
            isDirty = true;
        }

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

    /// <summary>The blended state of every volume component type, for a camera (or any point).</summary>
    public sealed class VolumeStack : IDisposable
    {
        internal readonly Dictionary<Type, VolumeComponent> components = new Dictionary<Type, VolumeComponent>();
        internal bool requiresReset = true;

        public bool isValid { get; private set; }

        internal VolumeStack() { }

        internal void Reload(Type[] baseTypes)
        {
            Clear();
            foreach (Type t in baseTypes)
            {
                var c = (VolumeComponent)ScriptableObject.CreateInstance(t);
                c.hideFlags = HideFlags.HideAndDontSave;
                components.Add(t, c);
            }
            requiresReset = true;
            isValid = true;
        }

        public T GetComponent<T>() where T : VolumeComponent => (T)GetComponent(typeof(T));

        public VolumeComponent GetComponent(Type type) => components.TryGetValue(type, out VolumeComponent c) ? c : null;

        void Clear()
        {
            foreach (VolumeComponent c in components.Values)
            {
                c.Release();
                CoreUtils.Destroy(c);
            }
            components.Clear();
            isValid = false;
        }

        public void Dispose() => Clear();
    }
}
