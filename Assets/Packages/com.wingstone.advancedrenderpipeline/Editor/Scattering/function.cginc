struct AtmosphereParameters 
{    
    float3 solar_irradiance;
    
    float sun_angular_radius;
    
    float bottom_radius;
    float bottom_radius2;
    
    float top_radius;
    float top_radius2;
    
    // DensityProfile rayleigh_density;
    
    float3 rayleigh_scattering;
    
    // DensityProfile mie_density;
    
    float3 mie_scattering;
    
    float3 mie_extinction;
    
    float mie_phase_function_g;
    
    // DensityProfile absorption_density;
    
    float3 absorption_extinction;       //ozone
    
    // float3 ground_albedo;
    
    // float mu_s_min;

    float scaleHeightR;

    float scaleHeightM;
};


float ClampCosine(float mu) 
{
    return clamp(mu, -1.0, 1.0);
}

float ClampDistance(float d) 
{
    return max(d, 0.0);
}

float ClampRadius(in AtmosphereParameters atmosphere, float r) 
{
    return clamp(r, atmosphere.bottom_radius, atmosphere.top_radius);
}

float SafeSqrt(float a) 
{
    return sqrt(max(a, 0));
}

float DistanceToTopAtmosphereBoundary(in AtmosphereParameters atmosphere,
float r, float mu) 
{
    //   assert(r <= atmosphere.top_radius);
    //   assert(mu >= -1.0 && mu <= 1.0);

    float discriminant = r * r * (mu * mu - 1.0) +
    atmosphere.top_radius * atmosphere.top_radius;
    return ClampDistance(-r * mu + SafeSqrt(discriminant));
}

float DistanceToBottomAtmosphereBoundary(in AtmosphereParameters atmosphere,
float r, float mu)
{
    //   assert(r >= atmosphere.bottom_radius);
    //   assert(mu >= -1.0 && mu <= 1.0);

    float discriminant = r * r * (mu * mu - 1.0) +
    atmosphere.bottom_radius * atmosphere.bottom_radius;
    return ClampDistance(-r * mu - SafeSqrt(discriminant));
}

bool RayIntersectsGround(in AtmosphereParameters atmosphere,
float r, float mu) 
{
    //   assert(r >= atmosphere.bottom_radius);
    //   assert(mu >= -1.0 && mu <= 1.0);

    return mu < 0.0 && r * r * (mu * mu - 1.0) +
    atmosphere.bottom_radius * atmosphere.bottom_radius >= 0.0;
}

float GetTextureCoordFromUnitRange(float x, float texture_size) 
{
    return 0.5 / float(texture_size) + x * (1.0 - 1.0 / float(texture_size));
}

float GetUnitRangeFromTextureCoord(float u, float texture_size) {
    return (u - 0.5 / float(texture_size)) / (1.0 - 1.0 / float(texture_size));
}

float2 GetTransmittanceTextureUvFromRMu(in AtmosphereParameters atmosphere,
float r, float mu) 
{
    // assert(r >= atmosphere.bottom_radius && r <= atmosphere.top_radius);
    // assert(mu >= -1.0 && mu <= 1.0);
    
    float H = sqrt(atmosphere.top_radius * atmosphere.top_radius -
    atmosphere.bottom_radius * atmosphere.bottom_radius);
    
    float rho =
    SafeSqrt(r * r - atmosphere.bottom_radius * atmosphere.bottom_radius);
    
    
    float d = DistanceToTopAtmosphereBoundary(atmosphere, r, mu);
    float d_min = atmosphere.top_radius - r;
    float d_max = rho + H;
    float x_mu = (d - d_min) / (d_max - d_min);
    float x_r = rho / H;
    return float2(GetTextureCoordFromUnitRange(x_mu, TRANSMITTANCE_TEXTURE_WIDTH),
    GetTextureCoordFromUnitRange(x_r, TRANSMITTANCE_TEXTURE_HEIGHT));
}

