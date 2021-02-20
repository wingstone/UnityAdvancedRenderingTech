using System;
using UnityEngine;
using UnityEngine.Rendering;
using PostProcessing;

[ExecuteAlways]
// [ImageEffectAllowedInSceneView]
[RequireComponent(typeof(Camera))]
public class GlobalPostEffect : MonoBehaviour
{
    //==== type

    class ShaderStrings
    {
        public static string uber = "Hidden/AdvancedRTR/Uber";
        public static string taa = "Hidden/AdvancedRTR/PlusTaa";
    }
    class ShaderConstants
    {
        public static int _SourceRT = Shader.PropertyToID("_SourceRT");
        public static int _TaaTmpRT = Shader.PropertyToID("_TaaTmpRT");
        public static int Jitter = Shader.PropertyToID("_Jitter");
        public static int Sharpness = Shader.PropertyToID("_Sharpness");
        public static int feedbackMin = Shader.PropertyToID("_FeedbackMin");
        public static int feedbackMax = Shader.PropertyToID("_FeedbackMax");
        public static int FinalBlendParameters = Shader.PropertyToID("_FinalBlendParameters");
        public static int HistoryTex = Shader.PropertyToID("_HistoryTex");
    }

    //====mono

    private void OnEnable()
    {
        Initial();
        UpdateRender();
    }

    private void OnPreCull()
    {
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
        _Camera.depthTextureMode = DepthTextureMode.None;
    }

    //=====method

    Vector2 GenerateRandomOffset()
    {
        // The variance between 0 and the actual halton sequence values reveals noticeable instability
        // in Unity's shadow maps, so we avoid index 0.
        var offset = new Vector2(
                HaltonSeq.Get((sampleIndex & 1023) + 1, 2) - 0.5f,
                HaltonSeq.Get((sampleIndex & 1023) + 1, 3) - 0.5f
            );

        if (++sampleIndex >= k_SampleCount)
            sampleIndex = 0;

        return offset;
    }

