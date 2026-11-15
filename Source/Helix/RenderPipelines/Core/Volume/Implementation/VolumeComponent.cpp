#include <Helix/RenderPipelines/Core/Volume/VolumeComponent.h>

#include <Helix/Core/Containers/HashMap.h>
#include <Helix/Core/Logging/Log.h>
#include <Helix/Engine/Script/ScriptClass.h>
#include <Helix/RenderPipelines/Core/Utilities/CoreUtils.h>

namespace helix::rendering
{
    namespace
    {
        LogChannel VolumeLog{"Volumes"};

        scripting::TypeId TypeOfObject(const engine::Object* o) { return o ? o->Class().type : scripting::TypeId{}; }
    }

    u32 VolumeComponentClass()
    {
        const engine::ScriptClass* s = engine::FindScriptClass(scripting::TypeOf<VolumeComponent>());
        return s && s->alive ? s->engineClass : engine::kNoClass;
    }

    bool IsVolumeComponentType(scripting::TypeId type)
    {
        const engine::ScriptClass* s = engine::FindScriptClass(type);
        const scripting::TypeInfo* t = scripting::GetType(type);
        u32 base = VolumeComponentClass();
        return s && s->alive && t && !(t->flags & scripting::TypeAbstract) && base != engine::kNoClass && s->engineClass != base &&
               engine::IsDerivedFrom(s->engineClass, base);
    }

    String ScriptTypeFullName(scripting::TypeId type)
    {
        const engine::ScriptClass* s = engine::FindScriptClass(type);
        if (!s)
            return String();
        String full(s->nameSpace);
        if (!full.Empty())
            full.Append(".");
        full.Append(s->name);
        return full;
    }

    // ------------------------------------------------------------------------------------------------ VolumeComponent

    // A copy has the members at the same offsets; C#'s parameters come again when it is enabled.
    VolumeComponent::VolumeComponent(const VolumeComponent& other)
        : ScriptableObject(other), active(other.active), displayName_(other.displayName_), offsets_(other.offsets_)
    {
    }

    VolumeComponent::~VolumeComponent()
    {
        for (ManagedParameter* p : managed_)
            delete p;
    }

    VolumeParameter* VolumeComponent::parameter(u32 index) const
    {
        if (index < offsets_.Size())
            return reinterpret_cast<VolumeParameter*>(reinterpret_cast<u8*>(const_cast<VolumeComponent*>(this)) + offsets_[index]);
        index -= u32(offsets_.Size());
        return index < managed_.Size() ? managed_[index] : nullptr;
    }

    void VolumeComponent::AddParameter(VolumeParameter& member)
    {
        offsets_.Push(u32(reinterpret_cast<const u8*>(&member) - reinterpret_cast<const u8*>(this)));
    }

    void VolumeComponent::SetManagedParameters(Span<const u64> handles)
    {
        for (ManagedParameter* p : managed_)
            delete p;
        managed_.Clear();
        for (u64 h : handles)
            managed_.Push(new ManagedParameter(h));
    }

    void VolumeComponent::Override(VolumeComponent* state, f32 interpFactor)
    {
        if (!state)
            return;
        u32 count = parameterCount() < state->parameterCount() ? parameterCount() : state->parameterCount();
        for (u32 i = 0; i < count; i++)
        {
            VolumeParameter* to = parameter(i);
            VolumeParameter* into = state->parameter(i);
            if (to && into && to->kind() == into->kind())
                into->OverrideFrom(*to, interpFactor);
        }
    }

    void VolumeComponent::SetAllOverridesTo(bool state)
    {
        for (u32 i = 0; i < parameterCount(); i++)
            parameter(i)->SetOverrideState(state);
    }

    bool VolumeComponent::AnyPropertiesIsOverridden() const
    {
        for (u32 i = 0; i < parameterCount(); i++)
            if (parameter(i)->overrideState())
                return true;
        return false;
    }

    void VolumeComponent::Release()
    {
        for (u32 i = 0; i < parameterCount(); i++)
            parameter(i)->Release();
    }

    void VolumeComponent::CopyFrom(const VolumeComponent& other)
    {
        active = other.active;
        u32 count = parameterCount() < other.parameterCount() ? parameterCount() : other.parameterCount();
        for (u32 i = 0; i < count; i++)
        {
            VolumeParameter* into = parameter(i);
            const VolumeParameter* from = other.parameter(i);
            if (into->kind() != from->kind())
                continue;
            into->SetValue(*from);
            into->SetOverrideState(from->overrideState());
        }
    }

