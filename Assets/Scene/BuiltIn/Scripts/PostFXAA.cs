using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PostProcessing;

// https://developer.download.nvidia.cn/assets/gamedev/files/sdk/11/FXAA_WhitePaper.pdf
// http://blog.simonrodriguez.fr/articles/30-07-2016_implementing_fxaa.html

/*
attention:
fxaa should use in srgb space;
fxaa shold use in LDR;
fxaa shold use bilinear filter;
*/

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class PostFXAA : MonoBehaviour
{
    //==== type

    class ShaderStrings
    {
        public static string fxaa = "Hidden/AdvancedRTR/FXAA";
    }
    class ShaderConstants
    {
        public static int _SourceRT = Shader.PropertyToID("_SourceRT");
        public static int _TaaTmpRT = Shader.PropertyToID("_TaaTmpRT");
        public static int Jitter = Shader.PropertyToID("_Jitter");

        //taa
        public static int Sharpness = Shader.PropertyToID("_Sharpness");
        public static int FinalBlendParameters = Shader.PropertyToID("_FinalBlendParameters");

        //plustaa
        public static int feedbackMin = Shader.PropertyToID("_FeedbackMin");
        public static int feedbackMax = Shader.PropertyToID("_FeedbackMax");

        public static int HistoryTex = Shader.PropertyToID("_HistoryTex");
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
        if (_FXAAMat == null)
        {

            Shader fxaa = Shader.Find(ShaderStrings.fxaa);
            _FXAAMat = new Material(fxaa);
            _FXAAMat.hideFlags = HideFlags.HideAndDontSave;
        }
    }

    private void Render()
    {
        _CommandBuffer = new CommandBuffer();
        _CommandBuffer.name = "AdvancedRTR";

        _CommandBuffer.GetTemporaryRT(ShaderConstants._SourceRT, _Camera.pixelWidth, _Camera.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);

        //copy
        RuntimeUtilities.BuiltinBlit(_CommandBuffer, BuiltinRenderTextureType.CameraTarget, ShaderConstants._SourceRT);

        //FXAA
        _CommandBuffer.BeginSample("FXAA");

        RuntimeUtilities.BlitFullscreenTriangle(_CommandBuffer, ShaderConstants._SourceRT, BuiltinRenderTextureType.CameraTarget, _FXAAMat, 0);

        _CommandBuffer.EndSample("FXAA");

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
    Material _FXAAMat = null;
}