    /// <summary>
    /// Generates a jittered projection matrix for a given camera.
    /// </summary>
    /// <param name="camera">The camera to get a jittered projection matrix for.</param>
    /// <returns>A jittered projection matrix.</returns>
    public Matrix4x4 GetJitteredProjectionMatrix(Camera camera)
    {
        Matrix4x4 cameraProj;
        jitter = GenerateRandomOffset();

        if (jitteredMatrixFunc != null)
        {
            cameraProj = jitteredMatrixFunc(camera, jitter);
        }
        else
        {
            cameraProj = camera.orthographic
                ? RuntimeUtilities.GetJitteredOrthographicProjectionMatrix(camera, jitter)
                : RuntimeUtilities.GetJitteredPerspectiveProjectionMatrix(camera, jitter);
        }

        jitter = new Vector2(jitter.x / camera.pixelWidth, jitter.y / camera.pixelHeight);
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

    RenderTexture CheckHistory(int id, CommandBuffer cmd)
    {

        if (_HistoryRT == null)
            _HistoryRT = new RenderTexture[2];

        var rt = _HistoryRT[id];

        if (m_ResetHistory || rt == null || !rt.IsCreated())
        {
            RenderTexture.ReleaseTemporary(rt);
            rt = RenderTexture.GetTemporary(_Camera.pixelWidth, _Camera.pixelHeight, 0, RenderTextureFormat.ARGBHalf);
            rt.filterMode = FilterMode.Bilinear;
            rt.name = "Taa" + id;

            cmd.Blit(BuiltinRenderTextureType.CameraTarget, rt);

            _HistoryRT[id] = rt;
        }
        else if (rt.width != _Camera.pixelWidth || rt.height != _Camera.pixelHeight)
        {
            var rt2 = RenderTexture.GetTemporary(_Camera.pixelWidth, _Camera.pixelHeight, 0, RenderTextureFormat.ARGBHalf);
            rt2.filterMode = FilterMode.Bilinear;
            rt2.name = "Taa" + id;

            cmd.Blit(rt, rt2);
            RenderTexture.ReleaseTemporary(rt);

            _HistoryRT[id] = rt2;
        }

        return _HistoryRT[id];
    }

    void Initial()
    {
        _Camera.forceIntoRenderTexture = true;
        _Camera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
    }

    private void Setting()
    {
        if (_UberMat == null)
        {

            Shader uber = Shader.Find(ShaderStrings.uber);
            _UberMat = new Material(uber);
            _UberMat.hideFlags = HideFlags.HideAndDontSave;
        }
        if (_TaaMat == null)
        {

            Shader taa = Shader.Find(ShaderStrings.taa);
            _TaaMat = new Material(taa);
            _TaaMat.hideFlags = HideFlags.HideAndDontSave;
        }
    }

    private void Render()
    {
        _CommandBuffer = new CommandBuffer();
        _CommandBuffer.name = "AdvancedRTR";

        _CommandBuffer.GetTemporaryRT(ShaderConstants._TaaTmpRT, _Camera.pixelWidth, _Camera.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);

        _CommandBuffer.GetTemporaryRT(ShaderConstants._SourceRT, _Camera.pixelWidth, _Camera.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);

        //copy
        _CommandBuffer.Blit(BuiltinRenderTextureType.CameraTarget, ShaderConstants._SourceRT);

        //taa
        _CommandBuffer.BeginSample("TemporalAntialiasing");

        int pp = m_HistoryPingPong;
        var historyRead = CheckHistory(++pp % 2, _CommandBuffer);
        var historyWrite = CheckHistory(++pp % 2, _CommandBuffer);
        m_HistoryPingPong = ++pp % 2;

        const float kMotionAmplification = 100f * 60f;
        _TaaMat.SetVector(ShaderConstants.Jitter, jitter);
        _TaaMat.SetFloat(ShaderConstants.Sharpness, sharpness);
        _TaaMat.SetFloat(ShaderConstants.feedbackMin, feedbackMin);
        _TaaMat.SetFloat(ShaderConstants.feedbackMax, feedbackMax);
        _TaaMat.SetVector(ShaderConstants.FinalBlendParameters, new Vector4(stationaryBlending, motionBlending, kMotionAmplification, 0f));
        _TaaMat.SetTexture(ShaderConstants.HistoryTex, historyRead);

        m_Mrt[0] = ShaderConstants._TaaTmpRT;
        m_Mrt[1] = historyWrite;

        // _CommandBuffer.SetGlobalTexture(ShaderConstants._MainTex, BuiltinRenderTextureType.CameraTarget);
        // _CommandBuffer.SetRenderTarget(m_Mrt, BuiltinRenderTextureType.CameraTarget);
        // _CommandBuffer.DrawMesh(RuntimeUtilities.fullscreenTriangle, Matrix4x4.identity, _TaaMat, 0, 0);
        RuntimeUtilities.BlitFullscreenTriangle(_CommandBuffer, ShaderConstants._SourceRT, m_Mrt, BuiltinRenderTextureType.CameraTarget, _TaaMat, 0);

        _CommandBuffer.EndSample("TemporalAntialiasing");

        m_ResetHistory = false;

        _CommandBuffer.Blit(ShaderConstants._TaaTmpRT, BuiltinRenderTextureType.CameraTarget, _UberMat);

        _CommandBuffer.ReleaseTemporaryRT(ShaderConstants._TaaTmpRT);
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

        for (int i = 0; i < _HistoryRT.Length; i++)
        {
            if (_HistoryRT[i] != null)
                RenderTexture.ReleaseTemporary(_HistoryRT[i]);
            _HistoryRT[i] = null;
        }
    }

    //===data
    [Range(0f, 3f)]
    public float sharpness = 0.25f;
    [Range(0f, 0.99f)]
    public float stationaryBlending = 0.95f;

    [Range(0f, 0.99f)]
    public float motionBlending = 0.85f;

    //plus data
    [Range(0.0f, 1.0f)]
    public float feedbackMin = 0.88f;
    [Range(0.0f, 1.0f)]
    public float feedbackMax = 0.97f;

    public Func<Camera, Vector2, Matrix4x4> jitteredMatrixFunc;
    public Vector2 jitter { get; private set; }

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
    Material _UberMat = null;

    int sampleIndex = 0;
    const int k_SampleCount = 8;
    int m_HistoryPingPong = 0;
    bool m_ResetHistory = true;
    Material _TaaMat = null;
    RenderTexture[] _HistoryRT = new RenderTexture[2];
    readonly RenderTargetIdentifier[] m_Mrt = new RenderTargetIdentifier[2];
}

