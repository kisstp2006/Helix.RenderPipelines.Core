#pragma once

#include <Helix/Engine/Script/GameBehaviour.h>
#include <Helix/RenderPipelines/Core/Volume/VolumeComponent.h>

// Volumes and the manager that blends them into stacks.
namespace helix::rendering
{
    // A profile's overrides in the scene: global (everywhere, by its weight) or local (around its position, fading out
    // over its blend distance; the collider shapes of local volumes come with physics). Higher priorities blend later.
    HX_CLASS(Namespace = "HelixEngine.Rendering")
    class Volume : public engine::GameBehaviour
    {
    public:
        Volume() = default;
        Volume(const Volume& other);
        ~Volume() override;

        HX_PROPERTY() bool isGlobal() const { return isGlobal_; }
        void SetIsGlobal(bool value) { isGlobal_ = value; }
        HX_PROPERTY() f32 priority() const { return priority_; }
        void SetPriority(f32 value);

        // The distance from a local volume at which it starts to blend in.
        HX_FIELD() f32 blendDistance = 0;
        // 0 to 1.
        HX_FIELD() f32 weight = 1;

        // The profile shared with other volumes (an asset).
        HX_PROPERTY() VolumeProfile* sharedProfile() const { return sharedProfile_.Get(); }
        void SetSharedProfile(VolumeProfile* profile) { sharedProfile_ = engine::PPtr<VolumeProfile>(profile); }
        // This volume's own copy of its shared profile, made on first access; changing it changes only this volume.
        HX_PROPERTY() VolumeProfile* profile();
        void SetProfile(VolumeProfile* profile) { internalProfile_ = engine::PPtr<VolumeProfile>(profile); }
        HX_FUNCTION() bool HasInstantiatedProfile() const { return internalProfile_.Get() != nullptr; }

        // The profile it blends: its own copy when it has one, else the shared one.
        VolumeProfile* profileRef() const;

    private:
        void OnEnable();
        void OnDisable();

        HX_FIELD(SerializeField, Name = "m_IsGlobal") bool isGlobal_ = true;
        HX_FIELD(SerializeField, Name = "m_Priority") f32 priority_ = 0;
        HX_FIELD(SerializeField, Name = "sharedProfile") engine::PPtr<VolumeProfile> sharedProfile_;
        engine::PPtr<VolumeProfile> internalProfile_;
    };

    // The volumes of the scenes by layer and priority, and the stacks they blend into: Update resets a stack to the
    // defaults (the components' own values, then the default profiles), then blends every volume of the layers in
    // priority order (low first): global ones by their weight, local ones by their weight and how far into their
    // blend distance the trigger is.
    class VolumeManager
    {
    public:
        static VolumeManager& instance();

        // The stack Update(trigger, layerMask) blends into.
        VolumeStack* stack() const { return stack_; }
        void SetStack(VolumeStack* stack) { stack_ = stack; }
        // Every volume component type of both languages that is not abstract, by full name.
        Span<const scripting::TypeId> baseComponentTypeArray() const { return Span<const scripting::TypeId>(types_.Data(), types_.Size()); }
        bool isInitialized() const { return initialized_; }
        VolumeProfile* globalDefaultProfile() const { return globalDefault_.Get(); }
        VolumeProfile* qualityDefaultProfile() const { return qualityDefault_.Get(); }

        // False with an error when it is initialized already.
        bool Initialize(VolumeProfile* globalDefaultVolumeProfile = nullptr, VolumeProfile* qualityDefaultVolumeProfile = nullptr);
        void Deinitialize();
        VolumeStack* CreateStack();
        void DestroyStack(VolumeStack* stack);
        // The main stack made again (for component types that came after Initialize).
        void ResetMainStack();

        void Register(Volume* volume);
        void Unregister(Volume* volume);
        void SetPriorityDirty() { sortNeeded_ = true; }

        // The volumes of the layers, by priority (low first).
        void GetVolumes(engine::LayerMask layerMask, Array<Volume*>& out);
        bool IsComponentActiveInMask(scripting::TypeId type, engine::LayerMask layerMask);

        void Update(engine::Transform* trigger, engine::LayerMask layerMask) { Update(stack_, trigger, layerMask); }
        // The stack blended for a trigger (null: the global volumes only).
        void Update(VolumeStack* stack, engine::Transform* trigger, engine::LayerMask layerMask);

    private:
        VolumeManager() = default;
        void FindComponentTypes();
        void BuildDefaults();
        void ClearDefaults();
        void SortIfNeeded();

        Array<engine::EntityId> volumes_ = Array<engine::EntityId>();
        Array<scripting::TypeId> types_ = Array<scripting::TypeId>();
        Array<engine::EntityId> defaults_ = Array<engine::EntityId>();
        Array<VolumeStack*> stacks_ = Array<VolumeStack*>();
        VolumeStack* stack_ = nullptr;
        VolumeStack* defaultStack_ = nullptr;
        engine::PPtr<VolumeProfile> globalDefault_, qualityDefault_;
        bool initialized_ = false, sortNeeded_ = true;
    };
}
