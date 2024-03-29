﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PostProcessing;

/*
attention:
fxaa should use in srgb space;
fxaa shold use in LDR;
fxaa shold use bilinear filter;
*/

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class SSPR : MonoBehaviour
{
    //==== type

    class ShaderStrings
    {
        public static string blend = "Hidden/AdvancedRTR/blend";
    }
    class ShaderConstants
    {
        public static int _SourceRT = Shader.PropertyToID("_SourceRT");
        public static int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
        public static int _ResultRT = Shader.PropertyToID("_ResultRT");
        public static int _IntermediateRT = Shader.PropertyToID("_IntermediateRT");
        public static int _InverseProjectionMatrix = Shader.PropertyToID("_InverseProjectionMatrix");
        public static int _ProjectionMatrix = Shader.PropertyToID("_ProjectionMatrix");
        public static int _ViewMatrix = Shader.PropertyToID("_ViewMatrix");
        public static int _CameraPos = Shader.PropertyToID("_CameraPos");
        public static int _PlaneHeight = Shader.PropertyToID("_PlaneHeight");
        public static int _PlaneBoxMin = Shader.PropertyToID("_PlaneBoxMin");
        public static int _PlaneBoxMax = Shader.PropertyToID("_PlaneBoxMax");
        public static int _TextureSize = Shader.PropertyToID("_TextureSize");
        public static int _StretchThreshold = Shader.PropertyToID("_StretchThreshold");
        public static int _StretchIntensity = Shader.PropertyToID("_StretchIntensity");
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
        if (_SSPRBlendMat == null)
        {

            Shader sspr = Shader.Find(ShaderStrings.blend);
            _SSPRBlendMat = new Material(sspr);
            _SSPRBlendMat.hideFlags = HideFlags.HideAndDontSave;
        }
    }

    private void Render()
    {
        _CommandBuffer = new CommandBuffer();
        _CommandBuffer.name = "AdvancedRTR";

        _CommandBuffer.GetTemporaryRT(ShaderConstants._SourceRT, _Camera.pixelWidth, _Camera.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear, 1, false);

        //copy
        RuntimeUtilities.BuiltinBlit(_CommandBuffer, BuiltinRenderTextureType.CameraTarget, ShaderConstants._SourceRT);

        //SSPR
        _CommandBuffer.BeginSample("SSPR");

        #region clear

        _CommandBuffer.GetTemporaryRT(ShaderConstants._IntermediateRT, _Camera.pixelWidth, _Camera.pixelHeight, 0, FilterMode.Point, RenderTextureFormat.RInt, RenderTextureReadWrite.Linear, 1, true);

        int width = _Camera.pixelWidth;
        int height = _Camera.pixelHeight;

        int kernelClear = computeShader.FindKernel("CSMainClear");
        _CommandBuffer.SetComputeTextureParam(computeShader, kernelClear, ShaderConstants._IntermediateRT, ShaderConstants._IntermediateRT);

        _CommandBuffer.DispatchCompute(computeShader, kernelClear, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
        #endregion

        #region projection

        int kernelGet = computeShader.FindKernel("CSMainGet");
        _CommandBuffer.SetComputeTextureParam(computeShader, kernelGet, ShaderConstants._CameraDepthTexture, BuiltinRenderTextureType.ResolvedDepth);
        _CommandBuffer.SetComputeTextureParam(computeShader, kernelGet, ShaderConstants._IntermediateRT, ShaderConstants._IntermediateRT);
        _CommandBuffer.SetComputeVectorParam(computeShader, ShaderConstants._TextureSize, new Vector4(1f / width, 1f / height, width, height));
        _CommandBuffer.SetComputeVectorParam(computeShader, ShaderConstants._CameraPos, _Camera.transform.position);
        _CommandBuffer.SetComputeFloatParam(computeShader, ShaderConstants._PlaneHeight, _height);
        _CommandBuffer.SetComputeVectorParam(computeShader, ShaderConstants._PlaneBoxMin, _boxmin);
        _CommandBuffer.SetComputeVectorParam(computeShader, ShaderConstants._PlaneBoxMax, _boxmax);

        _CommandBuffer.SetComputeFloatParam(computeShader, ShaderConstants._StretchThreshold, _threshlod);
        _CommandBuffer.SetComputeFloatParam(computeShader, ShaderConstants._StretchIntensity, _intensity);

        var projectionMatrix = GL.GetGPUProjectionMatrix(_Camera.projectionMatrix, false);
        var vp = projectionMatrix * _Camera.worldToCameraMatrix;

        _CommandBuffer.SetComputeMatrixParam(computeShader, ShaderConstants._InverseProjectionMatrix, vp.inverse);
        _CommandBuffer.SetComputeMatrixParam(computeShader, ShaderConstants._ProjectionMatrix, vp);
        _CommandBuffer.SetComputeMatrixParam(computeShader, ShaderConstants._ViewMatrix, _Camera.worldToCameraMatrix);

        _CommandBuffer.DispatchCompute(computeShader, kernelGet, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);

        #endregion

        #region resolve

        _CommandBuffer.GetTemporaryRT(ShaderConstants._ResultRT, _Camera.pixelWidth, _Camera.pixelHeight, 0, FilterMode.Point, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear, 1, true);

        int kernelSet = computeShader.FindKernel("CSMainSet");
        _CommandBuffer.SetComputeTextureParam(computeShader, kernelSet, ShaderConstants._SourceRT, ShaderConstants._SourceRT);
        _CommandBuffer.SetComputeTextureParam(computeShader, kernelSet, ShaderConstants._IntermediateRT, ShaderConstants._IntermediateRT);
        _CommandBuffer.SetComputeTextureParam(computeShader, kernelSet, ShaderConstants._ResultRT, ShaderConstants._ResultRT);
        _CommandBuffer.SetComputeVectorParam(computeShader, ShaderConstants._TextureSize, new Vector4(1f / width, 1f / height, width, height));

        _CommandBuffer.DispatchCompute(computeShader, kernelSet, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);

        #endregion

        _CommandBuffer.EndSample("SSPR");

        _CommandBuffer.Blit(ShaderConstants._ResultRT, BuiltinRenderTextureType.CameraTarget, _SSPRBlendMat);

        _CommandBuffer.ReleaseTemporaryRT(ShaderConstants._SourceRT);
        _CommandBuffer.ReleaseTemporaryRT(ShaderConstants._IntermediateRT);
        _CommandBuffer.ReleaseTemporaryRT(ShaderConstants._ResultRT);
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

    // gizmos
    private void OnDrawGizmos()
    {
        Vector3 minPos = new Vector3(_boxmin.x, _height, _boxmin.y);
        Vector3 maxPos = new Vector3(_boxmax.x, _height, _boxmax.y);

        Gizmos.color = Color.green;
        Gizmos.DrawLine(minPos, minPos + new Vector3(0, 0, maxPos.z - minPos.z));
        Gizmos.DrawLine(minPos, minPos + new Vector3(maxPos.x - minPos.x, 0, 0));
        Gizmos.DrawLine(maxPos - new Vector3(0, 0, maxPos.z - minPos.z), maxPos);
        Gizmos.DrawLine(maxPos - new Vector3(maxPos.x - minPos.x, 0, 0), maxPos);
        Gizmos.color = Color.white;
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
    Material _SSPRBlendMat = null;

    //===serial data
    [SerializeField]
    float _threshlod = 0.0f;
    [SerializeField]
    float _intensity = 0.0f;
    [SerializeField]
    float _height = 0.0f;
    [SerializeField]
    Vector2 _boxmin = Vector2.zero;
    [SerializeField]
    Vector2 _boxmax = Vector2.one;
    [SerializeField]
    ComputeShader computeShader = null;
}
