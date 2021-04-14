Shader "Hidden/AdvancedRTR/SSR"
{
    Properties
    {
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "Ray Matching Test"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #define TRAVERSAL_SCHEME_RAY_MARCH_3D
            #define RAY_MARCH_ORIGIN_OFFSET_EPSILON 0.0001

            #include "UnityCG.cginc"
            #include "Common.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            sampler2D _CameraGBufferTexture0; // albedo = g[0].rgb
            sampler2D _CameraGBufferTexture1; // roughness = g[1].a
            sampler2D _CameraGBufferTexture2; // normal.xyz 2. * g[2].rgb - 1.
            sampler2D _CameraDepthTexture; //depth = r;

            float4x4 _ViewMatrix;
            float4x4 _InverseViewMatrix;
            float4x4 _InverseProjectionMatrix;
            float4x4 _ScreenSpaceProjectionMatrix;
            float _RayMatchSteps;
            float _RayMatchDistance;
            float _DepthThickness;

            struct Result
            {
                float2 uv;
                float4 color;
            };

            struct Ray
            {
                float3 origin;
                float3 direction;
            };

            float3 GetViewSpacePosition(float2 uv)
            {
                float depth = tex2D(_CameraDepthTexture, uv).r;
                float4 result = mul(_InverseProjectionMatrix, float4(2.0 * uv - 1.0, depth, 1.0));
                return result.xyz / result.w;
            }

            Result RayMatching(Ray ray)
            {
                Result result = (Result)0;
                
                // float matchStep = min(_MainTex_TexelSize.x, _MainTex_TexelSize.y);
                float stepLen = _RayMatchDistance/_RayMatchSteps;
                UNITY_LOOP
                for(int i = 0; i < _RayMatchSteps; i++)
                {
                    ray.origin += ray.direction*stepLen;
                    float4 position = mul(_ScreenSpaceProjectionMatrix, float4(ray.origin, 1));
                    position.xyz = position.xyz / position.w;
                    float2 uv = position.xy*0.5+0.5;

                    if(uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1 )
                    {
                        break;
                    }

                    float realZ = position.z;
                    // result.color = tex2D(_CameraDepthTexture, uv).r;
                    // break;
                    float sceneDepth = tex2D(_CameraDepthTexture, uv).r;
                    if(sceneDepth > realZ && sceneDepth < realZ + stepLen)
                    {
                        result.uv = uv;
                        result.color = tex2D(_MainTex, uv);
                        // result.color.rg = uv;
                        // result.color.b = 0;
                        break;
                    }
                }
                
                return result;
            }

            bool rayMarch3D(float3 originVS, float3 dirVS, inout float iterations, out float3 hitPointSS)
            {
                //Prevent immediate self intersection.
                originVS = originVS + dirVS * RAY_MARCH_ORIGIN_OFFSET_EPSILON;
                float deltaT = _RayMatchDistance/_RayMatchSteps;
                float3 raySS = 0;
                float3 rayVS = originVS;
                bool missed = false;
                //Ray Marching loop
                UNITY_LOOP
                for (int i = 0; i < _RayMatchSteps; i++)
                {
                    iterations++;
                    rayVS += dirVS * deltaT;
                    //Convert to homogeneous clip space.
                    float4 rayHS = mul(_ScreenSpaceProjectionMatrix, float4(rayVS, 1));
                    
                    //Perform perspective divide to convert to pixel coordinates.
                    raySS.xyz = rayHS.xyz / rayHS.w;

                    float2 uv = raySS.xy*0.5+0.5;
                    if(uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1 )
                    {
                        break;
                    }

                    float sceneDepth = tex2D(_CameraDepthTexture, uv).r;
                    float realZ = raySS.z;
                    sceneDepth = LinearEyeDepth(sceneDepth);
                    realZ = rayHS.w;
                    // if(sceneDepth > realZ && sceneDepth < realZ + _DepthThickness)
                    if( realZ > sceneDepth  && realZ < sceneDepth + deltaT)
                    {
                        missed = true;
                        break;
                    }
                }

                //Build hitpoint
                hitPointSS = float3(raySS.xy*0.5+0.5, raySS.z);
                return missed;
            }

            // bool ncDDATraversalLoopCondition(float pSSx, float stepDirection, float endSS, float iterations, float maxSteps, bool outOfBounds, float sceneZMin, float rayZMin, float rayZMax)
            // {
            //     #if (SSRT_LAYERS > 1)
            //         return (pSSx * stepDirection <= endSS)//Break if traveled to end point distance.
            //         && (iterations < maxSteps)//Break if max steps reached.
            //         && !outOfBounds);//Break if sampling outside of the depth buffer.
            //     #else
            //         float sceneZMax = sceneZMin + cbSsrData.zThickness;
            //         return ((pSSx * stepDirection) <= endSS)
            //         && (iterations < maxSteps)
            //         && !outOfBounds
            //         && !intersectsDepthBuffer(sceneZMin, sceneZMax,
            //         rayZMin, rayZMax); //Break if intersection found.
            //     #endif
            // }

            // bool nonConservativeDDATracing(float2 pSS0, float2 pSS1, float pHS1w, float3 originVS, float3 endPointVS, inout float iterations, out float3 hitPointSS, out float hitLayer)
            // {
            //     //Project the origin into screen-space pixel coodinates.
            //     //pHS1 is already given in input.
            //     float pHS0w = mul(float4(originVS, 1), cbFrustumData.vsToHSProjMatrix).w;
            //     //Perspective Division terms.
            //     float k0 = 1.0f / pHS0w;
            //     float k1 = 1.0f / pHS1w;
            //     //Convert to points which can by linearly interpolated in 2D.
            //     float3 q0 = originVS * k0;
            //     float3 q1 = endPointVS * k1;
            //     //Initialize to off-screen
            //     float2 hitPixel = float2(-1.0f, -1.0f);
            //     //If the line is degenerate, make it cover at least one pixel
            //     pSS1 += (float2)((distanceSquared(pSS0, pSS1) < 0.0001f) ? 0.01f : 0.0f);
            //     //Permute so that the primary iteration is in x to reduce branches later.
            //     bool permute = false;
            //     float2 delta = pSS1 - pSS0;
            //     if (abs(delta.x) < abs(delta.y))
            //     {
            //         //This is a more vertical line.
            //         //Create a permutation that swaps x and y in the output.
            //         permute = true;
            //         delta = delta.yx;
            //         pSS0 = pSS0.yx;
            //         pSS1 = pSS1.yx;
            //     }
            //     //From now on, x is the primary iteration direction and y is the second one.
            //     float stepDirection = sign(delta.x);
            //     float invDx = stepDirection / delta.x;
            //     float2 dPSS = float2(stepDirection, invDx * delta.y);

            //     //Track the derivatives of q and k.
            //     float3 dQ = (q1 - q0) * invDx;
            //     float dk = (k1 - k0) * invDx;
            //     //Scale Derivatives by pixel stride.
            //     dPSS *= cbSsrData.stride;
            //     dQ *= cbSsrData.stride;
            //     dk *= cbSsrData.stride;
            //     //Slide pSS from p0SS to p1SS, q from q0 to q1 and k from k0 to k1
            //     float2 pSS = pSS0;
            //     float3 q = q0;
            //     float k = k0;
            //     //pSS1.x is never modified after this point
            //     //so pre-scale it by the step direction for a signed comparison
            //     float endSS = pSS1.x * stepDirection;
            //     //Move to the next pixel to prevent immediate self intersection.
            //     pSS += dPSS;
            //     q.z += dQ.z;
            //     k += dk;
            //     //Setup the ray depth interval.
            //     //Keep track of previous ray depth, so only 1 value is computed per iteration
            //     float rayZMax = originVS.z;
            //     float rayZMin = originVS.z;
            //     float sceneZMin = rayZMax + cbFrustumData.farZ;//Sufficiently far away
            //     float sceneZMax = sceneZMin;
            //     hitLayer = -1.0f;
            //     bool outOfBounds = false;
            //     //Loop until an intersection or the end of the ray has been reached.
            //     //Single Layer traversal bundles intersection check here as an optimization.
            //     [loop]
            //     for (pSS; ncDDATraversalLoopCondition(pSS.x, stepDirection, endSS,
            //     iterations, cbSsrData.maxSteps, outOfBounds, sceneZMin, rayZMin, rayZMax);
            //     pSS += dPSS, q.z += dQ.z, k += dk)
            //     {
            //         Iterations++;
            //         hitPixel = permute ? pSS.yx : pSS;
            //         //Use max from previous iteration as the current min.
            //         rayZMin = rayZMax;
            //         //Compute the maximum depth of the ray in this pixel.
            //         rayZMax = (q.z + dQ.z * 0.5f) / (k + dk * 0.5f);
            //         //Check each layer for intersection.
            //         for (float layer = 0; layer < SSRT_LAYERS; layer++)
            //         {
            //             //Get the viewspace depth from the depth buffer.
            //             sceneZMin = readDepthBuffer(depthSRV, hitPixel, layer).x;
            //             outOfBounds = (sceneZMin == 0.0f);
            //             #if defined(Z_BUFFER_IS_HYPERBOLIC)
            //                 sceneZMin = linearizeDepth(sceneZMin, cbFrustumData.linearDepthConversion);
            //             #endif
            //             sceneZMax = sceneZMin + cbSsrData.zThickness;
            //             #if (SSRT_LAYERS > 1)
            //                 if (outOfBounds || sceneZMin >= cbFrustumData.farZ)
            //                 break;
            //                 //Break if intersected the depth buffer or sampled outside depth buffer.
            //                 //Single layer will perform this check in the loop condition.
            //                 if (intersectsDepthBuffer(sceneZMin, sceneZMax, rayZMin, rayZMax))
            //                 {
            //                     hitLayer = layer;
            //                     break;
            //                 }
            //             #endif
            //         }
            //         //Multilayer double loop exit condition.
            //         #if (SSRT_LAYERS > 1)
            //             if (hitLayer >= 0)
            //             break;
            //         #endif
            //     }
            //     //Calculate hipoint with viewspace depth
            //     hitPointSS = float3(hitPixel.xy, q.z * (1.0f / k));
            //     //Dicard hitpoints that are outside of the view intersecting depth buffer.
            //     //If the ray does not intersect the depth buffer, then discard the hitpoint.
            //     return ((any(hitPointSS.xy < 0) || any(hitPointSS.xy >= cbFrustumData.resolution))
            //     || !intersectsDepthBuffer(sceneZMin, sceneZMax, rayZMin, rayZMax));
            // }

            float4 frag(VaryingsDefault i) : SV_Target0
            {
                float3 normal = tex2D(_CameraGBufferTexture2, i.uv).rgb*2.0-1.0;
                normal = mul(_ViewMatrix, normal);

                Ray ray = (Ray)0;
                ray.origin = GetViewSpacePosition(i.uv);
                ray.direction = reflect(normalize(ray.origin), normal);
                
                // face camera
                if(ray.direction.z > 0)
                    return tex2D(_MainTex, i.uv);

                // Result result = RayMatching(ray);
                // return result.color;

                float3 hitPointSS = float3(-1.0f, -1.0f, 0.0f);
                float hitLayer = -1.0f;
                bool missed = false;
                float iterations = 0.0f;

                #ifdef TRAVERSAL_SCHEME_RAY_MARCH_3D
                    missed = rayMarch3D(ray.origin, ray.direction, iterations, hitPointSS);
                #endif

                #ifdef TRAVERSAL_SCHEME_NON_CONSERVATIVE
                    missed = nonConservativeDDATracing(pointSS0.xy, pointSS1.xy, pointHS1.w, originVS, endPointVS, iterations, hitPointSS);
                #endif

                float4 color = tex2D(_MainTex, hitPointSS.xy);

                return float4(color.rgb, missed);
            }
            ENDCG
        }
    }
}
