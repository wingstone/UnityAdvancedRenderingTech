Shader "Environment/Sky"
{
    Properties
    {
    }
    SubShader
    {
        Tags {"Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off 
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define SKY_GROUND_THRESHOLD 0.2

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

            static const float EarthRadius = 6360e3;
            static const float EarthRadius2 = 6360e3*6360e3;
            static const float AtmosphereRadius = 6420e3;
            static const float AtmosphereRadius2 = 6420e3*6420e3;
            static const float ScaleHeightR = 7994;
            static const float ScaleHeightM = 1200;
            static const float3 ScatterR0 = float3(5.8e-6f, 13.5e-6f, 33.1e-6f);        //Rayleigh 海平面散射系数
            static const float3 ScatterM0 = 21e-6f;                                     // Mie 海平面散射系数
            static const int NumSample = 16;
            static const int NumSampleLight = 8;


            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.viewDir = normalize(mul(unity_ObjectToWorld, v.vertex).xyz);

                return o;
            }


            half4 frag (v2f i) : SV_Target
            {
                half3 col = 0;
                float3 camerapos = float3(0, EarthRadius+1, 0);
                float3 eyeRay = normalize(i.viewDir);
                float3 sunDirection = _WorldSpaceLightPos0.xyz;

                float rayLength = sqrt(AtmosphereRadius2 + camerapos.y*camerapos.y * (eyeRay.y * eyeRay.y - 1)) - camerapos.y * eyeRay.y;

                //在地球表面以上
                if(eyeRay.y>0 || camerapos.y*camerapos.y * (1-eyeRay.y * eyeRay.y) >EarthRadius2)
                {
                    int numSamples = 16; 
                    int numSamplesLight = 8; 
                    float segmentLength = rayLength / numSamples; 
                    float matchingLength = 0; 

                    float3 sumR = 0;            // Rayleigh contribution 
                    float3 sumM = 0;            // Mie contribution 
                    float opticalDepthR = 0;         // for Rayleigh transmittance 
                    float opticalDepthM = 0;        // for Mie transmittance 

                    float cosTheta = dot(eyeRay, sunDirection);     // cosTheta between the sun direction and the ray direction 
                    float phaseR = 3.f / (16.f * UNITY_PI) * (1 + cosTheta * cosTheta);     // Rayleigh phase function
                    float g = 0.76f; 
                    float phaseM = 3.f / (8.f * UNITY_PI) * ((1.f - g * g) * (1.f + cosTheta * cosTheta)) / ((2.f + g * g) * pow(1.f + g * g - 2.f * g * cosTheta, 1.5f));          // Mie phase function

                    for (int i = 0; i < numSamples; ++i)
                    { 
                        float3 samplePosition = camerapos + (matchingLength + segmentLength * 0.5f) * eyeRay; 
                        float height = length(samplePosition) - EarthRadius; 

                        // compute optical depth for light
                        float hr = exp(-height / ScaleHeightR) * segmentLength; 
                        float hm = exp(-height / ScaleHeightM) * segmentLength; 
                        opticalDepthR += hr; 
                        opticalDepthM += hm; 

                        // light optical depth
                        float lightLength = sqrt(AtmosphereRadius2 + samplePosition.y*samplePosition.y * (sunDirection.y * sunDirection.y - 1)) - samplePosition.y * sunDirection.y;

                        if(sunDirection.y>0 || samplePosition.y*samplePosition.y * (1-sunDirection.y * sunDirection.y) >EarthRadius2)
                        {

                            float segmentLengthLight = lightLength / numSamplesLight;
                            float matchingLengthLight = 0; 
                            float opticalDepthLightR = 0, opticalDepthLightM = 0; 
                            int j; 
                            for (j = 0; j < numSamplesLight; ++j) { 
                                float3 samplePositionLight = samplePosition + (matchingLengthLight + segmentLengthLight * 0.5f) * sunDirection; 
                                float heightLight = length(samplePositionLight) - EarthRadius; 
                                if (heightLight < 0) break; 
                                opticalDepthLightR += exp(-heightLight / ScaleHeightR) * segmentLengthLight; 
                                opticalDepthLightM += exp(-heightLight / ScaleHeightM) * segmentLengthLight; 
                                matchingLengthLight += segmentLengthLight; 
                            } 
                            float3 tau = ScatterR0 * (opticalDepthR + opticalDepthLightR) + ScatterM0 * 1.1f * (opticalDepthM + opticalDepthLightM); 
                            float3 attenuation = float3(exp(-tau.x), exp(-tau.y), exp(-tau.z)); 
                            sumR += attenuation * hr; 
                            sumM += attenuation * hm; 
                        }
                        matchingLength += segmentLength; 
                    } 
                    
                    col = (sumR * ScatterR0 * phaseR + sumM * ScatterM0 * phaseM) * 20;
                }

                return half4(col, 1);
            }
            ENDCG
        }
    }
}
