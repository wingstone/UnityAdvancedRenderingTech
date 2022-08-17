Shader "ARP/SkyBakeScattering"
{
    Properties
    {
        _SkyViewLut("Sky View Lut", 2D) = "white" {}
        _TransmittanceLut ("TransmittanceTex", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "PreviewType" = "Skybox" }
        Cull Off 
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            CBUFFER_START(UnityPerMaterial)
                float4 _TransmittanceLut_TexelSize;
                float4 _SkyViewLut_TexelSize;
            CBUFFER_END

            #define TRANSMITTANCE_TEXTURE_WIDTH _TransmittanceLut_TexelSize.z
            #define TRANSMITTANCE_TEXTURE_HEIGHT _TransmittanceLut_TexelSize.w
            #define SKYVIEW_TEXTURE_WIDTH _SkyViewLut_TexelSize.z
            #define SKYVIEW_TEXTURE_HEIGHT _SkyViewLut_TexelSize.w

            #include "UnityCG.cginc"
            #include "Packages/com.wingstone.urpextend/Editor/Scattering/function.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 viewDir : TEXCOORD0;
            };

            sampler2D _TransmittanceLut;
            sampler2D _SkyViewLut;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.viewDir = normalize(mul((float3x3)unity_ObjectToWorld, v.vertex.xyz));

                return o;
            }

            float4 frag (v2f i) : SV_Target
            {                
                AtmosphereParameters atmosphere = (AtmosphereParameters)0;

                //---atmosphere setting

                // 所有距离以km为单位进行推导
                atmosphere.solar_irradiance = 20;
                atmosphere.sun_angular_radius = 1;
                
                atmosphere.bottom_radius = 6360;
                atmosphere.bottom_radius2 = 6360 * 6360;
                atmosphere.top_radius = 6460;
                atmosphere.top_radius2 = 6460 * 6460;
                atmosphere.rayleigh_scattering = float3(5.8e-3f, 13.5e-3f, 33.1e-3f);      //Rayleigh 海平面散射系数
                atmosphere.mie_scattering = 21e-3f;          // Mie 海平面散射系数
                atmosphere.mie_extinction = 21e-3f*1.1;            // Mie 海平面消散系数
                atmosphere.mie_scattering = 3.996e-3f;          // Mie 海平面散射系数
                atmosphere.mie_extinction = 4.4e-3f;            // Mie 海平面消散系数
                atmosphere.mie_phase_function_g = 0.8;          // Mie phase suntion g
                atmosphere.absorption_extinction = float3(0.65e-3f, 1.881e-3f, 0.085e-3f);  // Ozone 海平面消散系数
                atmosphere.scaleHeightR = 8;
                atmosphere.scaleHeightM = 1.2;

                //---atmosphere setting end


                float3 col = 0;

                float3 camerapos = float3(0, atmosphere.bottom_radius, 0) + _WorldSpaceCameraPos*1e-3;
                float startR = length(camerapos);
                startR = max(startR, atmosphere.bottom_radius);
                
                float3 eyeRay = normalize(i.viewDir);
                float3 sunDirection = _WorldSpaceLightPos0.xyz;

                float rayLength = sqrt(atmosphere.top_radius2 - startR*startR * (1 - eyeRay.y * eyeRay.y)) - startR * eyeRay.y;

                bool rayHitEarth = eyeRay.y < 0 && startR*startR * (1-eyeRay.y * eyeRay.y) < atmosphere.bottom_radius2;

                float2 uv = 0;
                float lightViewCosAngle = eyeRay.x/sqrt(1-eyeRay.y*eyeRay.y);
                SkyViewLutParamsToUv(atmosphere, rayHitEarth, eyeRay.y, lightViewCosAngle,startR, uv);

                col = tex2D(_SkyViewLut, uv);

                float r = length(camerapos);
                float mu = dot(camerapos, eyeRay)/r;
                float d = rayLength;
                float3 transmittance = GetTransmittance(atmosphere, _TransmittanceLut, r, mu, d, rayHitEarth);

                // L0
                float sunSize = 0.999;
                if(dot(eyeRay, sunDirection) > sunSize && !rayHitEarth)
                {
                    col += transmittance * atmosphere.solar_irradiance;
                }

                return float4(col, 1);
            }
            ENDCG
        }
    }
}
