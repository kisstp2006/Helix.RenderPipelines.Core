using System;
using System.Collections.Generic;
using System.Linq;

namespace HelixEngine.Rendering
{
    /// <summary>
    /// The volumes of the scenes by layer and priority, and the stacks they blend into: Update resets a stack to the
    /// defaults (the components' own values, then the default profiles), then blends every volume of the layers in
    /// priority order (low first): global ones by their weight, local ones by their weight and how far into their
    /// blend distance the trigger is.
    /// </summary>
    public sealed class VolumeManager
    {
        static readonly Lazy<VolumeManager> s_Instance = new Lazy<VolumeManager>(() => new VolumeManager());

        public static VolumeManager instance => s_Instance.Value;

        readonly List<Volume> m_Volumes = new List<Volume>();
        readonly Dictionary<Type, VolumeComponent> m_DefaultState = new Dictionary<Type, VolumeComponent>();
        readonly List<VolumeStack> m_CreatedStacks = new List<VolumeStack>();
        bool m_SortNeeded = true;
        VolumeStack m_DefaultStack;

        VolumeManager() { }

        /// <summary>The stack Update(trigger, layerMask) blends into.</summary>
        public VolumeStack stack { get; set; }

        /// <summary>Every volume component type that is not abstract.</summary>
        public Type[] baseComponentTypeArray { get; private set; } = Array.Empty<Type>();

        public bool isInitialized { get; private set; }

        public VolumeProfile globalDefaultProfile { get; private set; }
        public VolumeProfile qualityDefaultProfile { get; private set; }

        public void Initialize(VolumeProfile globalDefaultVolumeProfile = null, VolumeProfile qualityDefaultVolumeProfile = null)
        {
            if (isInitialized)
                throw new InvalidOperationException("VolumeManager is already initialized.");
            globalDefaultProfile = globalDefaultVolumeProfile;
            qualityDefaultProfile = qualityDefaultVolumeProfile;
            baseComponentTypeArray = FindComponentTypes();
            BuildDefaults();
            m_DefaultStack = CreateStack();
            stack = m_DefaultStack;
            isInitialized = true;
        }

        public void Deinitialize()
        {
            foreach (VolumeStack s in m_CreatedStacks.ToArray())
                s.Dispose();
            m_CreatedStacks.Clear();
            foreach (VolumeComponent c in m_DefaultState.Values)
                CoreUtils.Destroy(c);
            m_DefaultState.Clear();
            m_DefaultStack = null;
            stack = null;
            baseComponentTypeArray = Array.Empty<Type>();
            globalDefaultProfile = qualityDefaultProfile = null;
            isInitialized = false;
        }

