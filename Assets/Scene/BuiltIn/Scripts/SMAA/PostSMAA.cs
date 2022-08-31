using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
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
public class PostSMAA : MonoBehaviour
{
    //==== type

    class ShaderStrings
    {
        public static string smaa = "Hidden/AdvancedRTR/SMAA";
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

    private void OnPreCull()
    {
        if (_UseT2X)
            ConfigureJitteredProjectionMatrix();
    }

    private void OnPreRender()
    {
        UpdateRender();
    }

    private void OnPostRender()
    {
        // reset projection
        _Camera.ResetProjectionMatrix();
    }

    private void OnDisable()
    {
        EndRender();
    }

    //=====method

    void Initial()
    {
        _Camera.depthTextureMode = DepthTextureMode.MotionVectors;
    }

    private void Setting()
    {
        if (_SMAAMat == null)
        {
            _SMAAShader = Shader.Find(ShaderStrings.smaa);
            _SMAAMat = new Material(_SMAAShader);
            _SMAAMat.hideFlags = HideFlags.HideAndDontSave;
        }

        _SMAAMat.SetTexture("_AreaTex", _AreaTex);
        _SMAAMat.SetTexture("_SearchTex", _SearchTex);
    }

    /// <summary>
    /// Generates a jittered projection matrix for a given camera.
    /// </summary>
    /// <param name="camera">The camera to get a jittered projection matrix for.</param>
    /// <returns>A jittered projection matrix.</returns>
    public Matrix4x4 GetJitteredProjectionMatrix(Camera camera)
    {
        Matrix4x4 cameraProj;

        Vector2[] jitterVector = new Vector2[]
        {
            new Vector2(0.25f, -0.25f),
            new Vector2(-0.25f, 0.25f)
        };
        Vector2 jitter = jitterVector[m_HistoryPingPong % 2];

        cameraProj = camera.orthographic
            ? UnityEngine.Rendering.PostProcessing.RuntimeUtilities.GetJitteredOrthographicProjectionMatrix(camera, jitter)
            : UnityEngine.Rendering.PostProcessing.RuntimeUtilities.GetJitteredPerspectiveProjectionMatrix(camera, jitter);

        return cameraProj;
    }

    /// <summary>
    /// Prepares the jittered and non jittered projection matrices.
    /// </summary>
    /// <param name="context">The current post-processing context.</param>
    public void ConfigureJitteredProjectionMatrix()
    {
        var camera = _Camera;
        camera.nonJitteredProjectionMatrix = camera.projectionMatrix;
        camera.projectionMatrix = GetJitteredProjectionMatrix(camera);
        camera.useJitteredProjectionMatrixForTransparentRendering = false;
    }

    RenderTexture CheckHistory(int id)
    {

        if (_HistoryRT == null)
            _HistoryRT = new RenderTexture[2];

        var rt = _HistoryRT[id];

        if (rt == null || !rt.IsCreated())
        {
            RenderTexture.ReleaseTemporary(rt);
            rt = RenderTexture.GetTemporary(_Camera.pixelWidth, _Camera.pixelHeight, 0, RenderTextureFormat.ARGBHalf);
            rt.filterMode = FilterMode.Bilinear;
            rt.name = "Taa" + id;

            _HistoryRT[id] = rt;
        }
        else if (rt.width != _Camera.pixelWidth || rt.height != _Camera.pixelHeight)
        {
            var rt2 = RenderTexture.GetTemporary(_Camera.pixelWidth, _Camera.pixelHeight, 0, RenderTextureFormat.ARGBHalf);
            rt2.filterMode = FilterMode.Bilinear;
            rt2.name = "Taa" + id;

            RenderTexture.ReleaseTemporary(rt);

            _HistoryRT[id] = rt2;
        }

        return _HistoryRT[id];
    }

