using System.Collections.Generic;
using HelixEngine.Rendering.RendererUtils;

namespace HelixEngine.Rendering
{
    /// <summary>The commands every kind of pass may record: globals, keywords, samples. The graph's command buffer underneath.</summary>
    public class BaseCommandBuffer
    {
        protected internal CommandBuffer m_WrappedCommandBuffer;

        internal BaseCommandBuffer(CommandBuffer wrapped) => m_WrappedCommandBuffer = wrapped;

        public string name => m_WrappedCommandBuffer.name;
        public int sizeInBytes => m_WrappedCommandBuffer.sizeInBytes;

        public void BeginSample(string name) => m_WrappedCommandBuffer.BeginSample(name);
        public void EndSample(string name) => m_WrappedCommandBuffer.EndSample(name);

        public void SetGlobalFloat(int nameID, float value) => m_WrappedCommandBuffer.SetGlobalFloat(nameID, value);
        public void SetGlobalFloat(string name, float value) => m_WrappedCommandBuffer.SetGlobalFloat(name, value);
        public void SetGlobalInt(int nameID, int value) => m_WrappedCommandBuffer.SetGlobalInt(nameID, value);
        public void SetGlobalInteger(int nameID, int value) => m_WrappedCommandBuffer.SetGlobalInteger(nameID, value);
        public void SetGlobalVector(int nameID, Vector4 value) => m_WrappedCommandBuffer.SetGlobalVector(nameID, value);
        public void SetGlobalVector(string name, Vector4 value) => m_WrappedCommandBuffer.SetGlobalVector(name, value);
        public void SetGlobalColor(int nameID, Color value) => m_WrappedCommandBuffer.SetGlobalColor(nameID, value);
        public void SetGlobalMatrix(int nameID, Matrix4x4 value) => m_WrappedCommandBuffer.SetGlobalMatrix(nameID, value);
        public void SetGlobalFloatArray(int nameID, float[] values) => m_WrappedCommandBuffer.SetGlobalFloatArray(nameID, values);
        public void SetGlobalVectorArray(int nameID, Vector4[] values) => m_WrappedCommandBuffer.SetGlobalVectorArray(nameID, values);
        public void SetGlobalMatrixArray(int nameID, Matrix4x4[] values) => m_WrappedCommandBuffer.SetGlobalMatrixArray(nameID, values);
        public void SetGlobalTexture(int nameID, RenderTargetIdentifier value) => m_WrappedCommandBuffer.SetGlobalTexture(nameID, value);
        public void SetGlobalTexture(string name, RenderTargetIdentifier value) => m_WrappedCommandBuffer.SetGlobalTexture(name, value);
        public void SetGlobalBuffer(int nameID, GraphicsBuffer value) => m_WrappedCommandBuffer.SetGlobalBuffer(nameID, value);
        public void SetGlobalBuffer(string name, GraphicsBuffer value) => m_WrappedCommandBuffer.SetGlobalBuffer(name, value);
        public void SetGlobalBuffer(int nameID, ComputeBuffer value) => m_WrappedCommandBuffer.SetGlobalBuffer(nameID, value);
        public void SetGlobalBuffer(string name, ComputeBuffer value) => m_WrappedCommandBuffer.SetGlobalBuffer(name, value);

        public void EnableShaderKeyword(string keyword) => m_WrappedCommandBuffer.EnableShaderKeyword(keyword);
        public void DisableShaderKeyword(string keyword) => m_WrappedCommandBuffer.DisableShaderKeyword(keyword);
        public void EnableKeyword(in GlobalKeyword keyword) => m_WrappedCommandBuffer.EnableKeyword(keyword);
        public void DisableKeyword(in GlobalKeyword keyword) => m_WrappedCommandBuffer.DisableKeyword(keyword);
        public void SetKeyword(in GlobalKeyword keyword, bool value) => m_WrappedCommandBuffer.SetKeyword(keyword, value);
    }

    /// <summary>The commands of a raster pass: draws into the attachments the graph bound, clears, viewport and matrices.</summary>
    public class RasterCommandBuffer : BaseCommandBuffer
    {
        internal RasterCommandBuffer(CommandBuffer wrapped) : base(wrapped) { }

        public void DrawRendererList(RendererList rendererList) => m_WrappedCommandBuffer.DrawRendererList(rendererList);

