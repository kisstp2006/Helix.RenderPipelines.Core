#pragma once

#include <Helix/Engine/Script/ScriptableObject.h>
#include <Helix/RenderPipelines/Core/Volume/VolumeParameter.h>

// Volume components, profiles and stacks. A component holds the settings of an effect as volume parameters: profiles
// hold the ones volumes override, a stack the blended result. A C++ component's parameters are its members (added in
// its constructor, in order); a C# component's are its C# fields, handed over when it is enabled.
namespace helix::rendering
{
    HX_CLASS(Namespace = "HelixEngine.Rendering")
    class VolumeComponent : public engine::ScriptableObject
    {
    public:
        VolumeComponent() = default;
        VolumeComponent(const VolumeComponent& other);
        ~VolumeComponent() override;

        // A component that is not active is left out of the blending.
        HX_FIELD() bool active = true;

        // The name of the component in the user interface.
        StringView displayName() const { return displayName_; }
        void SetDisplayName(StringView name) { displayName_ = String(name); }

        u32 parameterCount() const { return u32(offsets_.Size() + managed_.Size()); }
        // The C++ members among them (the first ones).
        u32 nativeParameterCount() const { return u32(offsets_.Size()); }
        VolumeParameter* parameter(u32 index) const;

        // The overridden parameters of this component blended into the state's by interpFactor.
        HX_FUNCTION() virtual void Override(VolumeComponent* state, f32 interpFactor);
        HX_FUNCTION() void SetAllOverridesTo(bool state);
        HX_FUNCTION() bool AnyPropertiesIsOverridden() const;
        HX_FUNCTION() virtual void Release();

        // The values and override states of another component of the same type.
        void CopyFrom(const VolumeComponent& other);
        // A stack's component back to the defaults: their values, nothing overridden.
        void ResetFrom(const VolumeComponent& defaults);
        // A C# component's parameters (GC handles of its C# parameter objects, in order), replacing earlier ones.
        void SetManagedParameters(Span<const u64> handles);

    protected:
        // A parameter member of a C++ component (its constructor adds them in order).
        void AddParameter(VolumeParameter& member);

    private:
        String displayName_ = String();
        // The members by their offset in the object (a copy's own members), then the C# parameters.
        Array<u32> offsets_ = Array<u32>();
        Array<ManagedParameter*> managed_ = Array<ManagedParameter*>();
    };

    // The volume components a volume overrides.
    HX_CLASS(Namespace = "HelixEngine.Rendering", Sealed)
    class VolumeProfile : public engine::ScriptableObject
    {
    public:
        // Set when components are added or removed; the manager reads it.
        HX_PROPERTY() bool isDirty() const { return isDirty_; }
        void SetIsDirty(bool value) { isDirty_ = value; }

        // Marks the profile dirty (C# gives its components their parameters again first).
        void Reset() { isDirty_ = true; }

        u32 componentCount() const { return u32(components_.Size()); }
        VolumeComponent* component(u32 index) const { return index < components_.Size() ? components_[index].Get() : nullptr; }
        // Changes with every Add and Remove (C#'s view of the list).
        u32 version() const { return version_; }

        // A new component of that type (null with an error when the profile has one or the type is no component).
        VolumeComponent* Add(scripting::TypeId type, bool overrides = false);
        template <class T>
        T* Add(bool overrides = false)
        {
            return static_cast<T*>(Add(scripting::TypeOf<T>(), overrides));
        }
        void Remove(scripting::TypeId type);
        bool Has(scripting::TypeId type) const { return Find(type, false) != nullptr; }
        bool HasSubclassOf(scripting::TypeId type) const { return Find(type, true) != nullptr; }
        // The component of the type (or of a class below it).
        VolumeComponent* Find(scripting::TypeId type, bool subclass) const;
        template <class T>
        T* TryGet() const
        {
            return static_cast<T*>(Find(scripting::TypeOf<T>(), false));
        }
        void AllSubclassOf(scripting::TypeId type, Array<VolumeComponent*>& out) const;

    private:
        void OnEnable();
        void OnDestroy();

        HX_FIELD(SerializeField, Name = "components") Array<engine::PPtr<VolumeComponent>> components_;
        bool isDirty_ = true;
        u32 version_ = 0;
    };

    // The blended state of every volume component type, for a camera (or any point).
    class VolumeStack
    {
    public:
        ~VolumeStack();
        VolumeStack(const VolumeStack&) = delete;
        VolumeStack& operator=(const VolumeStack&) = delete;

        static VolumeStack* Find(u64 id);
        u64 Id() const { return id_; }

        bool isValid() const { return valid_; }
        VolumeComponent* GetComponent(scripting::TypeId type) const;
        template <class T>
        T* GetComponent() const
        {
            return static_cast<T*>(GetComponent(scripting::TypeOf<T>()));
        }
        void Dispose();

        u32 componentCount() const { return u32(components_.Size()); }
        VolumeComponent* component(u32 index) const { return index < components_.Size() ? components_[index].component.Get() : nullptr; }

    private:
        friend class VolumeManager;
        VolumeStack();
        void Reload(Span<const scripting::TypeId> types);

        struct Entry
        {
            scripting::TypeId type;
            engine::PPtr<VolumeComponent> component;
        };
        Array<Entry> components_ = Array<Entry>();
        bool valid_ = false;
        u64 id_ = 0;
    };

    // The engine class of volume components and a type's script class below it (both languages).
    u32 VolumeComponentClass();
    bool IsVolumeComponentType(scripting::TypeId type);
    // The namespace and name of a script class ("Tests.CsTintPost"): the order of component types.
    String ScriptTypeFullName(scripting::TypeId type);
}
