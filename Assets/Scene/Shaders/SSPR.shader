Shader "Hidden/AdvancedRTR/SSPR"
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

            float4 frag(VaryingsDefault i) : SV_Target0
            {
                float3 normal = tex2D(_CameraGBufferTexture2, i.uv).rgb;
                // skybox
                if (dot(normal, 1.0) == 0.0)
                {
                    return float4(tex2D(_MainTex, i.uv).rgb, 0);
                }

                normal = normal*2.0-1.0;
                normal = mul(_ViewMatrix, normal);

                float3 rayOri = GetViewSpacePosition(i.uv);
                float3 rayDir = reflect(normalize(rayOri), normal);
                
                // face camera
                if(rayDir.z > 0)
                {
                    return float4(tex2D(_MainTex, i.uv).rgb, 0);
                }

                float3 hitPointSS = float3(-1.0f, -1.0f, 0.0f);
                bool hited = false;
                float factor = 0.0f;
                float noise = hash12(i.uv + _Time.y);

                #ifdef TRAVERSAL_SCHEME_RAY_MARCH_3D
                    hited = rayMarch3D(rayOri, rayDir, noise, factor, hitPointSS);
                #endif

                #ifdef TRAVERSAL_SCHEME_NON_CONSERVATIVE
                    float3 originVS = rayOri;
                    float3 endPointVS = originVS + rayDir;
                    float4 clipPos0 = mul(_ScreenSpaceProjectionMatrix, float4(originVS, 1));
                    clipPos0.xy /= clipPos0.w;
                    float2 pointSS0 = (clipPos0.xy*0.5 + 0.5)*_MainTex_TexelSize.zw;
                    float4 clipPos1 = mul(_ScreenSpaceProjectionMatrix, float4(endPointVS, 1));
                    clipPos1.xy /= clipPos1.w;
                    float2 pointSS1 = (clipPos1.xy*0.5 + 0.5)*_MainTex_TexelSize.zw;
                    float pointHS0 = clipPos0.w;
                    float pointHS1 = clipPos1.w;
                    hited = nonConservativeDDATracing(pointSS0.xy, pointSS1.xy, pointHS0, pointHS1, originVS, endPointVS, noise, factor, hitPointSS);
                #endif

                float4 color = tex2D(_MainTex, hitPointSS.xy);
                //  color = float4(hitPointSS.xy, 0, 1);

                return float4(color.rgb, hited);
            }
            ENDCG
        }
    }
}
