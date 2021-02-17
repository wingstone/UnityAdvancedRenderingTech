Shader "Hidden/AdvancedRTR/PlusTaa"
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

            #define _CLOSEST_FRAG_HEIGH 1
            #define _RESOLVE_HEIGH 1
            #define USE_YCOCG 1

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

            uniform float2 _Jitter;
            uniform float _FeedbackMin;
            uniform float _FeedbackMax;
            
            #if defined(UNITY_REVERSED_Z)
                #define COMPARE_DEPTH(a, b) step(b, a)
            #else
                #define COMPARE_DEPTH(a, b) step(a, b)
            #endif

            float3 find_closest_fragment_3x3(float2 uv)
            {
                float2 dd = abs(_CameraDepthTexture_TexelSize.xy);
                float2 du = float2(dd.x, 0.0);
                float2 dv = float2(0.0, dd.y);

                float3 dtl = float3(-1, -1, tex2D(_CameraDepthTexture, uv - dv - du).x);
                float3 dtc = float3( 0, -1, tex2D(_CameraDepthTexture, uv - dv).x);
                float3 dtr = float3( 1, -1, tex2D(_CameraDepthTexture, uv - dv + du).x);

                float3 dml = float3(-1, 0, tex2D(_CameraDepthTexture, uv - du).x);
                float3 dmc = float3( 0, 0, tex2D(_CameraDepthTexture, uv).x);
                float3 dmr = float3( 1, 0, tex2D(_CameraDepthTexture, uv + du).x);

                float3 dbl = float3(-1, 1, tex2D(_CameraDepthTexture, uv + dv - du).x);
                float3 dbc = float3( 0, 1, tex2D(_CameraDepthTexture, uv + dv).x);
                float3 dbr = float3( 1, 1, tex2D(_CameraDepthTexture, uv + dv + du).x);

                float3 dmin = dtl;
                if (COMPARE_DEPTH(dmin.z, dtc.z)) dmin = dtc;
                if (COMPARE_DEPTH(dmin.z, dtr.z)) dmin = dtr;

                if (COMPARE_DEPTH(dmin.z, dml.z)) dmin = dml;
                if (COMPARE_DEPTH(dmin.z, dmc.z)) dmin = dmc;
                if (COMPARE_DEPTH(dmin.z, dmr.z)) dmin = dmr;

                if (COMPARE_DEPTH(dmin.z, dbl.z)) dmin = dbl;
                if (COMPARE_DEPTH(dmin.z, dbc.z)) dmin = dbc;
                if (COMPARE_DEPTH(dmin.z, dbr.z)) dmin = dbr;

                // return float2(uv + dd.xy * dmin.xy);
                return float3(uv + dd.xy * dmin.xy, LinearEyeDepth(dmin.z));
            }

            float3 find_closest_fragment_5tap(float2 uv)
            {
                float2 dd = abs(_CameraDepthTexture_TexelSize.xy);
                float2 du = float2(dd.x, 0.0);
                float2 dv = float2(0.0, dd.y);

                float2 tl = -dv - du;
                float2 tr = -dv + du;
                float2 bl =  dv - du;
                float2 br =  dv + du;

                float dtl = tex2D(_CameraDepthTexture, uv + tl).x;
                float dtr = tex2D(_CameraDepthTexture, uv + tr).x;
                float dmc = tex2D(_CameraDepthTexture, uv).x;
                float dbl = tex2D(_CameraDepthTexture, uv + bl).x;
                float dbr = tex2D(_CameraDepthTexture, uv + br).x;

                float dmin = dmc;
                float2 dif = 0.0;

                if (COMPARE_DEPTH(dmin, dtl)) { dmin = dtl; dif = tl; }
                if (COMPARE_DEPTH(dmin, dtr)) { dmin = dtr; dif = tr; }
                if (COMPARE_DEPTH(dmin, dbl)) { dmin = dbl; dif = bl; }
                if (COMPARE_DEPTH(dmin, dbr)) { dmin = dbr; dif = br; }

                // return float2(uv + dif);
                return float3(uv + dif, LinearEyeDepth(dmin));
            }


            float3 GetClosestFragment(float2 uv)
            {
                #if _CLOSEST_FRAG_HEIGH
                    return find_closest_fragment_3x3(uv);
                #else
                    return find_closest_fragment_5tap(uv);
                #endif
            }

            // todo add tonemap
            float3 ToneMap(float3 color)
            {
                return color / (1 + Luminance(color));
            }

            float3 UnToneMap(float3 color)
            {
                return color / (1 - Luminance(color));
            }

            // https://software.intel.com/en-us/node/503873
            float3 RGB_YCoCg(float3 c)
            {
                // Y = R/4 + G/2 + B/4
                // Co = R/2 - B/2
                // Cg = -R/4 + G/2 - B/4
                return float3(
                c.x/4.0 + c.y/2.0 + c.z/4.0,
                c.x/2.0 - c.z/2.0,
                -c.x/4.0 + c.y/2.0 - c.z/4.0
                );
            }

            // https://software.intel.com/en-us/node/503873
            float3 YCoCg_RGB(float3 c)
            {
                // R = Y + Co - Cg
                // G = Y + Cg
                // B = Y - Co - Cg
                return saturate(float3(
                c.x + c.y - c.z,
                c.x + c.z,
                c.x - c.y - c.z
                ));
            }

            float4 sample_color(sampler2D tex, float2 uv)
            {
                #if USE_YCOCG
                    float4 c = tex2D(tex, uv);
                    return float4(RGB_YCoCg(c.rgb), c.a);
                #else
                    return tex2D(tex, uv);
                #endif
            }

            float4 ClipToAABB(float4 color, float3 minimum, float3 maximum, float4 cen)
            {
                // Note: only clips towards aabb center (but fast!)
                float3 center = 0.5 * (maximum + minimum);
                float3 extents = 0.5 * (maximum - minimum);

                // This is actually `distance`, however the keyword is reserved
                float3 offset = color.rgb - center;

                float3 ts = abs(extents / (offset + 0.0001));
                float t = saturate(min(ts.x, min(ts.y, ts.z)));
                color.rgb = center + offset * t;
                return color;

                // true
                /*
                float4 r = color - cent;
                float3 rmax = maximum - cent.xyz;
                float3 rmin = minimum - cent.xyz;

                const float eps = FLT_EPS;

                if (r.x > rmax.x + eps)
                r *= (rmax.x / r.x);
                if (r.y > rmax.y + eps)
                r *= (rmax.y / r.y);
                if (r.z > rmax.z + eps)
                r *= (rmax.z / r.z);

                if (r.x < rmin.x - eps)
                r *= (rmin.x / r.x);
                if (r.y < rmin.y - eps)
                r *= (rmin.y / r.y);
                if (r.z < rmin.z - eps)
                r *= (rmin.z / r.z);

                return cent + r;
                */
            }

            float4 VariancesClip(float4 color, float4 history, float2 uv)
            {
                const float VARIANCE_CLIPPING_GAMMA = 1.0;
                const float2 k = _MainTex_TexelSize.xy;

                float4 NearColor0 = sample_color(_MainTex, uv + k*float2(1, 0));
                float4 NearColor1 = sample_color(_MainTex, uv + k*float2(0, 1));
                float4 NearColor2 = sample_color(_MainTex, uv + k*float2(-1, 0));
                float4 NearColor3 = sample_color(_MainTex, uv + k*float2(0, -1));

                // Compute the two moments
                float4 M1 = color + NearColor0 + NearColor1 + NearColor2 + NearColor3;
                float4 M2 = color * color + NearColor0 * NearColor0 + NearColor1 * NearColor1 
                + NearColor2 * NearColor2 + NearColor3 * NearColor3;

                float4 MU = M1 / 5.0;
                float4 Sigma = sqrt(M2 / 5.0 - MU * MU);

                float4 BoxMin = MU - VARIANCE_CLIPPING_GAMMA * Sigma;
                float4 BoxMax = MU + VARIANCE_CLIPPING_GAMMA * Sigma;

                history = clamp(history, BoxMin, BoxMax);
                return history;
            }
            
            float PDnrand( float2 n ) {
                return frac( sin(dot(n.xy, float2(12.9898f, 78.233f)))* 43758.5453f );
            }
            float2 PDnrand2( float2 n ) {
                return frac( sin(dot(n.xy, float2(12.9898f, 78.233f)))* float2(43758.5453f, 28001.8384f) );
            }
            float3 PDnrand3( float2 n ) {
                return frac( sin(dot(n.xy, float2(12.9898f, 78.233f)))* float3(43758.5453f, 28001.8384f, 50849.4141f ) );
            }
            float4 PDnrand4( float2 n ) {
                return frac( sin(dot(n.xy, float2(12.9898f, 78.233f)))* float4(43758.5453f, 28001.8384f, 50849.4141f, 12996.89f) );
            }

            float4 sample_color_motion(sampler2D tex, float2 uv, float2 ss_vel)
            {
                const float2 v = 0.5 * ss_vel;
                const int taps = 3;// on either side!

                float srand = PDnrand(uv + _SinTime.xx);
                float2 vtap = v / taps;
                float2 pos0 = uv + vtap * (0.5 * srand);
                float4 accu = 0.0;
                float wsum = 0.0;

                [unroll]
                for (int i = -taps; i <= taps; i++)
                {
                    float w = 1.0;// box
                    //float w = taps - abs(i) + 1;// triangle
                    //float w = 1.0 / (1 + abs(i));// pointy triangle
                    accu += w * sample_color(tex, pos0 + i * vtap);
                    wsum += w;
                }

                return accu / wsum;
            }

            struct OutputSolver
            {
                float4 destination : SV_Target0;
                float4 history     : SV_Target1;
            };

            OutputSolver Solve(float2 motion, float2 texcoord, float vs_dist)
            {
                const float2 k = _MainTex_TexelSize.xy;

                float4 color = sample_color(_MainTex, texcoord- _Jitter);     // unjitter main color
                float4 history = sample_color(_HistoryTex, (texcoord - motion));

                // sample neighbour
                float2 uv = texcoord;
                #if _RESOLVE_HEIGH
                    // MINMAX_3X3_ROUNDED
                    float2 du = float2(_MainTex_TexelSize.x, 0.0);
                    float2 dv = float2(0.0, _MainTex_TexelSize.y);

                    float4 ctl = sample_color(_MainTex, uv - dv - du);
                    float4 ctc = sample_color(_MainTex, uv - dv);
                    float4 ctr = sample_color(_MainTex, uv - dv + du);
                    float4 cml = sample_color(_MainTex, uv - du);
                    float4 cmc = sample_color(_MainTex, uv);
                    float4 cmr = sample_color(_MainTex, uv + du);
                    float4 cbl = sample_color(_MainTex, uv + dv - du);
                    float4 cbc = sample_color(_MainTex, uv + dv);
                    float4 cbr = sample_color(_MainTex, uv + dv + du);

                    float4 cmin = min(ctl, min(ctc, min(ctr, min(cml, min(cmc, min(cmr, min(cbl, min(cbc, cbr))))))));
                    float4 cmax = max(ctl, max(ctc, max(ctr, max(cml, max(cmc, max(cmr, max(cbl, max(cbc, cbr))))))));

                    float4 cavg = (ctl + ctc + ctr + cml + cmc + cmr + cbl + cbc + cbr) / 9.0;

                    float4 cmin5 = min(ctc, min(cml, min(cmc, min(cmr, cbc))));
                    float4 cmax5 = max(ctc, max(cml, max(cmc, max(cmr, cbc))));
                    float4 cavg5 = (ctc + cml + cmc + cmr + cbc) / 5.0;
                    cmin = 0.5 * (cmin + cmin5);
                    cmax = 0.5 * (cmax + cmax5);
                    cavg = 0.5 * (cavg + cavg5);
                #else
                    // MINMAX_4TAP_VARYING
                    const float _SubpixelThreshold = 0.5;
                    const float _GatherBase = 0.5;
                    const float _GatherSubpixelMotion = 0.1666;

                    float2 texel_vel = motion / _MainTex_TexelSize.xy;
                    float texel_vel_mag = length(texel_vel) * vs_dist;
                    float k_subpixel_motion = saturate(_SubpixelThreshold / (FLT_EPS + texel_vel_mag));
                    float k_min_max_support = _GatherBase + _GatherSubpixelMotion * k_subpixel_motion;

                    float2 ss_offset01 = k_min_max_support * float2(-_MainTex_TexelSize.x, _MainTex_TexelSize.y);
                    float2 ss_offset11 = k_min_max_support * float2(_MainTex_TexelSize.x, _MainTex_TexelSize.y);
                    float4 c00 = sample_color(_MainTex, uv - ss_offset11);
                    float4 c10 = sample_color(_MainTex, uv - ss_offset01);
                    float4 c01 = sample_color(_MainTex, uv + ss_offset01);
                    float4 c11 = sample_color(_MainTex, uv + ss_offset11);

                    float4 cmin = min(c00, min(c10, min(c01, c11)));
                    float4 cmax = max(c00, max(c10, max(c01, c11)));

                #endif

                // shrink chroma min-max
                #if USE_YCOCG
                    float2 chroma_extent = 0.25 * 0.5 * (cmax.r - cmin.r);
                    float2 chroma_center = color.gb;
                    cmin.yz = chroma_center - chroma_extent;
                    cmax.yz = chroma_center + chroma_extent;
                    cavg.yz = chroma_center;
                #endif

                // Clip history samples
                // AABB clip
                // history = ClipToAABB(history, cmin.xyz, cmax.xyz, clamp(cavg, cmin, cmax));

                // vairance clip
                history = VariancesClip(color, history, texcoord);

                // feedback weight from unbiased luminance diff (t.lottes)
                // https://community.arm.com/developer/tools-software/graphics/b/blog/posts/temporal-anti-aliasing
                #if USE_YCOCG
                    float lum0 = color.r;
                    float lum1 = history.r;
                #else
                    float lum0 = Luminance(texel0.rgb);
                    float lum1 = Luminance(texel1.rgb);
                #endif
                float unbiased_diff = abs(lum0 - lum1) / max(lum0, max(lum1, 0.2));
                float unbiased_weight = 1.0 - unbiased_diff;
                float unbiased_weight_sqr = unbiased_weight * unbiased_weight;
                float weight = lerp(_FeedbackMin, _FeedbackMax, unbiased_weight_sqr);

                color = lerp(color, history, weight);
                history = color;

                // motion blending
                float vel_mag = length(motion * _MainTex_TexelSize.zw);
                const float vel_trust_full = 2.0;
                const float vel_trust_none = 15.0;
                const float vel_trust_span = vel_trust_none - vel_trust_full;
                float trust = 1.0 - clamp(vel_mag - vel_trust_full, 0.0, vel_trust_span) / vel_trust_span;

                float4 color_motion = sample_color_motion(_MainTex, texcoord - _Jitter, motion);

                color = lerp(color_motion, color, trust);

                // 2rgb
                #if USE_YCOCG
                    color = float4(YCoCg_RGB(color.rgb).rgb, color.a);
                    history = float4(YCoCg_RGB(history.rgb).rgb, history.a);
                #else
                    color = color;
                    history = history;
                #endif

                // add noise
                float4 noise4 = (PDnrand4(uv + _Time.y)-0.5) / 255.0;
                color = saturate(color + noise4);
                history = saturate(history + noise4);

                OutputSolver output;
                output.destination = color;
                output.history = history;
                return output;
            }

            OutputSolver frag(VaryingsDefault i)
            {
                float3 closest = GetClosestFragment(i.texcoord);
                float2 motion = tex2D(_CameraMotionVectorsTexture, closest.xy).xy;
                return Solve(motion, i.texcoord, closest.z);
            }

            ENDCG
        }
    }
}
