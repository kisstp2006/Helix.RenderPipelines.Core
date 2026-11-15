#include <Support/CoreHost.h>

#include <Helix/RenderPipelines/Core/RenderGraph/RenderGraph.h>
#include <Helix/Testing/Test.h>

using namespace helix;
using namespace helix::rendering;
using namespace helix::rpcore::testing;

namespace
{
    struct DrawData
    {
        TextureHandle source;
        String* log = nullptr;
    };
}

HX_TEST(Cpp, RenderGraphCullsMergesAndAliases)
{
    // The C# graph test's passes in C++: two passes into the same attachments (merged), one nothing reads (culled),
    // one that may not be culled, one sharing its texture (aliased) and publishing it, one into the backbuffer that
    // reads the color, and an unsafe pass after it.
    HX_REQUIRE(StartScripts());
    RTHandles::Initialize(64, 32);
    RTHandles::SetReferenceSize(64, 32);
    String log;
    engine::CommandBuffer cmd;
    {
        RenderGraph graph("CppGraph");
        for (i32 frame = 0; frame < 2; frame++)
        {
            log.Clear();
            RenderGraphParameters parameters;
            parameters.commandBuffer = &cmd;
            HX_REQUIRE(graph.BeginRecording(parameters));
            RenderTargetInfo info;
            info.width = 64, info.height = 32, info.format = GraphicsFormat::R8G8B8A8_UNorm;
            TextureHandle backbuffer = graph.ImportBackbuffer(RenderTargetIdentifier(engine::BuiltinRenderTextureType::CameraTarget), info);
            TextureDesc colorDesc(64, 32);
            colorDesc.colorFormat = GraphicsFormat::R8G8B8A8_UNorm, colorDesc.clearBuffer = true, colorDesc.name = "Color";
            TextureHandle color = graph.CreateTexture(colorDesc);
            TextureDesc depthDesc(Vector2(1, 1));
            depthDesc.colorFormat = GraphicsFormat::None, depthDesc.depthBufferBits = DepthBits::Depth32, depthDesc.clearBuffer = true, depthDesc.name = "Depth";
            TextureHandle depth = graph.CreateTexture(depthDesc);
            HX_CHECK(graph.GetTextureDesc(depth).Size(Vector2Int(64, 32)) == Vector2Int(64, 32));

            auto note = [](DrawData& d, RasterGraphContext&) { *d.log += "r"; };
            DrawData* data = nullptr;
            {
                auto b = graph.AddRasterRenderPass("Opaque", data);
                data->log = &log;
                b.SetRenderAttachment(color, 0);
                b.SetRenderAttachmentDepth(depth);
                b.SetRenderFunc([&graph, color](DrawData& d, RasterGraphContext& ctx) {
                    RTHandle* h = graph.Resolve(color);
                    *d.log += h && h->rt() && h->rt()->width() == 64 && ctx.cmd ? "O" : "o";
                });
            }
            {
                auto b = graph.AddRasterRenderPass("Tint", data);
                data->log = &log;
                b.SetRenderAttachment(color, 0);
                b.SetRenderAttachmentDepth(depth);
                b.SetRenderFunc([](DrawData& d, RasterGraphContext&) { *d.log += "T"; });
            }
            TextureDesc smallDesc(8, 8);
            smallDesc.colorFormat = GraphicsFormat::R8G8B8A8_UNorm, smallDesc.clearBuffer = true, smallDesc.name = "Small";
            {
                auto b = graph.AddRasterRenderPass("Unused", data);
                data->log = &log;
                b.SetRenderAttachment(graph.CreateTexture(smallDesc), 0);
                b.SetRenderFunc([](DrawData& d, RasterGraphContext&) { *d.log += "U"; });
            }
            {
                auto b = graph.AddRasterRenderPass("Kept", data);
                data->log = &log;
                b.SetRenderAttachment(graph.CreateTexture(smallDesc), 0);
                b.AllowPassCulling(false);
                b.SetRenderFunc([](DrawData& d, RasterGraphContext&) { *d.log += "K"; });
            }
            TextureHandle late = graph.CreateTexture(smallDesc);
            {
                auto b = graph.AddRasterRenderPass("Late", data);
                data->log = &log;
                b.SetRenderAttachment(late, 0, AccessFlags::WriteAll);
                b.SetGlobalTextureAfterPass(late, engine::PropertyToID("_LateTexture"));
                b.SetRenderFunc([](DrawData& d, RasterGraphContext&) { *d.log += "L"; });
            }
            {
                auto b = graph.AddRasterRenderPass("ToScreen", data);
                data->log = &log;
                data->source = color;
                b.UseTexture(color);
                b.SetRenderAttachment(backbuffer, 0, AccessFlags::WriteAll);
                // A texture set as an attachment can not also be used as one.
                b.UseTexture(backbuffer);
                HX_CHECK(!b.error().Empty());
                b.SetRenderFunc(note);
            }
            {
                auto b = graph.AddUnsafePass("Corner", data);
                data->log = &log;
                b.UseGlobalTexture(engine::PropertyToID("_LateTexture"));
                b.UseTexture(backbuffer, AccessFlags::Write);
                b.SetRenderFunc([](DrawData& d, UnsafeGraphContext& ctx) { *d.log += ctx.defaultResources ? "C" : "c"; });
            }
            HX_CHECK(graph.EndRecordingAndExecute());
            const RenderGraphStatistics& s = graph.statistics();
            HX_CHECK(log == StringView("OTKLrC"));
            HX_CHECK(s.passes == 7 && s.culledPasses == 1 && s.renderPasses == 4 && s.mergedPasses == 1 && s.textures == 4);
            // The first frame makes three pooled textures; the second takes them all again.
            HX_CHECK(s.pooledTexturesCreated == (frame == 0 ? 3 : 0) && s.aliasedTextures == 1 && s.pooledTextures == 3);
            if (!(s.passes == 7 && s.culledPasses == 1 && s.renderPasses == 4 && s.mergedPasses == 1 && s.textures == 4 && s.aliasedTextures == 1))
                std::printf("    frame %d: %.*s %d %d %d %d %d %d %d %d\n", frame, int(log.Size()), log.Data(), s.passes, s.culledPasses, s.renderPasses, s.mergedPasses, s.textures,
                            s.pooledTexturesCreated, s.aliasedTextures, s.pooledTextures);
            // Handles of an executed frame resolve to nothing.
            HX_CHECK(graph.Resolve(color) == nullptr);
            graph.EndFrame();
            cmd.Clear();
        }

        // Misuse reports an error instead of recording.
        RenderGraphParameters none;
        HX_CHECK(!graph.BeginRecording(none) && !graph.error().Empty());
        RenderGraphParameters parameters;
        parameters.commandBuffer = &cmd;
        HX_REQUIRE(graph.BeginRecording(parameters));
        DrawData* data = nullptr;
        {
            auto b = graph.AddRasterRenderPass("NoFunction", data);
            b.AllowPassCulling(false);
        }
        HX_CHECK(!graph.error().Empty());
        HX_CHECK(graph.EndRecordingAndExecute());
        HX_CHECK(graph.statistics().passes == 0);
        graph.Cleanup();
    }
    engine::testing::Cleanup();
}
