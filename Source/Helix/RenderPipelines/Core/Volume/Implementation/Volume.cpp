#include <Helix/RenderPipelines/Core/Volume/Volume.h>

#include <Helix/Core/Logging/Log.h>
#include <Helix/Engine/GameObject/GameObject.h>
#include <Helix/Engine/Script/ScriptClass.h>
#include <Helix/Engine/Transform/Transform.h>
#include <Helix/RenderPipelines/Core/Utilities/CoreUtils.h>

#include <algorithm>

namespace helix::rendering
{
    namespace
    {
        LogChannel VolumeLog{"Volumes"};

        scripting::TypeId TypeOfObject(const engine::Object* o) { return o ? o->Class().type : scripting::TypeId{}; }
    }

    // ------------------------------------------------------------------------------------------------ Volume

    // A copy's own profile is not copied (it is made again on access).
    Volume::Volume(const Volume& other)
        : GameBehaviour(other), blendDistance(other.blendDistance), weight(other.weight), isGlobal_(other.isGlobal_), priority_(other.priority_),
          sharedProfile_(other.sharedProfile_)
    {
    }

    Volume::~Volume() = default;

    void Volume::SetPriority(f32 value)
    {
        priority_ = value;
        VolumeManager::instance().SetPriorityDirty();
    }

    VolumeProfile* Volume::profile()
    {
        if (VolumeProfile* own = internalProfile_.Get())
            return own;
        auto* own = static_cast<VolumeProfile*>(engine::ScriptableObject::CreateInstance(scripting::TypeOf<VolumeProfile>()));
        if (!own)
            return nullptr;
        own->hideFlags = engine::HideFlags::DontSave;
        if (VolumeProfile* shared = sharedProfile_.Get())
        {
            own->SetName(shared->Name());
            for (u32 i = 0; i < shared->componentCount(); i++)
                if (VolumeComponent* item = shared->component(i))
                    if (VolumeComponent* copy = own->Add(item->Class().type))
                        copy->CopyFrom(*item);
        }
        internalProfile_ = engine::PPtr<VolumeProfile>(own);
        return own;
    }

    VolumeProfile* Volume::profileRef() const
    {
        VolumeProfile* own = internalProfile_.Get();
        return own ? own : sharedProfile_.Get();
    }

    void Volume::OnEnable() { VolumeManager::instance().Register(this); }

    void Volume::OnDisable() { VolumeManager::instance().Unregister(this); }

    // ------------------------------------------------------------------------------------------------ VolumeManager

    VolumeManager& VolumeManager::instance()
    {
        static VolumeManager manager;
        return manager;
    }

    bool VolumeManager::Initialize(VolumeProfile* globalDefaultVolumeProfile, VolumeProfile* qualityDefaultVolumeProfile)
    {
        if (initialized_)
        {
            HX_LOG_ERROR(VolumeLog, "VolumeManager is already initialized.");
            return false;
        }
        globalDefault_ = engine::PPtr<VolumeProfile>(globalDefaultVolumeProfile);
        qualityDefault_ = engine::PPtr<VolumeProfile>(qualityDefaultVolumeProfile);
        FindComponentTypes();
        BuildDefaults();
        defaultStack_ = CreateStack();
        stack_ = defaultStack_;
        initialized_ = true;
        return true;
    }

    void VolumeManager::Deinitialize()
    {
        for (VolumeStack* s : stacks_)
            delete s;
        stacks_.Clear();
        ClearDefaults();
        defaultStack_ = stack_ = nullptr;
        types_.Clear();
        globalDefault_ = qualityDefault_ = engine::PPtr<VolumeProfile>();
        initialized_ = false;
    }

    void VolumeManager::FindComponentTypes()
    {
        types_.Clear();
        for (u32 i = 0; i < engine::ScriptClassCount(); i++)
            if (const engine::ScriptClass* s = engine::GetScriptClass(i); s && IsVolumeComponentType(s->type))
                types_.Push(s->type);
        std::sort(types_.begin(), types_.end(), [](scripting::TypeId a, scripting::TypeId b) {
            return ScriptTypeFullName(a).View() < ScriptTypeFullName(b).View();
        });
    }

    // The state a stack starts each update from: each type's own values, the global default profile, the quality one.
    void VolumeManager::BuildDefaults()
    {
        ClearDefaults();
        for (scripting::TypeId t : types_)
        {
            auto* c = static_cast<VolumeComponent*>(engine::ScriptableObject::CreateInstance(t));
            if (c)
                c->hideFlags = engine::HideFlags::HideAndDontSave;
            defaults_.Push(c ? c->GetEntityId() : engine::EntityId());
        }
        for (VolumeProfile* p : {globalDefault_.Get(), qualityDefault_.Get()})
            if (p)
                for (u32 i = 0; i < p->componentCount(); i++)
                    if (VolumeComponent* c = p->component(i); c && c->active)
                        for (usize k = 0; k < types_.Size(); k++)
                            if (types_[k] == TypeOfObject(c))
                                if (auto* d = engine::EntityIdToObject<VolumeComponent>(defaults_[k]))
                                    c->Override(d, 1.0f);
    }

    void VolumeManager::ClearDefaults()
    {
        for (engine::EntityId id : defaults_)
            CoreUtils::Destroy(engine::EntityIdToObject(id));
        defaults_.Clear();
    }

