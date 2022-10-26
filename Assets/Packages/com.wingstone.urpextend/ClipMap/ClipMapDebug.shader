Shader "Custom/ClipMapDebug"
{
    Properties
    {
        _ClipmapArray ("Texture", 2DArray) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
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
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            // sampler2D _ClipmapArray;
            UNITY_DECLARE_TEX2DARRAY(_ClipmapArray);

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float3 WrapUV(float2 uv)
            {
                float y = uv.y * 6;
                int index = y;
                return float3(uv.x, y - index, index);
            }

            float4 frag (v2f i) : SV_Target
            {

                // float4 col = tex2D(_ClipmapArray, i.uv);
                float4 col = UNITY_SAMPLE_TEX2DARRAY(_ClipmapArray, WrapUV(i.uv.xy));
                
                return col;
            }
            ENDCG
        }
    }
}