        static Type[] FindComponentTypes()
        {
            var types = new List<Type>();
            foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] all;
                try
                {
                    all = a.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException e)
                {
                    all = e.Types.Where(t => t != null).ToArray();
                }
                foreach (Type t in all)
                    if (t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters && t.IsSubclassOf(typeof(VolumeComponent)))
                        types.Add(t);
            }
            types.Sort((x, y) => string.CompareOrdinal(x.FullName, y.FullName));
            return types.ToArray();
        }

        // The state a stack starts each update from: each type's own values, the global default profile, the quality one.
        void BuildDefaults()
        {
            foreach (Type t in baseComponentTypeArray)
            {
                var c = (VolumeComponent)ScriptableObject.CreateInstance(t);
                c.hideFlags = HideFlags.HideAndDontSave;
                m_DefaultState[t] = c;
            }
            foreach (VolumeProfile p in new[] { globalDefaultProfile, qualityDefaultProfile })
                if (p != null)
                    foreach (VolumeComponent c in p.components)
                        if (c != null && c.active && m_DefaultState.TryGetValue(c.GetType(), out VolumeComponent d))
                            c.Override(d, 1.0f);
        }

        public VolumeStack CreateStack()
        {
            var s = new VolumeStack();
            s.Reload(baseComponentTypeArray);
            m_CreatedStacks.Add(s);
            return s;
        }

        public void DestroyStack(VolumeStack stack)
        {
            m_CreatedStacks.Remove(stack);
            stack.Dispose();
        }

        /// <summary>The main stack made again (for component types that came after Initialize).</summary>
        public void ResetMainStack()
        {
            baseComponentTypeArray = FindComponentTypes();
            foreach (VolumeComponent c in m_DefaultState.Values)
                CoreUtils.Destroy(c);
            m_DefaultState.Clear();
            BuildDefaults();
            if (m_DefaultStack != null)
                DestroyStack(m_DefaultStack);
            m_DefaultStack = CreateStack();
            stack = m_DefaultStack;
        }

        public void Register(Volume volume)
        {
            if (volume == null || m_Volumes.Contains(volume))
                return;
            m_Volumes.Add(volume);
            m_SortNeeded = true;
        }

        public void Unregister(Volume volume)
        {
            m_Volumes.Remove(volume);
            m_SortNeeded = true;
        }

        internal void SetPriorityDirty() => m_SortNeeded = true;

        void SortIfNeeded()
        {
            if (!m_SortNeeded)
                return;
            // Stable: volumes of the same priority keep the order they registered in.
            var sorted = m_Volumes.Select((v, i) => (v, i)).OrderBy(x => x.v.priority).ThenBy(x => x.i).Select(x => x.v).ToList();
            m_Volumes.Clear();
            m_Volumes.AddRange(sorted);
            m_SortNeeded = false;
        }

        static bool InMask(Volume v, LayerMask layerMask) => (layerMask.value & (1 << v.gameObject.layer)) != 0;

        /// <summary>The volumes of the layers, by priority (low first).</summary>
        public Volume[] GetVolumes(LayerMask layerMask)
        {
            SortIfNeeded();
            return m_Volumes.Where(v => v != null && InMask(v, layerMask)).ToArray();
        }

        public bool IsComponentActiveInMask<T>(LayerMask layerMask) where T : VolumeComponent
        {
            foreach (Volume v in GetVolumes(layerMask))
            {
                if (!v.enabled || v.profileRef == null)
                    continue;
                if (v.profileRef.TryGet(out T component) && component.active)
                    return true;
            }
            return false;
        }

        public void Update(Transform trigger, LayerMask layerMask) => Update(stack, trigger, layerMask);

        /// <summary>The stack blended for a trigger (null: the global volumes only).</summary>
        public void Update(VolumeStack stack, Transform trigger, LayerMask layerMask)
        {
            if (!isInitialized || stack == null)
                return;
            // The defaults first.
            foreach (KeyValuePair<Type, VolumeComponent> e in stack.components)
            {
                if (!m_DefaultState.TryGetValue(e.Key, out VolumeComponent d))
                    continue;
                VolumeParameter[] into = e.Value.parameterList, from = d.parameterList;
                for (int i = 0; i < into.Length && i < from.Length; i++)
                {
                    into[i].SetValue(from[i]);
                    into[i].overrideState = false;
                }
            }
            stack.requiresReset = false;
            bool onlyGlobal = trigger == null;
            Vector3 triggerPosition = onlyGlobal ? Vector3.zero : trigger.position;
            foreach (Volume volume in GetVolumes(layerMask))
            {
                if (!volume.enabled || !volume.gameObject.activeInHierarchy)
                    continue;
                VolumeProfile profile = volume.profileRef;
                if (profile == null || volume.weight <= 0f)
                    continue;
                float weight = Mathf.Clamp01(volume.weight);
                if (volume.isGlobal)
                {
                    OverrideData(stack, profile.components, weight);
                    continue;
                }
                if (onlyGlobal)
                    continue;
                // Without colliders a local volume is its blend distance around its position.
                float distanceSqr = (triggerPosition - volume.transform.position).sqrMagnitude;
                float blendDistanceSqr = volume.blendDistance * volume.blendDistance;
                if (distanceSqr > blendDistanceSqr)
                    continue;
                float interpFactor = blendDistanceSqr > 0f ? 1f - distanceSqr / blendDistanceSqr : 1f;
                OverrideData(stack, profile.components, interpFactor * weight);
            }
        }

        static void OverrideData(VolumeStack stack, List<VolumeComponent> components, float interpFactor)
        {
            foreach (VolumeComponent c in components)
            {
                if (c == null || !c.active)
                    continue;
                VolumeComponent state = stack.GetComponent(c.GetType());
                if (state != null)
                    c.Override(state, interpFactor);
            }
        }
    }
}
