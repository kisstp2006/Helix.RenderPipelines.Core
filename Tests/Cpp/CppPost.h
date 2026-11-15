#pragma once

#include <Helix/Engine/GameObject/GameObject.h>
#include <Helix/Engine/Material/Material.h>
#include <Helix/Engine/Mesh/Mesh.h>
#include <Helix/Engine/Rendering/Camera.h>
#include <Helix/Engine/Rendering/Renderer.h>
#include <Helix/Engine/Script/GameBehaviour.h>
#include <Helix/Engine/Shader/Shader.h>
#include <Helix/Engine/Transform/Transform.h>
#include <Helix/RenderPipelines/Core/PostProcessing/PostProcessing.h>
#include <Helix/RenderPipelines/Core/Utilities/Blitter.h>
#include <Helix/RenderPipelines/Core/Utilities/CoreUtils.h>
#include <Helix/RenderPipelines/Core/Volume/Volume.h>

// The C# post test's effects and scene in C++ (the test program's game module): a tint by a color and a mirror with the
// core's blit, run by the C# pipeline's passes. The core's types by name: the test program reaches the module's
// registry, not its TypeOf.
namespace helix::rpcore::testing
{
    // A tint by a color (Tests/Core/Tint): active when the color is not white.
    HX_CLASS(Namespace = "CoreTests")
    class CppTintPost : public rendering::PostProcessVolumeComponent
    {
    public:
        CppTintPost() { AddParameter(tint); }
        CppTintPost(const CppTintPost& other) : PostProcessVolumeComponent(other), tint(other.tint) {}

        rendering::ColorParameter tint{Color(1, 1, 1, 1)};

        bool IsActive() const override { return !(tint.value() == Color(1, 1, 1, 1)); }
        rendering::PostProcessInjectionPoint injectionPoint() const override { return rendering::PostProcessInjectionPoint::AfterOpaqueAndSky; }

        void Setup() override { material_ = rendering::CoreUtils::CreateEngineMaterial("Tests/Core/Tint"); }

        void Render(engine::CommandBuffer& cmd, rendering::RTHandle* source) override
        {
            block_.SetColor(engine::Shader::PropertyToID("_TintColor"), tint.value());
            block_.SetVector(engine::Shader::PropertyToID("_BlitScaleBias"), Vector4(1, 1, 0, 0));
            if (source)
                cmd.SetGlobalTexture(engine::Shader::PropertyToID("_BlitTexture"), *source);
            cmd.DrawProcedural(Matrix4x4::identity, material_, 0, engine::MeshTopology::Triangles, 3, 1, &block_);
        }

        void Cleanup() override
        {
            rendering::CoreUtils::Destroy(material_);
            material_ = nullptr;
        }

    private:
        engine::Material* material_ = nullptr;
        engine::MaterialPropertyBlock block_;
    };

    // A mirror left to right with the core's blit: active when mirror is on.
    HX_CLASS(Namespace = "CoreTests")
    class CppMirrorPost : public rendering::PostProcessVolumeComponent
    {
    public:
        CppMirrorPost() { AddParameter(mirror); }
        CppMirrorPost(const CppMirrorPost& other) : PostProcessVolumeComponent(other), mirror(other.mirror) {}

        rendering::BoolParameter mirror{false};

        bool IsActive() const override { return mirror.value(); }
        rendering::PostProcessInjectionPoint injectionPoint() const override { return rendering::PostProcessInjectionPoint::AfterOpaqueAndSky; }

        void Render(engine::CommandBuffer& cmd, rendering::RTHandle* source) override
        {
            if (source)
                rendering::Blitter::BlitTexture(cmd, *source, Vector4(-1, 1, 1, 0), 0, false);
        }
    };

    // CsPostScene with the C++ effects: a red quad on the left and a global volume tinting and mirroring it; the second
    // frame the volume weighs nothing.
    HX_CLASS(Namespace = "CoreTests")
    class CppPostScene : public engine::GameBehaviour
    {
        void Start()
        {
            using namespace engine;
            rendering::Blitter::Initialize(Shader::Find("Hidden/Core/Blit"), Shader::Find("Hidden/Core/BlitColorAndDepth"));
            rendering::VolumeManager::instance().Initialize();
            GameObject* eye = GameObject::Create("PostEye");
            auto* cam = eye->AddComponent<Camera>();
            eye->transform()->SetPosition(Vector3(0, 0, -10));
            cam->SetOrthographic(true);
            cam->SetOrthographicSize(2);
            Material* red = Material::Create(Shader::Find("Tests/Unlit"));
            red->SetColor("color", Color(1, 0, 0, 1));
            Mesh* mesh = Mesh::Create();
            const Vector3 vertices[] = {Vector3(-1, -1, 0), Vector3(-1, 1, 0), Vector3(1, 1, 0), Vector3(1, -1, 0)};
            const i32 triangles[] = {0, 1, 2, 0, 2, 3};
            mesh->SetVertices(Span<const Vector3>(vertices, 4));
            mesh->SetTriangles(Span<const i32>(triangles, 6));
            mesh->RecalculateBounds();
            GameObject* shape = GameObject::Create("PostShape");
            shape->AddComponent<MeshFilter>()->SetSharedMesh(mesh);
            shape->AddComponent<MeshRenderer>()->SetSharedMaterial(red);
            shape->transform()->SetPosition(Vector3(-1, 0, 0));
            shape->transform()->SetLocalScale(Vector3(0.75f, 0.75f, 1));
            auto* profile = static_cast<rendering::VolumeProfile*>(ScriptableObject::CreateInstance(scripting::FindType("HelixEngine.Rendering.VolumeProfile")));
            static_cast<CppTintPost*>(profile->Add(scripting::FindType("CoreTests.CppTintPost")))->tint.Override(Color(0.5f, 1, 1, 1));
            static_cast<CppMirrorPost*>(profile->Add(scripting::FindType("CoreTests.CppMirrorPost")))->mirror.Override(true);
            volume_ = static_cast<rendering::Volume*>(GameObject::Create("PostVolume")->AddComponent(scripting::FindType("HelixEngine.Rendering.Volume")));
            volume_->SetSharedProfile(profile);
        }

        void Update()
        {
            if (frame_++ == 1)
                volume_->weight = 0;
        }

        void OnDestroy()
        {
            rendering::Blitter::Cleanup();
            rendering::VolumeManager::instance().Deinitialize();
        }

        rendering::Volume* volume_ = nullptr;
        i32 frame_ = 0;
    };
}
