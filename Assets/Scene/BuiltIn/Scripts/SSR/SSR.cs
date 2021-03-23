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
public class SSR : MonoBehaviour
{
    //==== type

    class ShaderStrings
    {
        public static string ssr = "Hidden/AdvancedRTR/SSR";
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
        _SSRMat.SetFloat("_RayMatchStep", _RayMatchStep);
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

        RuntimeUtilities.BuiltinBlit(_CommandBuffer, ShaderConstants._TestRT, BuiltinRenderTextureType.CameraTarget);

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
    [SerializeField]
    float _RayMatchStep = 0.01f;
}