    VolumeStack* VolumeManager::CreateStack()
    {
        auto* s = new VolumeStack();
        s->Reload(Span<const scripting::TypeId>(types_.Data(), types_.Size()));
        stacks_.Push(s);
        return s;
    }

    void VolumeManager::DestroyStack(VolumeStack* stack)
    {
        if (!stack)
            return;
        for (usize i = 0; i < stacks_.Size(); i++)
            if (stacks_[i] == stack)
            {
                stacks_.RemoveAt(i);
                break;
            }
        if (stack_ == stack)
            stack_ = nullptr;
        if (defaultStack_ == stack)
            defaultStack_ = nullptr;
        delete stack;
    }

    void VolumeManager::ResetMainStack()
    {
        FindComponentTypes();
        BuildDefaults();
        if (defaultStack_)
            DestroyStack(defaultStack_);
        defaultStack_ = CreateStack();
        stack_ = defaultStack_;
    }

    void VolumeManager::Register(Volume* volume)
    {
        if (!volume)
            return;
        for (engine::EntityId id : volumes_)
            if (id == volume->GetEntityId())
                return;
        volumes_.Push(volume->GetEntityId());
        sortNeeded_ = true;
    }

    void VolumeManager::Unregister(Volume* volume)
    {
        if (!volume)
            return;
        for (usize i = 0; i < volumes_.Size(); i++)
            if (volumes_[i] == volume->GetEntityId())
            {
                volumes_.RemoveAt(i);
                break;
            }
        sortNeeded_ = true;
    }

    void VolumeManager::SortIfNeeded()
    {
        if (!sortNeeded_)
            return;
        // Stable: volumes of the same priority keep the order they registered in.
        std::stable_sort(volumes_.begin(), volumes_.end(), [](engine::EntityId a, engine::EntityId b) {
            auto* va = engine::EntityIdToObject<Volume>(a);
            auto* vb = engine::EntityIdToObject<Volume>(b);
            return (va ? va->priority() : 0.0f) < (vb ? vb->priority() : 0.0f);
        });
        sortNeeded_ = false;
    }

    void VolumeManager::GetVolumes(engine::LayerMask layerMask, Array<Volume*>& out)
    {
        SortIfNeeded();
        for (engine::EntityId id : volumes_)
            if (auto* v = engine::EntityIdToObject<Volume>(id))
                if (engine::GameObject* g = v->gameObject(); g && (layerMask.value() & (1 << g->layer())) != 0)
                    out.Push(v);
    }

    bool VolumeManager::IsComponentActiveInMask(scripting::TypeId type, engine::LayerMask layerMask)
    {
        Array<Volume*> volumes;
        GetVolumes(layerMask, volumes);
        for (Volume* v : volumes)
        {
            VolumeProfile* p = v->enabled() ? v->profileRef() : nullptr;
            if (VolumeComponent* c = p ? p->Find(type, false) : nullptr; c && c->active)
                return true;
        }
        return false;
    }

    namespace
    {
        void OverrideData(VolumeStack& stack, const VolumeProfile& profile, f32 interpFactor)
        {
            for (u32 i = 0; i < profile.componentCount(); i++)
            {
                VolumeComponent* c = profile.component(i);
                if (!c || !c->active)
                    continue;
                if (VolumeComponent* state = stack.GetComponent(TypeOfObject(c)))
                    c->Override(state, interpFactor);
            }
        }
    }

    void VolumeManager::Update(VolumeStack* stack, engine::Transform* trigger, engine::LayerMask layerMask)
    {
        if (!initialized_ || !stack)
            return;
        // The defaults first.
        for (u32 i = 0; i < stack->componentCount(); i++)
            if (VolumeComponent* c = stack->component(i))
                for (usize k = 0; k < types_.Size(); k++)
                    if (types_[k] == stack->components_[i].type)
                        if (auto* d = engine::EntityIdToObject<VolumeComponent>(defaults_[k]))
                            c->ResetFrom(*d);
        bool onlyGlobal = trigger == nullptr;
        Vector3 at = onlyGlobal ? Vector3(0, 0, 0) : trigger->position();
        Array<Volume*> volumes;
        GetVolumes(layerMask, volumes);
        for (Volume* volume : volumes)
        {
            engine::GameObject* g = volume->gameObject();
            if (!volume->enabled() || !g || !g->activeInHierarchy())
                continue;
            VolumeProfile* profile = volume->profileRef();
            if (!profile || volume->weight <= 0.0f)
                continue;
            f32 weight = volume->weight > 1.0f ? 1.0f : volume->weight;
            if (volume->isGlobal())
            {
                OverrideData(*stack, *profile, weight);
                continue;
            }
            if (onlyGlobal)
                continue;
            // Without colliders a local volume is its blend distance around its position.
            Vector3 p = volume->transform()->position();
            f32 dx = at.x - p.x, dy = at.y - p.y, dz = at.z - p.z;
            f32 distanceSqr = dx * dx + dy * dy + dz * dz;
            f32 blendDistanceSqr = volume->blendDistance * volume->blendDistance;
            if (distanceSqr > blendDistanceSqr)
                continue;
            f32 interpFactor = blendDistanceSqr > 0.0f ? 1.0f - distanceSqr / blendDistanceSqr : 1.0f;
            OverrideData(*stack, *profile, interpFactor * weight);
        }
    }
}
