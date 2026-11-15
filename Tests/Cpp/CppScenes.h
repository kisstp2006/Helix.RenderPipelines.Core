#pragma once

#include <Cpp/CppPost.h>

#include <Helix/Core/Strings/Format.h>
#include <Helix/Engine/GameObject/Lifecycle.h>
#include <Helix/Engine/Rendering/Graphics.h>
#include <Helix/Engine/Rendering/Light.h>
#include <Helix/Engine/Rendering/Pipeline/CommandBuffer.h>
#include <Helix/Engine/Rendering/Pipeline/RenderPipeline.h>
#include <Helix/Engine/Rendering/Pipeline/ScriptableRenderContext.h>
#include <Helix/Engine/Script/ScriptableObject.h>
#include <Helix/Engine/Texture/Texture.h>
#include <Helix/RenderPipelines/Core/RenderGraph/RenderGraph.h>
#include <Helix/RenderPipelines/Core/Textures/RTHandle.h>

// The C# tests' scenes and pipeline in C++ (the test program's game module): the blits, the shader library, and the
// core's whole stack as a C++ pipeline on the C++ render graph drawing the engine's golden scene.
namespace helix::rpcore::testing
{
    // What the C++ scenes and pipeline note (the C# scripts' Notes, in C++).
    inline Array<String>& CppNotes()
    {
        static Array<String> notes;
        return notes;
    }

    // CsBlitTests: a 2x2 texture into the screen straight, through a render texture handle, into the top right
    // quarter, and with the color and depth blit; a mode each update.
    HX_CLASS(Namespace = "CoreTests")
    class CppBlitTests : public engine::GameBehaviour
    {
        void Start()
        {
            using namespace engine;
            rendering::Blitter::Initialize(Shader::Find("Hidden/Core/Blit"), Shader::Find("Hidden/Core/BlitColorAndDepth"));
            texture_ = Texture2D::Create(2, 2, TextureFormat::RGBA32, false, true);
            texture_->SetFilterMode(FilterMode::Point);
            const Color32 pixels[] = {Color32(255, 0, 0, 255), Color32(0, 255, 0, 255), Color32(0, 0, 255, 255), Color32(255, 255, 255, 255)};
            texture_->SetPixels32(Span<const Color32>(pixels, 4));
            texture_->Apply(false);
            source_ = rendering::RTHandles::DefaultInstance().Alloc(texture_);
            rendering::RTHandleAllocInfo info;
            info.format = GraphicsFormat::R8G8B8A8_UNorm;
            info.name = "BlitTarget";
            target_ = rendering::RTHandles::DefaultInstance().Alloc(4, 4, info);
        }

        void Update()
        {
            using namespace engine;
            using rendering::Blitter;
            using rendering::ClearFlag;
            using rendering::CoreUtils;
            mode_++;
            CommandBuffer cmd;
            cmd.SetName("Blits");
            RenderTargetIdentifier screen(BuiltinRenderTextureType::CameraTarget);
            const Color black(0, 0, 0, 1);
            if (mode_ == 0)
            {
                CoreUtils::SetRenderTarget(cmd, screen, ClearFlag::All, black);
                Blitter::BlitTexture(cmd, *source_, Vector4(1, 1, 0, 0), 0, false);
            }
            else if (mode_ == 1)
            {
                Blitter::BlitCameraTexture(cmd, *source_, *target_);
                CoreUtils::SetRenderTarget(cmd, screen, ClearFlag::All, black);
                Blitter::BlitTexture(cmd, *target_, Vector4(1, 1, 0, 0), 0, true);
            }
            else if (mode_ == 2)
            {
                CoreUtils::SetRenderTarget(cmd, screen, ClearFlag::All, black);
                Blitter::BlitQuad(cmd, texture_, Vector4(1, 1, 0, 0), Vector4(0.5f, 0.5f, 0.5f, 0.5f), 0, false);
            }
            else if (mode_ == 3)
            {
                CoreUtils::SetRenderTarget(cmd, screen, ClearFlag::All, black);
                Blitter::BlitColorAndDepth(cmd, texture_, nullptr, Vector4(1, 1, 0, 0), 0, false);
            }
            Graphics::ExecuteCommandBuffer(cmd);
        }

