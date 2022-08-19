namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Draws full screen mesh using given material and pass and reading from source target.
    /// </summary>
    internal class SkyScatteringRenderPass : ScriptableRenderPass
    {
        public FilterMode filterMode { get; set; }
        public SkyScatteringRenderFeature.Settings settings;

        RenderTargetIdentifier source;
        RenderTargetIdentifier destination;

        // --- scattering
        int _TransmittanceLutId = Shader.PropertyToID("_TransmittanceLut");
        int _SkyViewLutId = Shader.PropertyToID("_SkrViewLut");
        int _MultiSactteringLutId = Shader.PropertyToID("_MultiSactteringLut");
        // int _VolumeScatteringId = Shader.PropertyToID("_VolumeScattering");

        Vector2Int transMittanceResolution = new Vector2Int(256, 64);
        Vector2Int skyviewResolution = new Vector2Int(256, 128);
        Vector2Int multiScatteringResolution = new Vector2Int(64, 64);
        Vector3Int volumeScatteringResolution = new Vector3Int(32, 32, 32);

        RenderTexture _VolumeScattering = null;

        Material scatteringBakeMat = null;
        // --- scattering

        string m_ProfilerTag;

        public SkyScatteringRenderPass(string tag)
        {
            m_ProfilerTag = tag;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor blitTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            blitTargetDescriptor.depthBufferBits = 0;
            var renderer = renderingData.cameraData.renderer;
            source = destination = renderer.cameraColorTarget;
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);
            Material scatterMat = settings.blitMaterial;

            cmd.GetTemporaryRT(_TransmittanceLutId, transMittanceResolution.x, transMittanceResolution.y, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            cmd.GetTemporaryRT(_MultiSactteringLutId, multiScatteringResolution.x, multiScatteringResolution.y, 1, FilterMode.Bilinear, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear, 1, true);
            cmd.GetTemporaryRT(_SkyViewLutId, skyviewResolution.x, skyviewResolution.y, 1, FilterMode.Bilinear, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            // cmd.GetTemporaryRTArray(_VolumeScatteringId, volumeScatteringResolution.x, volumeScatteringResolution.y, volumeScatteringResolution.z, 1, FilterMode.Bilinear, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear, 1, true);
            if (_VolumeScattering == null)
            {
                _VolumeScattering = new RenderTexture(volumeScatteringResolution.x, volumeScatteringResolution.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                _VolumeScattering.enableRandomWrite = true;
                _VolumeScattering.volumeDepth = volumeScatteringResolution.z;
                _VolumeScattering.dimension = TextureDimension.Tex3D;
                _VolumeScattering.Create();
            }


            if (scatteringBakeMat == null)
            {
                scatteringBakeMat = new Material(settings.scatteringBakeShader);
                scatteringBakeMat.hideFlags = HideFlags.HideAndDontSave;
            }

            // transmittanceLut
            scatteringBakeMat.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
            cmd.Blit(_TransmittanceLutId, _TransmittanceLutId, scatteringBakeMat, 0);

            // multiScatteringLut
            ComputeShader multiScatteringCS = settings.multiScatteringComputerShader;
            int multiScatteringKernel = multiScatteringCS.FindKernel("CSMain");
            multiScatteringCS.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
            multiScatteringCS.SetVector("_MultiScattering_Size", new Vector4(1f / multiScatteringResolution.x, 1f / multiScatteringResolution.y, multiScatteringResolution.x, multiScatteringResolution.y));
            multiScatteringCS.SetVector("_GroundColor", Vector4.zero);
            multiScatteringCS.SetFloat("_SolarIrradiance", scatteringBakeMat.GetFloat("_SolarIrradiance"));

            cmd.SetComputeTextureParam(multiScatteringCS, multiScatteringKernel, "_TransmittanceLut", _TransmittanceLutId);
            cmd.SetComputeTextureParam(multiScatteringCS, multiScatteringKernel, "_Result", _MultiSactteringLutId);
            cmd.DispatchCompute(multiScatteringCS, multiScatteringKernel, multiScatteringResolution.x, multiScatteringResolution.y, 1);

            // skyviewLut
            scatteringBakeMat.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
            scatteringBakeMat.SetVector("_SkyViewLutSize", new Vector4(1f / skyviewResolution.x, 1f / skyviewResolution.y, skyviewResolution.x, skyviewResolution.y));
            scatteringBakeMat.SetVector("_MultiSactteringLut_Size", new Vector4(1f / multiScatteringResolution.x, 1f / multiScatteringResolution.y, multiScatteringResolution.x, multiScatteringResolution.y));
            scatteringBakeMat.SetFloat("_SolarIrradiance", scatterMat.GetFloat("_SolarIrradiance"));
            scatteringBakeMat.SetFloat("_LightDirectionPhi", scatterMat.GetFloat("_LightDirectionPhi"));
            scatteringBakeMat.SetFloat("_LightDirectionTheta", scatterMat.GetFloat("_LightDirectionTheta"));
            scatteringBakeMat.SetFloat("_CameraHeight", scatterMat.GetFloat("_CameraHeight"));
            scatteringBakeMat.SetColor("_GroundColor", scatterMat.GetColor("_GroundColor"));

            cmd.SetGlobalTexture("_TransmittanceLut", _TransmittanceLutId);
            cmd.SetGlobalTexture("_MultiSactteringLut", _MultiSactteringLutId);
            cmd.Blit(_SkyViewLutId, _SkyViewLutId, scatteringBakeMat, 1);

            // scattering volume
            ComputeShader scatterVolumeCS = settings.scatteringVolume;
            int scatterVolumeKernel = scatterVolumeCS.FindKernel("GatherScattering");
            scatterVolumeCS.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
            scatterVolumeCS.SetVector("_MultiScatteringLut_Size", new Vector4(1f / multiScatteringResolution.x, 1f / multiScatteringResolution.y, multiScatteringResolution.x, multiScatteringResolution.y));
            scatterVolumeCS.SetFloat("_SolarIrradiance", scatterMat.GetFloat("_SolarIrradiance"));
            scatterVolumeCS.SetVector("_VolumeSize", new Vector4(volumeScatteringResolution.x, volumeScatteringResolution.y, volumeScatteringResolution.z, 0));
            scatterVolumeCS.SetVector("_CameraPos", renderingData.cameraData.camera.transform.position);
            Matrix4x4 viewproj = renderingData.cameraData.camera.projectionMatrix * renderingData.cameraData.camera.worldToCameraMatrix;
            scatterVolumeCS.SetMatrix("_InverseViewProj", viewproj.inverse);

            float phi = scatterMat.GetFloat("_LightDirectionPhi") / 180 * Mathf.PI;
            float theta = scatterMat.GetFloat("_LightDirectionTheta") / 180 * Mathf.PI;

            Vector3 sunDirection = new Vector3(Mathf.Cos(phi) * Mathf.Cos(theta), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Sin(theta));
            
            scatterVolumeCS.SetVector("_SunDirection", sunDirection);

            cmd.SetComputeTextureParam(scatterVolumeCS, scatterVolumeKernel, "_TransmittanceLut", _TransmittanceLutId);
            cmd.SetComputeTextureParam(scatterVolumeCS, scatterVolumeKernel, "_VolumeScattering", _VolumeScattering);
            cmd.DispatchCompute(scatterVolumeCS, scatterVolumeKernel, volumeScatteringResolution.x / 8, volumeScatteringResolution.y / 8, volumeScatteringResolution.z);

            // blend to scene
            scatterMat.SetVector("_SkyViewLutSize", new Vector4(1f / skyviewResolution.x, 1f / skyviewResolution.y, skyviewResolution.x, skyviewResolution.y));
            cmd.SetGlobalTexture("_SkyViewLut", _SkyViewLutId);
            cmd.SetGlobalTexture("_VolumeScattering", _VolumeScattering);
            cmd.Blit(source, destination, scatterMat, 0);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        /// <inheritdoc/>
        public override void FrameCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(_TransmittanceLutId);
            cmd.ReleaseTemporaryRT(_MultiSactteringLutId);
            cmd.ReleaseTemporaryRT(_SkyViewLutId);
        }
    }
}