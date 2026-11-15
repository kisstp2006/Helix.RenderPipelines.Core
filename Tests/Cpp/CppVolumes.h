#pragma once

#include <Helix/RenderPipelines/Core/Volume/Volume.h>

// C++ volume components of the test program's game module (RenderPipelinesCoreTests): the C# volume test's
// components, their parameters members in order.
namespace helix::rpcore::testing
{
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
        }

        rendering::ClampedFloatParameter intensity{1.0f, 0.0f, 20.0f};
        rendering::ColorParameter tint{Color(1, 1, 1, 1)};
        rendering::BoolParameter flag{false};
        rendering::NoInterpFloatParameter steps{2.0f};
        rendering::FloatParameter untouched{5.0f};
    };

    HX_CLASS(Namespace = "CoreTests")
    class CppOtherVolume : public rendering::VolumeComponent
    {
    public:
        CppOtherVolume() { AddParameter(count); }

        rendering::IntParameter count{3};
    };
}
