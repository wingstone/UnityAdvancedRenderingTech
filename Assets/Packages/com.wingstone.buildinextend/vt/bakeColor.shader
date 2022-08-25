Shader "ARP/bakeColor"
{
    Properties
    {
        _ColorTex0 ("Texture", 2D) = "white" {}
        _ColorTex1 ("Texture", 2D) = "white" {}
        _ColorTex2 ("Texture", 2D) = "white" {}
        _ColorTex3 ("Texture", 2D) = "white" {}
        _WeightMask("Weight mask", 2D) = "black" {}
        _Tiling("Tiling", Vector) = (1,1,1,1)
        _UVScaleOffset("UV Scale Offset", Vector) = (1,1,0,0)
    }
    SubShader
    {
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
                float2 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 vertex : SV_POSITION;
            };

            sampler2D _ColorTex0;
            sampler2D _ColorTex1;
            sampler2D _ColorTex2;
            sampler2D _ColorTex3;
            sampler2D _WeightMask;
            float4 _Tiling;
            float4 _UVScaleOffset;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                v.uv = v.uv*_UVScaleOffset.xy + _UVScaleOffset.zw;
                o.uv0 = v.uv;
                o.uv1.xy = v.uv*_Tiling.x;
                o.uv1.zw = v.uv*_Tiling.y;
                o.uv2.xy = v.uv*_Tiling.z;
                o.uv2.zw = v.uv*_Tiling.w;

                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 col0 = tex2D(_ColorTex0, i.uv1.xy);
                float4 col1 = tex2D(_ColorTex1, i.uv1.zw);
                float4 col2 = tex2D(_ColorTex2, i.uv2.xy);
                float4 col3 = tex2D(_ColorTex3, i.uv2.zw);
                float4 weights = tex2D(_WeightMask, i.uv0);

                float4 col = col0*weights.x + col1*weights.y + col2*weights.z + col3*weights.w;

                col.w = 1;
                return col;
            }
            ENDCG
        }
    }
}
