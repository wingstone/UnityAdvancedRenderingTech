Shader "ARP/terrainShading"
{
    Properties
    {
        _PhysicalTex ("PhysicalTex", 2D) = "white" {}
        _PageTable ("PageTable", 2D) = "white" {}
        _CameraUV("Camera UV", Vector) = (0,0,0,0)
        _CameraPage("Camera Page", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

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

            sampler2D _PhysicalTex;
            sampler2D _PageTable;
            float4 _CameraPage;
            float4 _CameraUV;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.uv = worldPos.xz/8192 + 0.5;
                // o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                int2 currentPage = i.uv*32;
                int2 cameraPage = int2(_CameraPage.x, _CameraPage.y);
                int2 pageOffset = abs(currentPage -cameraPage);
                int maxOffset = max(pageOffset.x, pageOffset.y);

                int mip = 0;
                for (int n = 0; n < 6; n++)
                {
                    int mipResolution = 8192 >> n;
                    int2 pageIndex = i.uv * mipResolution / 256;
                    int2 cameraPage = _CameraUV * mipResolution / 256;
                    if (pageIndex.x >= cameraPage.x - 1 &&
                        pageIndex.x <= cameraPage.x + 1 &&
                        pageIndex.y >= cameraPage.y - 1 &&
                        pageIndex.y <= cameraPage.y + 1)
                    {
                        mip = n;
                        break;
                    }
                }

                // int mip = log2(maxOffset+1);

                // float2 dx = ddx(i.uv*8192);
                // float2 dy = ddy(i.uv*8192);
                // float d = max( sqrt( dot( dx.x, dx.x ) + dot( dx.y, dx.y ) ), sqrt( dot( dy.x, dy.x ) + dot( dy.y, dy.y ) ) );
                // int mip = (int)log2(d);

                float4 pageTexel = tex2Dlod(_PageTable, float4(i.uv, 0, mip));

                float2 uv = pageTexel.xy +1.0f / 2048 + fmod(i.uv * 8192, 256 << mip) / (256 << mip) / 8.0f * 254.0f / 256;  //一个像素border

                fixed4 col = tex2D(_PhysicalTex, uv);
                 //col = float4(uv.x, uv.x, 0, 1);
                // col = tex2Dlod(_PageTable, float4(i.uv, 0, 1));
                //col = tex2Dlod(_PageTable, float4(i.uv, 0, 2));
                return col;
            }
            ENDCG
        }
    }
}
