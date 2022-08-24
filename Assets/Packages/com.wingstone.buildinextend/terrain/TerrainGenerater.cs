using System.Collections;
using System.Collections.Generic;
using UnityEngine;
// using UnityEditor;

public class TerrainGenerater : MonoBehaviour
{
    // 地图大小
    public int worldSize = 2048;
    public float detailRadius = 10;
    public Material mat = null;
    public Camera currentCamera = null;

    private static Mesh _patchMesh = null;
    private static int _patchSize = 16;
    private static float _patchGridExtent = 1f;
    public static Mesh patchMesh
    {
        get
        {
            if (_patchMesh == null)
            {
                _patchMesh = new Mesh();

                List<Vector3> vertices = new List<Vector3>();
                List<Vector2> uvs = new List<Vector2>();
                List<int> indices = new List<int>();
                Vector3 offset = new Vector3(-_patchGridExtent * _patchSize * 0.5f, 0f, -_patchGridExtent * _patchSize * 0.5f);

                for (int i = 0; i <= _patchSize; i++)
                {
                    for (int j = 0; j <= _patchSize; j++)
                    {
                        vertices.Add(new Vector3(_patchGridExtent * i + offset.x, 0, _patchGridExtent * j + offset.z));
                        uvs.Add(new Vector2((float)i / _patchSize, (float)j / _patchSize));
                    }
                }

                for (int i = 0; i < _patchSize; i++)
                {
                    for (int j = 0; j < _patchSize; j++)
                    {
                        indices.Add(i * (_patchSize + 1) + j);
                        indices.Add((i + 1) * (_patchSize + 1) + j + 1);
                        indices.Add((i + 1) * (_patchSize + 1) + j);

                        indices.Add(i * (_patchSize + 1) + j);
                        indices.Add(i * (_patchSize + 1) + j + 1);
                        indices.Add((i + 1) * (_patchSize + 1) + j + 1);
                    }
                }

                _patchMesh.SetVertices(vertices);
                _patchMesh.SetUVs(0, uvs);
                _patchMesh.SetIndices(indices, MeshTopology.Triangles, 0);

            }
            return _patchMesh;
        }
    }

    class QuadTreeNode
    {
        public List<QuadTreeNode> childNodes;

        public int lod;
        public Vector2 center;

        public QuadTreeNode(int lod, Vector2 center)
        {
            this.lod = lod;
            this.center = center;
            childNodes = null;
        }
    }

    QuadTreeNode root = null;

    void BuildQuadTree(QuadTreeNode node)
    {
        float patchExtent = worldSize / Mathf.Pow(2, node.lod);
        float patchGridExtent = patchExtent / 16;
        if (patchGridExtent > 1)
        {
            node.childNodes = new List<QuadTreeNode>();
            node.childNodes.Add(new QuadTreeNode(node.lod + 1, node.center + new Vector2(-patchExtent * 0.25f, -patchExtent * 0.25f)));
            node.childNodes.Add(new QuadTreeNode(node.lod + 1, node.center + new Vector2(-patchExtent * 0.25f, patchExtent * 0.25f)));
            node.childNodes.Add(new QuadTreeNode(node.lod + 1, node.center + new Vector2(patchExtent * 0.25f, -patchExtent * 0.25f)));
            node.childNodes.Add(new QuadTreeNode(node.lod + 1, node.center + new Vector2(patchExtent * 0.25f, patchExtent * 0.25f)));

            for (int i = 0; i < 4; i++)
            {
                BuildQuadTree(node.childNodes[i]);
            }
        }
    }

    List<QuadTreeNode> renderQuadTreeNodes = null;

    void Start()
    {
        root = new QuadTreeNode(0, Vector2.zero);
        BuildQuadTree(root);
    }

    void CullQuadTreeNode(QuadTreeNode node, ref List<QuadTreeNode> culledNode)
    {
        if (node.childNodes == null)
        {
            culledNode.Add(node);
            return;
        }


        //if (CameraViewed(camera, root.lod, root.center))
        {
            if (NeedDevided(node.lod, node.center, currentCamera.transform.position))
            {
                // CullQuadTreeNode(node.childNodes[0], ref culledNode, new Vector4(lodoffset.x+1, lodoffset.y+1, lodoffset.z, lodoffset.w));
                // CullQuadTreeNode(node.childNodes[1], ref culledNode, new Vector4(lodoffset.x, lodoffset.y+1, lodoffset.z+1, lodoffset.w));
                // CullQuadTreeNode(node.childNodes[2], ref culledNode, new Vector4(lodoffset.x+1, lodoffset.y, lodoffset.z, lodoffset.w+1));
                // CullQuadTreeNode(node.childNodes[3], ref culledNode, new Vector4(lodoffset.x, lodoffset.y, lodoffset.z+1, lodoffset.w+1));

                foreach (var childNode in node.childNodes)
                {
                    CullQuadTreeNode(childNode, ref culledNode);
                }
            }
            else if (CameraViewed(currentCamera, node.lod, node.center))
            {
                culledNode.Add(node);
            }
        }
    }

    bool CameraViewed(Camera cam, int lod, Vector2 center)
    {
        Plane[] planes = new Plane[6];
        GeometryUtility.CalculateFrustumPlanes(cam, planes);

        float patchExtent = worldSize / Mathf.Pow(2, lod);
        Bounds bounds = new Bounds();
        bounds.center = new Vector3(center.x, 0, center.y);
        bounds.size = new Vector3(patchExtent, 2000, patchExtent);

        return GeometryUtility.TestPlanesAABB(planes, bounds);
    }

