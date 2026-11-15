#include <Helix/RenderPipelines/Core/Native/VolumesNative.h>

#include <bit>

namespace helix::rendering
{
    namespace
    {
        template <class F>
        F As(u64 f)
        {
            return std::bit_cast<F>(uptr(f));
        }

        Array<engine::EntityId>& Listed()
        {
            static Array<engine::EntityId> listed;
            return listed;
        }
    }

    void VolumesNative::SetManagedCallbacks(u64 overrideState, u64 setOverrideState, u64 interp, u64 setValue, u64 overrideFrom, u64 resetTo, u64 release,
                                            u64 free)
    {
        ManagedParameter::Callbacks c;
        c.overrideState = As<decltype(c.overrideState)>(overrideState);
        c.setOverrideState = As<decltype(c.setOverrideState)>(setOverrideState);
        c.interp = As<decltype(c.interp)>(interp);
        c.setValue = As<decltype(c.setValue)>(setValue);
        c.overrideFrom = As<decltype(c.overrideFrom)>(overrideFrom);
        c.resetTo = As<decltype(c.resetTo)>(resetTo);
        c.release = As<decltype(c.release)>(release);
        c.free = As<decltype(c.free)>(free);
        ManagedParameter::SetCallbacks(c);
    }

    u64 VolumesNative::ParameterId(VolumeComponent* component, i32 index)
    {
        VolumeParameter* p = component && index >= 0 ? component->parameter(u32(index)) : nullptr;
        return p ? p->Id() : 0;
    }

    void VolumesNative::SetManagedParameters(VolumeComponent* component, Span<const u64> handles)
    {
        if (component)
            component->SetManagedParameters(handles);
    }

    void VolumesNative::CopyFrom(VolumeComponent* into, VolumeComponent* from)
    {
        if (into && from)
            into->CopyFrom(*from);
    }

    i32 VolumesNative::Kind(u64 parameter)
    {
        VolumeParameter* p = VolumeParameter::Find(parameter);
        return p ? i32(p->kind()) : -1;
    }

    ParameterValue VolumesNative::GetValue(u64 parameter)
    {
        VolumeParameter* p = VolumeParameter::Find(parameter);
        return p ? p->Get() : ParameterValue();
    }

    void VolumesNative::SetValue(u64 parameter, ParameterValue value)
    {
        if (VolumeParameter* p = VolumeParameter::Find(parameter))
            p->Set(value);
    }

    bool VolumesNative::OverrideState(u64 parameter)
    {
        VolumeParameter* p = VolumeParameter::Find(parameter);
        return p && p->overrideState();
    }

    void VolumesNative::SetOverrideState(u64 parameter, bool value)
    {
        if (VolumeParameter* p = VolumeParameter::Find(parameter))
            p->SetOverrideState(value);
    }

    VolumeComponent* VolumesNative::Add(VolumeProfile* profile, scripting::TypeId type, bool overrides) { return profile ? profile->Add(type, overrides) : nullptr; }

    void VolumesNative::Remove(VolumeProfile* profile, scripting::TypeId type)
    {
        if (profile)
            profile->Remove(type);
    }

    bool VolumesNative::StackValid(u64 stack)
    {
        VolumeStack* s = VolumeStack::Find(stack);
        return s && s->isValid();
    }

    VolumeComponent* VolumesNative::StackComponent(u64 stack, scripting::TypeId type)
    {
        VolumeStack* s = VolumeStack::Find(stack);
        return s ? s->GetComponent(type) : nullptr;
    }

    i32 VolumesNative::StackComponentCount(u64 stack)
    {
        VolumeStack* s = VolumeStack::Find(stack);
        return s ? i32(s->componentCount()) : 0;
    }

    VolumeComponent* VolumesNative::StackComponentAt(u64 stack, i32 index)
    {
        VolumeStack* s = VolumeStack::Find(stack);
        return s && index >= 0 ? s->component(u32(index)) : nullptr;
    }

    void VolumesNative::StackDispose(u64 stack)
    {
        if (VolumeStack* s = VolumeStack::Find(stack))
            s->Dispose();
    }

    scripting::TypeId VolumesNative::TypeAt(i32 index)
    {
        Span<const scripting::TypeId> types = VolumeManager::instance().baseComponentTypeArray();
        return index >= 0 && usize(index) < types.Size() ? types[usize(index)] : scripting::TypeId{};
    }

    u64 VolumesNative::CreateStack() { return VolumeManager::instance().CreateStack()->Id(); }

    void VolumesNative::DestroyStack(u64 stack) { VolumeManager::instance().DestroyStack(VolumeStack::Find(stack)); }

    u64 VolumesNative::MainStack()
    {
        VolumeStack* s = VolumeManager::instance().stack();
        return s ? s->Id() : 0;
    }

    void VolumesNative::SetMainStack(u64 stack) { VolumeManager::instance().SetStack(VolumeStack::Find(stack)); }

    i32 VolumesNative::GetVolumes(i32 layerMask)
    {
        Array<Volume*> volumes;
        VolumeManager::instance().GetVolumes(engine::LayerMask(layerMask), volumes);
        Listed().Clear();
        for (Volume* v : volumes)
            Listed().Push(v->GetEntityId());
        return i32(volumes.Size());
    }

    Volume* VolumesNative::VolumeAt(i32 index) { return index >= 0 && usize(index) < Listed().Size() ? engine::EntityIdToObject<Volume>(Listed()[usize(index)]) : nullptr; }

    void VolumesNative::Update(u64 stack, engine::Transform* trigger, i32 layerMask)
    {
        VolumeManager::instance().Update(VolumeStack::Find(stack), trigger, engine::LayerMask(layerMask));
    }
}
