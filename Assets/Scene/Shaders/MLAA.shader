Shader "Hidden/AdvancedRTR/MLAA"
{
    Properties
    {
        _AreaTex("Area Tex", 2D) = "black"{}
        // _MaxSearchSteps("max search steps", float) = 8   //this number is _AreaDistance/4;
        // _AreaDistance("area distance", float) = 32       //this number is _AreaTex.size/5;
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "Edge Detection"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #define MLAA_THRELOD 0.1

            #include "UnityCG.cginc"
            #include "Common.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            float Load(float2 uv, float2 offset)
            {
                float3 color = tex2D(_MainTex, uv + offset*_MainTex_TexelSize.xy).rgb;
                color = LinearToGammaSpace(color);
                return Luminance(color);
            }
            float Load(float2 uv)
            {
                float3 color = tex2D(_MainTex, uv).rgb;
                color = LinearToGammaSpace(color);
                return Luminance(color);
            }

            float4 frag(VaryingsDefault i) : SV_Target0
            {
                // threlod test
                float lumiCenter = Load(i.uv);
                float lumiUp = Load(i.uv, float2(0, -1));
                float lumiBottom = Load(i.uv, float2(0, 1));
                float lumiRight = Load(i.uv, float2(1, 0));
                float lumiLeft = Load(i.uv, float2(-1, 0));

                float4 delta = abs(lumiCenter.xxxx - float4(lumiLeft, lumiUp, lumiRight, lumiBottom));
                float4 edges = step(MLAA_THRELOD.xxxx, delta);

                if(dot(edges, 1.0) == 0.0)
                {
                    edges = 0;
                }

                edges.zw = 0;
                return edges;
            }
            ENDCG
        }

        Pass
        {
            Name "Blend Weight"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Common.cginc"

            // define
            #define _MaxSearchSteps 8
            #define _AreaDistance 32

            // property
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _AreaTex;

            float SearchXLeft ( float2 texcoord ) 
            {
                texcoord -= float2 ( 1.5 , 0.0 ) *  _MainTex_TexelSize.xy ;
                float e = 0.0;
                // We offset by 0.5 to sample between edges , thus fetching
                // two in a row.
                for ( int i = 0 ; i < _MaxSearchSteps ; i++) 
                {
                    e = tex2D(_MainTex, texcoord).g;
                    // We compare with 0.9 to prevent bilinear access precision
                    // problems.
                    if ( e < 0.9 ) break ;
                    texcoord -= float2 ( 2.0 , 0.0 )*  _MainTex_TexelSize.xy ;
                }
                // When we exit the loop without finding the end , we return
                // -2*_MaxSearchSteps.
                return max(-2.0 * i - 2.0 * e , -2.0 * _MaxSearchSteps ) ;
            }

            float SearchXRight ( float2 texcoord ) 
            {
                texcoord += float2 ( 1.5 , 0.0 ) *  _MainTex_TexelSize.xy ;
                float e = 0.0;
                for ( int i = 0 ; i < _MaxSearchSteps ; i++) 
                {
                    e = tex2D(_MainTex, texcoord).g;
                    if ( e < 0.9 ) break ;
                    texcoord += float2 ( 2.0 , 0.0 )*  _MainTex_TexelSize.xy ;
                }
                return min(2.0 * i + 2.0 * e , 2.0 * _MaxSearchSteps ) ;
            }

            float SearchYUp ( float2 texcoord ) 
            {
                texcoord -= float2 ( 0.0 , 1.5 ) *  _MainTex_TexelSize.xy ;
                float e = 0.0;
                for ( int i = 0 ; i < _MaxSearchSteps ; i++) 
                {
                    e = tex2D(_MainTex, texcoord).r;
                    if ( e < 0.9 ) break ;
                    texcoord -= float2 ( 0.0 , 2.0 )*  _MainTex_TexelSize.xy ;
                }
                return max(-2.0 * i - 2.0 * e , -2.0 * _MaxSearchSteps ) ;
            }

            float SearchYDown ( float2 texcoord ) 
            {
                texcoord += float2 ( 0.0 , 1.5 ) *  _MainTex_TexelSize.xy ;
                float e = 0.0;
                for ( int i = 0 ; i < _MaxSearchSteps ; i++) 
                {
                    e = tex2D(_MainTex, texcoord).r;
                    if ( e < 0.9 ) break ;
                    texcoord += float2 ( 0.0 , 2.0 )*  _MainTex_TexelSize.xy ;
                }
                return min(2.0 * i + 2.0 * e , 2.0 * _MaxSearchSteps ) ;
            }

            float2 Area(float2 distance, float e1, float e2)
            {
                float areaSize = _AreaDistance * 5;
                float2 pixcoord = _AreaDistance * round(4.0*float2(e1, e2)) + distance;
                float2 texcoord = pixcoord / (areaSize - 1.0);
                return tex2D(_AreaTex, texcoord).rg;
            }

            float4 frag(VaryingsDefault i) : SV_Target0
            {
                float4 weights = 0;
                float2 edge = tex2D(_MainTex, i.uv).rg;

                if(edge.g)  // edge at north
                {
                    float2 endEdge = float2(SearchXLeft(i.uv), SearchXRight(i.uv));

                    float4 coord = mad(float4(endEdge.x, -0.25, endEdge.y + 1.0, -0.25), _MainTex_TexelSize.xyxy, i.uv.xyxy);

                    float edge1 = tex2D(_MainTex, coord.xy).r;
                    float edge2 = tex2D(_MainTex, coord.zw).r;
                    
                    weights.rg = Area(abs(endEdge), edge1, edge2);
                }
                
                if(edge.r)  // edge at west
                {
                    float2 endEdge = float2(SearchYUp(i.uv), SearchYDown(i.uv));

                    float4 coord = mad(float4(-0.25, endEdge.x, -0.25, endEdge.y + 1.0), _MainTex_TexelSize.xyxy, i.uv.xyxy);

                    float edge1 = tex2D(_MainTex, coord.xy).g;
                    float edge2 = tex2D(_MainTex, coord.zw).g;
                    
                    weights.ba = Area(abs(endEdge), edge1, edge2);
                }
                return weights;
            }
            ENDCG
        }

        
        Pass
        {
            Name "Blend Process"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Common.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _BlendWeightTex;

            float4 Load(sampler2D _Tex, float2 uv)
            {
                float4 color = tex2D(_Tex, uv);
                return color;
            }

            float4 frag(VaryingsDefault i) : SV_Target0
            {
                float4 topLeft = tex2D(_BlendWeightTex, i.uv);
                float right = tex2D(_BlendWeightTex, i.uv + float2(0 , 1)*_MainTex_TexelSize.xy).g;
                float bottom = tex2D(_BlendWeightTex, i.uv + float2(1, 0)*_MainTex_TexelSize.xy).a;
                
                float4 a = float4(topLeft.r, right, topLeft.b, bottom);
                float sum = dot(a, 1.0);

                if(sum > 0.0)
                {
                    float4 o = a * _MainTex_TexelSize.yyxx;
                    float4 color = 0;
                    color = mad(Load(_MainTex, i.uv + float2(0, -o.r)), a.r, color);
                    color = mad(Load(_MainTex, i.uv + float2(0, o.g)), a.g, color);
                    color = mad(Load(_MainTex, i.uv + float2(-o.b, 0)), a.b, color);
                    color = mad(Load(_MainTex, i.uv + float2(o.a, 0)), a.a, color);
                    return color / sum;
                }
                else
                {
                    return tex2D(_MainTex, i.uv);
                }
            }
            ENDCG
        }

    }
}
