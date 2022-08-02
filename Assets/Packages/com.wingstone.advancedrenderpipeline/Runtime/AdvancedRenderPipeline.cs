using Unity.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine.Scripting.APIUpdating;
using Lightmapping = UnityEngine.Experimental.GlobalIllumination.Lightmapping;
#if ENABLE_VR && ENABLE_XR_MODULE
using UnityEngine.XR;
#endif

namespace ARP
{
    public class AdvancedRenderPipeline : RenderPipeline
    {
        class ShaderConstants
        {
            public static readonly ShaderTagId _passTagRender = new ShaderTagId("ARP");
            public static readonly ShaderTagId _passTagDepth = new ShaderTagId("Depth");
            public static readonly ShaderTagId _passTagShadowCaster = new ShaderTagId("ShadowCaster");
            public static readonly int _Depth = Shader.PropertyToID("_CameraDepthTexture");
            public static readonly int _ShadowMap = Shader.PropertyToID("_ShadowMap");
            public static readonly int _ShadowMapTex = Shader.PropertyToID("_ShadowMapTexture");
        }
        private PipelineAsset pipelineAsset;

        public AdvancedRenderPipeline(PipelineAsset asset)
        {
            // It is possible to initial some data from asset
            pipelineAsset = asset;
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            BeginFrameRendering(context, cameras);

            foreach (Camera camera in cameras)
            {
                RenderSingleCamera(context, camera);
            }

            EndFrameRendering(context, cameras);
        }

        private void RenderSingleCamera(ScriptableRenderContext context, Camera camera)
        {
            BeginCameraRendering(context, camera);

            //Culling
            ScriptableCullingParameters cullingParams;
            if (!camera.TryGetCullingParameters(out cullingParams))
                return;

            cullingParams.shadowDistance = pipelineAsset._ShadowDistance;
            CullingResults cullingResults = context.Cull(ref cullingParams);

            // profile
            ProfilingSampler sampler = new ProfilingSampler(camera.name);
            using (new ProfilingScope())
            {
                RenderShadowMap(context, cullingResults, camera, pipelineAsset._ShadowResolution);

                RenderDepthMap(context, cullingResults, camera);

                RenderLight(context, cullingResults, camera);
            }

            context.Submit();

            //Submit the CommandBuffers
            context.Submit();

            EndCameraRendering(context, camera);
        }

        int GetMainLight(CullingResults cullings)
        {
            for (int i = 0; i < cullings.visibleLights.Length; i++)
            {
                if (cullings.visibleLights[i].lightType == LightType.Directional)
                    return i;
            }

            return -1;
        }

        private void RenderShadowMap(ScriptableRenderContext context, CullingResults culling, Camera camera, int shadowResolution)
        {
            int mainLight = GetMainLight(culling);
            if (mainLight == -1) return;

            CommandBuffer shadowmapCmd = CommandBufferPool.Get("RenderShadowMap");
            RenderTextureDescriptor shadowmapDesc = new RenderTextureDescriptor(shadowResolution, shadowResolution, RenderTextureFormat.Shadowmap, 32);

            shadowmapCmd.GetTemporaryRT(ShaderConstants._ShadowMap, shadowmapDesc);
            shadowmapCmd.SetRenderTarget(ShaderConstants._ShadowMap);
            shadowmapCmd.ClearRenderTarget(true, true, Color.black);

            // get matrix
            Matrix4x4 viewMat, projMat;
            ShadowSplitData splitData = new ShadowSplitData();
            culling.ComputeDirectionalShadowMatricesAndCullingPrimitives(mainLight, 0, 1, Vector3.one, shadowResolution, 0, out viewMat, out projMat, out splitData);
            shadowmapCmd.SetViewMatrix(viewMat);
            shadowmapCmd.SetProjectionMatrix(projMat);

            context.ExecuteCommandBuffer(shadowmapCmd);
            CommandBufferPool.Release(shadowmapCmd);

            ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(culling, mainLight);
            shadowDrawingSettings.splitData = splitData;
            shadowDrawingSettings.useRenderingLayerMaskTest = true;

            context.DrawShadows(ref shadowDrawingSettings);
        }

        private void RenderDepthMap(ScriptableRenderContext context, CullingResults culling, Camera camera)
        {
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;

            CommandBuffer depthCmd = CommandBufferPool.Get("RenderDepthMap");
            RenderTextureDescriptor depthDesc = new RenderTextureDescriptor(width, height, RenderTextureFormat.Depth, 32);

            depthCmd.GetTemporaryRT(ShaderConstants._Depth, depthDesc);
            depthCmd.SetRenderTarget(ShaderConstants._Depth);
            depthCmd.ClearRenderTarget(true, true, Color.black);
            context.ExecuteCommandBuffer(depthCmd);
            CommandBufferPool.Release(depthCmd);


            SortingSettings sortingSettings = new SortingSettings(camera);
            DrawingSettings drawingSettings = new DrawingSettings(ShaderConstants._passTagDepth, sortingSettings);
            FilteringSettings filteringSettings = new FilteringSettings();

            context.DrawRenderers(culling, ref drawingSettings, ref filteringSettings);
        }

        private void RenderLight(ScriptableRenderContext context, CullingResults culling, Camera camera)
        {
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;

            CommandBuffer cmd = CommandBufferPool.Get();

            //Camera setup some builtin variables e.g. camera projection matrices etc
            context.SetupCameraProperties(camera);

            //Get the setting from camera component
            bool drawSkyBox = camera.clearFlags == CameraClearFlags.Skybox ? true : false;
            bool clearDepth = camera.clearFlags == CameraClearFlags.Depth ? true : false;
            bool clearColor = camera.clearFlags == CameraClearFlags.Color ? true : false;
            Color backColor = camera.backgroundColor;

            // clear
            if (clearColor || drawSkyBox) cmd.ClearRenderTarget(true, true, backColor);
            if (clearDepth) cmd.ClearRenderTarget(true, false, backColor);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            SortingSettings sortingSettings = new SortingSettings(camera);
            DrawingSettings drawingSettings = new DrawingSettings(ShaderConstants._passTagRender, sortingSettings);
            FilteringSettings filteringSettings = new FilteringSettings();

            // Opaque objects
            sortingSettings.criteria = SortingCriteria.CommonOpaque;
            drawingSettings.sortingSettings = sortingSettings;
            filteringSettings.layerMask = -1;
            filteringSettings.renderingLayerMask = 4294967295;
            filteringSettings.renderQueueRange = RenderQueueRange.opaque;
            context.DrawRenderers(culling, ref drawingSettings, ref filteringSettings);

            // Skybox
            if (drawSkyBox) { context.DrawSkybox(camera); }

            // Transparent objects
            sortingSettings.criteria = SortingCriteria.CommonTransparent;
            drawingSettings.sortingSettings = sortingSettings;
            filteringSettings.renderQueueRange = RenderQueueRange.transparent;
            context.DrawRenderers(culling, ref drawingSettings, ref filteringSettings);
        }

        private void RenderShadow(ScriptableRenderContext context, CullingResults culling, Camera camera)
        {
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;

            CommandBuffer shadowCmd = CommandBufferPool.Get("RenderShadow");
            shadowCmd.SetGlobalTexture(ShaderConstants._ShadowMapTex, ShaderConstants._ShadowMap);
            shadowCmd.Blit(ShaderConstants._Depth, BuiltinRenderTextureType.CameraTarget, pipelineAsset._ShadowMat);

            context.ExecuteCommandBuffer(shadowCmd);
            CommandBufferPool.Release(shadowCmd);
        }
    }
}