        void OnDestroy()
        {
            if (target_)
                target_->Release();
            if (source_)
                source_->Release();
            rendering::Blitter::Cleanup();
            if (texture_)
                engine::DestroyImmediate(texture_);
        }

        engine::Texture2D* texture_ = nullptr;
        rendering::RTHandle* source_ = nullptr;
        rendering::RTHandle* target_ = nullptr;
        i32 mode_ = -1;
    };

    // CsLibraryScene: a quad drawn with the shader library's checks, white where every function is right; then instanced.
    HX_CLASS(Namespace = "CoreTests")
    class CppLibraryScene : public engine::GameBehaviour
    {
        void Start()
        {
            using namespace engine;
            GameObject* eye = GameObject::Create("LibraryEye");
            auto* cam = eye->AddComponent<Camera>();
            eye->transform()->SetPosition(Vector3(0, 0, -10));
            cam->SetOrthographic(true);
            cam->SetOrthographicSize(2);
            material_ = Material::Create(Shader::Find("Tests/Core/Library"));
            material_->SetEnableInstancing(true);
            mesh_ = Quad();
            CppNotes().Push(Format("library:{}:{}", material_->shader()->isSupported() ? "True" : "False", material_->shader()->passCount()));
        }

        void Update()
        {
            using namespace engine;
            RenderParams params;
            params.material = material_;
            Graphics::RenderMesh(params, mesh_, 0, Matrix4x4::TRS(Vector3(-1, 0, 0), Quaternion::identity, Vector3(0.75f, 0.75f, 1)));
            const Matrix4x4 instances[] = {Matrix4x4::TRS(Vector3(1, 1, 0), Quaternion::identity, Vector3(0.75f, 0.75f, 1)),
                                           Matrix4x4::TRS(Vector3(1, -1, 0), Quaternion::identity, Vector3(0.75f, 0.75f, 1))};
            Graphics::RenderMeshInstanced(params, mesh_, 0, Span<const Matrix4x4>(instances, 2));
        }

        void OnDestroy()
        {
            engine::DestroyImmediate(material_);
            engine::DestroyImmediate(mesh_);
        }

    public:
        // A quad of side 2 around the origin facing -z, with uvs.
        static engine::Mesh* Quad()
        {
            engine::Mesh* mesh = engine::Mesh::Create();
            const Vector3 vertices[] = {Vector3(-1, -1, 0), Vector3(-1, 1, 0), Vector3(1, 1, 0), Vector3(1, -1, 0)};
            const Vector2 uvs[] = {Vector2(0, 0), Vector2(0, 1), Vector2(1, 1), Vector2(1, 0)};
            const i32 triangles[] = {0, 1, 2, 0, 2, 3};
            mesh->SetVertices(Span<const Vector3>(vertices, 4));
            mesh->SetUv1(Span<const Vector2>(uvs, 4));
            mesh->SetTriangles(Span<const i32>(triangles, 6));
            mesh->RecalculateNormals();
            mesh->RecalculateBounds();
            return mesh;
        }

    private:
        engine::Material* material_ = nullptr;
        engine::Mesh* mesh_ = nullptr;
    };

    // CsCorePipeline on the C++ graph: per camera the volumes blended for it, opaque then transparent renderer lists
    // into graph textures (one render pass), the post process components after them, the result blitted into the
    // camera's target. Native render passes when native.
    class CppCorePipeline final : public engine::RenderPipeline
    {
    public:
        static inline bool native = false;

        CppCorePipeline()
        {
            rendering::Blitter::Initialize(engine::Shader::Find("Hidden/Core/Blit"), engine::Shader::Find("Hidden/Core/BlitColorAndDepth"));
            if (!rendering::VolumeManager::instance().isInitialized())
                rendering::VolumeManager::instance().Initialize();
            stack_ = rendering::VolumeManager::instance().CreateStack();
        }