void GetRMuFromTransmittanceTextureUv(in AtmosphereParameters atmosphere,
in float2 uv, out float r, out float mu)
{
    // assert(uv.x >= 0.0 && uv.x <= 1.0);
    // assert(uv.y >= 0.0 && uv.y <= 1.0);

    float x_mu = GetUnitRangeFromTextureCoord(uv.x, TRANSMITTANCE_TEXTURE_WIDTH);
    float x_r = GetUnitRangeFromTextureCoord(uv.y, TRANSMITTANCE_TEXTURE_HEIGHT);
    
    float H = sqrt(atmosphere.top_radius * atmosphere.top_radius -
    atmosphere.bottom_radius * atmosphere.bottom_radius);
    
    float rho = H * x_r;
    r = sqrt(rho * rho + atmosphere.bottom_radius * atmosphere.bottom_radius);
    
    float d_min = atmosphere.top_radius - r;
    float d_max = rho + H;
    float d = d_min + x_mu * (d_max - d_min);
    mu = d == 0 ? float(1.0) : (H * H - rho * rho - d * d) / (2.0 * r * d);
    mu = ClampCosine(mu);
}

// We should precompute those terms from resolutions (Or set resolution as #defined constants)
float fromUnitToSubUvs(float u, float resolution) { return (u + 0.5f / resolution) * (resolution / (resolution + 1.0f)); }
float fromSubUvsToUnit(float u, float resolution) { return (u - 0.5f / resolution) * (resolution / (resolution - 1.0f)); }

#define NONLINEARSKYVIEWLUT 1

#if NONLINEARSKYVIEWLUT

    void UvToSkyViewLutParams(AtmosphereParameters Atmosphere, out float viewZenithCosAngle, out float lightViewCosAngle, in float viewHeight, in float2 uv)
    {
        // Constrain uvs to valid sub texel range (avoid zenith derivative issue making LUT usage visible)
        uv = float2(fromSubUvsToUnit(uv.x, SKYVIEW_TEXTURE_WIDTH), fromSubUvsToUnit(uv.y, SKYVIEW_TEXTURE_HEIGHT));

        float Vhorizon = SafeSqrt(viewHeight * viewHeight - Atmosphere.bottom_radius * Atmosphere.bottom_radius);
        float CosBeta = Vhorizon / viewHeight;				// GroundToHorizonCos
        float Beta = acos(CosBeta);
        float ZenithHorizonAngle = UNITY_PI - Beta;

        if (uv.y < 0.5f)
        {
            float coord = 2.0*uv.y;
            coord = 1.0 - coord;
            #if NONLINEARSKYVIEWLUT
                coord *= coord;
            #endif
            coord = 1.0 - coord;
            viewZenithCosAngle = cos(ZenithHorizonAngle * coord);
        }
        else
        {
            float coord = uv.y*2.0 - 1.0;
            #if NONLINEARSKYVIEWLUT
                coord *= coord;
            #endif
            viewZenithCosAngle = cos(ZenithHorizonAngle + Beta * coord);
        }

        float coord = uv.x;
        coord *= coord;
        lightViewCosAngle = coord*2.0 - 1.0;
    }

    void SkyViewLutParamsToUv(AtmosphereParameters Atmosphere, in bool IntersectGround, in float viewZenithCosAngle, in float lightViewCosAngle, in float viewHeight, out float2 uv)
    {
        float Vhorizon = SafeSqrt(viewHeight * viewHeight - Atmosphere.bottom_radius * Atmosphere.bottom_radius);
        float CosBeta = Vhorizon / viewHeight;				// GroundToHorizonCos
        float Beta = acos(CosBeta);
        float ZenithHorizonAngle = UNITY_PI - Beta;

        if (!IntersectGround)
        {
            float coord = acos(viewZenithCosAngle) / ZenithHorizonAngle;
            coord = 1.0 - coord;
            #if NONLINEARSKYVIEWLUT
                coord = SafeSqrt(coord);
            #endif
            coord = 1.0 - coord;
            uv.y = coord * 0.5f;
        }
        else
        {
            float coord = (acos(viewZenithCosAngle) - ZenithHorizonAngle) / Beta;
            #if NONLINEARSKYVIEWLUT
                coord = SafeSqrt(coord);
            #endif
            uv.y = coord * 0.5f + 0.5f;
        }

        {
            float coord = lightViewCosAngle * 0.5f + 0.5f;
            coord = SafeSqrt(coord);
            uv.x = coord;
        }

        // Constrain uvs to valid sub texel range (avoid zenith derivative issue making LUT usage visible)
        uv = float2(fromUnitToSubUvs(uv.x, SKYVIEW_TEXTURE_WIDTH), fromUnitToSubUvs(uv.y, SKYVIEW_TEXTURE_HEIGHT));
    }

