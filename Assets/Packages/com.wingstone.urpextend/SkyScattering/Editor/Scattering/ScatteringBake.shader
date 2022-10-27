Shader "ARP/ScatteringBake"
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
        _LightDirectionTheta("Light Direction Theta", Range(0, 360)) = 0
        _CameraHeight("Camera Height", Range(0, 100)) = 0


    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        // 0 transmittance lut
        Pass   
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 _TransmittanceLut_Size;
            float4 _SkyViewLutSize;

            #define TRANSMITTANCE_TEXTURE_WIDTH _TransmittanceLut_Size.z
            #define TRANSMITTANCE_TEXTURE_HEIGHT _TransmittanceLut_Size.w
            #define SKYVIEW_TEXTURE_WIDTH _SkyViewLutSize.z
            #define SKYVIEW_TEXTURE_HEIGHT _SkyViewLutSize.w

            #include "UnityCG.cginc"
            #include "function.cginc"

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

                AtmosphereParameters atmosphere = (AtmosphereParameters)0;

                //---atmosphere setting

                // 所有距离以km为单位进行推导
                atmosphere.solar_irradiance = 1;
                atmosphere.sun_angular_radius = 1;
                
                atmosphere.bottom_radius = 6360;
                atmosphere.bottom_radius2 = 6360 * 6360;
                atmosphere.top_radius = 6460;
                atmosphere.top_radius2 = 6460 * 6460;
                atmosphere.rayleigh_scattering = float3(5.8e-3f, 13.5e-3f, 33.1e-3f);      //Rayleigh 海平面散射系数
                atmosphere.mie_scattering = 3.996e-3f;          // Mie 海平面散射系数
                atmosphere.mie_extinction = 4.4e-3f;            // Mie 海平面消散系数
                atmosphere.mie_phase_function_g = 0.8;           // Mie phase suntion g
                atmosphere.absorption_extinction = float3(0.65e-3f, 1.881e-3f, 0.085e-3f);
                atmosphere.scaleHeightR = 8;
                atmosphere.scaleHeightM = 1.2;

                //---atmosphere setting end

                col.rgb = ComputeTransmittanceToTopAtmosphereBoundaryTexture(atmosphere, i.uv);

                return col;
            }
            ENDCG
        }

        // 1 sky view lut
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 _TransmittanceLut_Size;
            float4 _SkyViewLutSize;

            #define TRANSMITTANCE_TEXTURE_WIDTH _TransmittanceLut_Size.z
            #define TRANSMITTANCE_TEXTURE_HEIGHT _TransmittanceLut_Size.w
            #define SKYVIEW_TEXTURE_WIDTH _SkyViewLutSize.z
            #define SKYVIEW_TEXTURE_HEIGHT _SkyViewLutSize.w

            #include "UnityCG.cginc"
            #include "function.cginc"
            #include "UnityLightingCommon.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            static const int NumSample = 256;
            sampler2D _TransmittanceLut;
            float _LightDirectionPhi;
            float _LightDirectionTheta;
            float4 _GroundColor;
            float _CameraHeight;
            float _SolarIrradiance;

            sampler2D _MultiSactteringLut;
            float4 _MultiSactteringLut_Size;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;

                return o;
            }

            float4 frag (v2f i) : SV_Target
            {                
                AtmosphereParameters atmosphere = (AtmosphereParameters)0;

                //---atmosphere setting

                // 所有距离以km为单位进行推导
                atmosphere.solar_irradiance = _LightColor0.rgb*3.1415926;
                atmosphere.sun_angular_radius = 1;
                
                atmosphere.bottom_radius = 6360;
                atmosphere.bottom_radius2 = 6360 * 6360;
                atmosphere.top_radius = 6460;
                atmosphere.top_radius2 = 6460 * 6460;
                atmosphere.rayleigh_scattering = float3(5.8e-3f, 13.5e-3f, 33.1e-3f);      //Rayleigh 海平面散射系数
                atmosphere.mie_scattering = 3.996e-3f;          // Mie 海平面散射系数
                atmosphere.mie_extinction = 4.4e-3f;            // Mie 海平面消散系数
                atmosphere.mie_phase_function_g = 0.8;          // Mie phase suntion g
                atmosphere.absorption_extinction = float3(0.65e-3f, 1.881e-3f, 0.085e-3f);  // Ozone 海平面消散系数
                atmosphere.scaleHeightR = 8;
                atmosphere.scaleHeightM = 1.2;

                //---atmosphere setting end

                // sun direction
                // float phi = _LightDirectionPhi/180*UNITY_PI;
                // float theta = _LightDirectionTheta/180*UNITY_PI;
                // float3 sunDirection = float3(-cos(phi), sin(phi), 0);
                
                float cos_phi = -sqrt(1 - _WorldSpaceLightPos0.y*_WorldSpaceLightPos0.y);
                float3 sunDirection = float3(cos_phi, _WorldSpaceLightPos0.y, 0);   // 太阳在-x屏幕上旋转
                
                float cameraHeight = _WorldSpaceCameraPos.y * 1e-3;
                // float cameraHeight = length(camerapos);
                float3 col = RenderSkyViewLut(atmosphere, _TransmittanceLut, cameraHeight, i.uv, sunDirection, _GroundColor, _MultiSactteringLut, _MultiSactteringLut_Size);

                return float4(col, 1);
            }
            ENDCG
        }

    }
}
