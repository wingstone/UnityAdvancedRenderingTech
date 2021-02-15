using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class UberRenderPassFeature : ScriptableRendererFeature
{

    [System.Serializable]
    public class BlitSettings
    {
        public RenderPassEvent Event = RenderPassEvent.AfterRenderingOpaques;
        public Shader ubShader = null;
    }

    public BlitSettings settings = new BlitSettings();

    /// <summary>
    /// Copy the given color buffer to the given destination color buffer.
    ///
    /// You can use this pass to copy a color buffer to the destination,
    /// so you can use it later in rendering. For example, you can copy
    /// the opaque texture to use it for distortion effects.
    /// </summary>
    internal class UberRenderPass : ScriptableRenderPass
    {

        public Material blitMaterial = null;

        private RenderTargetIdentifier source { get; set; }

        RenderTargetHandle m_TemporaryColorTexture;
        bool m_UsefulShader = false;
        string m_ProfilerTag;

        /// <summary>
        /// Create the CopyColorPass
        /// </summary>
        public UberRenderPass(RenderPassEvent renderPassEvent, Shader uberShader, string tag)
        {
            this.renderPassEvent = renderPassEvent;
            m_ProfilerTag = tag;
            m_UsefulShader = uberShader != null;
            if (!m_UsefulShader)
                return;

            blitMaterial = new Material(uberShader);
            blitMaterial.hideFlags = HideFlags.HideAndDontSave;
            m_TemporaryColorTexture.Init("_MainTex");

        }

        /// <summary>
        /// Configure the pass with the source and destination to execute on.
        /// </summary>
        /// <param name="source">Source Render Target</param>
        /// <param name="destination">Destination Render Target</param>
        public void Setup(RenderTargetIdentifier source)
        {
            this.source = source;
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);

            RenderTextureDescriptor opaqueDesc = renderingData.cameraData.cameraTargetDescriptor;
            opaqueDesc.depthBufferBits = 0;

            if (!m_UsefulShader)
                return;

            // Can't read and write to same color target, create a temp render target to blit. 
            cmd.GetTemporaryRT(m_TemporaryColorTexture.id, opaqueDesc, FilterMode.Bilinear);
            Blit(cmd, source, m_TemporaryColorTexture.Identifier());
            for (int i = 0; i < blitMaterial.passCount; i++)
            {
                cmd.Blit(m_TemporaryColorTexture.Identifier(), source, blitMaterial, i);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        /// <inheritdoc/>
        public override void FrameCleanup(CommandBuffer cmd)
        {
            if (!m_UsefulShader)
                return;

            cmd.ReleaseTemporaryRT(m_TemporaryColorTexture.id);
        }
    }

    UberRenderPass uberPass;

    public override void Create()
    {
        uberPass = new UberRenderPass(settings.Event, settings.ubShader, name);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var src = renderer.cameraColorTarget;
        uberPass.Setup(src);
        renderer.EnqueuePass(uberPass);
    }


}


