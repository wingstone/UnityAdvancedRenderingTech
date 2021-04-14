using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PostProcessing;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class PostPaper : MonoBehaviour
{
    //==== type

    class ShaderStrings
    {
        public static string paper = "Hidden/AdvancedRTR/Paper";
    }
    class ShaderConstants
    {
        public static int _SourceRT = Shader.PropertyToID("_SourceRT");
        public static int _BlurRadius = Shader.PropertyToID("_BlurRadius");
        public static int _PaperNoise = Shader.PropertyToID("_PaperNoise");
        public static int _NoiseTiling = Shader.PropertyToID("_NoiseTiling");
    }
    public enum PaperMethod
    {
        KuwaharaFiler,
        PaperFilter,
    }

    //====mono

    private void OnEnable()
    {
        Initial();
        UpdateRender();
    }

    private void OnPreRender()
    {
        UpdateRender();
    }

    private void OnDisable()
    {
        EndRender();
    }

    //=====method

    void Initial()
    {
        _Camera.depthTextureMode = DepthTextureMode.None;
    }

    private void Setting()
    {
        if (_PaperMat == null)
        {
            Shader paper = Shader.Find(ShaderStrings.paper);
            _PaperMat = new Material(paper);
            _PaperMat.hideFlags = HideFlags.HideAndDontSave;
        }
        _PaperMat.SetFloat(ShaderConstants._BlurRadius, BlurRadius);
        _PaperMat.SetTexture(ShaderConstants._PaperNoise, paperNoise);
        _PaperMat.SetFloat(ShaderConstants._NoiseTiling, noiseTiling);
    }

    private void Render()
    {
        _CommandBuffer = new CommandBuffer();
        _CommandBuffer.name = "AdvancedRTR";

        _CommandBuffer.GetTemporaryRT(ShaderConstants._SourceRT, _Camera.pixelWidth, _Camera.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);

        //copy
        RuntimeUtilities.BuiltinBlit(_CommandBuffer, BuiltinRenderTextureType.CameraTarget, ShaderConstants._SourceRT);

        //Paper
        _CommandBuffer.BeginSample("Paper");

        RuntimeUtilities.BlitFullscreenTriangle(_CommandBuffer, ShaderConstants._SourceRT, BuiltinRenderTextureType.CameraTarget, _PaperMat, (int)BlurMethod);

        _CommandBuffer.EndSample("Paper");

        _CommandBuffer.ReleaseTemporaryRT(ShaderConstants._SourceRT);
        _Camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, _CommandBuffer);
    }

    private void UpdateRender()
    {
        if (_CommandBuffer != null)
            _Camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _CommandBuffer);

        Setting();
        Render();
    }

    private void EndRender()
    {
        if (_CommandBuffer != null)
            _Camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _CommandBuffer);
    }

    //===data
    Camera _Camera
    {
        get
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }
            return _camera;
        }
    }
    Camera _camera = null;
    CommandBuffer _CommandBuffer = null;
    Material _PaperMat = null;

    //====display
    [SerializeField]
    PaperMethod BlurMethod = PaperMethod.KuwaharaFiler;
    [SerializeField]
    [Range(0.1f, 10f)]
    float BlurRadius = 5;
    [SerializeField]
    Texture2D paperNoise = null;
    [SerializeField]
    [Range(1f, 100f)]
    float noiseTiling = 10;
}
