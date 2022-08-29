using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class rvt : MonoBehaviour
{
    public Transform camTrans;
    public Texture2D colorTex0;
    public Texture2D colorTex1;
    public Texture2D colorTex2;
    public Texture2D colorTex3;
    public Texture2D weightMask;
    public Shader bakeShader;
    public Vector4 tilings;

    public RenderTexture _physicalTexture;
    Texture2D _pageTable;
    public Material bakeMat;
    Material shadingMat;
    int activePageCount;
    bool[,] physicalPageFlags = null;

    const int physicalResolution = 2048;
    const int terrainResolution = 10 * 1024;
    const int vtResolution = 8192;
    const int pageResolution = 256;
    const int maxPages = 64;

    void Start()
    {
        _physicalTexture = new RenderTexture(physicalResolution, physicalResolution, 0, RenderTextureFormat.ARGB32);
        _pageTable = new Texture2D(32, 32, TextureFormat.RGBAFloat, true, true);    //8bit只能代表256个索引
        _pageTable.wrapMode = TextureWrapMode.Clamp;
        _pageTable.filterMode = FilterMode.Point;
        for (int i = 0; i < _pageTable.mipmapCount; i++)
        {
            int resolution = pageResolution >> i;
            for (int m = 0; m < resolution; m++)
            {
                for (int n = 0; n < resolution; n++)
                {
                    _pageTable.SetPixel(m, n, new Color(0, 0, 0, 0), i);
                }
            }
        }
        _pageTable.Apply(false);

        bakeMat = new Material(bakeShader);
        bakeMat.hideFlags = HideFlags.HideAndDontSave;
        bakeMat.SetTexture("_ColorTex0", colorTex0);
        bakeMat.SetTexture("_ColorTex1", colorTex1);
        bakeMat.SetTexture("_ColorTex2", colorTex2);
        bakeMat.SetTexture("_ColorTex3", colorTex3);
        bakeMat.SetTexture("_WeightMask", weightMask);
        bakeMat.SetVector("_Tiling", tilings);

        shadingMat = GetComponent<MeshRenderer>().sharedMaterial;
        activePageCount = 0;
        physicalPageFlags = new bool[8, 8];
        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                physicalPageFlags[i, j] = false;
            }
        }
    }

    void Update()
    {
        UpdatePageTable();

        Vector3 camPos = camTrans.position;
        Vector2 camUV = new Vector2((camPos.x + 8192 * 0.5f) / 8192, (camPos.z + 8192 * 0.5f) / 8192);
        Vector2 pageIndex = camUV * vtResolution / pageResolution;
        Vector2Int cameraPage = new Vector2Int((int)(pageIndex.x), (int)(pageIndex.x));
        shadingMat.SetVector("_CameraUV", camUV);
        shadingMat.SetVector("_CameraPage", new Vector4(cameraPage.x, cameraPage.y));
        shadingMat.SetTexture("_PhysicalTex", _physicalTexture);
        shadingMat.SetTexture("_PageTable", _pageTable);
    }

    private void OnDestroy()
    {
        GameObject.Destroy(_physicalTexture);
        GameObject.Destroy(_pageTable);
        GameObject.Destroy(bakeMat);
    }

    // return mips and page index
    List<List<Vector2Int>> FeedBack()
    {
        Vector3 camPos = camTrans.position;
        Vector2 uv = new Vector2((camPos.x + 8192 * 0.5f) / 8192, (camPos.z + 8192 * 0.5f) / 8192);

        List<List<Vector2Int>> pageIndices = new List<List<Vector2Int>>();

        for (int i = 0; i < _pageTable.mipmapCount; i++)
        {
            pageIndices.Add(new List<Vector2Int>());
            int mipResolution = vtResolution >> i;
            Vector2 pageIndex = uv * mipResolution / pageResolution;

            // 3x3更新区域
            int pageLen = mipResolution / pageResolution;
            for (int m = Mathf.Max(0, (int)pageIndex.x - 1); m <= Mathf.Min(pageLen-1, (int)pageIndex.x + 1); m++)
            {
                for (int n = Mathf.Max(0, (int)pageIndex.y - 1); n <= Mathf.Min(pageLen-1, (int)pageIndex.y + 1); n++)
                {
                    pageIndices[i].Add(new Vector2Int(m, n));
                }
            }
        }

        return pageIndices;
    }

    Mesh _pageQuad;
    Mesh pageQuad
    {
        get
        {
            if (_pageQuad == null)
            {
                _pageQuad = new Mesh();
                _pageQuad.vertices = new Vector3[4]{
                    new Vector3(-1, -1,0),
                    new Vector3(-1, 1,0),
                    new Vector3(1, -1,0),
                    new Vector3(1, 1,0),
                };
                _pageQuad.triangles = new int[6]{
                    0,1,2,1,3,2
                };
                _pageQuad.uv = new Vector2[4]{
                    new Vector2(0, 0),
                    new Vector2(0, 1),
                    new Vector2(1, 0),
                    new Vector2(1, 1),
                };
            }

            return _pageQuad;
        }
    }

    void UpdatePageTable()
    {
        // get needed page
        int needAddPage = 0;
        List<List<Vector2Int>> visiablePages = FeedBack();
        for (int i = 0; i < visiablePages.Count; i++)
        {
            int mip = i;
            for (int k = 0; k < visiablePages[i].Count; k++)
            {
                Color pageValue = _pageTable.GetPixel(visiablePages[i][k].x, visiablePages[i][k].y, mip);
                if (pageValue.a < 0.5)
                {
                    needAddPage++;
                }
            }
        }

        // unload unused page
        int totalPages = activePageCount + needAddPage;
        if (totalPages > maxPages)
        {
            for (int i = 0; i < _pageTable.mipmapCount; i++)
            {
                int mip = i;
                int resolution = pageResolution >> i;
                for (int k = 0; k < visiablePages[i].Count; k++)
                {
                    for (int m = 0; m < resolution; m++)
                    {
                        for (int n = 0; n < resolution; n++)
                        {
                            if (!visiablePages[i].Contains(new Vector2Int(m,n)))
                            {
                                Color pagePixel = _pageTable.GetPixel(m, n, i);
                                if (pagePixel.a > 0.5)
                                {
                                    _pageTable.SetPixel(m, n, new Color(0, 0, 0, 0), i);
                                    totalPages--;
                                    int pageX = (int)(pagePixel.r * 8 + 0.5f);
                                    int pageY = (int)(pagePixel.g * 8 + 0.5f);
                                    physicalPageFlags[pageX, pageY] = false;
                                    activePageCount--;
                                }
                            }
                        }
                    }
                }

                if (totalPages < maxPages)
                    break;
            }
            _pageTable.Apply(false);
        }

        // prepare physical pages
        Stack<Vector2Int> phyCanUsePages = new Stack<Vector2Int>();
        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                if (!physicalPageFlags[i, j])
                {
                    phyCanUsePages.Push(new Vector2Int(i, j));
                }
            }
        }
        if(needAddPage > 0)
        {
            int a = 0;
        }

        // render needed pages
        CommandBuffer cmd = new CommandBuffer();
        for (int i = 0; i < visiablePages.Count; i++)
        {
            int mip = i;
            for (int k = 0; k < visiablePages[i].Count; k++)
            {
                Color pageValue = _pageTable.GetPixel(visiablePages[i][k].x, visiablePages[i][k].y, mip);
                if (pageValue.a < 0.5)
                {
                    // update page table
                    Vector2Int page = phyCanUsePages.Peek();
                    _pageTable.SetPixel(visiablePages[i][k].x, visiablePages[i][k].y, new Color((float)(page.x) / 8f, (float)(page.y) / 8f, 0, 1f), mip);
                    phyCanUsePages.Pop();
                    physicalPageFlags[page.x, page.y] = true;

                    // update physical texture
                    cmd.Clear();
                    cmd.SetRenderTarget(_physicalTexture);
                    cmd.SetViewport(new Rect(page.x * pageResolution, page.y * pageResolution, pageResolution, pageResolution));
                    cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                    cmd.ClearRenderTarget(true, true, Color.yellow);
                    float scale = 1f / (32 >> mip);
                    Vector4 scaleOffset = new Vector4(1, 1, visiablePages[i][k].x, visiablePages[i][k].y) * scale;
                    bakeMat.SetVector("_UVScaleOffset", scaleOffset);
                    cmd.DrawMesh(pageQuad, Matrix4x4.identity, bakeMat, 0);
                    Graphics.ExecuteCommandBuffer(cmd);
                    activePageCount++;
                }
            }
        }
        _pageTable.Apply(false);
        cmd.Release();
    }
}
