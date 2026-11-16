#pragma once

#include <Helix/Core/Strings/String.h>
#include <Helix/Engine/Rendering/Pipeline/CommandBuffer.h>

// A named sample of a frame's commands: what Begin and End put around them in a command buffer.
namespace helix::rendering
{
    class ProfilingSampler
    {
    public:
        explicit ProfilingSampler(StringView name) : name_(name) {}

        StringView name() const { return name_; }
        void Begin(engine::CommandBuffer& cmd) const { cmd.BeginSample(name_); }
        void End(engine::CommandBuffer& cmd) const { cmd.EndSample(name_); }

    private:
        String name_;
    };
}
