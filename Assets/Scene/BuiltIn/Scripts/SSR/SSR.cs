using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PostProcessing;

// https://blog.csdn.net/puppet_master/article/details/80808486
// http://jcgt.org/published/0003/04/04/paper.pdf
// http://www.cse.chalmers.se/edu/year/2018/course/TDA361/Advanced%20Computer%20Graphics/Screen-space%20reflections.pdf

/*
attention:
*/

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class SSR : MonoBehaviour
{
    //==== type

    class ShaderStrings
    {
        public static string ssr = "Hidden/AdvancedRTR/SSR";
        public static string blend = "Hidden/AdvancedRTR/blend";
    }
    class ShaderConstants
    {
        public static int _SourceRT = Shader.PropertyToID("_SourceRT");
        public static int _TestRT = Shader.PropertyToID("_TestResult");
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

    // only run in defer
    bool isSupport()
    {
        bool support = true;
        support = support && SystemInfo.supportsMotionVectors;
        support = support && _Camera.actualRenderingPath == RenderingPath.DeferredShading;
        support = support && SystemInfo.supportsComputeShaders;
        return support;
    }

    DepthTextureMode GetCameraFlags()
    {
        return DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
    }
    CameraEvent CameraPoint
    {
        get
        {
            return CameraEvent.BeforeImageEffectsOpaque;
        }
    }

    void Initial()
    {
    }

    private void Setting()
    {
        if (_SSRMat == null)
        {
            Shader ssr = Shader.Find(ShaderStrings.ssr);
            _SSRMat = new Material(ssr);
            _SSRMat.hideFlags = HideFlags.HideAndDontSave;
        }
        int size = 1;
        var screenSpaceProjectionMatrix = new Matrix4x4();
        screenSpaceProjectionMatrix.SetRow(0, new Vector4(size * 0.5f, 0f, 0f, size * 0.5f));
        screenSpaceProjectionMatrix.SetRow(1, new Vector4(0f, size * 0.5f, 0f, size * 0.5f));
        screenSpaceProjectionMatrix.SetRow(2, new Vector4(0f, 0f, 1f, 0f));
        screenSpaceProjectionMatrix.SetRow(3, new Vector4(0f, 0f, 0f, 1f));

        var projectionMatrix = GL.GetGPUProjectionMatrix(_Camera.projectionMatrix, false);
        screenSpaceProjectionMatrix = projectionMatrix;

        _SSRMat.SetMatrix("_ViewMatrix", _Camera.worldToCameraMatrix);
        _SSRMat.SetMatrix("_InverseViewMatrix", _Camera.worldToCameraMatrix.inverse);
        _SSRMat.SetMatrix("_InverseProjectionMatrix", projectionMatrix.inverse);
        _SSRMat.SetMatrix("_ScreenSpaceProjectionMatrix", projectionMatrix);
        _SSRMat.SetFloat("_RayMatchSteps", _RayMatchSteps);
        _SSRMat.SetFloat("_RayMatchDistance", _RayMatchDistance);
        _SSRMat.SetFloat("_DepthThickness", _DepthThickness);
        _SSRMat.SetFloat("_ScreenDistance", _ScreenDistance);
        _SSRMat.SetFloat("_ThicknessScale", _ThicknessScale);

        if(_BlendMat == null)
        {
            Shader blend = Shader.Find(ShaderStrings.blend);
            _BlendMat = new Material(blend);
            _BlendMat.hideFlags = HideFlags.HideAndDontSave;
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
        _CommandBuffer.BeginSample("SSR");

        _CommandBuffer.GetTemporaryRT(ShaderConstants._TestRT, _Camera.pixelWidth, _Camera.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        RuntimeUtilities.BlitFullscreenTriangle(_CommandBuffer, ShaderConstants._SourceRT, ShaderConstants._TestRT, _SSRMat, 0);

        RuntimeUtilities.BuiltinBlit(_CommandBuffer, ShaderConstants._TestRT, BuiltinRenderTextureType.CameraTarget, _BlendMat, 0);

        _CommandBuffer.EndSample("SSR");

        _CommandBuffer.ReleaseTemporaryRT(ShaderConstants._SourceRT);
        _CommandBuffer.ReleaseTemporaryRT(ShaderConstants._TestRT);
        _Camera.AddCommandBuffer(CameraPoint, _CommandBuffer);
    }

    private void UpdateRender()
    {
        if (_CommandBuffer != null)
            _Camera.RemoveCommandBuffer(CameraPoint, _CommandBuffer);

        Setting();
        Render();
    }

    private void EndRender()
    {
        if (_CommandBuffer != null)
            _Camera.RemoveCommandBuffer(CameraPoint, _CommandBuffer);
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
    Material _SSRMat = null;
    Material _BlendMat = null;

    [Range(0, 100)]
    [SerializeField]
    int _RayMatchSteps = 20;
    [Header("Ray Match Parmaters")]
    [Range(0, 5f)]
    [SerializeField]
    float _RayMatchDistance = 3.0f;
    [Range(0, 2f)]
    [SerializeField]
    float _DepthThickness = 0.1f;
    [Header("DDA Tracing Parmaters")]
    [Range(1, 512f)]
    [SerializeField]
    float _ScreenDistance = 1.0f;
    [Range(0.3f, 5.0f)]
    [SerializeField]
    float _ThicknessScale = 1.0f;
}
