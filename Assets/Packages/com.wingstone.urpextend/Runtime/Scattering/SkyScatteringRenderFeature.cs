namespace UnityEngine.Rendering.Universal
{
    public class SkyScatteringRenderFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

            public Material blitMaterial = null;
            public int blitMaterialPassIndex = 0;
            public ComputeShader multiScatteringComputerShader = null;
            public ComputeShader scatteringVolume = null;
            public Shader scatteringBakeShader = null;
        }

        public Settings settings = new Settings();
        SkyScatteringRenderPass blitPass;

        public override void Create()
        {
            blitPass = new SkyScatteringRenderPass(name);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings.blitMaterial == null)
            {
                Debug.LogWarningFormat("Missing Blit Material. {0} blit pass will not execute. Check for missing reference in the assigned renderer.", GetType().Name);
                return;
            }

            blitPass.renderPassEvent = settings.renderPassEvent;
            blitPass.settings = settings;
            renderer.EnqueuePass(blitPass);
        }
    }
}
