using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class VolumetricFog : MonoBehaviour
{
    Vector3Int m_VolumeResolution = new Vector3Int(64, 64, 128);


    private void OnEnable()
    {
        commandBuffer = new CommandBuffer();
        commandBuffer.name = "AdvancedRTR";
        Camera camera = GetComponent<Camera>();
        camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, commandBuffer);


        injectRt = new RenderTexture(m_VolumeResolution.x, m_VolumeResolution.y, 0, RenderTextureFormat.ARGBFloat);
        injectRt.volumeDepth = m_VolumeResolution.z;
        injectRt.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        injectRt.enableRandomWrite = true;
        injectRt.Create();

        scatteringRt = new RenderTexture(m_VolumeResolution.x, m_VolumeResolution.y, 0, RenderTextureFormat.ARGBFloat);
        scatteringRt.volumeDepth = m_VolumeResolution.z;
        scatteringRt.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        scatteringRt.enableRandomWrite = true;
        scatteringRt.Create();
    }

    private void OnPreRender()
    {
        Render();
    }

    private void OnDisable()
    {
        Camera camera = GetComponent<Camera>();
        camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, commandBuffer);
        commandBuffer.Release();
    }

    private void Render()
    {
        commandBuffer.Clear();
        Camera camera = GetComponent<Camera>();

        computeShader.SetVector("_LightDirection", -light.transform.forward);
        computeShader.SetVector("_VolumeResolution", new Vector4(m_VolumeResolution.x, m_VolumeResolution.y, m_VolumeResolution.z));
        computeShader.SetVector("_CameraPos",camera.transform.position);
        computeShader.SetVector("_lightRadiance", light.color*light.intensity);
        computeShader.SetFloat("_Anisotropy", _Anisotropy);
        computeShader.SetFloat("_SeaLevelScattering", _SeaLevelScattering);
        computeShader.SetFloat("_ScaleHeight", _ScaleHeight);
        var projectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            Matrix4x4 viewproj = projectionMatrix * camera.worldToCameraMatrix;
        computeShader.SetMatrix("_InverseViewProj", viewproj.inverse);
        computeShader.SetVector("_RayCenter", camera.transform.forward);
        Vector3 up = camera.transform.up*Mathf.Tan(Mathf.Deg2Rad*camera.fieldOfView/2);
        computeShader.SetVector("_RayOffsetY", up);
        computeShader.SetVector("_RayOffsetX", camera.transform.right*up.magnitude*camera.aspect);

        int kernel = computeShader.FindKernel("injectInscattering");
        RenderTexture shadowmap = fogLightBind.RawShadow;
        commandBuffer.SetComputeTextureParam(computeShader, kernel, "_ShadowMap", shadowmap);
        commandBuffer.SetComputeTextureParam(computeShader, kernel, "_InjectRT", injectRt);
        commandBuffer.DispatchCompute(computeShader, kernel, m_VolumeResolution.x/8, m_VolumeResolution.y/8, m_VolumeResolution.z);

        int kernel1 = computeShader.FindKernel("accomuScattering");

        commandBuffer.SetComputeTextureParam(computeShader, kernel1, "_InjectRT", injectRt);
        commandBuffer.SetComputeTextureParam(computeShader, kernel1, "_ScatteringRT", scatteringRt);
        commandBuffer.DispatchCompute(computeShader, kernel1, m_VolumeResolution.x/8, m_VolumeResolution.y/8, m_VolumeResolution.z);

        commandBuffer.SetGlobalVector("_RayCenter", camera.transform.forward);
        commandBuffer.SetGlobalVector("_RayOffsetY", up);
        commandBuffer.SetGlobalVector("_RayOffsetX", camera.transform.right*up.magnitude*camera.aspect);
        commandBuffer.SetGlobalMatrix("_InverseViewProj", viewproj.inverse);
        commandBuffer.SetGlobalVector("_CameraPos",camera.transform.position);
        commandBuffer.SetGlobalVector("_VolumeResolution", new Vector4(m_VolumeResolution.x, m_VolumeResolution.y, m_VolumeResolution.z));
        commandBuffer.SetGlobalTexture("_ScatteringRT", scatteringRt);
        commandBuffer.Blit(null, BuiltinRenderTextureType.CameraTarget, volumeMaterial);
    }

    CommandBuffer commandBuffer = null;
    RenderTexture injectRt = null;
    RenderTexture scatteringRt = null;

    public ComputeShader computeShader = null;
    public Material volumeMaterial = null;
    public FogLightBind fogLightBind = null;
    public Light light = null;
    public float _Anisotropy;
    [Range(1e-5f, 5f)]
    public float _SeaLevelScattering;
    [Range(1e-5f, 100f)]
    public float _ScaleHeight;
}