#else

    void UvToSkyViewLutParams(AtmosphereParameters Atmosphere, out float viewZenithCosAngle, out float lightViewCosAngle, in float viewHeight, in float2 uv)
    {
        float y = uv.y*uv.y;
        viewZenithCosAngle = y*2-1;
        float x = uv.x*uv.x;
        lightViewCosAngle = x*2-1;
    }

    void SkyViewLutParamsToUv(AtmosphereParameters Atmosphere, in bool IntersectGround, in float viewZenithCosAngle, in float lightViewCosAngle, in float viewHeight, out float2 uv)
    {
        uv.y = sqrt(viewZenithCosAngle*0.5 + 0.5);
        uv.x = sqrt(lightViewCosAngle*0.5 + 0.5);
    }

#endif

SamplerState samplerLinearClamp;


// implement function

void ComputeOpticalLengthToTopAtmosphereBoundary(
in AtmosphereParameters atmosphere, 
float r, float mu, out float opticalDepthR, out float opticalDepthM, out float opticalDepthO) 
{
    // assert(r >= atmosphere.bottom_radius && r <= atmosphere.top_radius);
    // assert(mu >= -1.0 && mu <= 1.0);
    
    const int SAMPLE_COUNT = 500;
    
    float dx = DistanceToTopAtmosphereBoundary(atmosphere, r, mu) / float(SAMPLE_COUNT);
    
    opticalDepthR = 0;
    opticalDepthM = 0;
    opticalDepthO = 0;

    for (int i = 0; i <= SAMPLE_COUNT; ++i) {
        float d_i = float(i) * dx;
        float r_i = sqrt(d_i * d_i + 2.0 * r * mu * d_i + r * r);

        float height = r_i - atmosphere.bottom_radius;

        float weight_i = i == 0 || i == SAMPLE_COUNT ? 0.5 : 1.0;

        opticalDepthR += exp(-height/atmosphere.scaleHeightR) * dx * weight_i;
        opticalDepthM += exp(-height/atmosphere.scaleHeightM) * dx * weight_i;
        opticalDepthO += max(0, 1-abs(height-25)/15) * dx * weight_i;
    }
}

float3 ComputeTransmittanceToTopAtmosphereBoundary(
in AtmosphereParameters atmosphere, float r, float mu) 
{
    // assert(r >= atmosphere.bottom_radius && r <= atmosphere.top_radius);
    // assert(mu >= -1.0 && mu <= 1.0);

    float opticalDepthR = 0;
    float opticalDepthM = 0;
    float opticalDepthO = 0;

    ComputeOpticalLengthToTopAtmosphereBoundary(
    atmosphere, r, mu, opticalDepthR, opticalDepthM, opticalDepthO);

    return exp(-(
    atmosphere.rayleigh_scattering * opticalDepthR +
    atmosphere.mie_extinction * opticalDepthM + 
    atmosphere.absorption_extinction * opticalDepthO));
    // atmosphere.absorption_extinction *
    // ComputeOpticalLengthToTopAtmosphereBoundary(
    // atmosphere, atmosphere.absorption_density, r, mu)));
}


float3 ComputeTransmittanceToTopAtmosphereBoundaryTexture(in AtmosphereParameters atmosphere, in float2 uv)
{
    float r;
    float mu;
    GetRMuFromTransmittanceTextureUv(atmosphere, uv, r, mu);
    return ComputeTransmittanceToTopAtmosphereBoundary(atmosphere, r, mu);
}



float3 GetTransmittanceToTopAtmosphereBoundary(
in AtmosphereParameters atmosphere,
in sampler2D transmittance_texture,
float r, float mu)
{
    // assert(r >= atmosphere.bottom_radius && r <= atmosphere.top_radius);

    float2 uv = GetTransmittanceTextureUvFromRMu(atmosphere, r, mu);
    return tex2D(transmittance_texture, uv).rgb;
}

