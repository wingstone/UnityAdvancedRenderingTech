namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Draws full screen mesh using given material and pass and reading from source target.
    /// </summary>
    internal class CylinderSkyRenderPass : ScriptableRenderPass
    {
        public FilterMode filterMode { get; set; }
        public CylinderSkyRenderFeature.Settings settings;

        RenderTargetIdentifier source;
        RenderTargetIdentifier destination;
        int temporaryRTId = Shader.PropertyToID("_TempRT");

        // --- scattering
        int _TransmittanceLutId = Shader.PropertyToID("_TransmittanceLut");
        int _SkyViewLutId = Shader.PropertyToID("_SkrViewLut");
        int _MultiSactteringLutId = Shader.PropertyToID("_MultiSactteringLut");

        Vector2Int transMittanceResolution = new Vector2Int(256, 64);
        Vector2Int skyviewResolution = new Vector2Int(256, 128);
        Vector2Int multiScatteringResolution = new Vector2Int(64, 64);

        Material scatteringBakeMat = null;
        // --- scattering

        int sourceId;
        int destinationId;
        bool isSourceAndDestinationSameTarget;

        string m_ProfilerTag;

        public CylinderSkyRenderPass(string tag)
        {
            m_ProfilerTag = tag;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor blitTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            blitTargetDescriptor.depthBufferBits = 0;

            isSourceAndDestinationSameTarget = settings.sourceType == settings.destinationType &&
                (settings.sourceType == BufferType.CameraColor || settings.sourceTextureId == settings.destinationTextureId);

            var renderer = renderingData.cameraData.renderer;

            if (settings.sourceType == BufferType.CameraColor)
            {
                sourceId = -1;
                source = renderer.cameraColorTarget;
            }
            else
            {
                sourceId = Shader.PropertyToID(settings.sourceTextureId);
                cmd.GetTemporaryRT(sourceId, blitTargetDescriptor, filterMode);
                source = new RenderTargetIdentifier(sourceId);
            }

            if (isSourceAndDestinationSameTarget)
            {
                destinationId = temporaryRTId;
                cmd.GetTemporaryRT(destinationId, blitTargetDescriptor, filterMode);
                destination = new RenderTargetIdentifier(destinationId);
            }
            else if (settings.destinationType == BufferType.CameraColor)
            {
                destinationId = -1;
                destination = renderer.cameraColorTarget;
            }
            else
            {
                destinationId = Shader.PropertyToID(settings.destinationTextureId);
                cmd.GetTemporaryRT(destinationId, blitTargetDescriptor, filterMode);
                destination = new RenderTargetIdentifier(destinationId);
            }
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
            Material cylinderMat = settings.blitMaterial;

            //--- begin lut update
            cmd.GetTemporaryRT(_TransmittanceLutId, transMittanceResolution.x, transMittanceResolution.y, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            cmd.GetTemporaryRT(_MultiSactteringLutId, multiScatteringResolution.x, multiScatteringResolution.y, 1, FilterMode.Bilinear, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear, 1, true);
            cmd.GetTemporaryRT(_SkyViewLutId, skyviewResolution.x, skyviewResolution.y, 1, FilterMode.Bilinear, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);

            if (scatteringBakeMat == null)
            {
                scatteringBakeMat = new Material(settings.scatteringBakeShader);
                scatteringBakeMat.hideFlags = HideFlags.HideAndDontSave;
            }

            // transmittanceLut
            scatteringBakeMat.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
            cmd.Blit(_TransmittanceLutId, _TransmittanceLutId, scatteringBakeMat, 0);

            // multiScatteringLut
            ComputeShader computeShader = settings.multiScatteringComputerShader;
            int kernel = computeShader.FindKernel("CSMain");
            computeShader.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
            computeShader.SetVector("_MultiScattering_Size", new Vector4(1f / multiScatteringResolution.x, 1f / multiScatteringResolution.y, multiScatteringResolution.x, multiScatteringResolution.y));
            computeShader.SetVector("_GroundColor", Vector4.zero);
            computeShader.SetFloat("_SolarIrradiance", scatteringBakeMat.GetFloat("_SolarIrradiance"));

            cmd.SetComputeTextureParam(computeShader, kernel, "_TransmittanceLut", _TransmittanceLutId);
            cmd.SetComputeTextureParam(computeShader, kernel, "_Result", _MultiSactteringLutId);
            cmd.DispatchCompute(computeShader, kernel, multiScatteringResolution.x, multiScatteringResolution.y, 1);

            // skyviewLut
            scatteringBakeMat.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
            scatteringBakeMat.SetVector("_SkyViewLutSize", new Vector4(1f / skyviewResolution.x, 1f / skyviewResolution.y, skyviewResolution.x, skyviewResolution.y));
            scatteringBakeMat.SetVector("_MultiSactteringLut_Size", new Vector4(1f / multiScatteringResolution.x, 1f / multiScatteringResolution.y, multiScatteringResolution.x, multiScatteringResolution.y));
            scatteringBakeMat.SetFloat("_SolarIrradiance", cylinderMat.GetFloat("_SolarIrradiance"));
            scatteringBakeMat.SetFloat("_LightDirectionPhi", cylinderMat.GetFloat("_LightDirectionPhi"));
            scatteringBakeMat.SetFloat("_LightDirectionTheta", cylinderMat.GetFloat("_LightDirectionTheta"));
            scatteringBakeMat.SetFloat("_CameraHeight", cylinderMat.GetFloat("_CameraHeight"));
            scatteringBakeMat.SetColor("_GroundColor", cylinderMat.GetColor("_GroundColor"));

            cmd.SetGlobalTexture("_TransmittanceLut", _TransmittanceLutId);
            cmd.SetGlobalTexture("_MultiSactteringLut", _MultiSactteringLutId);
            cmd.Blit(_SkyViewLutId, _SkyViewLutId, scatteringBakeMat, 1);

            cylinderMat.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
            cylinderMat.SetVector("_SkyViewLutSize", new Vector4(1f / skyviewResolution.x, 1f / skyviewResolution.y, skyviewResolution.x, skyviewResolution.y));

            cmd.SetGlobalTexture("_TransmittanceLut", _TransmittanceLutId);
            cmd.SetGlobalTexture("_SkyViewLut", _SkyViewLutId);
            cmd.SetGlobalTexture("_MultiSactteringLut", _MultiSactteringLutId);
            cylinderMat.SetVector("_MultiSactteringLut_Size", new Vector4(1f / multiScatteringResolution.x, 1f / multiScatteringResolution.y, multiScatteringResolution.x, multiScatteringResolution.y));

            // --- end lut update


            // Can't read and write to same color target, create a temp render target to blit. 
            if (isSourceAndDestinationSameTarget)
            {
                Blit(cmd, source, destination, settings.blitMaterial, settings.blitMaterialPassIndex);
                Blit(cmd, destination, source);
            }
            else
            {
                Blit(cmd, source, destination, settings.blitMaterial, settings.blitMaterialPassIndex);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        /// <inheritdoc/>
        public override void FrameCleanup(CommandBuffer cmd)
        {
            if (destinationId != -1)
                cmd.ReleaseTemporaryRT(destinationId);

            if (source == destination && sourceId != -1)
                cmd.ReleaseTemporaryRT(sourceId);

            cmd.ReleaseTemporaryRT(_TransmittanceLutId);
            cmd.ReleaseTemporaryRT(_MultiSactteringLutId);
            cmd.ReleaseTemporaryRT(_SkyViewLutId);
        }
    }
}