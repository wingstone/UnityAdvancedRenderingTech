using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif


public class ClipMap : MonoBehaviour
{
    const int virturalTextureResolution = 8192;
    const int mipCount = 6; //log2(8192/256)+1
    const int vtPageResolution = 32;
    const int vtSubpageResolution = 128;
    const int physicalSubPageResolution = 64;

    [HideInInspector]
    public RenderTexture clipMapDebug;
    [HideInInspector]
    public RenderTexture clipmap;
    public Transform cameTrans;
    public Texture2D detailTex;
    public Material testMat;

    Material debugMat;
    Material bakeMat;
    Vector2Int clipmapPage;
    Vector2Int clipmapSubPage;
    Vector2 uv;
    Vector2Int[] clipLayerIDs;

    static Mesh _fullscreenTriangle;
    static Mesh fullscreenTriangle
    {
        get
        {
            if (_fullscreenTriangle != null)
                return _fullscreenTriangle;

            _fullscreenTriangle = new Mesh { name = "Fullscreen Triangle" };

            // Because we have to support older platforms (GLES2/3, DX9 etc) we can't do all of
            // this directly in the vertex shader using vertex ids :(
            _fullscreenTriangle.SetVertices(new List<Vector3>
                {
                    new Vector3(-1f, -1f, 0f),
                    new Vector3(-1f,  3f, 0f),
                    new Vector3( 3f, -1f, 0f)
                });
            _fullscreenTriangle.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0, false);
            _fullscreenTriangle.UploadMeshData(false);

            return _fullscreenTriangle;
        }
    }

    void Start()
    {
        clipMapDebug = new RenderTexture(256, 256 * mipCount, 0, RenderTextureFormat.ARGB32);
        clipMapDebug.Create();
        clipMapDebug.hideFlags = HideFlags.HideAndDontSave;

        clipmap = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGB32);
        clipmap.dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray;
        clipmap.volumeDepth = 6;
        clipmap.Create();
        clipmap.wrapMode = TextureWrapMode.Repeat;
        clipmap.hideFlags = HideFlags.HideAndDontSave;

        clipLayerIDs = new Vector2Int[mipCount];
        for (int i = 0; i < mipCount; i++)
        {
            clipLayerIDs[i] = new Vector2Int(-1, -1);
        }

        Shader debugShader = Shader.Find("Custom/ClipMapDebug");
        debugMat = new Material(debugShader);
        debugMat.hideFlags = HideFlags.HideAndDontSave;

        Shader bakeShader = Shader.Find("Custom/ClipMapBake");
        bakeMat = new Material(bakeShader);
        bakeMat.hideFlags = HideFlags.HideAndDontSave;

        // InitialClipmap();
    }

    void DebugClipmap()
    {
        debugMat.SetTexture("_ClipmapArray", clipmap);
        Graphics.Blit(null, clipMapDebug, debugMat);
    }
    void InitialClipmap()
    {
        Vector3 camePos = cameTrans.transform.position;
        Vector3 planeScale = transform.localScale;
        uv = new Vector2(camePos.x / planeScale.x, camePos.z / planeScale.z);
        uv.x = Mathf.Clamp01(uv.x);
        uv.y = Mathf.Clamp01(uv.y);

        for (int i = 0; i < mipCount; i++)
        {
            int pageCount = 1 << i;
            Vector2Int page = new Vector2Int((int)(uv.x * pageCount), (int)(uv.y * pageCount));
            bakeMat.SetTexture("_DetailTex", detailTex);
            bakeMat.SetVector("pageParameter", new Vector4(page.x, page.y, pageCount, pageCount));
            Graphics.Blit(null, clipmap, bakeMat, 0, mipCount - i - 1);
        }

        clipmapPage = new Vector2Int((int)(uv.x * 32), (int)(uv.y * 32));
        clipmapSubPage = new Vector2Int(clipmapPage.x * 4 + 2, clipmapPage.y * 4 + 2);
    }

    void UpdateCoordinate()
    {
        Vector3 camePos = cameTrans.transform.position;
        Vector3 planeScale = transform.localScale;
        uv = new Vector2(camePos.x / planeScale.x, camePos.z / planeScale.z);
        uv.x = Mathf.Clamp01(uv.x);
        uv.y = Mathf.Clamp01(uv.y);

        clipmapPage = new Vector2Int((int)(uv.x * 32), (int)(uv.y * 32));
        clipmapSubPage = new Vector2Int((int)(uv.x * 128), (int)(uv.y * 128));
    }

    void UpdateMat()
    {
        testMat.SetTexture("_ClipmapArray", clipmap);
        Vector2Int currentLayerID = new Vector2Int(Mathf.Clamp(clipmapSubPage.x, 2, 128 - 2), Mathf.Clamp(clipmapSubPage.y, 2, 128 - 2));
        testMat.SetVector("pageParameter", new Vector4(currentLayerID.x, currentLayerID.y, vtSubpageResolution, vtSubpageResolution));
    }

    void UpdateClipmap()
    {
        CommandBuffer cmd = new CommandBuffer();
        cmd.name = "bake clipmap";
        // 每layer更新，而不是没sub page更新
        // todo clipmap 环形更新
        for (int i = 0; i < mipCount; i++)
        {
            int subPageCount = 128 >> i;
            Vector2Int currentLayerID_UnWrap = clipmapSubPage / (1 << i);
            Vector2Int currentLayerID = new Vector2Int(Mathf.Clamp(currentLayerID_UnWrap.x, 2, subPageCount - 2), Mathf.Clamp(currentLayerID_UnWrap.y, 2, subPageCount - 2));
            if (clipLayerIDs[i] != currentLayerID)
            {
                
                for (int m = currentLayerID.x - 2; m <= currentLayerID.x + 1; m++)
                {
                    for (int n = currentLayerID.y - 2; n <= currentLayerID.y + 1; n++)
                    {
                        // update physical albedo and normal texture
                        cmd.Clear();
                        cmd.SetRenderTarget(clipmap, clipmap, 0, CubemapFace.Unknown, i);
                        cmd.SetViewport(new Rect((m % 4) * physicalSubPageResolution, (n % 4) * physicalSubPageResolution, physicalSubPageResolution, physicalSubPageResolution));
                        cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                        bakeMat.SetTexture("_DetailTex", detailTex);
                        bakeMat.SetVector("pageParameter", new Vector4(m, n, subPageCount, subPageCount));
                        cmd.DrawMesh(fullscreenTriangle, Matrix4x4.identity, bakeMat, 0, 0);
                        Graphics.ExecuteCommandBuffer(cmd);
                    }
                }

                clipLayerIDs[i] = currentLayerID;
            }
        }
    }

    // Update is called once per frame
    void Update()
    {
        UpdateCoordinate();

        UpdateClipmap();

        UpdateMat();

        DebugClipmap();
    }

    void OnDestroy()
    {
        GameObject.Destroy(clipMapDebug);
        GameObject.Destroy(clipmap);
        GameObject.Destroy(debugMat);
        GameObject.Destroy(bakeMat);
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(ClipMap))]
public class ClipMapEditor : Editor
{
    const int mipCount = 6; //log2(8192/256)+1
    public override void OnInspectorGUI()
    {
        if (!Application.isPlaying)
            base.OnInspectorGUI();

        ClipMap clipmap = target as ClipMap;

        if (clipmap.clipMapDebug != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            GUILayout.Label("Clipmap Preview");

            Rect rect = EditorGUILayout.GetControlRect(GUILayout.Width(256), GUILayout.Height(256 * mipCount));
            rect.center = new Vector2(Screen.width / 2, rect.center.y);
            EditorGUI.DrawPreviewTexture(rect, clipmap.clipMapDebug);
        }
    }
}
#endif
