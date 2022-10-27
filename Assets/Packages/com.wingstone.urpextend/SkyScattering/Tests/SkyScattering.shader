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
            Blend One SrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 _TransmittanceLut_Size;
            float4 _SkyViewLutSize;

            #define TRANSMITTANCE_TEXTURE_WIDTH _TransmittanceLut_Size.z
            #define TRANSMITTANCE_TEXTURE_HEIGHT _TransmittanceLut_Size.w
            #define SKYVIEW_TEXTURE_WIDTH _SkyViewLutSize.z
            #define SKYVIEW_TEXTURE_HEIGHT _SkyViewLutSize.w

            #define UNITY_PI 3.1415926
            #define UNITY_TWO_PI 6.2831853071796
            #define UNITY_HALF_PI 1.5707963267949

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Assets/Packages/com.wingstone.urpextend/SkyScattering/Editor/Scattering/function.cginc"
            // #include "UnityCG.cginc"

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
            sampler2D _TransmittanceLut;
            sampler2D _SkyViewLut;
            float _LightDirectionPhi;
            float _LightDirectionTheta;
            float _SolarIrradiance;
            float4 _WorldSpaceLightPos0;
            float4 _LightColor0;

            // float4 _SkyViewLutSize;
            // float3 _WorldSpaceCameraPos;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 col = 0;

                AtmosphereParameters atmosphere = (AtmosphereParameters)0;

                //---atmosphere setting

                // 所有距离以km为单位进行推导
                atmosphere.solar_irradiance = _LightColor0.rgb*3.1415926;
                atmosphere.sun_angular_radius = 1;
                
                atmosphere.bottom_radius = 6360;
                atmosphere.bottom_radius2 = 6360 * 6360;
                atmosphere.top_radius = 6460;
                atmosphere.top_radius2 = 6460 * 6460;
                atmosphere.rayleigh_scattering = float3(5.8e-3f, 13.5e-3f, 33.1e-3f);        //Rayleigh 海平面散射系数
                atmosphere.mie_scattering = 21e-3f;                                     // Mie 海平面散射系数
                atmosphere.mie_extinction = 21e-3f*1.1;                                     // Mie 海平面散射系数
                atmosphere.scaleHeightR = 8;
                atmosphere.scaleHeightM = 1.2;

                //---atmosphere setting end

                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(i.uv);
                #else
                    // Adjust Z to match NDC for OpenGL ([-1, 1])
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(i.uv));
                #endif

                // Reconstruct the world space positions.
                float3 worldPos = ComputeWorldSpacePosition(i.uv, depth, UNITY_MATRIX_I_VP);

                // sky area
                #if UNITY_REVERSED_Z
                    if(depth < 1e-5)
                #else
                    if(depth > 1- 1e-5)
                #endif
                    {
                        float3 eyeRay = normalize(worldPos - _WorldSpaceCameraPos);
                        
                        // float phi = _LightDirectionPhi/180*UNITY_PI;
                        // float theta = _LightDirectionTheta/180*UNITY_PI;
                        
                        float3 sunDirection = _WorldSpaceLightPos0.xyz;

                                
                        float3 camerapos = _WorldSpaceCameraPos*1e-3 + float3(0, atmosphere.bottom_radius, 0);
                        float startR = length(camerapos);
                        startR = max(startR, atmosphere.bottom_radius);

                        float rayLength = sqrt(atmosphere.top_radius2 - startR*startR * (1 - eyeRay.y * eyeRay.y)) - startR * eyeRay.y;

                        bool rayHitEarth = eyeRay.y < 0 && startR*startR * (1-eyeRay.y * eyeRay.y) < atmosphere.bottom_radius2;

                        float r = startR;
                        float mu = dot(camerapos, eyeRay)/r;
                        float d = rayLength;
                        float3 transmittance = GetTransmittance(atmosphere, _TransmittanceLut, r, mu, d, rayHitEarth);

                        // L0
                        float3 L = 0;
                        float sunSize = 0.999;
                        if(dot(eyeRay, sunDirection) > sunSize && !rayHitEarth)
                        {
                            L += transmittance * atmosphere.solar_irradiance;
                        }

                        // L1 and so on

                        float2 uv = 0;
                        // phi = (i.uv.y*2-1)*UNITY_HALF_PI;
                        // theta = i.uv.x*UNITY_TWO_PI - (_LightDirectionTheta-180)/180*UNITY_PI;
                        // eyeRay = float3(cos(phi)*cos(theta), sin(phi), cos(phi)*sin(theta));
                        float cos_v = eyeRay.x/sqrt(1-eyeRay.y*eyeRay.y);
                        float sin_v = eyeRay.z/sqrt(1-eyeRay.y*eyeRay.y);
                        float cos_l = sunDirection.x/sqrt(1-sunDirection.y*sunDirection.y);
                        float sin_l = sunDirection.z/sqrt(1-sunDirection.y*sunDirection.y);
                        float cos_v_l = cos_v*cos_l + sin_v*sin_l;  // cos(a-b)公式
                        float lightViewCosAngle = -cos_v_l;
                        SkyViewLutParamsToUv(atmosphere, rayHitEarth, eyeRay.y, lightViewCosAngle,startR, uv);

                        L += tex2D(_SkyViewLut, uv);
                        return float4(L, 0);
                    }

                float3 uvw = 0;
                uvw.xy = i.uv;
                // uvw.z = distance(worldPos, _WorldSpaceCameraPos)/32*1e-3;
                uvw.z = DistanceToSlice(distance(worldPos, _WorldSpaceCameraPos)*1e-3);

                col = tex3D(_VolumeScattering, uvw);
                
                return col;
            }
            ENDHLSL
        }
    }
}
