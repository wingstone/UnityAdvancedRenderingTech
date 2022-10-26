Shader "Universal Render Pipeline/Terrain/VT_Bake"
{
    Properties
    {
        _LayerTex0 ("Layer Texture0", 2D) = "white" {}
        _LayerTex1 ("Layer Texture1", 2D) = "white" {}
        _LayerTex2 ("Layer Texture2", 2D) = "white" {}
        _LayerTex3 ("Layer Texture3", 2D) = "white" {}

        _LayerColorTint0("Layer Color Tint0", Color) = (1,1,1,1)
        _LayerColorTint1("Layer Color Tint1", Color) = (1,1,1,1)
        _LayerColorTint2("Layer Color Tint2", Color) = (1,1,1,1)
        _LayerColorTint3("Layer Color Tint3", Color) = (1,1,1,1)

        _LayerNormalTex0 ("Texture", 2D) = "bump" {}
        _LayerNormalTex1 ("Texture", 2D) = "bump" {}
        _LayerNormalTex2 ("Texture", 2D) = "bump" {}
        _LayerNormalTex3 ("Texture", 2D) = "bump" {}

        _LayerMaskTex0 ("Texture", 2D) = "gray" {}
        _LayerMaskTex1 ("Texture", 2D) = "gray" {}
        _LayerMaskTex2 ("Texture", 2D) = "gray" {}
        _LayerMaskTex3 ("Texture", 2D) = "gray" {}

        _WeightMask("Weight mask", 2D) = "black" {}
        _PageResolution("Page Resolution", Float) = 256
        
        _Layer_ST0("Layer Scale Offset0", Vector) = (1,1,0,0)
        _Layer_ST1("Layer Scale Offset1", Vector) = (1,1,0,0)
        _Layer_ST2("Layer Scale Offset2", Vector) = (1,1,0,0)
        _Layer_ST3("Layer Scale Offset3", Vector) = (1,1,0,0)
        
        _Layer_NormalScale("Layer Normal Scale", Vector) = (1,1,1,1)
        
        _Layer_RemapMin0("Layer Remap Min0", Vector) = (1,1,1,1)
        _Layer_RemapMin1("Layer Remap Min1", Vector) = (1,1,1,1)
        _Layer_RemapMin2("Layer Remap Min2", Vector) = (1,1,1,1)
        _Layer_RemapMin3("Layer Remap Min3", Vector) = (1,1,1,1)
        
        _Layer_RemapMax0("Layer Remap Max0", Vector) = (0,0,0,0)
        _Layer_RemapMax1("Layer Remap Max1", Vector) = (0,0,0,0)
        _Layer_RemapMax2("Layer Remap Max2", Vector) = (0,0,0,0)
        _Layer_RemapMax3("Layer Remap Max3", Vector) = (0,0,0,0)

        [HideInInspector] _SrcBlend("Src", Float) = 1.0
        [HideInInspector] _DstBlend("Dst", Float) = 0.0
    }
    SubShader
    {
        Pass
        {
            Name "Bake albedo and normal"
            Cull Off
            Blend[_SrcBlend][_DstBlend]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 vertex : SV_POSITION;
            };
            struct FragmentOutput
            {
                float4 output0 : SV_Target0;
                float4 output1 : SV_Target1;
            };

            sampler2D _LayerTex0;
            sampler2D _LayerTex1;
            sampler2D _LayerTex2;
            sampler2D _LayerTex3;

            float4 _LayerColorTint0;
            float4 _LayerColorTint1;
            float4 _LayerColorTint2;
            float4 _LayerColorTint3;

            sampler2D _LayerNormalTex0;
            sampler2D _LayerNormalTex1;
            sampler2D _LayerNormalTex2;
            sampler2D _LayerNormalTex3;

            sampler2D _LayerMaskTex0;
            sampler2D _LayerMaskTex1;
            sampler2D _LayerMaskTex2;
            sampler2D _LayerMaskTex3;

            sampler2D _WeightMask;

            float4 _Layer_ST0;
            float4 _Layer_ST1;
            float4 _Layer_ST2;
            float4 _Layer_ST3;

            float4 _Layer_NormalScale;

            float4 _Layer_RemapMin0;
            float4 _Layer_RemapMin1;
            float4 _Layer_RemapMin2;
            float4 _Layer_RemapMin3;

            float4 _Layer_RemapMax0;
            float4 _Layer_RemapMax1;
            float4 _Layer_RemapMax2;
            float4 _Layer_RemapMax3;

            float4 _UVScaleOffset;
            float _PageResolution;
            
            float2 TransformTriangleVertexToUV(float2 vertex)
            {
                float2 uv = (vertex + 1.0) * 0.5;
                return uv;
            }

            float2 ProcessBorder(float2 uv)
            {
                return uv*(_PageResolution)/(_PageResolution-2) - 1.0f/(_PageResolution-2);
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = float4(v.vertex.xy, 0.0, 1.0);
                o.uv0 = TransformTriangleVertexToUV(v.vertex.xy);
                            
                #if UNITY_UV_STARTS_AT_TOP
                    o.uv0 = o.uv0 * float2(1.0, -1.0) + float2(0.0, 1.0);
                #endif

                o.uv0 = ProcessBorder(o.uv0);
                o.uv0 = o.uv0*_UVScaleOffset.xy + _UVScaleOffset.zw;

                o.uv1.xy = o.uv0*_Layer_ST0.xy + _Layer_ST0.zw;
                o.uv1.zw = o.uv0*_Layer_ST1.xy + _Layer_ST1.zw;
                o.uv2.xy = o.uv0*_Layer_ST2.xy + _Layer_ST2.zw;
                o.uv2.zw = o.uv0*_Layer_ST3.xy + _Layer_ST3.zw;

                return o;
            }

            // float4 frag (v2f i) : SV_Target0
            FragmentOutput frag (v2f i)
            {
                float4 col0 = tex2D(_LayerTex0, i.uv1.xy) * _LayerColorTint0;
                float4 col1 = tex2D(_LayerTex1, i.uv1.zw) * _LayerColorTint1;
                float4 col2 = tex2D(_LayerTex2, i.uv2.xy) * _LayerColorTint2;
                float4 col3 = tex2D(_LayerTex3, i.uv2.zw) * _LayerColorTint3;

                float4 mask0 = tex2D(_LayerMaskTex0, i.uv1.xy);
                mask0 = lerp(_Layer_RemapMin0, _Layer_RemapMax0, mask0);
                float4 mask1 = tex2D(_LayerMaskTex1, i.uv1.zw);
                mask1 = lerp(_Layer_RemapMin1, _Layer_RemapMax1, mask1);
                float4 mask2 = tex2D(_LayerMaskTex2, i.uv2.xy);
                mask2 = lerp(_Layer_RemapMin2, _Layer_RemapMax2, mask2);
                float4 mask3 = tex2D(_LayerMaskTex3, i.uv2.zw);
                mask3 = lerp(_Layer_RemapMin3, _Layer_RemapMax3, mask3);
                
                float3 normal0 = UnpackNormalWithScale(tex2D(_LayerNormalTex0, i.uv1.xy), _Layer_NormalScale.x)*0.5+0.5;
                float3 normal1 = UnpackNormalWithScale(tex2D(_LayerNormalTex1, i.uv1.zw), _Layer_NormalScale.y)*0.5+0.5;
                float3 normal2 = UnpackNormalWithScale(tex2D(_LayerNormalTex2, i.uv2.xy), _Layer_NormalScale.z)*0.5+0.5;
                float3 normal3 = UnpackNormalWithScale(tex2D(_LayerNormalTex3, i.uv2.zw), _Layer_NormalScale.w)*0.5+0.5;

                float4 weights = tex2D(_WeightMask, i.uv0);

                float4 col = col0*weights.x + col1*weights.y + col2*weights.z + col3*weights.w;

                float4 mask = mask0*weights.x + mask1*weights.y + mask2*weights.z + mask3*weights.w;

                float4 normal = 0;

                normal.rgb = saturate(normal0*weights.x + normal1*weights.y + normal2*weights.z + normal3*weights.w);
                
                FragmentOutput Output;
                Output.output0 = float4(col.rgb, mask.g);   // rgb: color, a: ao
                Output.output1 = float4(normal.rgb, mask.a);    // rgb: normal, a: smoothness
                return Output;
                // return float4(col.rgb, mask.g);
            }
            ENDCG
        }
    }
}
