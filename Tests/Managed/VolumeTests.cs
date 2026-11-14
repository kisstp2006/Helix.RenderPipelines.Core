using System;
using HelixEngine;
using HelixEngine.Rendering;

namespace Tests
{
    public class CsTestVolume : VolumeComponent
    {
        public ClampedFloatParameter intensity = new ClampedFloatParameter(1f, 0f, 20f);
        public ColorParameter tint = new ColorParameter(Color.white);
        public BoolParameter flag = new BoolParameter(false);
        public NoInterpFloatParameter steps = new NoInterpFloatParameter(2f);
        public FloatParameter untouched = new FloatParameter(5f);
    }

    public class CsOtherVolume : VolumeComponent
    {
        public IntParameter count = new IntParameter(3);
    }

    // Volumes blended into a stack: two global ones by priority and weight, a local one by distance, layers, the defaults.
    public class CsVolumeTests : GameBehaviour
    {
        VolumeStack m_Stack;
        GameObject[] m_Objects;

        static VolumeProfile Profile(Action<CsTestVolume> set)
        {
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            set(p.Add<CsTestVolume>());
            return p;
        }

        Volume Make(string name, VolumeProfile profile, bool global, float priority, float weight = 1f)
        {
            var go = new GameObject(name);
            Volume v = go.AddComponent<Volume>();
            v.sharedProfile = profile;
            v.isGlobal = global;
            v.priority = priority;
            v.weight = weight;
            return v;
        }

        string State(Transform trigger, LayerMask mask)
        {
            VolumeManager.instance.Update(m_Stack, trigger, mask);
            var s = m_Stack.GetComponent<CsTestVolume>();
            var o = m_Stack.GetComponent<CsOtherVolume>();
            return $"{Notes.F(s.intensity.value)}:{Notes.F(s.tint.value.g)}:{s.flag.value}:{Notes.F(s.steps.value)}:{Notes.F(s.untouched.value)}:{o.count.value}:{s.intensity.overrideState}";
        }

        void Start()
        {
            var defaults = ScriptableObject.CreateInstance<VolumeProfile>();
            defaults.Add<CsOtherVolume>().count.Override(7);
            VolumeManager.instance.Initialize(defaults);
            Type[] types = VolumeManager.instance.baseComponentTypeArray;
            Notes.Note($"init:{VolumeManager.instance.isInitialized}:{Array.IndexOf(types, typeof(CsTestVolume)) >= 0}:{Array.IndexOf(types, typeof(CsOtherVolume)) >= 0}");
            m_Stack = VolumeManager.instance.CreateStack();

            Volume a = Make("VolumeA", Profile(c =>
            {
                c.intensity.Override(4f);
                c.tint.Override(Color.red);
                c.flag.Override(true);
            }), true, 0);
            Volume b = Make("VolumeB", Profile(c =>
            {
                c.intensity.Override(10f);
                c.steps.Override(8f);
            }), true, 1, 0.5f);
            Volume local = Make("VolumeLocal", Profile(c =>
            {
                c.intensity.value = 25f;
                c.intensity.overrideState = true;
            }), false, 2);
            local.blendDistance = 4;
            local.transform.position = new Vector3(10, 0, 0);
            m_Objects = new[] { a.gameObject, b.gameObject, local.gameObject };
            var trigger = new GameObject("Trigger");
            trigger.transform.position = new Vector3(8, 0, 0);

            var p = a.sharedProfile.components[0] as CsTestVolume;
            Notes.Note($"params:{p.parameters.Count}:{p.parameters[0] == p.intensity}:{p.parameters[4] == p.untouched}:{local.sharedProfile.components[0].parameters[0].GetValue<float>()}");
            Notes.Note("global:" + State(null, ~0));
            Notes.Note("near:" + State(trigger.transform, ~0));
            trigger.transform.position = new Vector3(100, 0, 0);
            Notes.Note("far:" + State(trigger.transform, ~0));
            b.gameObject.layer = 4;
            Notes.Note("layers:" + State(null, ~(1 << 4)) + ":" + VolumeManager.instance.GetVolumes(~0).Length + ":" + VolumeManager.instance.GetVolumes(1 << 4).Length);
            b.gameObject.layer = 0;
            a.weight = 0;
            Notes.Note("weight:" + State(null, ~0));
            a.weight = 1;
            p.active = false;
            Notes.Note("inactive:" + State(null, ~0));
            p.active = true;
            // Priority order: A above B now.
            a.priority = 5;
            Notes.Note("priority:" + State(null, ~0));
            Notes.Note($"active:{VolumeManager.instance.IsComponentActiveInMask<CsTestVolume>(~0)}:{VolumeManager.instance.IsComponentActiveInMask<CsOtherVolume>(~0)}");

            // The instance profile: a copy only this volume blends.
            bool before = a.HasInstantiatedProfile();
            a.profile.TryGet(out CsTestVolume copy);
            copy.intensity.Override(0f);
            Notes.Note($"instance:{before}:{a.HasInstantiatedProfile()}:{Notes.F(p.intensity.value)}:{State(null, ~0)}");

            VolumeProfile profile = b.sharedProfile;
            bool threw = false;
            try
            {
                profile.Add<CsTestVolume>();
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            profile.Add<CsOtherVolume>(true);
            bool had = profile.Has<CsOtherVolume>() && profile.TryGet(out CsOtherVolume other) && other.count.overrideState;
            profile.Remove<CsOtherVolume>();
            Notes.Note($"profile:{threw}:{had}:{profile.Has<CsOtherVolume>()}:{profile.components.Count}");
            Destroy(trigger);
        }

        void OnDestroy()
        {
            if (m_Objects != null)
                foreach (GameObject o in m_Objects)
                    DestroyImmediate(o);
            VolumeManager.instance.DestroyStack(m_Stack);
            VolumeManager.instance.Deinitialize();
        }
    }
}
