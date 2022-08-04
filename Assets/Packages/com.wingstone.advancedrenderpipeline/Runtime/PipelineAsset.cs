using Unity.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARP
{
     public enum ShadowResolutionEnum
    {
        _512,
        _1024,
        _2048,
    }
     public enum ShadowBlendEnum
    {
        NoBlend,
        Blend,
        Dither,
    }
    public class PipelineAsset : RenderPipelineAsset
    {
        //==data
        [Header("ShadowSetting")]
        public ShadowResolutionEnum _ShadowResolutioinEnum = ShadowResolutionEnum._1024;
        [HideInInspector]
        [Range(256, 4096)]
        public int _ShadowResolution = 1024;
        [Range(1f, 2000f)]
        public float _ShadowDistance = 100f;
        [Range(0.001f, 10f)]
        public float _ShadowDepthBias = 0.1f;
        [Range(0.1f, 10f)]
        public float _ShadowBorderFadeLength = 3f;

        [Header("CascadeSetting")]
        [Range(0.001f, 1f)]
        public float _Split0 = 0.1f;
        [Range(0.001f, 1f)]
        public float _Split1 = 0.25f;
        [Range(0.001f, 1f)]
        public float _Split2 = 0.5f;
        [Range(0.01f, 5f)]
        public float _ShadowBlendLenth = 1f;
        public ShadowBlendEnum _blendMethod = ShadowBlendEnum.NoBlend;

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
