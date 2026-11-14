using System;

namespace HelixEngine.Rendering
{
    /// <summary>
    /// A profile's overrides in the scene: global (everywhere, by its weight) or local (around its position, fading
    /// out over its blend distance; the collider shapes of local volumes come with physics). Higher priorities blend later.
    /// </summary>
    public class Volume : GameBehaviour
    {
        [SerializeField] bool m_IsGlobal = true;
        [SerializeField] float m_Priority;

        public bool isGlobal
        {
            get => m_IsGlobal;
            set => m_IsGlobal = value;
        }

        public float priority
        {
            get => m_Priority;
            set
            {
                m_Priority = value;
                VolumeManager.instance.SetPriorityDirty();
            }
        }

        /// <summary>The distance from a local volume at which it starts to blend in.</summary>
        public float blendDistance;

        /// <summary>0 to 1.</summary>
        public float weight = 1f;

        /// <summary>The profile shared with other volumes (an asset).</summary>
        public VolumeProfile sharedProfile;

        [NonSerialized] VolumeProfile m_InternalProfile;

        /// <summary>This volume's own copy of its shared profile, made on first access; changing it changes only this volume.</summary>
        public VolumeProfile profile
        {
            get
            {
                if (m_InternalProfile == null)
                {
                    m_InternalProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                    m_InternalProfile.hideFlags = HideFlags.DontSave;
                    if (sharedProfile != null)
                    {
                        m_InternalProfile.name = sharedProfile.name;
                        foreach (VolumeComponent item in sharedProfile.components)
                        {
                            if (item == null)
                                continue;
                            VolumeComponent copy = m_InternalProfile.Add(item.GetType());
                            copy.CopyFrom(item);
                        }
                    }
                }
                return m_InternalProfile;
            }
            set => m_InternalProfile = value;
        }

        /// <summary>The profile the volume blends: its own copy when it has one, else the shared one.</summary>
        internal VolumeProfile profileRef => m_InternalProfile != null ? m_InternalProfile : sharedProfile;

        public bool HasInstantiatedProfile() => m_InternalProfile != null;

        void OnEnable() => VolumeManager.instance.Register(this);

        void OnDisable() => VolumeManager.instance.Unregister(this);
    }
}
