Shader "Hidden/AdvancedRTR/Paper"
{
    Properties
    {
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        // https://zhuanlan.zhihu.com/p/350003528
        //Paper filter : http://www.heathershrewsbury.com/dreu2010/wp-content/uploads/2010/07/InteractiveWatercolorRenderingWithTemporalCoherenceAndAbstraction.pdf
        //kuwahara filter : https://zhuanlan.zhihu.com/p/74807856
        
        // KuwaharaFiler
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Common.cginc"

            float _BlurRadius;
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;


            float4 GetKernelMeanAndVariance(float2 uv, float4 Range)
            {
                float3 Mean = 0;
                float3 Variance = 0;
                float Samples = 0;

                for (int x = Range.x; x <= Range.y; x++)
                {
                    for (int y = Range.z; y <= Range.w; y++)
                    {
                        float2 offset = float2(x, y);
                        float3 color = tex2D(_MainTex, uv + offset*_MainTex_TexelSize.xy).rgb;
                        Mean += color;
                        Variance += color * color;
                        Samples++;
                    }
                }

                Mean /= Samples;
                Variance = Variance / Samples - Mean * Mean;
                float TotalVariance = Variance.r + Variance.g + Variance.b;
                return float4(Mean.r, Mean.g, Mean.b, TotalVariance);
            }

            float4 frag (VaryingsDefault i) : SV_Target
            {
                float4 mainColor = tex2D(_MainTex, i.uv);
                float4 meanVariance[4];

                meanVariance[0] = GetKernelMeanAndVariance(i.uv, float4(-_BlurRadius, 0, -_BlurRadius, 0));
                meanVariance[1] = GetKernelMeanAndVariance(i.uv, float4(0, _BlurRadius, -_BlurRadius, 0));
                meanVariance[2] = GetKernelMeanAndVariance(i.uv, float4(0, _BlurRadius, 0, _BlurRadius));
                meanVariance[3] = GetKernelMeanAndVariance(i.uv, float4(-_BlurRadius, 0, 0, _BlurRadius));

                float3 finalColor = meanVariance[0].rgb;
                float MinimumVariance = meanVariance[0].a;

                for (int i = 1; i < 4; i++)
                {
                    if (meanVariance[i].a < MinimumVariance)
                    {
                        finalColor = meanVariance[i].rgb;
                        MinimumVariance = meanVariance[i].a;
                    }
                }

                return float4(finalColor, 1);
            }
            ENDCG
        }

        // PaperFilter
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Common.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _BlurRadius;
            sampler2D _PaperNoise;
            float _NoiseTiling;

            float4 frag (VaryingsDefault i) : SV_Target
            {
                float4 mainColor = tex2D(_MainTex, i.uv);
                float noise = tex2D(_PaperNoise, i.uv*_NoiseTiling).r;

                noise = noise*2.0;
                
                float3 finalColor = mainColor.rgb;
                finalColor = finalColor - (finalColor - finalColor*finalColor)*(noise - 1);
                finalColor = max(finalColor, 0);

                return float4(finalColor, 1);
            }
            ENDCG
        }
    }
}
