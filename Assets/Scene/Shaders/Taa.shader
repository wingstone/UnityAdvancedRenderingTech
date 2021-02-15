Shader "Hidden/AdvancedRTR/TAA"
{
    Properties
    {
        // _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct AttributesDefault
            {
                float3 vertex : POSITION;
            };
            struct VaryingsDefault
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            // Vertex manipulation
            float2 TransformTriangleVertexToUV(float2 vertex)
            {
                float2 uv = (vertex + 1.0) * 0.5;
                return uv;
            }
            
            VaryingsDefault vert(AttributesDefault v)
            {
                VaryingsDefault o;
                o.vertex = float4(v.vertex.xy, 0.0, 1.0);
                o.texcoord = TransformTriangleVertexToUV(v.vertex.xy);

                #if UNITY_UV_STARTS_AT_TOP
                    o.texcoord = o.texcoord * float2(1.0, -1.0) + float2(0.0, 1.0);
                #endif

                return o;
            }

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            sampler2D _HistoryTex;

            sampler2D _CameraDepthTexture;
            float4 _CameraDepthTexture_TexelSize;

            sampler2D _CameraMotionVectorsTexture;

            float2 _Jitter;
            float4 _FinalBlendParameters; // x: static, y: dynamic, z: motion amplification
            float _Sharpness;

            // from 5 pixel select near depth pixel 
            // common is 3x3 pixel
            float2 GetClosestFragment(float2 uv)
            {
                const float2 k = _CameraDepthTexture_TexelSize.xy;

                const float4 neighborhood = float4(
                tex2D(_CameraDepthTexture, (uv - k)).r,
                tex2D(_CameraDepthTexture, (uv + float2(k.x, -k.y))).r,
                tex2D(_CameraDepthTexture, (uv + float2(-k.x, k.y))).r,
                tex2D(_CameraDepthTexture, (uv + k)).r
                );

                #if defined(UNITY_REVERSED_Z)
                    #define COMPARE_DEPTH(a, b) step(b, a)
                #else
                    #define COMPARE_DEPTH(a, b) step(a, b)
                #endif

                float3 result = float3(0.0, 0.0, tex2D(_CameraDepthTexture, uv).r);
                result = lerp(result, float3(-1.0, -1.0, neighborhood.x), COMPARE_DEPTH(neighborhood.x, result.z));
                result = lerp(result, float3( 1.0, -1.0, neighborhood.y), COMPARE_DEPTH(neighborhood.y, result.z));
                result = lerp(result, float3(-1.0,  1.0, neighborhood.z), COMPARE_DEPTH(neighborhood.z, result.z));
                result = lerp(result, float3( 1.0,  1.0, neighborhood.w), COMPARE_DEPTH(neighborhood.w, result.z));

                return (uv + result.xy * k);
            }

            float Min3(float a, float b, float c)
            {
                return min(min(a, b), c);
            }

            #define HALF_MAX_MINUS1 65472.0 // (2 - 2^-9) * 2^15

            float4 ClipToAABB(float4 color, float3 minimum, float3 maximum)
            {
                // Note: only clips towards aabb center (but fast!)
                float3 center = 0.5 * (maximum + minimum);
                float3 extents = 0.5 * (maximum - minimum);

                // This is actually `distance`, however the keyword is reserved
                float3 offset = color.rgb - center;

                float3 ts = abs(extents / (offset + 0.0001));
                float t = saturate(Min3(ts.x, ts.y, ts.z));
                color.rgb = center + offset * t;
                return color;
            }

            struct OutputSolver
            {
                float4 destination : SV_Target0;
                float4 history     : SV_Target1;
            };

            OutputSolver Solve(float2 motion, float2 texcoord)
            {
                const float2 k = _MainTex_TexelSize.xy;
                float2 uv = (texcoord - _Jitter);

                float4 color = tex2D(_MainTex, uv);

                float4 topLeft = tex2D(_MainTex, (uv - k * 0.5));
                float4 bottomRight = tex2D(_MainTex, (uv + k * 0.5));

                float4 corners = 4.0 * (topLeft + bottomRight) - 2.0 * color;

                // Sharpen output
                color += (color - (corners * 0.166667)) * 2.718282 * _Sharpness;
                color = clamp(color, 0.0, HALF_MAX_MINUS1);

                // Tonemap color and history samples
                float4 average = (corners + color) * 0.142857;

                float4 history = tex2D(_HistoryTex, (texcoord - motion));

                float motionLength = length(motion);
                float2 luma = float2(Luminance(average), Luminance(color));
                //float nudge = 4.0 * abs(luma.x - luma.y);
                float nudge = lerp(4.0, 0.25, saturate(motionLength * 100.0)) * abs(luma.x - luma.y);

                float4 minimum = min(bottomRight, topLeft) - nudge;
                float4 maximum = max(topLeft, bottomRight) + nudge;

                // Clip history samples
                history = ClipToAABB(history, minimum.xyz, maximum.xyz);

                // Blend method
                float weight = clamp(
                lerp(_FinalBlendParameters.x, _FinalBlendParameters.y, motionLength * _FinalBlendParameters.z),
                _FinalBlendParameters.y, _FinalBlendParameters.x
                );

                color = lerp(color, history, weight);
                color = clamp(color, 0.0, HALF_MAX_MINUS1);

                OutputSolver output;
                output.destination = color;
                output.history = color;
                return output;
            }

            OutputSolver frag(VaryingsDefault i)
            {
                float2 closest = GetClosestFragment(i.texcoord);
                float2 motion = tex2D(_CameraMotionVectorsTexture, closest).xy;
                return Solve(motion, i.texcoord);
            }

            ENDCG
        }
    }
}
