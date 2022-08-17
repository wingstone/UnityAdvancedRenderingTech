Shader "ARP/SkySingleScattering"
{
    Properties
    {
        _GroundColor("Ground Color", Color) = (0.5,0.5,0.5,1.0)
    }
    SubShader
    {
        Tags {"Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off 
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 viewDir : TEXCOORD0;
                float3 skyColor : TEXCOORD1;
                float3 groundColor : TEXCOORD2;
            };

            // 所有距离以km为单位进行推导
            static const float EarthRadius = 6360;
            static const float EarthRadius2 = 6360*6360;
            static const float AtmosphereRadius = 6420;
            static const float AtmosphereRadius2 = 6420*6420;
            static const float ScaleHeightR = 8;
            static const float ScaleHeightM = 1.2;
            static const float3 ScatterR0 = float3(5.8e-3f, 13.5e-3f, 33.1e-3f);        //Rayleigh 海平面散射系数
            static const float3 ScatterM0 = 21e-3f;                                     // Mie 海平面散射系数
            static const int NumSample = 64;
            static const int NumSampleLight = 16;


            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.viewDir = normalize(mul(unity_ObjectToWorld, v.vertex).xyz - _WorldSpaceCameraPos);

                return o;
            }

            float4 _GroundColor;

            float4 frag (v2f i) : SV_Target
            {
                float3 col = 0;
                float3 camerapos = float3(0, EarthRadius, 0) + _WorldSpaceCameraPos * 1e-3;
                float3 eyeRay = normalize(i.viewDir);
                float3 sunDirection = _WorldSpaceLightPos0.xyz;

                float rayLength = sqrt(AtmosphereRadius2 - camerapos.y*camerapos.y * (1 - eyeRay.y * eyeRay.y)) - camerapos.y * eyeRay.y;

                bool rayHitEarth = eyeRay.y < 0 && camerapos.y*camerapos.y * (1-eyeRay.y * eyeRay.y) < EarthRadius2;

                if(rayHitEarth)
                {
                    float len = sqrt(EarthRadius2 - camerapos.y*camerapos.y * (1-eyeRay.y * eyeRay.y));
                    rayLength = -camerapos.y * eyeRay.y - len;
                }

                float segmentLength = rayLength / NumSample; 
                float matchingLength = 0; 

                float3 accumulateInscatterR = 0;            // Rayleigh transmittance 
                float3 accumulateInscatterM = 0;            // Mie transmittance 
                float opticalDepthViewR = 0;         // for Rayleigh transmittance 
                float opticalDepthViewM = 0;        // for Mie transmittance 

                float cosTheta = dot(eyeRay, sunDirection);     // cosTheta between the sun direction and the ray direction 
                float phaseR = 3.f / (16.f * UNITY_PI) * (1 + cosTheta * cosTheta);     // Rayleigh phase function
                float g = 0.76f; 
                float phaseM = 3.f / (8.f * UNITY_PI) * ((1.f - g * g) * (1.f + cosTheta * cosTheta)) / ((2.f + g * g) * pow(1.f + g * g - 2.f * g * cosTheta, 1.5f));          // Mie phase function

                for (int i = 0; i < NumSample; ++i)
                { 
                    float3 samplePosition = camerapos + (matchingLength + segmentLength * 0.5f) * eyeRay; 
                    float height = length(samplePosition) - EarthRadius; 

                    // compute optical depth for light
                    float deltaOpticalDepthR_NoScatterR0 = exp(-height / ScaleHeightR) * segmentLength; 
                    float deltaOpticalDepthM_NoScatterM0 = exp(-height / ScaleHeightM) * segmentLength; 
                    opticalDepthViewR += deltaOpticalDepthR_NoScatterR0; 
                    opticalDepthViewM += deltaOpticalDepthM_NoScatterM0; 

                    float3 inscatterR = ScatterR0 * deltaOpticalDepthR_NoScatterR0;
                    float3 inscatterM = ScatterM0 * deltaOpticalDepthM_NoScatterM0;

                    // light depth
                    float lightLength = sqrt(AtmosphereRadius2 + samplePosition.y*samplePosition.y * (sunDirection.y * sunDirection.y - 1)) - samplePosition.y * sunDirection.y;

                    bool lightRayHitEarth = sunDirection.y < 0 && samplePosition.y*samplePosition.y * (1-sunDirection.y * sunDirection.y) < EarthRadius2;

                    if(!lightRayHitEarth)
                    {

                        float segmentLengthLight = lightLength / NumSampleLight;
                        float matchingLengthLight = 0; 
                        float opticalDepthLightR = 0, opticalDepthLightM = 0; 
                        int j; 
                        for (j = 0; j < NumSampleLight; ++j) { 
                            float3 samplePositionLight = samplePosition + (matchingLengthLight + segmentLengthLight * 0.5f) * sunDirection; 
                            float heightLight = length(samplePositionLight) - EarthRadius; 
                            opticalDepthLightR += exp(-heightLight / ScaleHeightR) * segmentLengthLight; 
                            opticalDepthLightM += exp(-heightLight / ScaleHeightM) * segmentLengthLight; 
                            matchingLengthLight += segmentLengthLight; 
                        } 
                        float3 tau = ScatterR0 * (opticalDepthViewR + opticalDepthLightR) + ScatterM0 * 1.1f * (opticalDepthViewM + opticalDepthLightM); 
                        float3 attenuation = float3(exp(-tau.x), exp(-tau.y), exp(-tau.z)); 
                        accumulateInscatterR += attenuation * inscatterR; 
                        accumulateInscatterM += attenuation * inscatterM; 
                    }
                    matchingLength += segmentLength; 
                } 

                col = (accumulateInscatterR * phaseR + accumulateInscatterM * phaseM) * 20;
            
                if(rayHitEarth)
                {
                    float3 transmitanceView = exp(-ScatterR0 * opticalDepthViewR -ScatterM0 * 1.1f * opticalDepthViewM);

                    col += transmitanceView * _GroundColor.rgb;
                }

                return float4(col, 1);
            }
            ENDCG
        }
    }
}