    bool NeedDevided(int lod, Vector2 center, Vector3 cameraPos)
    {
        float patchExtent = worldSize / Mathf.Pow(2, lod);
        Vector2 refPos = new Vector2(Mathf.Abs(cameraPos.x - center.x), Mathf.Abs(cameraPos.z - center.y)) - Vector2.one * patchExtent;
        refPos.x = Mathf.Max(0, refPos.x);
        refPos.y = Mathf.Max(0, refPos.y);
        float dist = refPos.magnitude;

        return dist <= detailRadius;
    }

    public Texture2D lodMap = null;

    Texture2D GenarateLodMap(List<QuadTreeNode> culledNode)
    {
        if (lodMap == null) lodMap = new Texture2D(128, 128, TextureFormat.Alpha8, 0, true);

        foreach (var node in culledNode)
        {
            if (node.lod == 7)
            {
                Vector2Int uv = new Vector2Int((int)((node.center.x + 1024) / 16), (int)((node.center.y + 1024) / 16));
                lodMap.SetPixel(uv.x, uv.y, new Color(0, 0, 0, 1));
                continue;
            }

            float patchExtent = worldSize / Mathf.Pow(2, node.lod);
            // float patchGridExtent = patchExtent / 16;
            Vector2 min = node.center + Vector2.one * 1024 - Vector2.one * (0.5f * patchExtent - 8);
            Vector2 max = node.center + Vector2.one * 1024 + Vector2.one * (0.5f * patchExtent - 8);
            for (int i = (int)(min.x / 16); i <= (int)(max.x / 16); i++)
            {
                for (int j = (int)(min.y / 16); j <= (int)(max.y / 16); j++)
                {
                    lodMap.SetPixel(i, j, new Color(0, 0, 0, node.lod / 7f));
                }
            }
        }

        lodMap.Apply();
        return lodMap;
    }

    void Update()
    {
        if (renderQuadTreeNodes != null)
        {
            renderQuadTreeNodes.Clear();
        }
        else
        {
            renderQuadTreeNodes = new List<QuadTreeNode>();
        }

        CullQuadTreeNode(root, ref renderQuadTreeNodes);

        Texture2D lodMap = GenarateLodMap(renderQuadTreeNodes);
        lodMap.filterMode = FilterMode.Point;
        lodMap.wrapMode = TextureWrapMode.Clamp;
        mat.SetTexture("_LodMap", lodMap);
        mat.SetFloat("_LodMapSize", 128);

        Vector4[] lodothers = new Vector4[renderQuadTreeNodes.Count];
        float[] lods = new float[renderQuadTreeNodes.Count];
        Matrix4x4[] matrixs = new Matrix4x4[renderQuadTreeNodes.Count];

        for (int i = 0; i < renderQuadTreeNodes.Count; i++)
        {
            var node = renderQuadTreeNodes[i];
            float scale = worldSize / Mathf.Pow(2, node.lod) / 16;

            // y方向使用相机高度，避免自动视锥剔除
            Matrix4x4 matrix = Matrix4x4.Translate(new Vector3(node.center.x, currentCamera.transform.position.y, node.center.y)) * Matrix4x4.Scale(new Vector3(scale, scale, scale));

            float patchExtent = worldSize / Mathf.Pow(2, node.lod);
            Vector2Int uv = new Vector2Int((int)((node.center.x + 1024) / 16), (int)((node.center.y + 1024) / 16));
            Vector2 min = node.center + Vector2.one * 1024 - Vector2.one * (0.5f * patchExtent + 8);
            Vector2 max = node.center + Vector2.one * 1024 + Vector2.one * (0.5f * patchExtent + 8);
            float leftLod = lodMap.GetPixel(Mathf.Max((int)(min.x / 16), 0), uv.y).a * 7;
            float rightLod = lodMap.GetPixel(Mathf.Max((int)(max.x / 16), 0), uv.y).a * 7;
            float bottomLod = lodMap.GetPixel(uv.x, Mathf.Min((int)(min.y / 16), 127)).a * 7;
            float upLod = lodMap.GetPixel(uv.x, Mathf.Min((int)(max.y / 16), 127)).a * 7;

            lodothers[i] = new Vector4(leftLod, rightLod, bottomLod, upLod);
            lods[i] = node.lod;
            matrixs[i] = matrix;
        }

        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        propertyBlock.SetVectorArray("_LodOther", lodothers);
        propertyBlock.SetFloatArray("_Lod", lods);
        Graphics.DrawMeshInstanced(patchMesh, 0, mat, matrixs, renderQuadTreeNodes.Count, propertyBlock, UnityEngine.Rendering.ShadowCastingMode.Off, false, LayerMask.NameToLayer("Default"));
    }

}



// [CustomEditor(typeof(TerrainGenerater))]
// public class TerrainGeneraterEditor : Editor {
//     public override void OnInspectorGUI() {
//         base.OnInspectorGUI();

//         TerrainGenerater terrainGenerater = target as TerrainGenerater;

//         if(terrainGenerater.lodMap!= null)
//         {
//             GUILayout.p
//         }
        
//     }
// }