    void VolumeComponent::ResetFrom(const VolumeComponent& defaults)
    {
        u32 count = parameterCount() < defaults.parameterCount() ? parameterCount() : defaults.parameterCount();
        for (u32 i = 0; i < count; i++)
            if (parameter(i)->kind() == defaults.parameter(i)->kind())
                parameter(i)->ResetTo(*defaults.parameter(i));
    }

    // ------------------------------------------------------------------------------------------------ VolumeProfile

    void VolumeProfile::OnEnable()
    {
        for (usize i = components_.Size(); i-- > 0;)
            if (!components_[i].Get())
                components_.RemoveAt(i);
    }

    void VolumeProfile::OnDestroy()
    {
        for (const engine::PPtr<VolumeComponent>& c : components_)
            CoreUtils::Destroy(c.Get());
    }

    VolumeComponent* VolumeProfile::Add(scripting::TypeId type, bool overrides)
    {
        const scripting::TypeInfo* t = scripting::GetType(type);
        if (!IsVolumeComponentType(type))
        {
            HX_LOG_ERROR(VolumeLog, "VolumeProfile.Add: {} is no volume component type", t ? StringView(t->name) : StringView("the type"));
            return nullptr;
        }
        if (Has(type))
        {
            HX_LOG_ERROR(VolumeLog, "Component already exists in the volume");
            return nullptr;
        }
        auto* component = static_cast<VolumeComponent*>(engine::ScriptableObject::CreateInstance(type));
        if (!component)
            return nullptr;
        component->hideFlags = engine::HideFlags::HideInInspector | engine::HideFlags::HideInHierarchy;
        component->SetName(t->name);
        component->SetAllOverridesTo(overrides);
        components_.Push(engine::PPtr<VolumeComponent>(component));
        isDirty_ = true;
        version_++;
        return component;
    }

    void VolumeProfile::Remove(scripting::TypeId type)
    {
        for (usize i = 0; i < components_.Size(); i++)
            if (VolumeComponent* c = components_[i].Get(); c && TypeOfObject(c) == type)
            {
                components_.RemoveAt(i);
                isDirty_ = true;
                version_++;
                return;
            }
    }

    VolumeComponent* VolumeProfile::Find(scripting::TypeId type, bool subclass) const
    {
        for (const engine::PPtr<VolumeComponent>& p : components_)
            if (VolumeComponent* c = p.Get())
            {
                scripting::TypeId t = TypeOfObject(c);
                if (subclass ? t != type && scripting::IsAssignableFrom(type, t) : t == type)
                    return c;
            }
        return nullptr;
    }

    void VolumeProfile::AllSubclassOf(scripting::TypeId type, Array<VolumeComponent*>& out) const
    {
        for (const engine::PPtr<VolumeComponent>& p : components_)
            if (VolumeComponent* c = p.Get())
                if (scripting::TypeId t = TypeOfObject(c); t != type && scripting::IsAssignableFrom(type, t))
                    out.Push(c);
    }

    // ------------------------------------------------------------------------------------------------ VolumeStack

    namespace
    {
        HashMap<u64, VolumeStack*>& Stacks()
        {
            static HashMap<u64, VolumeStack*> stacks;
            return stacks;
        }
        u64 gNextStack = 0;
    }

    VolumeStack::VolumeStack() : id_(++gNextStack) { Stacks().Set(id_, this); }

    VolumeStack::~VolumeStack()
    {
        Dispose();
        Stacks().Remove(id_);
    }

    VolumeStack* VolumeStack::Find(u64 id)
    {
        VolumeStack** s = Stacks().Find(id);
        return s ? *s : nullptr;
    }

    void VolumeStack::Reload(Span<const scripting::TypeId> types)
    {
        Dispose();
        for (scripting::TypeId t : types)
        {
            auto* c = static_cast<VolumeComponent*>(engine::ScriptableObject::CreateInstance(t));
            if (!c)
                continue;
            c->hideFlags = engine::HideFlags::HideAndDontSave;
            components_.Push({t, engine::PPtr<VolumeComponent>(c)});
        }
        valid_ = true;
    }

    VolumeComponent* VolumeStack::GetComponent(scripting::TypeId type) const
    {
        for (const Entry& e : components_)
            if (e.type == type)
                return e.component.Get();
        return nullptr;
    }

    void VolumeStack::Dispose()
    {
        for (const Entry& e : components_)
            if (VolumeComponent* c = e.component.Get())
            {
                c->Release();
                CoreUtils::Destroy(c);
            }
        components_.Clear();
        valid_ = false;
    }
}