        public void DrawMesh(Mesh mesh, Matrix4x4 matrix, Material material, int submeshIndex = 0, int shaderPass = -1, MaterialPropertyBlock properties = null) =>
            m_WrappedCommandBuffer.DrawMesh(mesh, matrix, material, submeshIndex, shaderPass, properties);

        public void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, int shaderPass, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties = null) =>
            m_WrappedCommandBuffer.DrawMeshInstanced(mesh, submeshIndex, material, shaderPass, matrices, count, properties);

        public void DrawRenderer(Renderer renderer, Material material, int submeshIndex = 0, int shaderPass = -1) =>
            m_WrappedCommandBuffer.DrawRenderer(renderer, material, submeshIndex, shaderPass);

        public void DrawProcedural(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int vertexCount, int instanceCount = 1,
                                   MaterialPropertyBlock properties = null) =>
            m_WrappedCommandBuffer.DrawProcedural(matrix, material, shaderPass, topology, vertexCount, instanceCount, properties);

        public void ClearRenderTarget(bool clearDepth, bool clearColor, Color backgroundColor, float depth = 1.0f) =>
            m_WrappedCommandBuffer.ClearRenderTarget(clearDepth, clearColor, backgroundColor, depth);

        public void ClearRenderTarget(RTClearFlags clearFlags, Color backgroundColor, float depth = 1.0f, uint stencil = 0) =>
            m_WrappedCommandBuffer.ClearRenderTarget(clearFlags, backgroundColor, depth, stencil);

