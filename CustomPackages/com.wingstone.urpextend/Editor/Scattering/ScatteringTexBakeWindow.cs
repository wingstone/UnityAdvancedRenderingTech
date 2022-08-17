using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using System.IO;


#pragma warning disable 0618

public class ScatteringTexBakeWindow : EditorWindow
{
    Material skyMat;
    Material bakeMat;

    Vector2Int transMittanceResolution = new Vector2Int(256, 64);
    Vector2Int skyviewResolution = new Vector2Int(256, 128);
    Vector2Int multiScatteringResolution = new Vector2Int(64, 64);

    [MenuItem("Window/ScatteringTexBakeWindow")]
    private static void ShowWindow()
    {
        var window = GetWindow<ScatteringTexBakeWindow>();
        window.titleContent = new GUIContent("ScatteringTexBakeWindow");
        window.Show();
    }

    private void OnGUI()
    {
        bakeMat = EditorGUILayout.ObjectField("bake material", bakeMat, typeof(Material), false) as Material;
        transMittanceResolution = EditorGUILayout.Vector2IntField("transMittance Resolution", transMittanceResolution);
        skyviewResolution = EditorGUILayout.Vector2IntField("skyview resolution", skyviewResolution);
        multiScatteringResolution = EditorGUILayout.Vector2IntField("multi scattering resolution", multiScatteringResolution);

        EditorGUILayout.Space();

        if (GUILayout.Button("CreateTransmittanceLut"))
        {
            CreateTransmittanceLut("transmittance.exr");
        }

        if (GUILayout.Button("CreateMultiScatteringLut"))
        {
            CreateMultiScatteringLut("multiscattering.exr");
        }

        if (GUILayout.Button("CreateSkyviewLut"))
        {
            CreateSkyviewLut("skyview.exr");
        }

    }