        void Render(engine::ScriptableRenderContext& context, Span<engine::Camera* const> cameras) override
        {
            using namespace engine;
            using namespace rendering;
            for (Camera* camera : cameras)
            {
                ScriptableCullingParameters parameters;
                if (!camera->TryGetCullingParameters(parameters))
                    continue;
                CullingResults culling = context.Cull(parameters);
                context.SetupCameraProperties(camera);
                RTHandles::SetReferenceSize(camera->pixelWidth(), camera->pixelHeight());
                VolumeManager::instance().Update(stack_, camera->transform(), LayerMask(i32(camera->cullingMask())));
                CommandBuffer cmd;
                cmd.SetName("Core");
                graph_.nativeRenderPassesEnabled = native;
                RenderGraphParameters rp;
                rp.commandBuffer = &cmd;
                rp.scriptableRenderContext = &context;
                rp.executionName = String(camera->Name());
                if (!graph_.BeginRecording(rp))
                    continue;
                RenderTargetInfo info;
                info.width = camera->pixelWidth();
                info.height = camera->pixelHeight();
                TextureHandle backbuffer = graph_.ImportBackbuffer(RenderTargetIdentifier(BuiltinRenderTextureType::CameraTarget), info);
                TextureDesc colorDesc(Vector2(1, 1));
                colorDesc.colorFormat = GraphicsFormat::R8G8B8A8_UNorm;
                colorDesc.clearBuffer = true;
                colorDesc.clearColor = camera->backgroundColor();
                colorDesc.name = String("CameraColor");
                TextureHandle color = graph_.CreateTexture(colorDesc);
                TextureDesc depthDesc(Vector2(1, 1));
                depthDesc.colorFormat = GraphicsFormat::None;
                depthDesc.depthBufferBits = DepthBits::Depth32;
                depthDesc.clearBuffer = true;
                depthDesc.name = String("CameraDepth");
                TextureHandle depth = graph_.CreateTexture(depthDesc);
                for (bool opaque : {true, false})
                {
                    RendererUtils::RendererListDesc desc;
                    desc.passNames.Push(ShaderTagId("SRPDefaultUnlit"));
                    desc.passNames.Push(ShaderTagId("ForwardBase"));
                    desc.cullingResults = culling;
                    desc.camera = camera;
                    desc.sortingCriteria = opaque ? SortingCriteria::CommonOpaque : SortingCriteria::CommonTransparent;
                    desc.renderQueueRange = opaque ? RenderQueueRange::opaque() : RenderQueueRange::transparent();
                    Draw(opaque ? "Opaque" : "Transparent", graph_.CreateRendererList(desc), color, depth);
                }
                TextureHandle result = post_.Record(graph_, stack_, PostProcessInjectionPoint::AfterOpaqueAndSky, color);
                CopyData* data = nullptr;
                PassBuilder<CopyData> builder = graph_.AddRasterRenderPass("FinalBlit", data);
                data->source = result;
                data->graph = &graph_;
                builder.UseTexture(result);
                builder.SetRenderAttachment(backbuffer, 0, AccessFlags::WriteAll);
                builder.SetRenderFunc([](CopyData& d, RasterGraphContext& ctx) {
                    if (RTHandle* source = d.graph->Resolve(d.source))
                        Blitter::BlitTexture(*ctx.cmd, *source, Vector4(1, 1, 0, 0), 0, false);
                });
                builder.Dispose();
                graph_.EndRecordingAndExecute();
                context.ExecuteCommandBuffer(cmd);
                context.Submit();
                const RenderGraphStatistics& s = graph_.statistics();
                CppNotes().Push(Format("core:{}:{}:{}:{}:{}:{}", native ? "True" : "False", s.passes, s.culledPasses, s.renderPasses, s.mergedPasses, s.textures));
                graph_.EndFrame();
            }
        }

    protected:
        void Dispose(bool) override
        {
            post_.Dispose();
            graph_.Cleanup();
            rendering::VolumeManager::instance().DestroyStack(stack_);
            rendering::VolumeManager::instance().Deinitialize();
            rendering::Blitter::Cleanup();
        }

    private:
        struct ListData
        {
            rendering::RendererListHandle list;
            rendering::RenderGraph* graph = nullptr;
        };

        struct CopyData
        {
            rendering::TextureHandle source;
            rendering::RenderGraph* graph = nullptr;
        };

        void Draw(StringView name, rendering::RendererListHandle list, rendering::TextureHandle color, rendering::TextureHandle depth)
        {
            using namespace rendering;
            ListData* data = nullptr;
            PassBuilder<ListData> builder = graph_.AddRasterRenderPass(name, data);
            data->list = list;
            data->graph = &graph_;
            builder.SetRenderAttachment(color, 0);
            builder.SetRenderAttachmentDepth(depth);
            builder.UseRendererList(list);
            builder.SetRenderFunc([](ListData& d, RasterGraphContext& ctx) { ctx.cmd->DrawRendererList(d.graph->ResolveList(d.list)); });
            builder.Dispose();
        }

