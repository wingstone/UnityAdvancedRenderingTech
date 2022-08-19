namespace UnityEngine.Rendering.Universal
{
    public enum BufferType
    {
        CameraColor,
        Custom 
    }

    public class CylinderSkyRenderFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

            public Material blitMaterial = null;
            public int blitMaterialPassIndex = 0;
            public BufferType sourceType = BufferType.CameraColor;
            public BufferType destinationType = BufferType.CameraColor;
            public string sourceTextureId = "_SourceTexture";
            public string destinationTextureId = "_DestinationTexture";
            public ComputeShader multiScatteringComputerShader = null;
            public Shader scatteringBakeShader = null;
        }

        public Settings settings = new Settings();
        CylinderSkyRenderPass blitPass;

        public override void Create()
        {
            blitPass = new CylinderSkyRenderPass(name);
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
