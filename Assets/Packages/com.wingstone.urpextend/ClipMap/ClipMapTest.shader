Shader "Custom/ClipMapTest"
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

            float4 pageParameter; // xy: page index, zw: page count
            

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f input) : SV_Target
            {

                // float4 col = tex2D(_ClipmapArray, i.uv);
                float2 center = float2(pageParameter.xy)/pageParameter.zw;
                int2 currentPage = input.uv*pageParameter.zw;
                int2 pageOffset = currentPage - pageParameter.xy;
                int maxOffset = max(pageOffset.x, pageOffset.y);
                int slice = 0;
                for(int i = 0; i < 6; i++)
                {
                    int scale = 1 << i;
                    if(currentPage.x / scale >= pageParameter.x / scale - 2
                    && currentPage.x / scale <= pageParameter.x / scale + 1
                    && currentPage.y / scale >= pageParameter.y / scale - 2
                    && currentPage.y / scale <= pageParameter.y / scale + 1)
                    {
                        slice = i;
                        break;
                    }
                }

                // float2 uvoffset = abs(input.uv - center);
                // float len = max(uvoffset.x, uvoffset.y);
                // int slice = log2(max(1,len*pageParameter.z));
                // slice = min(slice, 5);

                // // accurate
                // float2 dx = ddx(input.uv * 8192);
                // float2 dy = ddy(input.uv * 8192);
                // float d = max( sqrt( dot( dx.x, dx.x ) + dot( dx.y, dx.y ) ) ,
                // sqrt( dot( dy.x, dy.x ) + dot( dy.y, dy.y ) ) );
                // float mipLevel = log2( d );
                // slice = mipLevel;

                float2 uv = input.uv.xy * (1 << (5-slice));
                float4 col = UNITY_SAMPLE_TEX2DARRAY(_ClipmapArray, float3(uv, slice));
            
                // col.rg = slice/6.0f;
                // col.b = 0;
                return col;
            }
            ENDCG
        }
    }
}
