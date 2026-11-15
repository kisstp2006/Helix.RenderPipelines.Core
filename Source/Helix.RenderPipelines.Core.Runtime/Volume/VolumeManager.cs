using System;
using HelixEngine.Rendering.Internal;

namespace HelixEngine.Rendering
{
    /// <summary>
    /// The volumes of the scenes by layer and priority, and the stacks they blend into: Update resets a stack to the
    /// defaults (the components' own values, then the default profiles), then blends every volume of the layers in
    /// priority order (low first): global ones by their weight, local ones by their weight and how far into their
    /// blend distance the trigger is. The native core's manager.
    /// </summary>
    public sealed class VolumeManager
    {
        static readonly Lazy<VolumeManager> s_Instance = new Lazy<VolumeManager>(() => new VolumeManager());

        public static VolumeManager instance => s_Instance.Value;

        Type[] m_Types = Array.Empty<Type>();

        VolumeManager() { }

        /// <summary>The stack Update(trigger, layerMask) blends into.</summary>
        public VolumeStack stack
        {
            get => VolumeStack.Of(VolumesNative.MainStack());
            set => VolumesNative.SetMainStack(value != null ? value.m_Id : 0);
        }

        /// <summary>Every volume component type of both languages that is not abstract, by full name.</summary>
        public Type[] baseComponentTypeArray => m_Types;

        public bool isInitialized => VolumesNative.IsInitialized();

        public VolumeProfile globalDefaultProfile => VolumesNative.GlobalDefaultProfile();
        public VolumeProfile qualityDefaultProfile => VolumesNative.QualityDefaultProfile();

        void ReadTypes()
        {
            int count = VolumesNative.TypeCount();
            m_Types = new Type[count];
            for (int i = 0; i < count; i++)
                m_Types[i] = VolumesNative.TypeAt(i);
        }

        public void Initialize(VolumeProfile globalDefaultVolumeProfile = null, VolumeProfile qualityDefaultVolumeProfile = null)
        {
            if (isInitialized)
                throw new InvalidOperationException("VolumeManager is already initialized.");
            ManagedParameters.Ensure();
            VolumesNative.Initialize(globalDefaultVolumeProfile, qualityDefaultVolumeProfile);
            ReadTypes();
        }

        public void Deinitialize()
        {
            VolumesNative.Deinitialize();
            m_Types = Array.Empty<Type>();
        }

        public VolumeStack CreateStack() => VolumeStack.Of(VolumesNative.CreateStack());

        public void DestroyStack(VolumeStack stack)
        {
            if (stack == null)
                return;
            VolumesNative.DestroyStack(stack.m_Id);
            VolumeStack.Forget(stack.m_Id);
        }

        /// <summary>The main stack made again (for component types that came after Initialize).</summary>
        public void ResetMainStack()
        {
            VolumesNative.ResetMainStack();
            ReadTypes();
        }

        public void Register(Volume volume) => VolumesNative.Register(volume);

        public void Unregister(Volume volume) => VolumesNative.Unregister(volume);

        /// <summary>The volumes of the layers, by priority (low first).</summary>
        public Volume[] GetVolumes(LayerMask layerMask)
        {
            int count = VolumesNative.GetVolumes(layerMask.value);
            var volumes = new Volume[count];
            for (int i = 0; i < count; i++)
                volumes[i] = VolumesNative.VolumeAt(i);
            return volumes;
        }

        public bool IsComponentActiveInMask<T>(LayerMask layerMask) where T : VolumeComponent => VolumesNative.IsComponentActiveInMask(typeof(T), layerMask.value);

        public void Update(Transform trigger, LayerMask layerMask) => Update(stack, trigger, layerMask);

        /// <summary>The stack blended for a trigger (null: the global volumes only).</summary>
        public void Update(VolumeStack stack, Transform trigger, LayerMask layerMask)
        {
            if (stack != null)
                VolumesNative.Update(stack.m_Id, trigger, layerMask.value);
        }
    }
}