    void CreateTransmittanceLut(string name)
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);

        if (path == "")
            path = "Assets";
        else if (Path.GetExtension(path) != "")
            path = path.Replace(Path.GetFileName(AssetDatabase.GetAssetPath(Selection.activeObject)), "");

        string assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(path + "/" + name);

        bakeMat.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));

        RenderTexture rt = new RenderTexture(transMittanceResolution.x, transMittanceResolution.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        Texture2D tex = new Texture2D(transMittanceResolution.x, transMittanceResolution.y, TextureFormat.RGBAFloat, true, true);
        Graphics.Blit(tex, rt, bakeMat, 0);
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, transMittanceResolution.x, transMittanceResolution.y), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        byte[] pngData = tex.EncodeToEXR();
        File.WriteAllBytes(assetPathAndName, pngData);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        AssetDatabase.Refresh();

        TextureImporter ti = (TextureImporter)TextureImporter.GetAtPath(assetPathAndName);
        ti.textureType = TextureImporterType.Default;
        ti.textureFormat = TextureImporterFormat.RGBAFloat;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.sRGBTexture = false;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = false;

        AssetDatabase.ImportAsset(assetPathAndName);
        AssetDatabase.Refresh();
    }

    void CreateMultiScatteringLut(string name)
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);

        if (path == "")
            path = "Assets";
        else if (Path.GetExtension(path) != "")
            path = path.Replace(Path.GetFileName(AssetDatabase.GetAssetPath(Selection.activeObject)), "");

        string assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(path + "/" + name);

        RenderTexture transmittanceLut = new RenderTexture(transMittanceResolution.x, transMittanceResolution.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        transmittanceLut.Create();
        bakeMat.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
        Graphics.Blit(transmittanceLut, transmittanceLut, bakeMat, 0);

        RenderTexture multiScatteringLut = new RenderTexture(multiScatteringResolution.x, multiScatteringResolution.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        multiScatteringLut.enableRandomWrite = true;
        multiScatteringLut.Create();

        ComputeShader computeShader = Resources.Load<ComputeShader>("MultiScattering");
        int kernel = computeShader.FindKernel("CSMain");

        computeShader.SetTexture(kernel, "_TransmittanceLut", transmittanceLut);
        computeShader.SetTexture(kernel, "_Result", multiScatteringLut);
        computeShader.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
        computeShader.SetVector("_MultiScattering_Size", new Vector4(1f / multiScatteringResolution.x, 1f / multiScatteringResolution.y, multiScatteringResolution.x, multiScatteringResolution.y));
        computeShader.SetVector("_GroundColor", Vector4.zero);
        computeShader.Dispatch(kernel, multiScatteringResolution.x, multiScatteringResolution.y, 1);

        RenderTexture.active = multiScatteringLut;
        Texture2D tex = new Texture2D(multiScatteringResolution.x, multiScatteringResolution.y, TextureFormat.RGBAFloat, true, true);
        tex.ReadPixels(new Rect(0, 0, multiScatteringResolution.x, multiScatteringResolution.y), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        byte[] data = tex.EncodeToEXR();
        File.WriteAllBytes(assetPathAndName, data);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(transmittanceLut);
        Object.DestroyImmediate(multiScatteringLut);
        AssetDatabase.Refresh();

        TextureImporter ti = (TextureImporter)TextureImporter.GetAtPath(assetPathAndName);
        ti.textureType = TextureImporterType.Default;
        ti.textureFormat = TextureImporterFormat.RGBAFloat;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.sRGBTexture = false;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = false;

        AssetDatabase.ImportAsset(assetPathAndName);
        AssetDatabase.Refresh();
    }

    void CreateSkyviewLut(string name)
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);

        if (path == "")
            path = "Assets";
        else if (Path.GetExtension(path) != "")
            path = path.Replace(Path.GetFileName(AssetDatabase.GetAssetPath(Selection.activeObject)), "");

        string assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(path + "/" + name);

        RenderTexture transmittanceLut = new RenderTexture(transMittanceResolution.x, transMittanceResolution.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        bakeMat.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
        Graphics.Blit(transmittanceLut, transmittanceLut, bakeMat, 0);



        RenderTexture multiScatteringLut = new RenderTexture(multiScatteringResolution.x, multiScatteringResolution.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        multiScatteringLut.enableRandomWrite = true;
        multiScatteringLut.Create();

        ComputeShader computeShader = Resources.Load<ComputeShader>("MultiScattering");
        int kernel = computeShader.FindKernel("CSMain");

        computeShader.SetTexture(kernel, "_TransmittanceLut", transmittanceLut);
        computeShader.SetTexture(kernel, "_Result", multiScatteringLut);
        computeShader.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
        computeShader.SetVector("_MultiScattering_Size", new Vector4(1f / multiScatteringResolution.x, 1f / multiScatteringResolution.y, multiScatteringResolution.x, multiScatteringResolution.y));
        computeShader.SetVector("_GroundColor", Vector4.zero);
        computeShader.Dispatch(kernel, multiScatteringResolution.x, multiScatteringResolution.y, 1);



        RenderTexture skyviewLut = new RenderTexture(skyviewResolution.x, skyviewResolution.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        bakeMat.SetTexture("_TransmittanceLut", transmittanceLut);
        bakeMat.SetVector("_TransmittanceLut_Size", new Vector4(1f / transMittanceResolution.x, 1f / transMittanceResolution.y, transMittanceResolution.x, transMittanceResolution.y));
        bakeMat.SetVector("_SkyViewLutSize", new Vector4(1f / skyviewResolution.x, 1f / skyviewResolution.y, skyviewResolution.x, skyviewResolution.y));
        bakeMat.SetTexture("_MultiSactteringLut", multiScatteringLut);
        bakeMat.SetVector("_MultiSactteringLut_Size", new Vector4(1f / multiScatteringResolution.x, 1f / multiScatteringResolution.y, multiScatteringResolution.x, multiScatteringResolution.y));
        bakeMat.SetVector("_GroundColor", Vector4.zero);
        Graphics.Blit(skyviewLut, skyviewLut, bakeMat, 1);


        RenderTexture.active = skyviewLut;
        Texture2D tex = new Texture2D(skyviewResolution.x, skyviewResolution.y, TextureFormat.RGBAFloat, true, true);
        tex.ReadPixels(new Rect(0, 0, skyviewResolution.x, skyviewResolution.y), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        byte[] data = tex.EncodeToEXR();
        File.WriteAllBytes(assetPathAndName, data);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(transmittanceLut);
        Object.DestroyImmediate(multiScatteringLut);
        Object.DestroyImmediate(skyviewLut);
        AssetDatabase.Refresh();

        TextureImporter ti = (TextureImporter)TextureImporter.GetAtPath(assetPathAndName);
        ti.textureType = TextureImporterType.Default;
        ti.textureFormat = TextureImporterFormat.RGBAFloat;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.sRGBTexture = false;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = false;

        AssetDatabase.ImportAsset(assetPathAndName);
        AssetDatabase.Refresh();
    }

}

#pragma warning restore 0618