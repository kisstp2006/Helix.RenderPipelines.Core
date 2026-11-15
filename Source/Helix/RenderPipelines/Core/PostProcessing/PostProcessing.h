#pragma once

#include <Helix/RenderPipelines/Core/RenderGraph/RenderGraph.h>
#include <Helix/RenderPipelines/Core/Volume/VolumeComponent.h>

// Effects driven by volume components: the blended component of a stack renders its effect at its injection point, and
// the passes record the active ones of a point into a render graph.
namespace helix::rendering
{
    // Where in a pipeline's frame post process components run.
    HX_ENUM(Namespace = "HelixEngine.Rendering")
    enum class PostProcessInjectionPoint : i32
    {
        AfterOpaqueAndSky,
        BeforePostProcess,
        AfterPostProcess,
    };

    // A volume component of an effect: active when its parameters make it do something. Abstract: C++ and C# effects
    // derive from it.
    HX_CLASS(Namespace = "HelixEngine.Rendering")
    class PostProcessVolumeComponent : public VolumeComponent
    {
    public:
        PostProcessVolumeComponent() = default;
        // A copy is not set up.
        PostProcessVolumeComponent(const PostProcessVolumeComponent& other) : VolumeComponent(other) {}

        HX_FUNCTION() virtual bool IsActive() const = 0;
        HX_PROPERTY() virtual PostProcessInjectionPoint injectionPoint() const { return PostProcessInjectionPoint::AfterPostProcess; }

        // Once before the first Render (materials, look up tables).
        HX_FUNCTION() virtual void Setup() {}
        // The effect from source into the bound target (a full screen draw). For an object of a C# class: its C#
        // Render, with the raster command buffer of the C# render graph executing the frame.
        virtual void Render(CommandBuffer& cmd, RTHandle* source);
        // What Setup made goes.
        HX_FUNCTION() virtual void Cleanup() {}

        bool isSetUp() const { return setUp_; }
        void EnsureSetup();
        void CleanupIfSetUp();

        // C#'s Render: an unmanaged function of (the GC handle of the component's C# object, the command buffer's id,
        // the source handle's id).
        static void SetManagedRender(void (*render)(u64 wrapper, u64 commandBuffer, u64 source));

    private:
        void OnDisable() { CleanupIfSetUp(); }

        bool setUp_ = false;
    };

    // The post process components of a stack recorded into a render graph: at a point, each active one in order as a
    // raster pass reading the previous result, alternating between two textures of the color's descriptor.
    class PostProcessPasses
    {
    public:
        PostProcessPasses();
        ~PostProcessPasses();
        PostProcessPasses(const PostProcessPasses&) = delete;
        PostProcessPasses& operator=(const PostProcessPasses&) = delete;

        static PostProcessPasses* Find(u64 id);
        u64 Id() const { return id_; }

        // The component types in the order they run (types not listed run after them, by full name).
        Array<scripting::TypeId>& order() { return order_; }

        // The components of a point that run, in order.
        void ActiveComponents(const VolumeStack* stack, PostProcessInjectionPoint point, Array<PostProcessVolumeComponent*>& out) const;
        // The passes of a point; the texture holding the result (color itself when no component runs).
        TextureHandle Record(RenderGraph& graph, const VolumeStack* stack, PostProcessInjectionPoint point, TextureHandle color);
        // The components it set up are cleaned up.
        void Dispose();

    private:
        Array<scripting::TypeId> order_ = Array<scripting::TypeId>();
        Array<engine::EntityId> setUp_ = Array<engine::EntityId>();
        u64 id_ = 0;
    };
}