float3 GetTransmittanceToTopAtmosphereBoundary(
in AtmosphereParameters atmosphere,
in Texture2D<float4> transmittance_texture,
float r, float mu)
{
    // assert(r >= atmosphere.bottom_radius && r <= atmosphere.top_radius);
    float2 uv = GetTransmittanceTextureUvFromRMu(atmosphere, r, mu);
    return transmittance_texture.SampleLevel(samplerLinearClamp, uv, 0).rgb;
}

float3 GetTransmittance(
in AtmosphereParameters atmosphere,
in sampler2D transmittance_texture,
float r, float mu, float d, bool ray_r_mu_intersects_ground) 
{
    // assert(r >= atmosphere.bottom_radius && r <= atmosphere.top_radius);
    // assert(mu >= -1.0 && mu <= 1.0);
    // assert(d >= 0.0 * m);

    float r_d = ClampRadius(atmosphere, sqrt(d * d + 2.0 * r * mu * d + r * r));
    float mu_d = ClampCosine((r * mu + d) / r_d);

    if (ray_r_mu_intersects_ground) 
    {
        return min(GetTransmittanceToTopAtmosphereBoundary(atmosphere, transmittance_texture, r_d, -mu_d) /
        GetTransmittanceToTopAtmosphereBoundary(atmosphere, transmittance_texture, r, -mu),
        (float3)1.0);
    }
    else 
    {
        return min(GetTransmittanceToTopAtmosphereBoundary(atmosphere, transmittance_texture, r, mu) /
        GetTransmittanceToTopAtmosphereBoundary(atmosphere, transmittance_texture, r_d, mu_d),
        (float3)1.0);
    }
}

float3 GetTransmittance(
in AtmosphereParameters atmosphere,
in Texture2D<float4> transmittance_texture,
float r, float mu, float d, bool ray_r_mu_intersects_ground) 
{
    // assert(r >= atmosphere.bottom_radius && r <= atmosphere.top_radius);
    // assert(mu >= -1.0 && mu <= 1.0);
    // assert(d >= 0.0 * m);

    float r_d = ClampRadius(atmosphere, sqrt(d * d + 2.0 * r * mu * d + r * r));
    float mu_d = ClampCosine((r * mu + d) / r_d);

    if (ray_r_mu_intersects_ground) 
    {
        return min(GetTransmittanceToTopAtmosphereBoundary(atmosphere, transmittance_texture, r_d, -mu_d) /
        GetTransmittanceToTopAtmosphereBoundary(atmosphere, transmittance_texture, r, -mu),
        (float3)1.0);
    }
    else 
    {
        return min(GetTransmittanceToTopAtmosphereBoundary(atmosphere, transmittance_texture, r, mu) /
        GetTransmittanceToTopAtmosphereBoundary(atmosphere, transmittance_texture, r_d, mu_d),
        (float3)1.0);
    }
}


float3 GetTransmittanceToSun(
in AtmosphereParameters atmosphere,
in sampler2D transmittance_texture,
float r, float mu_s) 
{
    float sin_theta_h = atmosphere.bottom_radius / r;
    float cos_theta_h = -sqrt(max(1.0 - sin_theta_h * sin_theta_h, 0.0));
    return GetTransmittanceToTopAtmosphereBoundary(atmosphere, transmittance_texture, r, mu_s) *
    smoothstep(-sin_theta_h * atmosphere.sun_angular_radius, sin_theta_h * atmosphere.sun_angular_radius, mu_s - cos_theta_h);
}

float3 GetMultipleScattering(sampler2D _MultiSactteringLut, AtmosphereParameters Atmosphere, float3 worlPos, float viewZenithCosAngle,float2 multiScatteringLutRes)
{
	float2 uv = saturate(float2(viewZenithCosAngle*0.5f + 0.5f, (length(worlPos) - Atmosphere.bottom_radius) / (Atmosphere.top_radius - Atmosphere.bottom_radius)));
	uv = float2(fromUnitToSubUvs(uv.x, multiScatteringLutRes.x), fromUnitToSubUvs(uv.y, multiScatteringLutRes.y));

	float3 multiScatteredLuminance = tex2D(_MultiSactteringLut, uv).rgb;
	return multiScatteredLuminance;
}

