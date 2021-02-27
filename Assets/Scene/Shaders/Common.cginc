#ifndef _COMMON_CGINC_
#define _COMMON_CGINC_

// vertex part

struct AttributesDefault
{
    float3 vertex : POSITION;
};

struct VaryingsDefault
{
    float4 vertex : SV_POSITION;
    float2 uv : TEXCOORD0;
};

float2 TransformTriangleVertexToUV(float2 vertex)
{
    float2 uv = (vertex + 1.0) * 0.5;
    return uv;
}

VaryingsDefault vert(AttributesDefault v)
{
    VaryingsDefault o;
    o.vertex = float4(v.vertex.xy, 0.0, 1.0);
    o.uv = TransformTriangleVertexToUV(v.vertex.xy);

    #if UNITY_UV_STARTS_AT_TOP
        o.uv = o.uv * float2(1.0, -1.0) + float2(0.0, 1.0);
    #endif

    return o;
}

// common funtion part

half Max4(half p1, half p2, half p3, half p4)
{
    return max(p1, max(p2, max(p3, p4)));
}

half Min4(half p1, half p2, half p3, half p4)
{
    return min(p1, min(p2, min(p3, p4)));
}

half Max5(half p1, half p2, half p3, half p4, half p5)
{
    return max(p1, max( max(p2, p3), max(p4, p5) ));
}

half Min5(half p1, half p2, half p3, half p4, half p5)
{
    return min(p1, min( min(p2, p3), min(p4, p5) ));
}

#endif