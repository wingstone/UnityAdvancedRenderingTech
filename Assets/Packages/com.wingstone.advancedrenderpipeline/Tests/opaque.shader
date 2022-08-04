Shader "ARP/Opaque"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags {  "RenderType"="Opaque" "Queue" = "Geometry"}
        // LOD 100

        Pass
        {
            Tags { "LightMode" = "ARP" }

            HLSLPROGRAM
            #pragma multi_compile _ CASCADE_NOBLEND CASCADE_BLEND CASCADE_DITHER
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

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
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
                float3 normal : TEXCOORD2;
                float4 pixCoord : TEXCOORD3;
                float4 vertex : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            
            float4 _ShadowSphereArray[4];
            float4x4 _ShadowMatrixArray[4];
            float3 _MainLightDirection;
            float _ShadowBorderFadeLength;
            TEXTURE2D_SHADOW(_ShadowMapTexture);               SAMPLER_CMP(sampler_linear_clamp_compare);
            float4 _ShadowMapTextureSize;
            float _ShadowBlendLenth;

            float4 _ScreenPramaters;

            float4 _ProjectionParams;
            float4 ComputeScreenPos(float4 positionCS)
            {
                float4 o = positionCS * 0.5f;
                o.xy = float2(o.x, o.y * _ProjectionParams.x) + o.w;
                o.zw = positionCS.zw;
                return o;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.worldPos = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0));
                o.vertex = mul(unity_MatrixVP, o.worldPos);
                o.uv = v.uv;
                o.normal = mul(unity_ObjectToWorld, float4(v.normal, 0.0)).xyz;
                o.pixCoord = ComputeScreenPos(o.vertex);
                return o;
            }

            float GetBlendFactor(float3 worldPos, int casacdeID)
            {
                return saturate( (distance(_ShadowSphereArray[casacdeID].xyz, worldPos) - _ShadowSphereArray[casacdeID].w) / _ShadowBlendLenth + 1 );
                
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 normal = normalize(i.normal);
                float diffuse = saturate(dot(normal, _MainLightDirection));

                int casacdeID = 3;
                float blendFactor = 0;

                #if CASCADE_DITHER
                    float noise = InterleavedGradientNoise(i.pixCoord.xy/i.pixCoord.w * _ScreenPramaters.zw, 0);
                #endif

                for(int n = 0; n < 3; n++)
                {
                    if(distance(_ShadowSphereArray[n].xyz, i.worldPos.xyz) < _ShadowSphereArray[n].w)
                    {
                        blendFactor =  GetBlendFactor(i.worldPos.xyz, n);
                        #if CASCADE_DITHER
                            casacdeID = blendFactor > noise? n + 1 : n;
                        #else
                            casacdeID = n;
                        #endif
                        break;
                    }
                }
                
                float3 posSTS = mul(_ShadowMatrixArray[casacdeID], i.worldPos).xyz;

                float weights[9];
                float2 positions[9];
                float4 size = _ShadowMapTextureSize;
                SampleShadow_ComputeSamples_Tent_5x5(size, posSTS.xy, weights, positions);

                float attantion = 0;
                for (int m = 0; m < 9; m++) {
                    attantion += weights[m] * SAMPLE_TEXTURE2D_SHADOW(_ShadowMapTexture, sampler_linear_clamp_compare, float3(positions[m].xy, posSTS.z));
                }

                #if CASCADE_BLEND
                    if(blendFactor > 0)
                    {
                        posSTS = mul(_ShadowMatrixArray[casacdeID+1], i.worldPos).xyz;
                        SampleShadow_ComputeSamples_Tent_5x5(size, posSTS.xy, weights, positions);
                        float nextAttantion = 0;
                        for (int n = 0; n < 9; n++) {
                            nextAttantion += weights[n] * SAMPLE_TEXTURE2D_SHADOW(_ShadowMapTexture, sampler_linear_clamp_compare, float3(positions[n].xy, posSTS.z));
                        }

                        attantion = lerp(attantion, nextAttantion, blendFactor);
                    }
                #endif

                if(posSTS.z >= 0.999 || posSTS.z <=0.001) attantion = 1;

                if(casacdeID == 3)
                {
                    float fadeAtten = saturate( (distance(_ShadowSphereArray[3].xyz, i.worldPos.xyz) - _ShadowSphereArray[3].w) / _ShadowBorderFadeLength + 1 );

                    attantion = max(attantion, fadeAtten);
                }
                
                return attantion * diffuse;
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }

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
            
            float _ShadowDepthBias;
            float3 _MainLightDirection;

            v2f vert (appdata v)
            {
                v2f o;
                float4 worldPos = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0));
                worldPos.xyz -= _MainLightDirection * _ShadowDepthBias;

                o.vertex = mul(unity_MatrixVP, worldPos);
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
