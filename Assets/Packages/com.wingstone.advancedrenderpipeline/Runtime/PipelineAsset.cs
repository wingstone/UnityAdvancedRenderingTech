using Unity.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARP
{
    public class PipelineAsset : RenderPipelineAsset
    {
        //==data
        [Header("ShadowSetting")]
        [Range(512, 2048)]
        public int _ShadowResolution = 1024;
        public float _ShadowDistance = 100f;
        public float _ShadowDepthBias = 0.1f;
        public float _ShadowBorderFadeLength = 3f;

        //== resources
        public Material _ShadowMat = null;

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Assets/Create/AdvancedRenderPipeline/PipelineAsset", priority = 0)]
        static void CreatePipelineAsset()
        {
            var instance = ScriptableObject.CreateInstance<PipelineAsset>();
            UnityEditor.AssetDatabase.CreateAsset(instance, "Assets/PipelineAsset.asset");
        }
#endif

        protected override RenderPipeline CreatePipeline()
        {
            return new AdvancedRenderPipeline(this);
        }
    }

}