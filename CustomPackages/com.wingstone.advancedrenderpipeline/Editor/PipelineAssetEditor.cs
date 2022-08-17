using UnityEngine;
using UnityEditor;

namespace ARP
{
    [CustomEditor(typeof(PipelineAsset))]
    public class PipelineAssetEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            PipelineAsset asset = target as PipelineAsset;
            switch (asset._ShadowResolutioinEnum)
            {
                case ShadowResolutionEnum._512:
                    asset._ShadowResolution = 512;
                    break;
                case ShadowResolutionEnum._1024:
                    asset._ShadowResolution = 1024;
                    break;
                case ShadowResolutionEnum._2048:
                    asset._ShadowResolution = 2048;
                    break;
                default:
                    asset._ShadowResolution = 2048;
                    break;
            }
        }
    }
}