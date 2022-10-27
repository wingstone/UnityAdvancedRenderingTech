using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace URPExtend
{
    [RequireComponent(typeof(Terrain))]
    public class TerrainVT : MonoBehaviour
    {
        //---Control Parameters
        const int pageResolution = 256;
        public Camera cam;
        public ComputeShader compressCompute;
        public bool useRuntimeCompress;
        public TerrainVTResolution VTResolution = TerrainVTResolution._65536;
        public TerrainVTPhysicalResolution VTPhysicalResolution = TerrainVTPhysicalResolution._2048;
        int vtResolution = 65536;  // 256x256 page
        int vtMips = 8;
        int virtualPageResolution = 256; // 65536/256
        int physicalResolution = 2048;
        int physicalPageResolution = 8; // 2048/256
        [Range(1, 4)]
        public int mipStepPageCount = 2;
        [Range(4, 10)]
        public int updatePagesPerFrame = 8;
        public bool bOpenVT = false;
        const float splitFactor = 8f;

        //---terrain setting
        Terrain _terrain;
        Vector3 terrainSize;
        public Material shadingMat;
        public Shader bakeShader;

        //---vt setting
#if UNITY_EDITOR
        [HideInInspector]
        public RenderTexture _physicalAlbedoTexture;
        [HideInInspector]
        public RenderTexture _physicalNormalTexture;
        [HideInInspector]
        public Texture2D _pageTable;
#else
        RenderTexture _physicalAlbedoTexture;
        RenderTexture _physicalNormalTexture;
        Texture2D _pageTable;
#endif
        Texture2D _compressAlbedoTexture;
        Texture2D _compressNormalTexture;
        Material bakeMat;
        List<Vector3Int> usedPageTable;
        List<Vector3Int> remappedPageTable;

        //---temp
        Material oldMat;
        int _activePageCount;
        bool[,] _physicalPageFlags = null;
        RenderTexture compressRT = null;


        //--- tools

        Vector4 GetTilingFromTileSizeOffset(Vector2 tileSize, Vector2 tileOffset)
        {
            Vector2 size = new Vector2(terrainSize.x / tileSize.x, terrainSize.z / tileSize.y);
            Vector2 offset = new Vector2(tileOffset.x / tileSize.x, tileOffset.y / tileSize.y);
            return new Vector4(size.x, size.y, offset.x, offset.y);
        }

        public enum TerrainVTPhysicalResolution
        {
            _2048,
            _3072,
            _4096,
        }

        public enum TerrainVTResolution
        {
            _4096,
            _8192,
            _16384,
            _32768,
            _65536,
        }

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

        //--- debug

        public void DebugPageTable()
        {
            int count = 0;
            for (int i = 0; i < _pageTable.mipmapCount; i++)
            {
                int resolution = pageResolution >> i;
                for (int m = 0; m < resolution; m++)
                {
                    for (int n = 0; n < resolution; n++)
                    {
                        if (_pageTable.GetPixel(m, n, i).a > 0.5f)
                            count++;
                    }
                }
            }
            Debug.Log("used pages: " + count);
        }

        //--- initial
        void InitialShadingMat()
        {
            shadingMat.SetFloat("_PageResolution", pageResolution);
            shadingMat.SetFloat("_VTResolution", vtResolution);
            shadingMat.SetFloat("_VTPageResolution", virtualPageResolution);
            shadingMat.SetFloat("_VTMips", vtMips);
            shadingMat.SetFloat("_PhysicalResolution", physicalResolution);
            shadingMat.SetFloat("_PhysicalPageResolution", physicalPageResolution);
        }

        void InitialVtResolution()
        {
            physicalResolution = (2 + ((int)VTPhysicalResolution)) * 1024;
            physicalPageResolution = physicalResolution / pageResolution;

            vtResolution = 4096 << (int)VTResolution;
            virtualPageResolution = vtResolution / 256;
            vtMips = (int)(Mathf.Log(virtualPageResolution, 2) + 0.5);
        }

        void InitialBakeMat()
        {
            bakeMat = new Material(bakeShader);
            bakeMat.hideFlags = HideFlags.HideAndDontSave;
            bakeMat.SetFloat("_PageResolution", pageResolution);
        }

        void initialRuntimeCompress()
        {
            compressRT = new RenderTexture(physicalResolution, physicalResolution, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
            };
            _physicalNormalTexture.Create();
            _physicalNormalTexture.hideFlags = HideFlags.HideAndDontSave;
            _physicalNormalTexture.name = "compressRT";

            GraphicsFormat format;
#if UNITY_ANDROID && !UNITY_EDITOR
                format = GraphicsFormat.RGBA_ETC2_UNorm;
                compressCompute.DisableKeyword("_COMPRESS_BC3");
                compressCompute.EnableKeyword("_COMPRESS_ETC2");
#else
            format = GraphicsFormat.RGBA_DXT5_UNorm;
            compressCompute.DisableKeyword("_COMPRESS_ETC2");
            compressCompute.EnableKeyword("_COMPRESS_BC3");
#endif

            _compressAlbedoTexture = new Texture2D(physicalResolution, physicalResolution, format, TextureCreationFlags.None);
            _compressAlbedoTexture.name = "vt Compress Albebo";
            _compressAlbedoTexture.hideFlags = HideFlags.HideAndDontSave;

            _compressNormalTexture = new Texture2D(physicalResolution, physicalResolution, format, TextureCreationFlags.None);
            _compressNormalTexture.name = "vt Compress Normal";
            _compressNormalTexture.hideFlags = HideFlags.HideAndDontSave;

        }

        void InitialVirtualTexture()
        {
            _physicalAlbedoTexture = new RenderTexture(physicalResolution, physicalResolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            _physicalAlbedoTexture.name = "vt Physical Albebo";
            _physicalAlbedoTexture.Create();
            _physicalAlbedoTexture.hideFlags = HideFlags.HideAndDontSave;

            _physicalNormalTexture = new RenderTexture(physicalResolution, physicalResolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            _physicalNormalTexture.Create();
            _physicalNormalTexture.hideFlags = HideFlags.HideAndDontSave;
            _physicalNormalTexture.name = "vt Physical Normal";

            if (useRuntimeCompress)
                initialRuntimeCompress();

            // todo: 8bit只能代表256个索引, 但是可以不存坐标，而存index来解决；
            // todo: 但是目前C#层没有raw byte的操作函数，只能搁置
            _pageTable = new Texture2D(virtualPageResolution, virtualPageResolution, TextureFormat.RGBAFloat, true, true);
            _pageTable.name = "vt Page Table";
            _pageTable.wrapMode = TextureWrapMode.Clamp;
            _pageTable.filterMode = FilterMode.Point;
            for (int i = 0; i < _pageTable.mipmapCount; i++)
            {
                int resolution = pageResolution >> i;
                for (int m = 0; m < resolution; m++)
                {
                    for (int n = 0; n < resolution; n++)
                    {
                        ClearPageTablePixel(m, n, i);
                    }
                }
            }
            _pageTable.Apply(false);

            usedPageTable = new List<Vector3Int>(64);
            remappedPageTable = new List<Vector3Int>(64);
        }

        void InitialPhysicalPageFlag()
        {
            _activePageCount = 0;
            _physicalPageFlags = new bool[physicalPageResolution, physicalPageResolution];
            for (int i = 0; i < physicalPageResolution; i++)
            {
                for (int j = 0; j < physicalPageResolution; j++)
                {
                    _physicalPageFlags[i, j] = false;
                }
            }
        }

        void SetBakeParameters(int weightMaskID, bool useAdd)
        {
            if (useAdd)
            {
                bakeMat.SetFloat("_SrcBlend", 1.0f);
                bakeMat.SetFloat("_DstBlend", 1.0f);
            }
            else
            {
                bakeMat.SetFloat("_SrcBlend", 1.0f);
                bakeMat.SetFloat("_DstBlend", 0.0f);
            }

            TerrainLayer[] terrainLayers = _terrain.terrainData.terrainLayers;
            Texture2D[] alphaTextures = _terrain.terrainData.alphamapTextures;
            int layerLength = terrainLayers.Length >= weightMaskID * 4 + 4 ? 4 : terrainLayers.Length % 4;
            Vector4 normalScales = Vector4.one;
            for (int i = 0; i < layerLength; i++)
            {
                int layerIndex = weightMaskID * 4 + i;
                bakeMat.SetTexture("_LayerTex" + i, terrainLayers[layerIndex].diffuseTexture);
                bakeMat.SetTexture("_LayerNormalTex" + i, terrainLayers[layerIndex].normalMapTexture);
                bakeMat.SetTexture("_LayerMaskTex" + i, terrainLayers[layerIndex].maskMapTexture);
                bakeMat.SetVector("_Layer_ST" + i, GetTilingFromTileSizeOffset(terrainLayers[layerIndex].tileSize, terrainLayers[layerIndex].tileOffset));
                bakeMat.SetVector("_LayerColorTint" + i, terrainLayers[layerIndex].diffuseRemapMax);
                bakeMat.SetVector("_Layer_RemapMin" + i, terrainLayers[i].maskMapRemapMin);
                bakeMat.SetVector("_Layer_RemapMax" + i, terrainLayers[i].maskMapRemapMax);
                normalScales[i] = terrainLayers[layerIndex].normalScale;
            }
            bakeMat.SetTexture("_WeightMask", alphaTextures[weightMaskID]);
            bakeMat.SetVector("_Layer_NormalScale", normalScales);
        }

        // update
        void UpdateShadingMat()
        {
            Vector3 localPos = transform.InverseTransformPoint(cam.transform.position);
            Vector2 camUV = new Vector2(localPos.x / terrainSize.x, localPos.z / terrainSize.z);
            Vector2 pageIndex = camUV * virtualPageResolution;
            Vector2Int cameraPage = new Vector2Int((int)(pageIndex.x), (int)(pageIndex.y));

            shadingMat.SetVector("_CameraUV", camUV);
            shadingMat.SetVector("_CameraPage", new Vector4(cameraPage.x, cameraPage.y));
            shadingMat.SetInt("_MipStepPageCount", mipStepPageCount);

            if (useRuntimeCompress)
            {
                shadingMat.SetTexture("_PhysicalAlbedoTex", _compressAlbedoTexture);
                shadingMat.SetTexture("_PhysicalNormalTex", _compressNormalTexture);
            }
            else
            {
                shadingMat.SetTexture("_PhysicalAlbedoTex", _physicalAlbedoTexture);
                shadingMat.SetTexture("_PhysicalNormalTex", _physicalNormalTexture);
            }

            shadingMat.SetTexture("_PageTable", _pageTable);
        }

        bool notRenderedPage(Color pageTablePixel)
        {
            return pageTablePixel.a < 0.5f;
        }

        void ClearPageTablePixel(int x, int y, int mip)
        {
            _pageTable.SetPixel(x, y, new Color(0, 0, 1, 0), mip);
        }

        void SetPageTablePixel(int x, int y, int mip, Color pixel)
        {
            _pageTable.SetPixel(x, y, pixel, mip);
        }


        // return mips and page index
        List<List<Vector3Int>> FeedBack(Vector3 cameraPos, Vector3 terrainSize)
        {
            Vector3 localPos = transform.InverseTransformPoint(cameraPos);
            Vector2 camUV = new Vector2(localPos.x / terrainSize.x, localPos.z / terrainSize.z);
            Vector3 camDir = cam.transform.forward;

            List<List<Vector3Int>> pageIndices = new List<List<Vector3Int>>();

            for (int i = 0; i < _pageTable.mipmapCount; i++)
            {
                pageIndices.Add(new List<Vector3Int>());
            }

            Queue<Vector3Int> nodes = new Queue<Vector3Int>();
            nodes.Enqueue(new Vector3Int(0, 0, _pageTable.mipmapCount - 1));
            while (nodes.Count > 0)
            {
                Vector3Int node = nodes.Dequeue();
                bool isNeedAdd = false;
                int pageLen = 1 << (_pageTable.mipmapCount - 1 - node.z);
                int m = node.x;
                int n = node.y;
                float pageSize = terrainSize.x / pageLen;
                Vector3 center = (new Vector3(m + 0.5f, 0, n + 0.5f)) * pageSize + transform.position;
                Vector3 viewDir = (center - cameraPos).normalized;
                float viewFactor = Vector3.Dot(viewDir, camDir);
                if (node.z == _pageTable.mipmapCount - 1)
                    isNeedAdd = true;

                // clipmap range
                Vector2 page = camUV * (virtualPageResolution >> node.z);
                Vector2Int cameraPage = new Vector2Int((int)page.x, (int)page.y);
                if (Mathf.Abs(node.x - cameraPage.x) > mipStepPageCount || Mathf.Abs(node.y - cameraPage.y) > mipStepPageCount)
                    continue;

                if (viewFactor > Mathf.Cos(Mathf.Deg2Rad * cam.fieldOfView * 0.5f * cam.aspect))
                    isNeedAdd = true;

                float area = pageSize * pageSize;
                float distSq = (center - cameraPos).sqrMagnitude;
                if (area * (viewFactor + 0.5f) * splitFactor > distSq || area > distSq)
                    isNeedAdd = true;

                if (isNeedAdd)
                    pageIndices[node.z].Add(node);

                if (node.z > 0 && isNeedAdd)
                {
                    nodes.Enqueue(new Vector3Int(node.x * 2, node.y * 2, node.z - 1));
                    nodes.Enqueue(new Vector3Int(node.x * 2 + 1, node.y * 2, node.z - 1));
                    nodes.Enqueue(new Vector3Int(node.x * 2, node.y * 2 + 1, node.z - 1));
                    nodes.Enqueue(new Vector3Int(node.x * 2 + 1, node.y * 2 + 1, node.z - 1));
                }
            }

            return pageIndices;
        }

        void AdjectFeedBackPages(ref List<List<Vector3Int>> feedPages, ref int needAddPage)
        {
            for (int i = feedPages.Count - 1; i >= 0; i--)
            {
                int mip = i;
                for (int k = feedPages[i].Count - 1; k >= 0; k--)
                {
                    if (needAddPage >= updatePagesPerFrame)
                    {
                        feedPages[i].RemoveAt(k);
                        continue;
                    }

                    Color pageValue = _pageTable.GetPixel(feedPages[i][k].x, feedPages[i][k].y, mip);
                    if (notRenderedPage(pageValue))
                    {
                        needAddPage++;
                    }
                }
            }
        }

        List<Vector3Int> FlatFeedBackPages(List<List<Vector3Int>> feedPages)
        {
            List<Vector3Int> flatVisiblePages = new List<Vector3Int>();

            for (int i = feedPages.Count - 1; i >= 0; i--)
            {
                for (int k = feedPages[i].Count - 1; k >= 0; k--)
                {
                    flatVisiblePages.Add(feedPages[i][k]);
                }
            }

            return flatVisiblePages;
        }

        void RenderVTPage(int weightMaskID, CommandBuffer cmd, Vector3Int phyPage, int mip, Vector3Int virPage, bool useAdd)
        {
            SetBakeParameters(weightMaskID, useAdd);
            Color clearColor = new Color(0, 0, 0, 0);

            // update physical albedo and normal texture
            cmd.Clear();
            RenderTargetIdentifier[] renderTargets = new RenderTargetIdentifier[2] { _physicalAlbedoTexture, _physicalNormalTexture };
            cmd.SetRenderTarget(renderTargets, renderTargets[0]);   // depth parameter not used
            cmd.SetViewport(new Rect(phyPage.x * pageResolution, phyPage.y * pageResolution, pageResolution, pageResolution));
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            float scale = 1f / (virtualPageResolution >> mip);
            Vector4 scaleOffset = new Vector4(1, 1, virPage.x, virPage.y) * scale;
            bakeMat.SetVector("_UVScaleOffset", scaleOffset);
            cmd.DrawMesh(fullscreenTriangle, Matrix4x4.identity, bakeMat, 0, 0);
            Graphics.ExecuteCommandBuffer(cmd);
        }

        void UpdatePageTable()
        {
            // clear unrendered page
            for (int i = remappedPageTable.Count - 1; i >= 0; i--)
            {
                ClearPageTablePixel(remappedPageTable[i].x, remappedPageTable[i].y, remappedPageTable[i].z);
                remappedPageTable.RemoveAt(i);
            }

            _pageTable.Apply(false);

            // get needed page
            int needAddPage = 0;
            List<List<Vector3Int>> visiablePages = FeedBack(cam.transform.position, terrainSize);
            List<Vector3Int> flatVisiblePages = FlatFeedBackPages(visiablePages);
            AdjectFeedBackPages(ref visiablePages, ref needAddPage);

            if (needAddPage == 0) return;

            usedPageTable.Sort(
                (p1, p2) =>
                {
                    return p1.z - p2.z;
                }
            );

            // unload unused page, low mip first
            int totalPages = _activePageCount + needAddPage;
            int maxPages = physicalPageResolution * physicalPageResolution;
            if (totalPages > maxPages)
            {
                for (int i = 0; i < usedPageTable.Count; i++)
                {
                    int mip = usedPageTable[i].z;
                    int resolution = pageResolution >> mip;
                    if (!visiablePages[mip].Contains(usedPageTable[i]))
                    {
                        Color pagePixel = _pageTable.GetPixel(usedPageTable[i].x, usedPageTable[i].y, usedPageTable[i].z);
                        if (!notRenderedPage(pagePixel))
                        {
                            ClearPageTablePixel(usedPageTable[i].x, usedPageTable[i].y, mip);
                            totalPages--;
                            int pageX = (int)(pagePixel.r * physicalPageResolution + 0.5f);
                            int pageY = (int)(pagePixel.g * physicalPageResolution + 0.5f);
                            _physicalPageFlags[pageX, pageY] = false;
                            _activePageCount--;
                        }
                    }

                    if (totalPages <= maxPages)
                        break;
                }

                _pageTable.Apply(false);
            }


            // prepare physical pages
            Queue<Vector3Int> phyCanUsePages = new Queue<Vector3Int>();
            for (int i = 0; i < physicalPageResolution; i++)
            {
                for (int j = 0; j < physicalPageResolution; j++)
                {
                    if (!_physicalPageFlags[i, j])
                    {
                        phyCanUsePages.Enqueue(new Vector3Int(i, j));
                    }
                }
            }

            // render needed pages
            CommandBuffer cmd = new CommandBuffer();
            for (int i = visiablePages.Count - 1; i >= 0; i--)
            {
                int mip = i;
                for (int k = 0; k < visiablePages[i].Count; k++)
                {
                    Color pageValue = _pageTable.GetPixel(visiablePages[i][k].x, visiablePages[i][k].y, mip);
                    if (notRenderedPage(pageValue))
                    {
                        // update page table
                        Vector3Int page;
                        if (phyCanUsePages.TryDequeue(out page))
                        {
                            SetPageTablePixel(visiablePages[i][k].x, visiablePages[i][k].y, mip, new Color((float)(page.x) / physicalPageResolution, (float)(page.y) / physicalPageResolution, mip / (float)vtMips, 1f));
                            _physicalPageFlags[page.x, page.y] = true;

                            usedPageTable.Add(new Vector3Int(visiablePages[i][k].x, visiablePages[i][k].y, mip));

                            for (int m = 0; m < _terrain.terrainData.alphamapTextureCount; m++)
                            {
                                if (m == 0)
                                {
                                    RenderVTPage(0, cmd, page, mip, visiablePages[i][k], false);

                                }
                                else
                                {
                                    RenderVTPage(m, cmd, page, mip, visiablePages[i][k], true);
                                }
                            }

                            _activePageCount++;
                        }
                    }
                }
            }
            cmd.Release();

            _pageTable.Apply(false);

            // set unrendered page map
            for (int i = 0; i < flatVisiblePages.Count; i++)
            {
                Vector3Int page = flatVisiblePages[i];
                Vector3Int unRenderedPage = page;
                Color pageTarget = new Color(0, 0, 1, 0);


                Color currentPixel = _pageTable.GetPixel(page.x, page.y, page.z);
                if (!notRenderedPage(currentPixel))
                    continue;

                while (true)
                {
                    Vector3Int prePage = new Vector3Int(page.x / 2, page.y / 2, page.z + 1);
                    if (prePage.z >= _pageTable.mipmapCount)
                        break;

                    Color pixel = _pageTable.GetPixel(prePage.x, prePage.y, prePage.z);
                    if (!notRenderedPage(pixel))
                    {
                        pageTarget = pixel;
                        break;
                    }
                    else
                    {
                        page = prePage;
                    }
                }

                // pageTarget
                SetPageTablePixel(unRenderedPage.x, unRenderedPage.y, unRenderedPage.z, pageTarget);
                remappedPageTable.Add(unRenderedPage);
            }

            _pageTable.Apply(false);

            if (useRuntimeCompress)
            {
                // compress albedo
                int kernelHandle = compressCompute.FindKernel("CSMain");
                compressCompute.SetTexture(kernelHandle, "Result", compressRT);
                compressCompute.SetTexture(kernelHandle, "RenderTexture0", _physicalAlbedoTexture);
                compressCompute.SetInts("DestRect", new int[4] { 0, 0, physicalResolution, physicalResolution });
                compressCompute.Dispatch(kernelHandle, physicalResolution / 4 / 8, physicalResolution / 4 / 8, 1);
                Graphics.CopyTexture(compressRT, 0, 0, 0, 0, physicalResolution / 4, physicalResolution / 4, _compressAlbedoTexture, 0, 0, 0, 0);
                // compress normal
                compressCompute.SetTexture(kernelHandle, "Result", compressRT);
                compressCompute.SetTexture(kernelHandle, "RenderTexture0", _physicalNormalTexture);
                compressCompute.SetInts("DestRect", new int[4] { 0, 0, physicalResolution, physicalResolution });
                compressCompute.Dispatch(kernelHandle, physicalResolution / 4 / 8, physicalResolution / 4 / 8, 1);
                Graphics.CopyTexture(compressRT, 0, 0, 0, 0, physicalResolution / 4, physicalResolution / 4, _compressNormalTexture, 0, 0, 0, 0);
            }
        }

        //--- mono
        void Awake()
        {
            // terrain
            _terrain = GetComponent<Terrain>();
            TerrainLayer[] terrainLayers = _terrain.terrainData.terrainLayers;
            Texture2D[] alphaTextures = _terrain.terrainData.alphamapTextures;
            terrainSize = _terrain.terrainData.size;
        }

        void Start()
        {
            if (bOpenVT)
            {
                _terrain.materialTemplate = shadingMat;
            }
            else
            {
                return;
            }

            InitialVtResolution();
            InitialShadingMat();
            InitialBakeMat();
            InitialVirtualTexture();
            InitialPhysicalPageFlag();


            // CommandBuffer cmd = new CommandBuffer();
            // cmd.Blit(tex, _physicalAlbedoTexture);
            // cmd.Blit(tex, _physicalNormalTexture);
            // Graphics.ExecuteCommandBuffer(cmd);
            // cmd.Release();
        }

        void Update()
        {
            if (!bOpenVT)
            {
                return;
            }

            UpdatePageTable();
            UpdateShadingMat();
        }

        void OnDestroy()
        {
            if (!bOpenVT)
            {
                return;
            }

            GameObject.Destroy(_physicalAlbedoTexture);
            GameObject.Destroy(_physicalNormalTexture);
            GameObject.Destroy(_pageTable);
            GameObject.Destroy(bakeMat);
            // GameObject.Destroy(_compressAlbedoTexture);
            // GameObject.Destroy(_compressNormalTexture);
        }

    }

#if UNITY_EDITOR
    [CustomEditor(typeof(TerrainVT))]
    public class TerrainVTEditor : Editor
    {
        int mipLevel = 0;
        public override void OnInspectorGUI()
        {
            if (!Application.isPlaying)
                base.OnInspectorGUI();

            TerrainVT terrainVT = target as TerrainVT;


            if (terrainVT._pageTable != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.Space();
                GUILayout.Label("Vt PageTable Preview");
                mipLevel = EditorGUILayout.IntSlider("pageTable mip", mipLevel, 0, terrainVT._pageTable.mipmapCount);

                Rect rect = EditorGUILayout.GetControlRect(GUILayout.Width(256), GUILayout.Height(256));
                rect.center = new Vector2(Screen.width / 2, rect.center.y);
                EditorGUI.DrawPreviewTexture(rect, terrainVT._pageTable, null, ScaleMode.ScaleToFit, 0, mipLevel);
            }

            if (terrainVT._physicalAlbedoTexture != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.Space();
                GUILayout.Label("Vt Albedo Preview");

                Rect rect = EditorGUILayout.GetControlRect(GUILayout.Width(400), GUILayout.Height(400));
                rect.center = new Vector2(Screen.width / 2, rect.center.y);
                EditorGUI.DrawPreviewTexture(rect, terrainVT._physicalAlbedoTexture);
            }

            if (terrainVT._physicalNormalTexture != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.Space();
                GUILayout.Label("Vt Normal Preview");

                Rect rect = EditorGUILayout.GetControlRect(GUILayout.Width(400), GUILayout.Height(400));
                rect.center = new Vector2(Screen.width / 2, rect.center.y);
                EditorGUI.DrawPreviewTexture(rect, terrainVT._physicalNormalTexture);
            }

        }
    }
#endif

}