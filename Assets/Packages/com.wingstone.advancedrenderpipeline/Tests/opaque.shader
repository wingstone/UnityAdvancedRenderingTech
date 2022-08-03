Shader "ARP/Opaque"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags {  "RenderType"="Opaque" "Queue" = "Geometry"}
        // LOD 100

        Pass
        {
            Tags { "LightMode" = "ARP" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            CBUFFER_START(UnityPerDraw)
                float4x4 unity_ObjectToWorld;
            CBUFFER_END

            CBUFFER_START(UnityPerFrame)
                float4x4 unity_MatrixVP;
            CBUFFER_END

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
                float3 normal : TEXCOORD2;
                float4 vertex : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            
            float4x4 _ShadowMatrix;
            float3 _MainLightDirection;
            float4 _ShadowSphere;
            float _ShadowBorderFadeLength;
            TEXTURE2D_SHADOW(_ShadowMapTexture);               SAMPLER_CMP(sampler_linear_clamp_compare);

            v2f vert (appdata v)
            {
                v2f o;
                o.worldPos = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0));
                o.vertex = mul(unity_MatrixVP, o.worldPos);
                o.uv = v.uv;
                o.normal = mul(unity_ObjectToWorld, float4(v.normal, 0.0)).xyz;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 normal = normalize(i.normal);
                float diffuse = saturate(dot(normal, _MainLightDirection));
                float3 posSTS = mul(_ShadowMatrix, i.worldPos).xyz * 0.5 + 0.5;
                
                float attantion = SAMPLE_TEXTURE2D_SHADOW(_ShadowMapTexture, sampler_linear_clamp_compare, posSTS).r;
                if(posSTS.z >= 0.999 || posSTS.z <=0.001) attantion = 1;
                float fadeAtten = saturate( (distance(_ShadowSphere.xyz, i.worldPos) - _ShadowSphere.w) / _ShadowBorderFadeLength );
                attantion = max(attantion, fadeAtten);
                

                // return float4(posSTS.xy, 0,1);
                return diffuse*attantion;
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            CBUFFER_START(UnityPerDraw)
                float4x4 unity_ObjectToWorld;
            CBUFFER_END

            CBUFFER_START(UnityPerFrame)
                float4x4 unity_MatrixVP;
            CBUFFER_END

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END
            
            float _ShadowDepthBias;
            float3 _MainLightDirection;

            v2f vert (appdata v)
            {
                v2f o;
                float4 worldPos = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0));
                worldPos.xyz -= _MainLightDirection * _ShadowDepthBias;

                o.vertex = mul(unity_MatrixVP, worldPos);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
