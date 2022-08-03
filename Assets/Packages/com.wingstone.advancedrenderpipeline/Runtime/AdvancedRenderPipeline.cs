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
            public static readonly ShaderTagId _passTagDepth = new ShaderTagId("DepthOnly");
            public static readonly ShaderTagId _passTagShadowCaster = new ShaderTagId("ShadowCaster");
            public static readonly int _ShadowMap = Shader.PropertyToID("_ShadowMap");
            public static readonly int _ShadowMapTex = Shader.PropertyToID("_ShadowMapTexture");
            public static readonly int _ColorAttatchment = Shader.PropertyToID("_ColorAttatchment");
            public static readonly int _DepthAttatchment = Shader.PropertyToID("_DepthAttatchment");
            public static readonly int _DepthTexture = Shader.PropertyToID("_DepthTexture");

            public static readonly int _ShadowMatrix = Shader.PropertyToID("_ShadowMatrix");
            public static readonly int _MainLightDirection = Shader.PropertyToID("_MainLightDirection");
            public static readonly int _ShadowDepthBias = Shader.PropertyToID("_ShadowDepthBias");
            public static readonly int _ShadowSphere = Shader.PropertyToID("_ShadowSphere");
            public static readonly int _ShadowBorderFadeLength = Shader.PropertyToID("_ShadowBorderFadeLength");
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

                RenderLight(context, cullingResults, camera);

                RenderFinalBlit(context, cullingResults, camera);
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

            Bounds bounds = new Bounds();
            if (!culling.GetShadowCasterBounds(mainLight, out bounds)) return;

            CommandBuffer shadowmapCmd = CommandBufferPool.Get("RenderShadowMap");
            RenderTextureDescriptor shadowmapDesc = new RenderTextureDescriptor(shadowResolution, shadowResolution, RenderTextureFormat.Shadowmap, 16);

            shadowmapCmd.GetTemporaryRT(ShaderConstants._ShadowMap, shadowmapDesc, FilterMode.Bilinear);
            shadowmapCmd.SetRenderTarget(ShaderConstants._ShadowMap);
            shadowmapCmd.ClearRenderTarget(true, true, Color.white);

            // get matrix
            Matrix4x4 viewMat, projMat;
            ShadowSplitData splitData = new ShadowSplitData();
            culling.ComputeDirectionalShadowMatricesAndCullingPrimitives(mainLight, 0, 1, Vector3.one, shadowResolution, 1f, out viewMat, out projMat, out splitData);

            Vector3 lightDir = culling.visibleLights[mainLight].localToWorldMatrix.MultiplyVector(-Vector3.forward);
            SetShadowSetting(viewMat, projMat, lightDir, splitData.cullingSphere, shadowmapCmd);

            context.ExecuteCommandBuffer(shadowmapCmd);
            CommandBufferPool.Release(shadowmapCmd);

            ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(culling, mainLight);
            shadowDrawingSettings.splitData = splitData;
            shadowDrawingSettings.useRenderingLayerMaskTest = true;

            context.DrawShadows(ref shadowDrawingSettings);
        }

        private void RenderLight(ScriptableRenderContext context, CullingResults culling, Camera camera)
        {
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;

            CommandBuffer cmd = CommandBufferPool.Get("RenderLight");

            // get color attatchment and depth attatchment
            RenderTextureDescriptor colorDesc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24);
            RenderTextureDescriptor depthDesc = new RenderTextureDescriptor(width, height, RenderTextureFormat.Depth, 16);

            cmd.GetTemporaryRT(ShaderConstants._ColorAttatchment, colorDesc);
            cmd.GetTemporaryRT(ShaderConstants._DepthAttatchment, depthDesc);
            cmd.SetRenderTarget((RenderTargetIdentifier)ShaderConstants._ColorAttatchment, (RenderTargetIdentifier)ShaderConstants._DepthAttatchment);
            cmd.ClearRenderTarget(true, true, Color.black);

            //Camera setup some builtin variables e.g. camera projection matrices etc
            context.SetupCameraProperties(camera);

            // set shadow map
            cmd.SetGlobalTexture(ShaderConstants._ShadowMapTex, ShaderConstants._ShadowMap);

            // set main light vector
            int mainLight = GetMainLight(culling);
            Vector3 lightDir = culling.visibleLights[mainLight].localToWorldMatrix.MultiplyVector(-Vector3.forward);
            cmd.SetGlobalVector(ShaderConstants._MainLightDirection, lightDir);

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
            shadowCmd.SetGlobalTexture(ShaderConstants._DepthTexture, ShaderConstants._DepthAttatchment);
            shadowCmd.Blit(null, BuiltinRenderTextureType.CameraTarget, pipelineAsset._ShadowMat);

            context.ExecuteCommandBuffer(shadowCmd);
            CommandBufferPool.Release(shadowCmd);
        }

        private void RenderFinalBlit(ScriptableRenderContext context, CullingResults culling, Camera camera)
        {
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;

            CommandBuffer shadowCmd = CommandBufferPool.Get("RenderFinalBlit");

            shadowCmd.Blit(ShaderConstants._ColorAttatchment, BuiltinRenderTextureType.CameraTarget);

            context.ExecuteCommandBuffer(shadowCmd);
            CommandBufferPool.Release(shadowCmd);
        }

        private void SetShadowSetting(Matrix4x4 view, Matrix4x4 proj, Vector3 lightDir, Vector4 shadowSphere, CommandBuffer cmd)
        {
            cmd.SetViewProjectionMatrices(view, proj);
            Matrix4x4 m = proj * view;
            if (SystemInfo.usesReversedZBuffer)
            {
                m.m20 = -m.m20;
                m.m21 = -m.m21;
                m.m22 = -m.m22;
                m.m23 = -m.m23;
            }
            cmd.SetGlobalMatrix(ShaderConstants._ShadowMatrix, m);
            cmd.SetGlobalFloat(ShaderConstants._ShadowDepthBias, pipelineAsset._ShadowDepthBias);
            cmd.SetGlobalFloat(ShaderConstants._ShadowBorderFadeLength, pipelineAsset._ShadowBorderFadeLength);
            cmd.SetGlobalVector(ShaderConstants._MainLightDirection, lightDir);
            cmd.SetGlobalVector(ShaderConstants._ShadowSphere, shadowSphere);
        }
    }
}