// singlescattering + multiscattering
float3 RenderSkyViewLut(AtmosphereParameters atmosphere, sampler2D transmittanceLut, float cameraHight, float2 uv, float3 lightDirection, float4 groundColor, sampler2D _MultiSactteringLut, float4 _MultiSactteringLut_Size)
{
    const int SAMPLE_COUNT = 512;

    float3 col = 0;

    // camera pos
    float3 camerapos = float3(0, atmosphere.bottom_radius + cameraHight, 0);
    camerapos = float3(0, length(camerapos), 0);
    float r = length(camerapos);
    float viewZenithCosAngle = 0, lightViewCosAngle = 0;
    UvToSkyViewLutParams(atmosphere, viewZenithCosAngle, lightViewCosAngle, r, uv);

    // sun direction
    float3 sunDirection = lightDirection;
    
    // eye direction
    float cos_phi = viewZenithCosAngle;
    float sin_phi = sqrt(1-cos_phi*cos_phi);
    float cos_theta = lightViewCosAngle;
    float sin_theta = sqrt(1-cos_theta*cos_theta);
    float3 eyeRay = float3(sin_phi*cos_theta, cos_phi, sin_phi*sin_theta);

    float mu = dot(camerapos, eyeRay)/r;

    float rayLength = sqrt(atmosphere.top_radius2 - r*r * (1 - eyeRay.y * eyeRay.y)) - r * eyeRay.y;

    bool rayHitEarth = eyeRay.y < 0 && r*r * (1-eyeRay.y * eyeRay.y) < atmosphere.bottom_radius2;

    if(rayHitEarth)
    {
        float len = sqrt(atmosphere.bottom_radius2 - r*r * (1-eyeRay.y * eyeRay.y));
        rayLength = -r * eyeRay.y - len;
    }

    float segmentLength = rayLength / SAMPLE_COUNT; 

    float3 accumulateInscatterR = 0;
    float3 accumulateInscatterM = 0;
    float3 accumulateMultiScattering = 0;

    // phase funtion
    float cosTheta = dot(eyeRay, sunDirection);     // cosTheta between the sun direction and the ray direction 
    float phaseR = 3.f / (16.f * UNITY_PI) * (1 + cosTheta * cosTheta);
    float g = atmosphere.mie_phase_function_g; 
    float phaseM = 3.f / (8.f * UNITY_PI) * ((1.f - g * g) * (1.f + cosTheta * cosTheta)) / ((2.f + g * g) * pow(1.f + g * g - 2.f * g * cosTheta, 1.5f));

    for (int i = 0; i <= SAMPLE_COUNT; ++i)
    { 
        float d = segmentLength * i;
        float3 samplePosition = camerapos + segmentLength *i * eyeRay; 
        float weight = i == 0 || i == SAMPLE_COUNT ? 0.5 : 1;

        float height = length(samplePosition) - atmosphere.bottom_radius; 

        float3 scatteringR = atmosphere.rayleigh_scattering * exp(-height / atmosphere.scaleHeightR) * segmentLength; 
        float3 scatteringM = atmosphere.mie_scattering * exp(-height / atmosphere.scaleHeightM) * segmentLength; 

        float3 transmittance = GetTransmittance(atmosphere, transmittanceLut, r, mu, d, rayHitEarth);

        bool lightRayHitEarth = sunDirection.y < 0 && samplePosition.y*samplePosition.y * (1-sunDirection.y * sunDirection.y) < atmosphere.bottom_radius2;

        if(!lightRayHitEarth)
        {
            float r_l = length(samplePosition);
            float mu_l = dot(samplePosition, sunDirection) / r_l;
            float3 transmittance_l = GetTransmittanceToTopAtmosphereBoundary(atmosphere, transmittanceLut, r_l, mu_l);

            float3 attenuation = transmittance * transmittance_l;
            accumulateInscatterR += attenuation * scatteringR*weight; 
            accumulateInscatterM += attenuation * scatteringM*weight; 
        }
        
		float SunZenithCosAngle = dot(samplePosition, sunDirection) / length(samplePosition);
        float3 multiScatteredLuminance = GetMultipleScattering(_MultiSactteringLut, atmosphere, samplePosition, SunZenithCosAngle, _MultiSactteringLut_Size.zw);
        
        accumulateMultiScattering += multiScatteredLuminance * (scatteringR + scatteringM) * transmittance*weight;
    } 
    
    // L_s
    col = (accumulateInscatterR * phaseR + accumulateInscatterM * phaseM + accumulateMultiScattering) * atmosphere.solar_irradiance;
    
    // L_r
    if(rayHitEarth)
    {
        float3 transmittance = GetTransmittance(atmosphere, transmittanceLut, r, mu, rayLength, rayHitEarth);
        col += transmittance * groundColor.rgb * saturate(dot(sunDirection, normalize(camerapos + rayLength * eyeRay)))/UNITY_PI;
    }

    return col;
}

