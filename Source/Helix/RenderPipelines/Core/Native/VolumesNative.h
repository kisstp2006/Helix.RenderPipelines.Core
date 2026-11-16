#pragma once

#include <Helix/RenderPipelines/Core/Volume/Volume.h>

// What C#'s volume types call (internal): parameters by id, the C# parameters' callbacks, profiles' component lists,
// stacks by id and the manager.
namespace helix::rendering
{
    HX_CLASS(Namespace = "HelixEngine.Rendering.Internal", Static, Internal)
    class VolumesNative
    {
    public:
        // The C# parameters' functions: unmanaged functions of GC handles (overrideState, setOverrideState, interp,
        // setValue, overrideFrom, resetTo, release, free).
        HX_FUNCTION() static void SetManagedCallbacks(u64 overrideState, u64 setOverrideState, u64 interp, u64 setValue, u64 overrideFrom, u64 resetTo,
                                                      u64 release, u64 free);

        // Components.
        HX_FUNCTION() static String DisplayName(VolumeComponent* component) { return component ? String(component->displayName()) : String(); }
        HX_FUNCTION() static void SetDisplayName(VolumeComponent* component, StringView name)
        {
            if (component)
                component->SetDisplayName(name);
        }
        HX_FUNCTION() static i32 NativeParameterCount(VolumeComponent* component) { return component ? i32(component->nativeParameterCount()) : 0; }
        HX_FUNCTION() static u64 ParameterId(VolumeComponent* component, i32 index);
        // GC handles of the component's C# parameters, in order (the component frees them).
        HX_FUNCTION() static void SetManagedParameters(VolumeComponent* component, Span<const u64> handles);
        HX_FUNCTION() static void CopyFrom(VolumeComponent* into, VolumeComponent* from);

        // Native parameters.
        HX_FUNCTION() static i32 Kind(u64 parameter);
        HX_FUNCTION() static ParameterValue GetValue(u64 parameter);
        HX_FUNCTION() static void SetValue(u64 parameter, ParameterValue value);
        // A texture parameter's texture.
        HX_FUNCTION() static engine::Texture* GetTexture(u64 parameter)
        {
            VolumeParameter* p = VolumeParameter::Find(parameter);
            return p && p->kind() == ParameterKind::Texture ? static_cast<TextureParameter*>(p)->texture() : nullptr;
        }
        HX_FUNCTION() static void SetTexture(u64 parameter, engine::Texture* texture)
        {
            if (VolumeParameter* p = VolumeParameter::Find(parameter); p && p->kind() == ParameterKind::Texture)
                static_cast<TextureParameter*>(p)->SetTexture(texture);
        }
        // A limited parameter's min (x) and max (y), i 1 (0: no limits).
        HX_FUNCTION() static ParameterValue Limits(u64 parameter)
        {
            ParameterValue v;
            if (VolumeParameter* p = VolumeParameter::Find(parameter); p && p->GetLimits(v.x, v.y))
                v.i = 1;
            return v;
        }
        HX_FUNCTION() static bool OverrideState(u64 parameter);
        HX_FUNCTION() static void SetOverrideState(u64 parameter, bool value);

        // Profiles.
        HX_FUNCTION() static VolumeComponent* Add(VolumeProfile* profile, scripting::TypeId type, bool overrides);
        HX_FUNCTION() static void Remove(VolumeProfile* profile, scripting::TypeId type);
        HX_FUNCTION() static i32 ComponentCount(VolumeProfile* profile) { return profile ? i32(profile->componentCount()) : 0; }
        HX_FUNCTION() static VolumeComponent* ComponentAt(VolumeProfile* profile, i32 index) { return profile ? profile->component(u32(index)) : nullptr; }
        HX_FUNCTION() static u32 Version(VolumeProfile* profile) { return profile ? profile->version() : 0; }

        // Stacks.
        HX_FUNCTION() static bool StackValid(u64 stack);
        HX_FUNCTION() static VolumeComponent* StackComponent(u64 stack, scripting::TypeId type);
        HX_FUNCTION() static i32 StackComponentCount(u64 stack);
        HX_FUNCTION() static VolumeComponent* StackComponentAt(u64 stack, i32 index);
        HX_FUNCTION() static void StackDispose(u64 stack);

        // The manager.
        HX_FUNCTION() static bool Initialize(VolumeProfile* globalDefault, VolumeProfile* qualityDefault)
        {
            return VolumeManager::instance().Initialize(globalDefault, qualityDefault);
        }
        HX_FUNCTION() static void Deinitialize() { VolumeManager::instance().Deinitialize(); }
        HX_FUNCTION() static bool IsInitialized() { return VolumeManager::instance().isInitialized(); }
        HX_FUNCTION() static VolumeProfile* GlobalDefaultProfile() { return VolumeManager::instance().globalDefaultProfile(); }
        HX_FUNCTION() static VolumeProfile* QualityDefaultProfile() { return VolumeManager::instance().qualityDefaultProfile(); }
        HX_FUNCTION() static i32 TypeCount() { return i32(VolumeManager::instance().baseComponentTypeArray().Size()); }
        HX_FUNCTION() static scripting::TypeId TypeAt(i32 index);
        HX_FUNCTION() static u64 CreateStack();
        HX_FUNCTION() static void DestroyStack(u64 stack);
        HX_FUNCTION() static u64 MainStack();
        HX_FUNCTION() static void SetMainStack(u64 stack);
        HX_FUNCTION() static void ResetMainStack() { VolumeManager::instance().ResetMainStack(); }
        HX_FUNCTION() static void Register(Volume* volume) { VolumeManager::instance().Register(volume); }
        HX_FUNCTION() static void Unregister(Volume* volume) { VolumeManager::instance().Unregister(volume); }
        // The volumes of a mask, kept for VolumeAt.
        HX_FUNCTION() static i32 GetVolumes(i32 layerMask);
        HX_FUNCTION() static Volume* VolumeAt(i32 index);
        HX_FUNCTION() static bool IsComponentActiveInMask(scripting::TypeId type, i32 layerMask)
        {
            return VolumeManager::instance().IsComponentActiveInMask(type, engine::LayerMask(layerMask));
        }
        HX_FUNCTION() static void Update(u64 stack, engine::Transform* trigger, i32 layerMask);
    };
}
