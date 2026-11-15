#pragma once

#include <Helix/RenderPipelines/Core/PostProcessing/PostProcessing.h>

// What C#'s post processing calls (internal): C#'s Render of components of C# classes, a C++ component's Render, and
// the passes by id.
namespace helix::rendering
{
    HX_CLASS(Namespace = "HelixEngine.Rendering.Internal", Static, Internal)
    class PostNative
    {
    public:
        // An unmanaged function of (GC handle of the component's C# object, command buffer id, source handle id).
        HX_FUNCTION() static void SetManagedRender(u64 render);
        // A C++ component's Render into the command buffer of that id.
        HX_FUNCTION() static void Render(PostProcessVolumeComponent* component, u64 commandBuffer, u64 source);
        HX_FUNCTION() static void CleanupIfSetUp(PostProcessVolumeComponent* component)
        {
            if (component)
                component->CleanupIfSetUp();
        }

        HX_FUNCTION() static u64 CreatePasses() { return (new PostProcessPasses())->Id(); }
        HX_FUNCTION() static void DestroyPasses(u64 passes) { delete PostProcessPasses::Find(passes); }
        // The order by native type ids.
        HX_FUNCTION() static void SetOrder(u64 passes, Span<const u32> order);
        // The active components of a point, kept for ActiveAt.
        HX_FUNCTION() static i32 ActiveComponents(u64 passes, u64 stack, i32 point);
        HX_FUNCTION() static PostProcessVolumeComponent* ActiveAt(i32 index);
        // The result's packed handle (the graph's error when it could not record).
        HX_FUNCTION() static u64 Record(u64 passes, u64 graph, u64 stack, i32 point, u64 color);
        HX_FUNCTION() static void Dispose(u64 passes)
        {
            if (PostProcessPasses* p = PostProcessPasses::Find(passes))
                p->Dispose();
        }
    };
}