    private void Render()
    {
        _CommandBuffer = new CommandBuffer();
        _CommandBuffer.name = "AdvancedRTR";

        // get rt
        _CommandBuffer.GetTemporaryRT(ShaderConstants._SourceRT, _Camera.pixelWidth, _Camera.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.RGB111110Float);
        _CommandBuffer.GetTemporaryRT(ShaderConstants._EdgeRT, _Camera.pixelWidth, _Camera.pixelHeight, 24, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
        _CommandBuffer.GetTemporaryRT(ShaderConstants._BlendWeightRT, _Camera.pixelWidth, _Camera.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);

        // copy
        PostProcessing.RuntimeUtilities.BuiltinBlit(_CommandBuffer, BuiltinRenderTextureType.CameraTarget, ShaderConstants._SourceRT);

        // SMAA
        _CommandBuffer.BeginSample("SMAA");

        _CommandBuffer.SetGlobalInt("_JitterIndex", m_HistoryPingPong % 2);
        _CommandBuffer.SetGlobalInt("_UseT2X", _UseT2X ? 1 : 0);
        if (_UseT2X) _CommandBuffer.EnableShaderKeyword("SMAA_REPROJECTION");
        else _CommandBuffer.DisableShaderKeyword("SMAA_REPROJECTION");
        int pp = m_HistoryPingPong;
        var historyRead = CheckHistory(++pp % 2);
        var historyWrite = CheckHistory(++pp % 2);
        m_HistoryPingPong = ++pp % 2;

        // edge
        _CommandBuffer.SetGlobalTexture("_MainTex", ShaderConstants._SourceRT);
        _CommandBuffer.SetRenderTarget(ShaderConstants._EdgeRT);
        _CommandBuffer.ClearRenderTarget(true, true, new Color(0,0,0,0));
        _CommandBuffer.DrawMesh(PostProcessing.RuntimeUtilities.fullscreenTriangle, Matrix4x4.identity, _SMAAMat, 1,0);
        // weight
        _CommandBuffer.SetGlobalTexture("_MainTex", ShaderConstants._EdgeRT);
        _CommandBuffer.SetRenderTarget(ShaderConstants._BlendWeightRT, (RenderTargetIdentifier)ShaderConstants._EdgeRT);
        _CommandBuffer.ClearRenderTarget(false, true, new Color(0,0,0,0));
        _CommandBuffer.DrawMesh(PostProcessing.RuntimeUtilities.fullscreenTriangle, Matrix4x4.identity, _SMAAMat, 1,1);
        if (_UseT2X)
        {
            // blend
            _CommandBuffer.SetGlobalTexture("_BlendWeightTex", ShaderConstants._BlendWeightRT);
            PostProcessing.RuntimeUtilities.BlitFullscreenTriangle(_CommandBuffer, ShaderConstants._SourceRT, historyWrite, _SMAAMat, 2);
            // resolve
            _CommandBuffer.SetGlobalTexture("_PreviousTex", historyRead);
            PostProcessing.RuntimeUtilities.BlitFullscreenTriangle(_CommandBuffer, historyWrite, BuiltinRenderTextureType.CameraTarget, _SMAAMat, 3);
        }
        else
        {
            // blend
            _CommandBuffer.SetGlobalTexture("_BlendWeightTex", ShaderConstants._BlendWeightRT);
            PostProcessing.RuntimeUtilities.BlitFullscreenTriangle(_CommandBuffer, ShaderConstants._SourceRT, BuiltinRenderTextureType.CameraTarget, _SMAAMat, 2);
        }

        // release rt
        _CommandBuffer.ReleaseTemporaryRT(ShaderConstants._SourceRT);
        _CommandBuffer.ReleaseTemporaryRT(ShaderConstants._EdgeRT);
        _CommandBuffer.ReleaseTemporaryRT(ShaderConstants._BlendWeightRT);

        _CommandBuffer.EndSample("SMAA");

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
    Material _SMAAMat = null;
    Shader _SMAAShader = null;
    RenderTexture[] _HistoryRT = null;
    int m_HistoryPingPong = 0;

    //===control
    [SerializeField]
    public Texture2D _AreaTex = null;
    [SerializeField]
    public Texture2D _SearchTex = null;
    public bool _UseT2X = true;
}
