using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PostProcessing;
using UnityEditor;

// http://www.iryoku.com/smaa/
// http://iryoku.com/aacourse/downloads/13-Anti-Aliasing-Methods-in-CryENGINE-3.pdf

/*
attention:
smaa should use in srgb space;
smaa shold use in LDR;
smaa shold use bilinear filter;
*/

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class PostMLAA : MonoBehaviour
{
    //==== type

    class ShaderStrings
    {
        public static string mlaa = "Hidden/AdvancedRTR/MLAA";
    }
    class ShaderConstants
    {
        public static int _SourceRT = Shader.PropertyToID("_SourceRT");
        public static int _EdgeRT = Shader.PropertyToID("_EdgeRT");
        public static int _BlendWeightRT = Shader.PropertyToID("_BlendWeightRT");
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
        if (_MLAAMat == null)
        {
            _MLAAShader = Shader.Find(ShaderStrings.mlaa);
            _MLAAMat = new Material(_MLAAShader);
            _MLAAMat.hideFlags = HideFlags.HideAndDontSave;
        }

        _MLAAMat.SetTexture("_AreaTex", _AreaTex);
        // _MLAAMat.SetFloat("_MaxSearchSteps", 8);    //this number is _AreaDistance/4;
        // _MLAAMat.SetFloat("_AreaDistance", 32);     //this number is _AreaTex.size/5;
    }

    private void Render()
    {
        _CommandBuffer = new CommandBuffer();
        _CommandBuffer.name = "AdvancedRTR";

        // get rt
        _CommandBuffer.GetTemporaryRT(ShaderConstants._SourceRT, _Camera.pixelWidth, _Camera.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
        _CommandBuffer.GetTemporaryRT(ShaderConstants._EdgeRT, _Camera.pixelWidth, _Camera.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
        _CommandBuffer.GetTemporaryRT(ShaderConstants._BlendWeightRT, _Camera.pixelWidth, _Camera.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);

        // copy
        RuntimeUtilities.BuiltinBlit(_CommandBuffer, BuiltinRenderTextureType.CameraTarget, ShaderConstants._SourceRT);

        // MLAA
        _CommandBuffer.BeginSample("MLAA");

        // edge
        RuntimeUtilities.BlitFullscreenTriangle(_CommandBuffer, ShaderConstants._SourceRT, ShaderConstants._EdgeRT, _MLAAMat, 0);
        // weight
        RuntimeUtilities.BlitFullscreenTriangle(_CommandBuffer, ShaderConstants._EdgeRT, ShaderConstants._BlendWeightRT, _MLAAMat, 1);
        // blend
        _CommandBuffer.SetGlobalTexture("_BlendWeightTex", ShaderConstants._BlendWeightRT);
        RuntimeUtilities.BlitFullscreenTriangle(_CommandBuffer, ShaderConstants._SourceRT, BuiltinRenderTextureType.CameraTarget, _MLAAMat, 2);

        // release rt
        _CommandBuffer.ReleaseTemporaryRT(ShaderConstants._SourceRT);
        _CommandBuffer.ReleaseTemporaryRT(ShaderConstants._EdgeRT);
        _CommandBuffer.ReleaseTemporaryRT(ShaderConstants._BlendWeightRT);

        _CommandBuffer.EndSample("MLAA");

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
    Material _MLAAMat = null;
    Shader _MLAAShader = null;

    //===control
    [SerializeField]
    public Texture2D _AreaTex = null;
}
