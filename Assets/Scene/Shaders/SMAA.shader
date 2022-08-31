Shader "Hidden/AdvancedRTR/SMAA"    //in fact, this is mlaa
{
    Properties
    {
        _AreaTex("Area Tex", 2D) = "black"{}
        _SearchTex("Search Tex", 2D) = "black"{}
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE

        /*
        *  6. Before including SMAA.h you'll have to setup the render target metrics,
        *     the target and any optional configuration defines. Optionally you can
        *     use a preset.
        *
        *     You have the following targets available: 
        *         SMAA_HLSL_3
        *         SMAA_HLSL_4
        *         SMAA_HLSL_4_1
        *         SMAA_GLSL_3 *
        *         SMAA_GLSL_4 *
        *
        *         * (See SMAA_INCLUDE_VS and SMAA_INCLUDE_PS below).
        *
        *     And four presets:
        *         SMAA_PRESET_LOW          (%60 of the quality)
        *         SMAA_PRESET_MEDIUM       (%80 of the quality)
        *         SMAA_PRESET_HIGH         (%95 of the quality)
        *         SMAA_PRESET_ULTRA        (%99 of the quality)
        *
        *     For example:
        *         #define SMAA_RT_METRICS float4(1.0 / 1280.0, 1.0 / 720.0, 1280.0, 720.0)
        *         #define SMAA_HLSL_4
        *         #define SMAA_PRESET_HIGH
        *         #include "SMAA.h"
        *
        *     Note that SMAA_RT_METRICS doesn't need to be a macro, it can be a
        *     uniform variable. The code is designed to minimize the impact of not
        *     using a constant value, but it is still better to hardcode it.
        *
        *     Depending on how you encoded 'areaTex' and 'searchTex', you may have to
        *     add (and customize) the following defines before including SMAA.h:
        *          #define SMAA_AREATEX_SELECT(sample) sample.rg
        *          #define SMAA_SEARCHTEX_SELECT(sample) sample.r
        *
        *     If your engine is already using porting macros, you can define
        *     SMAA_CUSTOM_SL, and define the porting functions by yourself.
        */

        #define SMAA_AREATEX_SELECT(s) s.rg
        #define SMAA_SEARCHTEX_SELECT(s) s.a
        #define SMAA_HLSL_3
        #define SMAA_PRESET_LOW
        #define SMAA_RT_METRICS _MainTex_TexelSize
        ENDCG

        Pass
        {
            Name "Edge Detection"
            Stencil {
                Ref 1
                Comp always
                Pass replace
            }

            CGPROGRAM

            #pragma vertex vertEdge
            #pragma fragment fragEdge
            
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            #include "SMAA.hlsl"
            #include "Common.cginc"

            struct VaryingsEdge
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                float4 offset[3] : TEXCOORD1;
            };

            VaryingsEdge vertEdge(AttributesDefault v)
            {
                VaryingsEdge o;
                o.vertex = float4(v.vertex.xy, 0.0, 1.0);
                o.texcoord = TransformTriangleVertexToUV(v.vertex.xy);

                #if UNITY_UV_STARTS_AT_TOP
                    o.texcoord = o.texcoord * float2(1.0, -1.0) + float2(0.0, 1.0);
                #endif

                SMAAEdgeDetectionVS(o.texcoord, o.offset);
                return o;
            }


            half4 fragEdge(VaryingsEdge i) : SV_Target0
            {
                half4 color = 0;
                color.rg = SMAALumaEdgeDetectionPS(i.texcoord, i.offset, _MainTex);
                return color;
            }
            
            ENDCG
        }

        Pass
        {
            Name "Blend Weight"
            Stencil {
                Ref 1
                Comp Equal
            }
            
            CGPROGRAM

            #pragma vertex vertBlend
            #pragma fragment fragBlend
             
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _AreaTex;
            sampler2D _SearchTex;
            int _JitterIndex;
            int _UseT2X;

            #include "SMAA.hlsl"
            #include "Common.cginc"

            struct VaryingsBlendWeight
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                float2 pixcoord : TEXCOORD1;
                float4 offset[3] : TEXCOORD2;
            };

            VaryingsBlendWeight vertBlend(AttributesDefault v)
            {
                VaryingsBlendWeight o;
                o.vertex = float4(v.vertex.xy, 0.0, 1.0);
                o.texcoord = TransformTriangleVertexToUV(v.vertex.xy);

                #if UNITY_UV_STARTS_AT_TOP
                    o.texcoord = o.texcoord * float2(1.0, -1.0) + float2(0.0, 1.0);
                #endif

                SMAABlendingWeightCalculationVS(o.texcoord, o.pixcoord, o.offset);
                return o;
            }

            half4 fragBlend(VaryingsBlendWeight i) : SV_Target0
            {
                half4 color = 0;
                float4 subsampleIndices = _JitterIndex == 0 ? float4(1, 1, 1, 0) : float4(2, 2, 2, 0);
                subsampleIndices = _UseT2X == 0 ? 0 : subsampleIndices;
                color = SMAABlendingWeightCalculationPS(i.texcoord, i.pixcoord, i.offset, _MainTex, _AreaTex, _SearchTex, subsampleIndices);
                return color;
            }
            
            ENDCG
        }

        Pass
        {
            Name "Blend"
            
            CGPROGRAM

            #pragma multi_compile _ SMAA_REPROJECTION
            #pragma vertex vertBlend
            #pragma fragment fragBlend
            
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _BlendWeightTex;
            sampler2D _CameraMotionVectorsTexture;

            #include "SMAA.hlsl"
            #include "Common.cginc"

            struct VaryingsBlend
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                float4 offset : TEXCOORD1;
            };

            VaryingsBlend vertBlend(AttributesDefault v)
            {
                VaryingsBlend o;
                o.vertex = float4(v.vertex.xy, 0.0, 1.0);
                o.texcoord = TransformTriangleVertexToUV(v.vertex.xy);

                #if UNITY_UV_STARTS_AT_TOP
                    o.texcoord = o.texcoord * float2(1.0, -1.0) + float2(0.0, 1.0);
                #endif

                SMAANeighborhoodBlendingVS(o.texcoord, o.offset);
                return o;
            }

            half4 fragBlend(VaryingsBlend i) : SV_Target0
            {
                half4 color = 0;
                color = SMAANeighborhoodBlendingPS(i.texcoord, i.offset, _MainTex, _BlendWeightTex
                #if SMAA_REPROJECTION
                    , _CameraMotionVectorsTexture
                #endif
                );
                return color;
            }
            
            ENDCG
        }

        Pass
        {
            Name "Resolve"
            
            CGPROGRAM

            #pragma multi_compile _ SMAA_REPROJECTION
            #pragma vertex vertResolve
            #pragma fragment fragResolve
            
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _PreviousTex;
            sampler2D _CameraMotionVectorsTexture;

            #include "SMAA.hlsl"
            #include "Common.cginc"

            struct VaryingsResolve
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                float4 offset : TEXCOORD1;
            };

            VaryingsResolve vertResolve(AttributesDefault v)
            {
                VaryingsResolve o;
                o.vertex = float4(v.vertex.xy, 0.0, 1.0);
                o.texcoord = TransformTriangleVertexToUV(v.vertex.xy);

                #if UNITY_UV_STARTS_AT_TOP
                    o.texcoord = o.texcoord * float2(1.0, -1.0) + float2(0.0, 1.0);
                #endif

                SMAANeighborhoodBlendingVS(o.texcoord, o.offset);
                return o;
            }
            
            half4 fragResolve(VaryingsResolve i) : SV_Target0
            {
                half4 color = 0;
                color = SMAAResolvePS(i.texcoord, _MainTex, _PreviousTex
                #if SMAA_REPROJECTION
                    , _CameraMotionVectorsTexture
                #endif    
                );
                return color;
            }
            
            ENDCG
        }

    }
}
