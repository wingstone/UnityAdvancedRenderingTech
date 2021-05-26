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
float _ScreenDistance;
float _ThicknessScale;

// PC view space: right coordinate, front:-z;
// PC screen range x:(-1,1), y:(-1,1), z:(1,0), reverse-z;
// LinearEyeDepth: view space, but +z;
// Linear01Depth: View Space, but(0,1), LinearEyeDepth/far;
float3 GetViewSpacePosition(float2 uv)
{
    float depth = tex2D(_CameraDepthTexture, uv).r;
    float4 result = mul(_InverseProjectionMatrix, float4(2.0 * uv - 1.0, depth, 1.0));
    return result.xyz / result.w;
}

float distanceSquared(float2 v0, float2 v1)
{
    float2 v = v1 - v0;
    return dot(v, v);
}

bool rayMarch3D(float3 originVS, float3 dirVS, inout float factor, out float3 hitPointSS)
{
    //Prevent immediate self intersection.
    originVS = originVS + dirVS * RAY_MARCH_ORIGIN_OFFSET_EPSILON;
    float deltaT = _RayMatchDistance/_RayMatchSteps;
    float3 raySS = 0;
    float3 rayVS = originVS;
    bool hited = false;

    //Ray Marching loop
    float iterations = 0;
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

        // view space test
        float realZ = rayVS.z;
        sceneDepth = -LinearEyeDepth(sceneDepth);
        if( realZ < sceneDepth  && realZ > sceneDepth - deltaT*_DepthThickness)
        {
            hited = true;
            factor = iterations / _RayMatchSteps;
            hitPointSS = float3(uv, raySS.z);
            break;
        }

    }

    return hited;
}


bool nonConservativeDDATracing(float2 pSS0, float2 pSS1, float pHS0w, float pHS1w, float3 originVS, float3 endPointVS, inout float factor, out float3 hitPointSS)
{
    //Perspective Division terms.
    float k0 = 1.0f / pHS0w;
    float k1 = 1.0f / pHS1w;

    //Convert to points which can by linearly interpolated in 2D.
    float3 q0 = originVS * k0;
    float3 q1 = endPointVS * k1;

    //Initialize to off-screen
    float2 hitPixel = float2(-1.0f, -1.0f);

    //If the line is degenerate, make it cover at least one pixel
    pSS1 += (float2)((distanceSquared(pSS0, pSS1) < 0.0001f) ? 0.01f : 0.0f);

    //Permute so that the primary iteration is in x to reduce branches later.
    bool permute = false;
    float2 delta = pSS1 - pSS0;
    if (abs(delta.x) < abs(delta.y))
    {
            //This is a more vertical line.
            //Create a permutation that swaps x and y in the output.
            permute = true;
            delta = delta.yx;
            pSS0 = pSS0.yx;
            pSS1 = pSS1.yx;
    }

    //From now on, x is the primary iteration direction and y is the second one.
    float stepDirection = sign(delta.x);
    float invDx = stepDirection / delta.x;
    float2 dPSS = float2(stepDirection, invDx * delta.y);

    //Track the derivatives of q and k.
    float3 dQ = (q1 - q0) * invDx;
    float dk = (k1 - k0) * invDx;

    //Scale Derivatives by pixel stride.
    float stride = _ScreenDistance / _RayMatchSteps;
    dPSS *= stride;
    dQ *= stride;
    dk *= stride;

    //Slide pSS from p0SS to p1SS, q from q0 to q1 and k from k0 to k1
    float2 pSS = pSS0;
    float3 q = q0;
    float k = k0;

    //pSS1.x is never modified after this point
    //so pre-scale it by the step direction for a signed comparison
    float endSS = pSS1.x * stepDirection;
    //Move to the next pixel to prevent immediate self intersection.
    pSS += dPSS;
    q.z += dQ.z;
    k += dk;
    
    //Setup the ray depth interval.
    //Keep track of previous ray depth, so only 1 value is computed per iteration
    float rayZMax = originVS.z;
    float rayZMin = originVS.z;

    //Loop until an intersection or the end of the ray has been reached.
    //Single Layer traversal bundles intersection check here as an optimization.
    bool hited = false;
    float iterations = 0;
    UNITY_LOOP
    for (int iterations = 0; iterations < _RayMatchSteps; iterations++, pSS += dPSS, q.z += dQ.z, k += dk)
    {
        hitPixel = permute ? pSS.yx : pSS;

        //Use max from previous iteration as the current min.
        rayZMin = rayZMax;

        //Compute the maximum depth of the ray in this pixel.
        rayZMax = (q.z + dQ.z * 0.5f) / (k + dk * 0.5f);

        // view space test
        float realZ = rayZMax;
        float2 uv = hitPixel*_MainTex_TexelSize.xy;
        if(uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1 )
        {
            break;
        }
        float sceneDepth = tex2D(_CameraDepthTexture, uv).r;
        // if(sceneDepth < 1e-4)   //skybox
        // {
        //     break;
        // }
        sceneDepth = -LinearEyeDepth(sceneDepth);
        float thickness = rayZMin - rayZMax;
        if( realZ < sceneDepth && realZ > sceneDepth - thickness*_ThicknessScale)
        {
            hited = true;
            factor = iterations / _RayMatchSteps;
            hitPointSS = float3(uv, q.z * (1.0f / k));
            break;
        }
        else if(realZ < sceneDepth)
        {
            break;
        }
    }

    factor = rayZMax;
    return hited;
}