Shader "ARP/ApplyFog"
{
    Properties
    {
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Blend One SrcAlpha

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

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler3D _ScatteringRT;
            float3 _CameraPos;
            float3 _VolumeResolution;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4x4 _InverseViewProj;
            
            float3 _RayCenter;
            float3 _RayOffsetX;
            float3 _RayOffsetY;

            float DistanceToW(float dist)
            {
                return saturate(dist/100);
            }

            int ihash(int n)
            {
                n = (n<<13)^n;
                return (n*(n*n*15731+789221)+1376312589) & 2147483647;
            }

            float frand(int n)
            {
                return ihash(n) / 2147483647.0;
            }

            float2 cellNoise(int2 p)
            {
                int i = p.y*256 + p.x;
                return float2(frand(i), frand(i + 57)) - 0.5;//*2.0-1.0;
            }
            
            float2 hash( float2 p ) // replace this by something better
            {
                p = float2( dot(p,float2(127.1,311.7)), dot(p,float2(269.5,183.3)) );
                return -0.5 + frac(sin(p)*43758.5453123 + _Time.y);
            }

            float4 frag (v2f i) : SV_Target
            {

                float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                // depth = Linear01Depth (depth);
                float4 pos = mul(_InverseViewProj, float4(i.uv*2-1, depth, 1));
                float3 worldPos = pos.xyz/pos.w;

                float2 uv = i.uv*2-1;
                float3 worldDir = _RayCenter + _RayOffsetX*uv.x + _RayOffsetY*uv.y;
                float w = DistanceToW(distance(worldPos, _CameraPos)/length(worldDir));

                float3 uvw = float3(i.uv, w);
	            uvw.xy += hash(i.uv * _ScreenParams.zw) / _VolumeResolution.xy;
                float4 col = tex3D(_ScatteringRT, uvw);

                // col = float4(col.rgb, 0);
                // col = float4(col.w,col.w,col.w, 0);
                // col = float4(hash(i.uv * _ScreenParams.zw), 0, 0);
                return col;
            }
            ENDCG
        }
    }
}
