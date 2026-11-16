#pragma once

#include <Helix/Engine/Script/GameBehaviour.h>
#include <Helix/RenderPipelines/Core/Volume/Volume.h>

// C++ volume components of the test program's game module (RenderPipelinesCoreTests): the C# volume test's
// components, their parameters members in order (C# reaches them as properties of their C# classes, files hold them).
namespace helix::rpcore::testing
{
    // An enum parameter of C++ components (its C# class is CoreTests.CppTestModeParameter).
    HX_ENUM(Namespace = "CoreTests")
    enum class CppTestMode : i32
    {
        First = 0,
        Second = 1,
        Third = 2,
    };

    HX_HANDLE(CSharp = "CoreTests.CppTestModeParameter")
    class CppTestModeParameter : public rendering::VolumeParameterOf<CppTestMode>
    {
    public:
        using VolumeParameterOf::VolumeParameterOf;
    };

    HX_CLASS(Namespace = "CoreTests")
    class CppTestVolume : public rendering::VolumeComponent
    {
    public:
        CppTestVolume()
        {
            AddParameter(intensity);
            AddParameter(tint);
            AddParameter(flag);
            AddParameter(steps);
            AddParameter(untouched);
            AddParameter(mode);
        }

        HX_FIELD() rendering::ClampedFloatParameter intensity{1.0f, 0.0f, 20.0f};
        HX_FIELD() rendering::ColorParameter tint{Color(1, 1, 1, 1)};
        HX_FIELD() rendering::BoolParameter flag{false};
        HX_FIELD() rendering::NoInterpFloatParameter steps{2.0f};
        HX_FIELD() rendering::FloatParameter untouched{5.0f};
        HX_FIELD() CppTestModeParameter mode{CppTestMode::Second};
    };

    HX_CLASS(Namespace = "CoreTests")
    class CppOtherVolume : public rendering::VolumeComponent
    {
    public:
        CppOtherVolume() { AddParameter(count); }

        HX_FIELD() rendering::IntParameter count{3};
    };

    // Parameters as fields of a C++ script: in scene files as the reference writes them.
    HX_CLASS(Namespace = "CoreTests")
    class CppParameterHolder : public engine::GameBehaviour
    {
    public:
        HX_FIELD() rendering::ClampedFloatParameter strength{1.0f, 0.0f, 10.0f};
        HX_FIELD() rendering::ColorParameter tint{Color(1, 1, 1, 1)};
        HX_FIELD() rendering::LayerMaskParameter mask{engine::LayerMask(1)};
        HX_FIELD() rendering::TextureParameter texture;
        HX_FIELD() CppTestModeParameter mode{CppTestMode::First};
    };
}