void GetSingleScattering_ForMultiScattering(AtmosphereParameters atmosphere, Texture2D<float4> transmittanceLut, float cameraHight, float3 sunDirection, float3 eyeRay, float4 groundColor, out float3 multiScatAs1, out float3 L)
{
    const int SAMPLE_COUNT = 20;

    L = 0;
    multiScatAs1 = 0 ;

    // camera pos
    float3 camerapos = float3(0, atmosphere.bottom_radius + cameraHight, 0);
    float r = length(camerapos);

    float mu = dot(camerapos, eyeRay)/r;

    float rayLength = sqrt(atmosphere.top_radius2 - r*r * (1 - eyeRay.y * eyeRay.y)) - r * eyeRay.y;

    bool rayHitEarth = eyeRay.y < 0 && r*r * (1-eyeRay.y * eyeRay.y) < atmosphere.bottom_radius2;

    if(rayHitEarth)
    {
        float len = sqrt(atmosphere.bottom_radius2 - r*r * (1-eyeRay.y * eyeRay.y));
        rayLength = -r * eyeRay.y - len;
    }

    float segmentLength = rayLength / SAMPLE_COUNT; 
    float matchingLength = 0; 

    float3 accumulateInscatterR = 0;            // Rayleigh transmittance 
    float3 accumulateInscatterM = 0;            // Mie transmittance 
    float opticalDepthViewR = 0;                // for Rayleigh transmittance 
    float opticalDepthViewM = 0;                // for Mie transmittance 

    // phase funtion
	const float uniformPhase = 1.0 / (4.0 * UNITY_PI);
    
    for (int i = 0; i < SAMPLE_COUNT; ++i)
    { 
        float3 samplePosition = camerapos + (matchingLength + segmentLength * 0.5f) * eyeRay; 
        float height = length(samplePosition) - atmosphere.bottom_radius; 

        float3 inscatterR = atmosphere.rayleigh_scattering * exp(-height / atmosphere.scaleHeightR) * segmentLength; 
        float3 inscatterM = atmosphere.mie_scattering * exp(-height / atmosphere.scaleHeightM) * segmentLength; 

        float d = matchingLength + segmentLength * 0.5f;
        float3 transmittance = GetTransmittance(atmosphere, transmittanceLut, r, mu, d, rayHitEarth);

        bool lightRayHitEarth = sunDirection.y < 0 && samplePosition.y*samplePosition.y * (1-sunDirection.y * sunDirection.y) < atmosphere.bottom_radius2;

        if(!lightRayHitEarth)
        {
            float r_l = length(samplePosition);
            float mu_l = dot(samplePosition, sunDirection) / r_l;
            float3 transmittance_l = GetTransmittanceToTopAtmosphereBoundary(atmosphere, transmittanceLut, r_l, mu_l);

            float3 attenuation = transmittance * transmittance_l;
            accumulateInscatterR += attenuation * inscatterR; 
            accumulateInscatterM += attenuation * inscatterM; 
        }
        matchingLength += segmentLength; 

        multiScatAs1 += transmittance * (atmosphere.rayleigh_scattering * exp(-height / atmosphere.scaleHeightR) + atmosphere.mie_scattering * exp(-height / atmosphere.scaleHeightM));
    } 
    
    // L_s
    L = (accumulateInscatterR * uniformPhase + accumulateInscatterM * uniformPhase);
    
    // L_r
    if(rayHitEarth)
    {
        float3 transmittance = GetTransmittance(atmosphere, transmittanceLut, r, mu, rayLength, rayHitEarth);
        L += transmittance * groundColor.rgb * saturate(dot(sunDirection, normalize(camerapos + rayLength * eyeRay)))/UNITY_PI;
    }
}