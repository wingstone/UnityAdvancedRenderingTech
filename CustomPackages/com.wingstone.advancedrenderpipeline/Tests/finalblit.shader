Shader "Hidden/ARP/finalblit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _UVStartAtUp

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

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

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = float4(v.uv*2-1, 0, 1);
                o.uv = v.uv;

                #if _UVStartAtUp
                    o.uv.y = 1- o.uv.y;
                #endif
                
                return o;
            }
            
            TEXTURE2D(_MainTex);                   SAMPLER(sampler_linear_clamp);

            float3 ACESToneMapping(float3 color)
            {
                const float A = 2.51f;
                const float B = 0.03f;
                const float C = 2.43f;
                const float D = 0.59f;
                const float E = 0.14f;

                return (color * (A * color + B)) / (color * (C * color + D) + E);
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_linear_clamp, i.uv);

                col.rgb = ACESToneMapping(col.rgb);

                return col;
            }
            ENDHLSL
        }
    }
}
