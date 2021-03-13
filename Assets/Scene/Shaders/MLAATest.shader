Shader "Hidden/AdvancedRTR/MLAATest"    //in fact, this is mlaa
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

        CGINCLUDE
        
        #define MLAA_THRELOD 0.1
        #define NUM_DISTANCES 32
        #define AREA_SIZE (NUM_DISTANCES * 5)
        #define _MaxSearchSteps 8
        
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        sampler2D _AreaTex;
        sampler2D _BlendWeightTex;

        float4 Load(sampler2D _Tex, float2 uv, float lod)
        {
            return tex2D(_Tex, uv);
        }

        float4 Load(sampler2D _Tex, float2 uv, float lod, float2 offset)
        {
            return tex2D(_Tex, uv + offset *_MainTex_TexelSize.xy);
        }

        float4 mad(float4 m, float4 a, float4 b) {
            return m * a + b;
        }

        float2 Area(float2 distance, float e1, float e2) {
            // * By dividing by AREA_SIZE - 1.0 below we are implicitely offsetting to
            //   always fall inside of a pixel
            // * Rounding prevents bilinear access precision problems
            float2 pixcoord = NUM_DISTANCES * round(4.0 * float2(e1, e2)) + distance;
            float2 texcoord = pixcoord / (AREA_SIZE - 1.0);
            return Load(_AreaTex, texcoord, 0).rg;
        }

        float4 EdgeDetectionPS(float4 position : SV_POSITION,
        float2 texcoord : TEXCOORD0) : SV_TARGET {
            float3 weights = float3(0.2126,0.7152, 0.0722);

            float L = dot(Load(_MainTex, texcoord, 0).rgb, weights);
            float Lleft = dot(Load(_MainTex, texcoord, 0, -float2(1, 0)).rgb, weights);
            float Ltop  = dot(Load(_MainTex, texcoord, 0, -float2(0, 1)).rgb, weights);
            float Lright = dot(Load(_MainTex, texcoord, 0, float2(1, 0)).rgb, weights);
            float Lbottom  = dot(Load(_MainTex, texcoord, 0, float2(0, 1)).rgb, weights);

            /**
            * We detect edges in gamma 1.0/2.0 space. Gamma space boosts the contrast
            * of the blacks, where the human vision system is more sensitive to small
            * gradations of intensity.
            */
            float4 delta = abs(sqrt(L).xxxx - sqrt(float4(Lleft, Ltop, Lright, Lbottom)));
            float4 edges = step(MLAA_THRELOD.xxxx, delta);

            if (dot(edges, 1.0) == 0.0) {
                discard;
            }

            return edges;
        }

        float SearchXLeft(float2 texcoord) {
            texcoord -= float2(1.5, 0.0) * _MainTex_TexelSize;
            float e = 0.0;
            // We offset by 0.5 to sample between edgels, thus fetching two in a row
            for (int i = 0; i < _MaxSearchSteps; i++) {
                e = Load(_MainTex, texcoord, 0).g;
                // We compare with 0.9 to prevent bilinear access precision problems
                [flatten] if (e < 0.9) break;
                texcoord -= float2(2.0, 0.0) * _MainTex_TexelSize;
            }
            // When we exit the loop without founding the end, we want to return
            // -2 * _MaxSearchSteps
            return max(-2.0 * i - 2.0 * e, -2.0 * _MaxSearchSteps);
        }

        float SearchXRight(float2 texcoord) {
            texcoord += float2(1.5, 0.0) * _MainTex_TexelSize;
            float e = 0.0;
            for (int i = 0; i < _MaxSearchSteps; i++) {
                e = Load(_MainTex, texcoord, 0).g;
                [flatten] if (e < 0.9) break;
                texcoord += float2(2.0, 0.0) * _MainTex_TexelSize;
            }
            return min(2.0 * i + 2.0 * e, 2.0 * _MaxSearchSteps);
        }

        float SearchYUp(float2 texcoord) {
            texcoord -= float2(0.0, 1.5) * _MainTex_TexelSize;
            float e = 0.0;
            for (int i = 0; i < _MaxSearchSteps; i++) {
                e = Load(_MainTex, texcoord, 0).r;
                [flatten] if (e < 0.9) break;
                texcoord -= float2(0.0, 2.0) * _MainTex_TexelSize;
            }
            return max(-2.0 * i - 2.0 * e, -2.0 * _MaxSearchSteps);
        }

        float SearchYDown(float2 texcoord) {
            texcoord += float2(0.0, 1.5) * _MainTex_TexelSize;
            float e = 0.0;
            for (int i = 0; i < _MaxSearchSteps; i++) {
                e = Load(_MainTex, texcoord, 0).r;
                [flatten] if (e < 0.9) break;
                texcoord += float2(0.0, 2.0) * _MainTex_TexelSize;
            }
            return min(2.0 * i + 2.0 * e, 2.0 * _MaxSearchSteps);
        }

        float4 BlendingWeightCalculationPS(float4 position : SV_POSITION,
        float2 texcoord : TEXCOORD0) : SV_TARGET {
            float4 weights = 0.0;

            float2 e = Load(_MainTex, texcoord, 0).rg;

            [branch]
            if (e.g) { // Edge at north
                float2 d = float2(SearchXLeft(texcoord), SearchXRight(texcoord));
                
                // Instead of sampling between edgels, we sample at -0.25,
                // to be able to discern what value each edgel has.
                float4 coords = mad(float4(d.x, -0.25, d.y + 1.0, -0.25),
                _MainTex_TexelSize.xyxy, texcoord.xyxy);
                float e1 = Load(_MainTex, coords.xy, 0).r;
                float e2 = Load(_MainTex, coords.zw, 0).r;
                weights.rg = Area(abs(d), e1, e2);
            }

            [branch]
            if (e.r) { // Edge at west
                float2 d = float2(SearchYUp(texcoord), SearchYDown(texcoord));

                float4 coords = mad(float4(-0.25, d.x, -0.25, d.y + 1.0),
                _MainTex_TexelSize.xyxy, texcoord.xyxy);
                float e1 = Load(_MainTex, coords.xy, 0).g;
                float e2 = Load(_MainTex, coords.zw, 0).g;
                weights.ba = Area(abs(d), e1, e2);
            }

            return weights;
        }

        float4 NeighborhoodBlendingPS(float4 position : SV_POSITION,
        float2 texcoord : TEXCOORD0) : SV_TARGET {
            float4 topLeft = Load(_BlendWeightTex, texcoord, 0);
            float right = Load(_BlendWeightTex, texcoord, 0, float2(0, 1)).g;
            float bottom = Load(_BlendWeightTex, texcoord, 0, float2(1, 0)).a;
            float4 a = float4(topLeft.r, right, topLeft.b, bottom);

            float sum = dot(a, 1.0);

            [branch]
            if (sum > 0.0) {
                float4 o = a * _MainTex_TexelSize.yyxx;
                float4 color = 0.0;
                color = mad(Load(_MainTex, texcoord + float2( 0.0, -o.r), 0), a.r, color);
                color = mad(Load(_MainTex, texcoord + float2( 0.0,  o.g), 0), a.g, color);
                color = mad(Load(_MainTex, texcoord + float2(-o.b,  0.0), 0), a.b, color);
                color = mad(Load(_MainTex, texcoord + float2( o.a,  0.0), 0), a.a, color);
                return color / sum;
                } else {
                return Load(_MainTex, texcoord, 0);
            }
        }

        ENDCG

        Pass
        {
            Name "Edge Detection"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment EdgeDetectionPS

            #include "UnityCG.cginc"
            #include "Common.cginc"
            ENDCG
        }

        
        Pass
        {
            Name "Blend Weight"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment BlendingWeightCalculationPS

            #include "UnityCG.cginc"
            #include "Common.cginc"
            
            ENDCG
        }

        
        Pass
        {
            Name "Blend Process"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment NeighborhoodBlendingPS

            #include "UnityCG.cginc"
            #include "Common.cginc"
            ENDCG
        }
    }
}
