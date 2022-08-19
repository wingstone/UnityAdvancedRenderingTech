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

            public static readonly int _MainLightDirection = Shader.PropertyToID("_MainLightDirection");
            public static readonly int _ShadowDepthBias = Shader.PropertyToID("_ShadowDepthBias");
            public static readonly int _ShadowBorderFadeLength = Shader.PropertyToID("_ShadowBorderFadeLength");
            public static readonly int _ShadowBlendLenth = Shader.PropertyToID("_ShadowBlendLenth");
            public static readonly int _ShadowMapTextureSize = Shader.PropertyToID("_ShadowMapTextureSize");
            public static readonly int _ShadowMatrixArray = Shader.PropertyToID("_ShadowMatrixArray");
            public static readonly int _ShadowSphereArray = Shader.PropertyToID("_ShadowSphereArray");

            public static readonly int _ScreenPramaters = Shader.PropertyToID("_ScreenPramaters");
        }

        struct CascadeData
        {
            public Matrix4x4 view;
            public Matrix4x4 proj;
            public ShadowSplitData splitdata;
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

        // main directional light
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

            context.ExecuteCommandBuffer(shadowmapCmd);
            shadowmapCmd.Clear();

            // get cascade data
            Vector3 cascadeSplitRatio = new Vector3(pipelineAsset._Split0, pipelineAsset._Split1, pipelineAsset._Split2);
            int cascadeResolution = shadowResolution / 2;
            CascadeData[] cascadeDatas = new CascadeData[4];
            Vector3 lightDir = culling.visibleLights[mainLight].localToWorldMatrix.MultiplyVector(-Vector3.forward);

            for (int i = 0; i < 4; i++)
            {
                culling.ComputeDirectionalShadowMatricesAndCullingPrimitives(mainLight, i, 4, cascadeSplitRatio, cascadeResolution, 1f, out cascadeDatas[i].view, out cascadeDatas[i].proj, out cascadeDatas[i].splitdata);

                SetShadowCasterConstant(context, lightDir, cascadeDatas[i], shadowmapCmd, cascadeResolution);

                Rect viewport = GetViewport(i, cascadeResolution);
                RenderCascadeShadow(context, culling, camera, cascadeDatas[i], viewport, shadowmapCmd);
            }

            SetShadowSetting(context, cascadeDatas, shadowmapCmd);

            CommandBufferPool.Release(shadowmapCmd);
        }

        private Rect GetViewport(int index, int cascaderesolution)
        {
            Vector2Int offset = new Vector2Int(index % 2, index / 2);
            return new Rect(offset.x * cascaderesolution, offset.y * cascaderesolution, cascaderesolution, cascaderesolution);
        }

        private void SetShadowCasterConstant(ScriptableRenderContext context, Vector3 lightDir, CascadeData cascadeData, CommandBuffer cmd, float cascadeResolution)
        {
            float frustumSize = 2.0f / cascadeData.proj.m00;
            float depthBias = frustumSize / cascadeResolution * pipelineAsset._ShadowDepthBias;
            cmd.SetGlobalFloat(ShaderConstants._ShadowDepthBias, depthBias);
            cmd.SetGlobalVector(ShaderConstants._MainLightDirection, lightDir);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        private void RenderCascadeShadow(ScriptableRenderContext context, CullingResults cullingResults, Camera camera, CascadeData cascadeData, Rect viewport, CommandBuffer shadowmapCmd)
        {
            shadowmapCmd.SetViewProjectionMatrices(cascadeData.view, cascadeData.proj);
            shadowmapCmd.SetViewport(viewport);
            context.ExecuteCommandBuffer(shadowmapCmd);
            shadowmapCmd.Clear();

            int mainLight = GetMainLight(cullingResults);
            ShadowDrawingSettings shadowDrawingSettings = new ShadowDrawingSettings(cullingResults, mainLight);
            shadowDrawingSettings.splitData = cascadeData.splitdata;
            shadowDrawingSettings.useRenderingLayerMaskTest = true;
            context.DrawShadows(ref shadowDrawingSettings);
        }

        private void SetCameraConstants(ScriptableRenderContext context, Camera camera, CommandBuffer cmd)
        {
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            cmd.SetGlobalVector(ShaderConstants._ScreenPramaters, new Vector4(1 / width, 1 / height, width, height));
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
            /// Configure shader variables and other unity properties that are required for rendering.
            /// * Setup Camera RenderTarget and Viewport
            /// * VR Camera Setup and SINGLE_PASS_STEREO props
            /// * Setup camera view, projection and their inverse matrices.
            /// * Setup properties: _WorldSpaceCameraPos, _ProjectionParams, _ScreenParams, _ZBufferParams, unity_OrthoParams
            /// * Setup camera world clip planes properties
            /// * Setup HDR keyword
            /// * Setup global time properties (_Time, _SinTime, _CosTime)
            context.SetupCameraProperties(camera);
            SetCameraConstants(context, camera, cmd);

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

        // private void RenderShadow(ScriptableRenderContext context, CullingResults culling, Camera camera)
        // {
        //     int width = camera.pixelWidth;
        //     int height = camera.pixelHeight;

        //     CommandBuffer shadowCmd = CommandBufferPool.Get("RenderShadow");

        //     shadowCmd.SetGlobalTexture(ShaderConstants._ShadowMapTex, ShaderConstants._ShadowMap);
        //     shadowCmd.SetGlobalTexture(ShaderConstants._DepthTexture, ShaderConstants._DepthAttatchment);
        //     shadowCmd.Blit(null, BuiltinRenderTextureType.CameraTarget, pipelineAsset._ShadowMat);

        //     context.ExecuteCommandBuffer(shadowCmd);
        //     CommandBufferPool.Release(shadowCmd);
        // }

        private void RenderFinalBlit(ScriptableRenderContext context, CullingResults culling, Camera camera)
        {
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;

            CommandBuffer shadowCmd = CommandBufferPool.Get("RenderFinalBlit");

            if(SystemInfo.graphicsUVStartsAtTop && camera.cameraType == CameraType.SceneView)
            {
                shadowCmd.EnableShaderKeyword("_UVStartAtUp");
            }
            else
            {
                shadowCmd.DisableShaderKeyword("_UVStartAtUp");
            }

            shadowCmd.Blit(ShaderConstants._ColorAttatchment, BuiltinRenderTextureType.CameraTarget, pipelineAsset._FinalBlitMat);

            context.ExecuteCommandBuffer(shadowCmd);
            CommandBufferPool.Release(shadowCmd);
        }

        private void SetShadowSetting(ScriptableRenderContext context, CascadeData[] cascadeDatas, CommandBuffer cmd)
        {
            int shadowResolution = pipelineAsset._ShadowResolution;

            Matrix4x4[] m = new Matrix4x4[4];
            float[] depthBias = new float[4];
            Vector4[] shadowSphere = new Vector4[4];
            for (int i = 0; i < 4; i++)
            {
                m[i] = cascadeDatas[i].proj * cascadeDatas[i].view;
                if (SystemInfo.usesReversedZBuffer)
                {
                    m[i].m20 = -m[i].m20;
                    m[i].m21 = -m[i].m21;
                    m[i].m22 = -m[i].m22;
                    m[i].m23 = -m[i].m23;
                }

                Matrix4x4 normalizeMat = new Matrix4x4(new Vector4(0.25f, 0.0f, 0.0f, 0.25f),
                                                    new Vector4(0.0f, 0.25f, 0.0f, 0.25f),
                                                    new Vector4(0.0f, 0.0f, 0.5f, 0.5f),
                                                    new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                Vector2 offset = new Vector2(i % 2, i / 2) * 0.5f;
                Matrix4x4 offsetMat = new Matrix4x4(new Vector4(1f, 0.0f, 0.0f, offset.x),
                                                    new Vector4(0.0f, 1f, 0.0f, offset.y),
                                                    new Vector4(0.0f, 0.0f, 1.0f, 0.0f),
                                                    new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                m[i] = offsetMat.transpose * normalizeMat.transpose * m[i];
                shadowSphere[i] = cascadeDatas[i].splitdata.cullingSphere;
            }

            switch (pipelineAsset._blendMethod)
            {
                case ShadowBlendEnum.NoBlend:
                    cmd.EnableShaderKeyword("CASCADE_NOBLEND");
                    cmd.DisableShaderKeyword("CASCADE_BLEND");
                    cmd.DisableShaderKeyword("CASCADE_DITHER");
                    break;
                case ShadowBlendEnum.Blend:
                    cmd.EnableShaderKeyword("CASCADE_BLEND");
                    cmd.DisableShaderKeyword("CASCADE_NOBLEND");
                    cmd.DisableShaderKeyword("CASCADE_DITHER");
                    break;
                case ShadowBlendEnum.Dither:
                    cmd.EnableShaderKeyword("CASCADE_DITHER");
                    cmd.DisableShaderKeyword("CASCADE_NOBLEND");
                    cmd.DisableShaderKeyword("CASCADE_BLEND");
                    break;
                default:
                    break;
            }

            cmd.SetGlobalMatrixArray(ShaderConstants._ShadowMatrixArray, m);
            cmd.SetGlobalVectorArray(ShaderConstants._ShadowSphereArray, shadowSphere);

            cmd.SetGlobalFloat(ShaderConstants._ShadowBorderFadeLength, pipelineAsset._ShadowBorderFadeLength);
            cmd.SetGlobalVector(ShaderConstants._ShadowMapTextureSize, new Vector4(1f / shadowResolution, 1f / shadowResolution, shadowResolution, shadowResolution));
            cmd.SetGlobalFloat(ShaderConstants._ShadowBlendLenth, pipelineAsset._ShadowBlendLenth);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }
}