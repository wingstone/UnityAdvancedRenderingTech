Shader "Hidden/AdvancedRTR/SSR"
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
            Name "Ray Matching Test"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #define MLAA_THRELOD 0.1

            #include "UnityCG.cginc"
            #include "Common.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            sampler2D _CameraGBufferTexture0; // albedo = g[0].rgb
            sampler2D _CameraGBufferTexture1; // roughness = g[1].a
            sampler2D _CameraGBufferTexture2; // normal.xyz 2. * g[2].rgb - 1.
            sampler2D _CameraDepthTexture; //depth = r;

            float4x4 _ViewMatrix;
            float4x4 _InverseViewMatrix;
            float4x4 _InverseProjectionMatrix;
            float4x4 _ScreenSpaceProjectionMatrix;
            float _RayMatchStep;

            struct Result
            {
                float2 uv;
                float4 color;
            };

            struct Ray
            {
                float3 origin;
                float3 direction;
            };

            float3 GetViewSpacePosition(float2 uv)
            {
                float depth = tex2D(_CameraDepthTexture, uv).r;
                float4 result = mul(_InverseProjectionMatrix, float4(2.0 * uv - 1.0, depth, 1.0));
                return result.xyz / result.w;
            }

            Result RayMatching(Ray ray)
            {
                Result result = (Result)0;
                
                float matchStep = min(_MainTex_TexelSize.x, _MainTex_TexelSize.y);
                for(int i = 0; i < 32; i++)
                {
                    ray.origin += ray.direction*_RayMatchStep * matchStep;
                    float4 position = mul(_ScreenSpaceProjectionMatrix, float4(ray.origin, 1));
                    position.xyz = position.xyz / position.w;
                    float2 uv = position.xy*0.5+0.5;

                    if(uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1 )
                    {
                        break;
                    }

                    float realZ = position.z;
                    if(tex2D(_CameraDepthTexture, uv).r > realZ)
                    {
                        result.uv = uv;
                        result.color = tex2D(_MainTex, uv);
                        // result.color.rg = uv;
                        // result.color.b = 0;
                        break;
                    }
                }
                
                return result;
            }

            float4 frag(VaryingsDefault i) : SV_Target0
            {
                float3 normal = tex2D(_CameraGBufferTexture2, i.uv).rgb*2.0-1.0;
                normal = mul(_ViewMatrix, normal);

                Ray ray = (Ray)0;
                ray.origin = GetViewSpacePosition(i.uv);
                ray.direction = reflect(normalize(ray.origin), normal);

                Result result = RayMatching(ray);

                // return float4(ray.direction, 1);

                return result.color;
            }
            ENDCG
        }
    }
}
