#include <Helix/RenderPipelines/Core/Volume/VolumeParameter.h>

#include <Helix/Core/Containers/HashMap.h>

namespace helix::rendering
{
    namespace
    {
        HashMap<u64, VolumeParameter*>& Parameters()
        {
            static HashMap<u64, VolumeParameter*> parameters;
            return parameters;
        }
        u64 gNextParameter = 0;
        ManagedParameter::Callbacks gManaged;
    }

    VolumeParameter::VolumeParameter() : id_(++gNextParameter) { Parameters().Set(id_, this); }

    // A copy is a parameter of its own (its id too).
    VolumeParameter::VolumeParameter(const VolumeParameter& other) : overrideState_(other.overrideState_), id_(++gNextParameter) { Parameters().Set(id_, this); }

    VolumeParameter& VolumeParameter::operator=(const VolumeParameter& other)
    {
        overrideState_ = other.overrideState_;
        return *this;
    }

    VolumeParameter::~VolumeParameter() { Parameters().Remove(id_); }

    VolumeParameter* VolumeParameter::Find(u64 id)
    {
        VolumeParameter** p = Parameters().Find(id);
        return p ? *p : nullptr;
    }

    void VolumeParameter::OverrideFrom(const VolumeParameter& to, f32 t)
    {
        if (!to.overrideState())
            return;
        SetOverrideState(true);
        Interp(*this, to, t);
    }

    void VolumeParameter::ResetTo(const VolumeParameter& defaults)
    {
        SetValue(defaults);
        SetOverrideState(false);
    }

    // ------------------------------------------------------------------------------------------------ C#'s

    void ManagedParameter::SetCallbacks(const Callbacks& callbacks) { gManaged = callbacks; }

    ManagedParameter::~ManagedParameter()
    {
        if (gManaged.free && handle_)
            gManaged.free(handle_);
    }

    namespace
    {
        // A C# parameter blends with C# parameters only (the stack's and the profiles' of one C# component type).
        u64 HandleOf(const VolumeParameter& p) { return p.kind() == ParameterKind::Managed ? static_cast<const ManagedParameter&>(p).handle() : 0; }
    }

    bool ManagedParameter::overrideState() const { return gManaged.overrideState && gManaged.overrideState(handle_) != 0; }

    void ManagedParameter::SetOverrideState(bool value)
    {
        if (gManaged.setOverrideState)
            gManaged.setOverrideState(handle_, value ? 1 : 0);
    }

    void ManagedParameter::Interp(const VolumeParameter& from, const VolumeParameter& to, f32 t)
    {
        if (gManaged.interp && HandleOf(from) && HandleOf(to))
            gManaged.interp(handle_, HandleOf(from), HandleOf(to), t);
    }

    void ManagedParameter::SetValue(const VolumeParameter& parameter)
    {
        if (gManaged.setValue && HandleOf(parameter))
            gManaged.setValue(handle_, HandleOf(parameter));
    }

    void ManagedParameter::Release()
    {
        if (gManaged.release)
            gManaged.release(handle_);
    }

    void ManagedParameter::OverrideFrom(const VolumeParameter& to, f32 t)
    {
        if (gManaged.overrideFrom && HandleOf(to))
            gManaged.overrideFrom(handle_, HandleOf(to), t);
    }

    void ManagedParameter::ResetTo(const VolumeParameter& defaults)
    {
        if (gManaged.resetTo && HandleOf(defaults))
            gManaged.resetTo(handle_, HandleOf(defaults));
    }
}
