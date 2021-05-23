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

            #include "SSR.hlsl"


            float4 frag(VaryingsDefault i) : SV_Target0
            {
                float3 normal = tex2D(_CameraGBufferTexture2, i.uv).rgb*2.0-1.0;
                normal = mul(_ViewMatrix, normal);

                float3 rayOri = GetViewSpacePosition(i.uv);
                float3 rayDir = reflect(normalize(rayOri), normal);
                
                // skybox
                if (dot(normal, 1.0) == 0.0)
                return float4(tex2D(_MainTex, i.uv).rgb, 0);
                
                // face camera
                if(rayDir.z > 0)
                return float4(tex2D(_MainTex, i.uv).rgb, 0);

                float3 hitPointSS = float3(-1.0f, -1.0f, 0.0f);
                bool hited = false;
                float factor = 0.0f;

                #ifdef TRAVERSAL_SCHEME_RAY_MARCH_3D
                    hited = rayMarch3D(rayOri, rayDir, factor, hitPointSS);
                #endif

                #ifdef TRAVERSAL_SCHEME_NON_CONSERVATIVE
                    hited = nonConservativeDDATracing(pointSS0.xy, pointSS1.xy, pointHS1.w, originVS, endPointVS, factor, hitPointSS);
                #endif

                float4 color = tex2D(_MainTex, hitPointSS.xy);

                return float4(color.rgb, hited);
            }
            ENDCG
        }
    }
}