        rendering::RenderGraph graph_{"CoreGraph"};
        rendering::PostProcessPasses post_;
        rendering::VolumeStack* stack_ = nullptr;
    };

    // Its asset: the engine calls CreatePipeline.
    HX_CLASS(Namespace = "CoreTests")
    class CppCorePipelineAsset : public engine::ScriptableObject
    {
    protected:
        engine::RenderPipeline* CreatePipeline() { return new CppCorePipeline(); }
    };

    HX_CLASS(Namespace = "CoreTests")
    class CppCoreDrawer : public engine::GameBehaviour
    {
    public:
        engine::Mesh* mesh = nullptr;
        engine::Material* material = nullptr;

    private:
        void Update()
        {
            engine::RenderParams params;
            params.material = material;
            engine::Graphics::RenderMesh(params, mesh, 0, Matrix4x4::TRS(Vector3(-2, 0, 0), Quaternion::identity, Vector3(1, 1, 1)));
        }
    };

    // CsCoreScene with CppTintPost: a camera, a light, a lit quad and a drawn textured mesh; and a global volume tinting
    // it, weighing nothing in the first frame.
    HX_CLASS(Namespace = "CoreTests")
    class CppCoreScene : public engine::GameBehaviour
    {
        void Start()
        {
            using namespace engine;
            GameObject* eye = GameObject::Create("CoreEye");
            auto* cam = eye->AddComponent<Camera>();
            eye->transform()->SetPosition(Vector3(0, 0, -10));
            cam->SetOrthographic(true);
            cam->SetOrthographicSize(4);
            cam->SetClearFlags(CameraClearFlags::SolidColor);
            cam->SetBackgroundColor(Color(0, 0, 0.25f, 1));

            GameObject* sun = GameObject::Create("CoreSun");
            auto* light = sun->AddComponent<Light>();
            light->SetType(LightType::Directional);
            light->SetIntensity(0.5f);

            Mesh* quad = CppLibraryScene::Quad();
            Material* lit = Material::Create(Shader::Find("Tests/Lambert"));
            lit->SetColor(Color(1, 0.5f, 0, 1));
            GameObject* shape = GameObject::Create("CoreShape");
            shape->AddComponent<MeshFilter>()->SetSharedMesh(quad);
            shape->AddComponent<MeshRenderer>()->SetSharedMaterial(lit);
            shape->transform()->SetPosition(Vector3(2, 0, 0));

            Texture2D* tex = Texture2D::Create(2, 2, TextureFormat::RGBA32, false, true);
            const Color32 pixels[] = {Color32(255, 255, 0, 255), Color32(0, 255, 0, 255), Color32(0, 0, 255, 255), Color32(255, 255, 255, 255)};
            tex->SetPixels32(Span<const Color32>(pixels, 4));
            tex->SetFilterMode(FilterMode::Point);
            tex->Apply(false);
            Material* textured = Material::Create(Shader::Find("Tests/Textured"));
            textured->SetMainTexture(tex);
            auto* drawer = static_cast<CppCoreDrawer*>(gameObject()->AddComponent(scripting::FindType("CoreTests.CppCoreDrawer")));
            drawer->mesh = quad;
            drawer->material = textured;

            auto* profile = static_cast<rendering::VolumeProfile*>(ScriptableObject::CreateInstance(scripting::FindType("HelixEngine.Rendering.VolumeProfile")));
            static_cast<CppTintPost*>(profile->Add(scripting::FindType("CoreTests.CppTintPost")))->tint.Override(Color(1, 0.5f, 0.25f, 1));
            volume_ = static_cast<rendering::Volume*>(GameObject::Create("CoreVolume")->AddComponent(scripting::FindType("HelixEngine.Rendering.Volume")));
            volume_->SetSharedProfile(profile);
            volume_->weight = 0;
        }

        // Frame 1: no post; frame 2: tinted; frame 3: tinted with native render passes.
        void Update()
        {
            frame_++;
            volume_->weight = frame_ >= 2 ? 1.0f : 0.0f;
            CppCorePipeline::native = frame_ >= 3;
        }

        rendering::Volume* volume_ = nullptr;
        i32 frame_ = 0;
    };
}
