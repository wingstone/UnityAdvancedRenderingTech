Shader "ARP/Transparent"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags {  "RenderType"="Transparent" "Queue" = "Transparent"}
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
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

			CBUFFER_START(UnityPerMaterial)
            float4 _Color;
			CBUFFER_END

            v2f vert (appdata v)
            {
                v2f o;
				o.vertex = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0));
				o.vertex = mul(unity_MatrixVP, o.vertex);
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