        public void SetViewport(Rect pixelRect) => m_WrappedCommandBuffer.SetViewport(pixelRect);
        public void EnableScissorRect(Rect scissor) => m_WrappedCommandBuffer.EnableScissorRect(scissor);
        public void DisableScissorRect() => m_WrappedCommandBuffer.DisableScissorRect();
        public void SetViewProjectionMatrices(Matrix4x4 view, Matrix4x4 proj) => m_WrappedCommandBuffer.SetViewProjectionMatrices(view, proj);
        public void SetInvertCulling(bool invertCulling) => m_WrappedCommandBuffer.SetInvertCulling(invertCulling);
    }

    /// <summary>The commands of a compute pass: values, textures and buffers of compute shaders, dispatches.</summary>
    public class ComputeCommandBuffer : BaseCommandBuffer
    {
        internal ComputeCommandBuffer(CommandBuffer wrapped) : base(wrapped) { }

        public void SetComputeFloatParam(ComputeShader cs, int nameID, float val) => m_WrappedCommandBuffer.SetComputeFloatParam(cs, nameID, val);
        public void SetComputeFloatParam(ComputeShader cs, string name, float val) => m_WrappedCommandBuffer.SetComputeFloatParam(cs, name, val);
        public void SetComputeIntParam(ComputeShader cs, int nameID, int val) => m_WrappedCommandBuffer.SetComputeIntParam(cs, nameID, val);
        public void SetComputeIntParam(ComputeShader cs, string name, int val) => m_WrappedCommandBuffer.SetComputeIntParam(cs, name, val);
        public void SetComputeVectorParam(ComputeShader cs, int nameID, Vector4 val) => m_WrappedCommandBuffer.SetComputeVectorParam(cs, nameID, val);
        public void SetComputeVectorParam(ComputeShader cs, string name, Vector4 val) => m_WrappedCommandBuffer.SetComputeVectorParam(cs, name, val);
        public void SetComputeMatrixParam(ComputeShader cs, int nameID, Matrix4x4 val) => m_WrappedCommandBuffer.SetComputeMatrixParam(cs, nameID, val);
        public void SetComputeMatrixParam(ComputeShader cs, string name, Matrix4x4 val) => m_WrappedCommandBuffer.SetComputeMatrixParam(cs, name, val);
        public void SetComputeFloatParams(ComputeShader cs, int nameID, params float[] values) => m_WrappedCommandBuffer.SetComputeFloatParams(cs, nameID, values);
        public void SetComputeFloatParams(ComputeShader cs, string name, params float[] values) => m_WrappedCommandBuffer.SetComputeFloatParams(cs, name, values);
        public void SetComputeIntParams(ComputeShader cs, int nameID, params int[] values) => m_WrappedCommandBuffer.SetComputeIntParams(cs, nameID, values);
        public void SetComputeIntParams(ComputeShader cs, string name, params int[] values) => m_WrappedCommandBuffer.SetComputeIntParams(cs, name, values);
        public void SetComputeVectorArrayParam(ComputeShader cs, int nameID, Vector4[] values) => m_WrappedCommandBuffer.SetComputeVectorArrayParam(cs, nameID, values);
        public void SetComputeMatrixArrayParam(ComputeShader cs, int nameID, Matrix4x4[] values) => m_WrappedCommandBuffer.SetComputeMatrixArrayParam(cs, nameID, values);
        public void SetComputeTextureParam(ComputeShader cs, int kernelIndex, int nameID, RenderTargetIdentifier rt) =>
            m_WrappedCommandBuffer.SetComputeTextureParam(cs, kernelIndex, nameID, rt);
        public void SetComputeTextureParam(ComputeShader cs, int kernelIndex, string name, RenderTargetIdentifier rt) =>
            m_WrappedCommandBuffer.SetComputeTextureParam(cs, kernelIndex, name, rt);
        public void SetComputeTextureParam(ComputeShader cs, int kernelIndex, int nameID, RenderTargetIdentifier rt, int mipLevel) =>
            m_WrappedCommandBuffer.SetComputeTextureParam(cs, kernelIndex, nameID, rt, mipLevel);
        public void SetComputeBufferParam(ComputeShader cs, int kernelIndex, int nameID, ComputeBuffer buffer) =>
            m_WrappedCommandBuffer.SetComputeBufferParam(cs, kernelIndex, nameID, buffer);
        public void SetComputeBufferParam(ComputeShader cs, int kernelIndex, string name, ComputeBuffer buffer) =>
            m_WrappedCommandBuffer.SetComputeBufferParam(cs, kernelIndex, name, buffer);
        public void SetComputeBufferParam(ComputeShader cs, int kernelIndex, int nameID, GraphicsBuffer buffer) =>
            m_WrappedCommandBuffer.SetComputeBufferParam(cs, kernelIndex, nameID, buffer);
        public void SetComputeBufferParam(ComputeShader cs, int kernelIndex, string name, GraphicsBuffer buffer) =>
            m_WrappedCommandBuffer.SetComputeBufferParam(cs, kernelIndex, name, buffer);
        public void SetComputeBufferParam(ComputeShader cs, int kernelIndex, int nameID, GraphicsBufferHandle bufferHandle) =>
            m_WrappedCommandBuffer.SetComputeBufferParam(cs, kernelIndex, nameID, bufferHandle);
        public void DispatchCompute(ComputeShader cs, int kernelIndex, int threadGroupsX, int threadGroupsY, int threadGroupsZ) =>
            m_WrappedCommandBuffer.DispatchCompute(cs, kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ);
        public void DispatchCompute(ComputeShader cs, int kernelIndex, ComputeBuffer indirectBuffer, uint argsOffset) =>
            m_WrappedCommandBuffer.DispatchCompute(cs, kernelIndex, indirectBuffer, argsOffset);
        public void DispatchCompute(ComputeShader cs, int kernelIndex, GraphicsBuffer indirectBuffer, uint argsOffset) =>
            m_WrappedCommandBuffer.DispatchCompute(cs, kernelIndex, indirectBuffer, argsOffset);
        public void EnableKeyword(ComputeShader cs, in LocalKeyword keyword) => m_WrappedCommandBuffer.EnableKeyword(cs, keyword);
        public void DisableKeyword(ComputeShader cs, in LocalKeyword keyword) => m_WrappedCommandBuffer.DisableKeyword(cs, keyword);
        public void SetKeyword(ComputeShader cs, in LocalKeyword keyword, bool value) => m_WrappedCommandBuffer.SetKeyword(cs, keyword, value);
        public void SetBufferData(GraphicsBuffer buffer, System.Array data) => m_WrappedCommandBuffer.SetBufferData(buffer, data);
        public void SetBufferData(ComputeBuffer buffer, System.Array data) => m_WrappedCommandBuffer.SetBufferData(buffer, data);
    }

    /// <summary>Every command, for passes the graph cannot reason about (targets set by the pass itself).</summary>
    public class UnsafeCommandBuffer : RasterCommandBuffer
    {
        internal UnsafeCommandBuffer(CommandBuffer wrapped) : base(wrapped) { }

        public void SetComputeFloatParam(ComputeShader cs, int nameID, float val) => m_WrappedCommandBuffer.SetComputeFloatParam(cs, nameID, val);
        public void SetComputeFloatParam(ComputeShader cs, string name, float val) => m_WrappedCommandBuffer.SetComputeFloatParam(cs, name, val);
        public void SetComputeIntParam(ComputeShader cs, int nameID, int val) => m_WrappedCommandBuffer.SetComputeIntParam(cs, nameID, val);
        public void SetComputeIntParam(ComputeShader cs, string name, int val) => m_WrappedCommandBuffer.SetComputeIntParam(cs, name, val);
        public void SetComputeVectorParam(ComputeShader cs, int nameID, Vector4 val) => m_WrappedCommandBuffer.SetComputeVectorParam(cs, nameID, val);
        public void SetComputeVectorParam(ComputeShader cs, string name, Vector4 val) => m_WrappedCommandBuffer.SetComputeVectorParam(cs, name, val);
        public void SetComputeMatrixParam(ComputeShader cs, int nameID, Matrix4x4 val) => m_WrappedCommandBuffer.SetComputeMatrixParam(cs, nameID, val);
        public void SetComputeMatrixParam(ComputeShader cs, string name, Matrix4x4 val) => m_WrappedCommandBuffer.SetComputeMatrixParam(cs, name, val);
        public void SetComputeFloatParams(ComputeShader cs, int nameID, params float[] values) => m_WrappedCommandBuffer.SetComputeFloatParams(cs, nameID, values);
        public void SetComputeFloatParams(ComputeShader cs, string name, params float[] values) => m_WrappedCommandBuffer.SetComputeFloatParams(cs, name, values);
        public void SetComputeIntParams(ComputeShader cs, int nameID, params int[] values) => m_WrappedCommandBuffer.SetComputeIntParams(cs, nameID, values);
        public void SetComputeIntParams(ComputeShader cs, string name, params int[] values) => m_WrappedCommandBuffer.SetComputeIntParams(cs, name, values);
        public void SetComputeVectorArrayParam(ComputeShader cs, int nameID, Vector4[] values) => m_WrappedCommandBuffer.SetComputeVectorArrayParam(cs, nameID, values);
        public void SetComputeMatrixArrayParam(ComputeShader cs, int nameID, Matrix4x4[] values) => m_WrappedCommandBuffer.SetComputeMatrixArrayParam(cs, nameID, values);
        public void SetComputeTextureParam(ComputeShader cs, int kernelIndex, int nameID, RenderTargetIdentifier rt) =>
            m_WrappedCommandBuffer.SetComputeTextureParam(cs, kernelIndex, nameID, rt);
        public void SetComputeTextureParam(ComputeShader cs, int kernelIndex, string name, RenderTargetIdentifier rt) =>
            m_WrappedCommandBuffer.SetComputeTextureParam(cs, kernelIndex, name, rt);
        public void SetComputeTextureParam(ComputeShader cs, int kernelIndex, int nameID, RenderTargetIdentifier rt, int mipLevel) =>
            m_WrappedCommandBuffer.SetComputeTextureParam(cs, kernelIndex, nameID, rt, mipLevel);
        public void SetComputeBufferParam(ComputeShader cs, int kernelIndex, int nameID, ComputeBuffer buffer) =>
            m_WrappedCommandBuffer.SetComputeBufferParam(cs, kernelIndex, nameID, buffer);
        public void SetComputeBufferParam(ComputeShader cs, int kernelIndex, string name, ComputeBuffer buffer) =>
            m_WrappedCommandBuffer.SetComputeBufferParam(cs, kernelIndex, name, buffer);
        public void SetComputeBufferParam(ComputeShader cs, int kernelIndex, int nameID, GraphicsBuffer buffer) =>
            m_WrappedCommandBuffer.SetComputeBufferParam(cs, kernelIndex, nameID, buffer);
        public void SetComputeBufferParam(ComputeShader cs, int kernelIndex, string name, GraphicsBuffer buffer) =>
            m_WrappedCommandBuffer.SetComputeBufferParam(cs, kernelIndex, name, buffer);
        public void SetComputeBufferParam(ComputeShader cs, int kernelIndex, int nameID, GraphicsBufferHandle bufferHandle) =>
            m_WrappedCommandBuffer.SetComputeBufferParam(cs, kernelIndex, nameID, bufferHandle);
        public void DispatchCompute(ComputeShader cs, int kernelIndex, int threadGroupsX, int threadGroupsY, int threadGroupsZ) =>
            m_WrappedCommandBuffer.DispatchCompute(cs, kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ);
        public void DispatchCompute(ComputeShader cs, int kernelIndex, ComputeBuffer indirectBuffer, uint argsOffset) =>
            m_WrappedCommandBuffer.DispatchCompute(cs, kernelIndex, indirectBuffer, argsOffset);
        public void DispatchCompute(ComputeShader cs, int kernelIndex, GraphicsBuffer indirectBuffer, uint argsOffset) =>
            m_WrappedCommandBuffer.DispatchCompute(cs, kernelIndex, indirectBuffer, argsOffset);
        public void EnableKeyword(ComputeShader cs, in LocalKeyword keyword) => m_WrappedCommandBuffer.EnableKeyword(cs, keyword);
        public void DisableKeyword(ComputeShader cs, in LocalKeyword keyword) => m_WrappedCommandBuffer.DisableKeyword(cs, keyword);
        public void SetKeyword(ComputeShader cs, in LocalKeyword keyword, bool value) => m_WrappedCommandBuffer.SetKeyword(cs, keyword, value);
        public void SetBufferData(GraphicsBuffer buffer, System.Array data) => m_WrappedCommandBuffer.SetBufferData(buffer, data);
        public void SetBufferData(ComputeBuffer buffer, System.Array data) => m_WrappedCommandBuffer.SetBufferData(buffer, data);

        public void SetRenderTarget(RenderTargetIdentifier rt) => m_WrappedCommandBuffer.SetRenderTarget(rt);
        public void SetRenderTarget(RenderTargetIdentifier rt, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction) =>
            m_WrappedCommandBuffer.SetRenderTarget(rt, loadAction, storeAction);
        public void SetRenderTarget(RenderTargetIdentifier rt, int mipLevel, CubemapFace cubemapFace, int depthSlice) =>
            m_WrappedCommandBuffer.SetRenderTarget(rt, mipLevel, cubemapFace, depthSlice);
        public void SetRenderTarget(RenderTargetIdentifier color, RenderTargetIdentifier depth) => m_WrappedCommandBuffer.SetRenderTarget(color, depth);
        public void SetRenderTarget(RenderTargetIdentifier[] colors, RenderTargetIdentifier depth) => m_WrappedCommandBuffer.SetRenderTarget(colors, depth);

        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest) => m_WrappedCommandBuffer.Blit(source, dest);
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest, Material mat) => m_WrappedCommandBuffer.Blit(source, dest, mat);
        public void Blit(RenderTargetIdentifier source, RenderTargetIdentifier dest, Material mat, int pass) => m_WrappedCommandBuffer.Blit(source, dest, mat, pass);
        public void Blit(Texture source, RenderTargetIdentifier dest) => m_WrappedCommandBuffer.Blit(source, dest);

        public void CopyTexture(RenderTargetIdentifier src, RenderTargetIdentifier dst) => m_WrappedCommandBuffer.CopyTexture(src, dst);
        public void CopyTexture(RenderTargetIdentifier src, int srcElement, int srcMip, RenderTargetIdentifier dst, int dstElement, int dstMip) =>
            m_WrappedCommandBuffer.CopyTexture(src, srcElement, srcMip, dst, dstElement, dstMip);

        public void GenerateMips(RenderTargetIdentifier rt) => m_WrappedCommandBuffer.GenerateMips(rt);
        public void ResolveAntiAliasedSurface(RenderTexture rt, RenderTexture target = null) => m_WrappedCommandBuffer.ResolveAntiAliasedSurface(rt, target);
    }

    /// <summary>The engine's command buffer under the graph's ones (for code that takes a CommandBuffer).</summary>
    public static class CommandBufferHelpers
    {
        public static CommandBuffer GetNativeCommandBuffer(UnsafeCommandBuffer baseBuffer) => baseBuffer.m_WrappedCommandBuffer;
    }
}
