Shader "Custom/ClipMapBake"
{
    Properties
    {
        _DetailTex("tex", 2D) = "white"
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

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

            float2 TransformTriangleVertexToUV(float2 vertex)
            {
                float2 uv = (vertex + 1.0) * 0.5;
                return uv;
            }


            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = float4(v.vertex.xy, 0.0, 1.0);
                o.uv = TransformTriangleVertexToUV(v.vertex.xy);
                # if UNITY_UV_STARTS_AT_TOP
                        o.uv.y = 1-o.uv.y;
                # endif
                return o;
            }

            float4 pageParameter; // xy: page index, zw: page count
            sampler2D _DetailTex;

            float2 PageUVToVirtualTextureUV(float2 uv)
            {
                return (uv + pageParameter.xy) / pageParameter.zw;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 col = tex2D(_DetailTex, PageUVToVirtualTextureUV(i.uv.xy) * 10);
                // col.rg = PageUVToVirtualTextureUV(i.uv.xy);
                // col.ba = 0;
                
                return col;
            }
            ENDCG
        }
    }
}
