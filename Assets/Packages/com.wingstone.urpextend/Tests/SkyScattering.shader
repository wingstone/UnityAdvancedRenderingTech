// Upgrade NOTE: commented out 'float3 _WorldSpaceCameraPos', a built-in variable

Shader "ARP/SkyScattering"
{
    Properties
    {        
        // _TransmittanceLut ("TransmittanceLut", 2D) = "white" {}
        _TransmittanceLut_Size("TransmittanceLut Size", Vector) = (1, 1, 265, 64)

        // _MultiSactteringLut ("MultiScatteringLut", 2D) = "white"{}
        _MultiSactteringLut_Size("MultiScatteringLut_Size", Vector) = (1, 1, 32, 32)
        
        // _SkyViewLut ("SkyViewLut", 2D) = "white"{}
        _SkyViewLutSize ("SkyViewLut size", Vector) = (1, 1, 256, 128)

        _SolarIrradiance("Solar Irradiance", Range(0,50)) = 1
        _GroundColor("Ground Color", Color) = (0.5,0.5,0.5,1.0)
        _LightDirectionPhi("Light Direction Phi", Range(-90, 90)) = 60
        _LightDirectionTheta("Light Direction Theta", Range(0, 360)) = 180
        _CameraHeight("Camera Height", Range(0, 100)) = 0
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always
        
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // #include "UnityCG.cginc"
            // #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            sampler2D _CameraDepthTexture;
            float4x4 UNITY_MATRIX_I_VP;

            float SampleSceneDepth(float2 uv)
            {
                return tex2D(_CameraDepthTexture, uv).r;
            }
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
            
            sampler3D _VolumeScattering;

            sampler2D _SkyViewLut;
            float4 _SkyViewLutSize;
            // float3 _WorldSpaceCameraPos;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 col = 0;

                float2 UV = i.uv;
                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(UV);
                #else
                    // Adjust Z to match NDC for OpenGL ([-1, 1])
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(UV));
                #endif

                // Reconstruct the world space positions.
                float3 worldPos = ComputeWorldSpacePosition(UV, depth, UNITY_MATRIX_I_VP);

                if(depth < 1e-5) return 0;

                float3 uvw = 0;
                uvw.xy = i.uv;
                uvw.z = distance(worldPos, _WorldSpaceCameraPos)/32;

                col = tex3D(_VolumeScattering, uvw);
col.a = 0;
                return col;
            }
            ENDCG
        }
    }
}
