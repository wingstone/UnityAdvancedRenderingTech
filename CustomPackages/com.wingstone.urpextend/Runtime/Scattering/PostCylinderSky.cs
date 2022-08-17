using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PostCylinderSky : MonoBehaviour
{

    public Material bakeMat;
    public ComputeShader computeShader;
    public Vector2Int transMittanceResolution = new Vector2Int(256, 64);
    public Vector2Int skyviewResolution = new Vector2Int(256, 128);
    public Vector2Int multiScatteringResolution = new Vector2Int(64, 64);
    private RenderTexture transmittanceLut;
    private RenderTexture multiScatteringLut;
    private RenderTexture skyviewLut;


    // Start is called before the first frame update
    void Awake()
    {

        transmittanceLut = new RenderTexture(transMittanceResolution.x, transMittanceResolution.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);

        multiScatteringLut = new RenderTexture(multiScatteringResolution.x, multiScatteringResolution.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);

        skyviewLut = new RenderTexture(skyviewResolution.x, skyviewResolution.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
    }

    private void OnDestroy()
    {
        Object.Destroy(transmittanceLut);
        Object.Destroy(multiScatteringLut);
        Object.Destroy(skyviewLut);
    }
    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        bakeMat.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
        Graphics.Blit(transmittanceLut, transmittanceLut, bakeMat, 0);

        multiScatteringLut.enableRandomWrite = true;
        multiScatteringLut.Create();

        int kernel = computeShader.FindKernel("CSMain");
        computeShader.SetTexture(kernel, "_TransmittanceLut", transmittanceLut);
        computeShader.SetTexture(kernel, "_Result", multiScatteringLut);
        computeShader.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
        computeShader.SetVector("_MultiScattering_Size", new Vector4(1f / multiScatteringResolution.x, 1f / multiScatteringResolution.y, multiScatteringResolution.x, multiScatteringResolution.y));
        computeShader.SetVector("_GroundColor", Vector4.zero);
        computeShader.SetFloat("_SolarIrradiance", bakeMat.GetFloat("_SolarIrradiance"));
        computeShader.Dispatch(kernel, multiScatteringResolution.x, multiScatteringResolution.y, 1);

        bakeMat.SetTexture("_TransmittanceLut", transmittanceLut);
        bakeMat.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
        bakeMat.SetVector("_SkyViewLutSize", new Vector4(1f / skyviewResolution.x, 1f / skyviewResolution.y, skyviewResolution.x, skyviewResolution.y));
        bakeMat.SetTexture("_MultiSactteringLut", multiScatteringLut);
        bakeMat.SetVector("_MultiSactteringLut_Size", new Vector4(1f / multiScatteringResolution.x, 1f / multiScatteringResolution.y, multiScatteringResolution.x, multiScatteringResolution.y));
        Graphics.Blit(skyviewLut, skyviewLut, bakeMat, 1);

        bakeMat.SetTexture("_TransmittanceLut", transmittanceLut);
        bakeMat.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
        bakeMat.SetTexture("_SkyViewLut", skyviewLut);
        bakeMat.SetVector("_SkyViewLutSize", new Vector4(1f / skyviewResolution.x, 1f / skyviewResolution.y, skyviewResolution.x, skyviewResolution.y));
        bakeMat.SetTexture("_MultiSactteringLut", multiScatteringLut);
        bakeMat.SetVector("_MultiSactteringLut_Size", new Vector4(1f / multiScatteringResolution.x, 1f / multiScatteringResolution.y, multiScatteringResolution.x, multiScatteringResolution.y));
        Graphics.Blit(src, dest, bakeMat,2);
    }
}
