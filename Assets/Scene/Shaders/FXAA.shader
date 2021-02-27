Shader "Hidden/AdvancedRTR/FXAA"
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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Common.cginc"

            #define FXAA_THRELOD_MIN 0.0312
            #define FXAA_THRELOD_MAX 0.125
            #define FXAA_ITERATION 5
            #define FXAA_SUBPIXEL_QUALITY 0.75

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            half Load(half2 uv, half2 offset)
            {
                half3 color = tex2D(_MainTex, uv + offset*_MainTex_TexelSize.xy).rgb;
                return Luminance(color);
            }
            half Load(half2 uv)
            {
                half3 color = tex2D(_MainTex, uv).rgb;
                return Luminance(color);
            }

            half4 frag(VaryingsDefault i) : SV_Target0
            {
                // threlod test

                half4 mainColor = tex2D(_MainTex, i.uv);
                half3 color = mainColor.rgb;
                half lumiCenter = Luminance(color);

                half lumiUp = Load(i.uv, half2(0, 1));
                half lumiDown = Load(i.uv, half2(0, -1));
                half lumiRight = Load(i.uv, half2(1, 0));
                half lumiLeft = Load(i.uv, half2(-1, 0));

                half maxLumi = Max5(lumiCenter, lumiUp, lumiDown, lumiRight, lumiLeft);
                half minLumi = Min5(lumiCenter, lumiUp, lumiDown, lumiRight, lumiLeft);

                half lumiDelta = maxLumi - minLumi;

                if(lumiDelta < max(FXAA_THRELOD_MIN, maxLumi * FXAA_THRELOD_MAX))
                {
                    return mainColor;
                }

                // calculate main direction

                half lumiUpLeft = Load(i.uv, half2(-1, 1));
                half lumiUpRight = Load(i.uv, half2(1, 1));
                half lumiDownLeft = Load(i.uv, half2(-1, -1));
                half lumiDownRight = Load(i.uv, half2(1, -1));

                half lumiUpDowmSum = lumiUp + lumiDown;
                half lumiLeftRightSum = lumiLeft + lumiRight;

                half lumiLeftCornerSum = lumiUpLeft + lumiDownLeft;
                half lumiRightCornerSum = lumiUpRight + lumiDownRight;
                half lumiUpCornerSum = lumiUpLeft + lumiUpRight;
                half lumiDownCornerSum = lumiDownLeft + lumiDownRight;
                
                half edgeHorizontal = abs(-2.0*lumiLeft + lumiLeftCornerSum)
                + abs(-2.0*lumiCenter + lumiUpDowmSum)*2.0 
                + abs(-2.0*lumiRight + lumiRightCornerSum);
                half edgeVertical = abs(-2.0*lumiUp + lumiUpCornerSum)
                + abs(-2.0*lumiCenter + lumiLeftRightSum)*2.0 
                + abs(-2.0*lumiDown + lumiDownCornerSum);

                bool isHorizontal = edgeHorizontal > edgeVertical;

                // search the edge border

                half lumi1 = isHorizontal ? lumiDown : lumiLeft;
                half lumi2 = isHorizontal ? lumiUp : lumiRight;

                half gradient1 = lumi1 - lumiCenter;
                half gradient2 = lumi2 - lumiCenter;

                bool is_1_Steepest = abs(gradient1) >= abs(gradient2);

                half gradientScaled = 0.25*max(abs(gradient1), abs(gradient2));     //why use 0.25 to normalize

                half stepLength = isHorizontal ? _MainTex_TexelSize.y : _MainTex_TexelSize.x;

                half lumiLocalAverage = 0;  //Edge border average
                if(is_1_Steepest)
                {
                    stepLength = -stepLength;
                    lumiLocalAverage = 0.5*(lumi1 + lumiCenter);
                }
                else
                {
                    lumiLocalAverage = 0.5*(lumi2 + lumiCenter);
                }

                half2 edgeBorderUV = i.uv;
                if(isHorizontal)
                {
                    edgeBorderUV.y += stepLength*0.5;
                }
                else
                {
                    edgeBorderUV.x += stepLength*0.5;
                }

                // find the ending along the edge

                // first finding 
                half2 offset = isHorizontal ? half2(_MainTex_TexelSize.x, 0) : half2(0, _MainTex_TexelSize.y);
                half2 uv1 = edgeBorderUV - offset;
                half2 uv2 = edgeBorderUV + offset;

                half lumiEnd1 = Load(uv1);
                half lumiEnd2 = Load(uv2);

                lumiEnd1 -= lumiLocalAverage;
                lumiEnd2 -= lumiLocalAverage;

                bool reached1 = abs(lumiEnd1) >= gradientScaled;
                bool reached2 = abs(lumiEnd2) >= gradientScaled;
                bool reachedBoth = reached1 && reached2;

                if(!reached1)
                {
                    uv1 -= offset;
                }

                if(!reached2)
                {
                    uv2 += offset;
                }

                // iterate find
                if(!reachedBoth)
                {
                    for(int i =2; i < FXAA_ITERATION; i++ )
                    {
                        if(!reached1)
                        {
                            lumiEnd1 = Load(uv1);
                            lumiEnd1 -= lumiLocalAverage;
                        }

                        if(!reached2)
                        {
                            lumiEnd2 = Load(uv2);
                            lumiEnd2 -= lumiLocalAverage;
                        }

                        reached1 = abs(lumiEnd1) >= gradientScaled;
                        reached2 = abs(lumiEnd2) >= gradientScaled;
                        reachedBoth = reached1 && reached2;

                        if(!reached1)
                        {
                            uv1 -= offset;
                        }
                        
                        if(!reached2)
                        {
                            uv2 += offset;
                        }

                        if( reachedBoth )
                        {
                            break;
                        } 
                    }
                }

                // Estimating offset, using near end point

                float distance1 = isHorizontal ? (i.uv.x - uv1.x) : (i.uv.y - uv1.y);
                float distance2 = isHorizontal ? (uv2.x - i.uv.x) : (uv2.y - i.uv.y);

                bool isDirection1 = distance1 < distance2;
                half distanceFinal = min(distance1, distance2);

                half edgeLength = distance1 + distance2;
                half pixelOffset = -distanceFinal/edgeLength + 0.5;

                // check the ending poing is coherent with curent

                bool isLumiCenterSmaller = lumiCenter < lumiLocalAverage;

                bool correctVariation = ((isDirection1 ? lumiEnd1 : lumiEnd2) < 0.0)  != isLumiCenterSmaller;

                half finalOffset = correctVariation ? pixelOffset : 0;

                // subpixel antialiasing

                half lumiAverage = (1.0/12.0)*(2.0*(lumiUpDowmSum + lumiLeftRightSum) + lumiLeftCornerSum + lumiRightCornerSum);
                half subPixelOffset1 = clamp(abs(lumiAverage - lumiCenter)/lumiDelta, 0.0, 1.0);
                half subPixelOffset2 = (-2.0*subPixelOffset1 + 3.0)*subPixelOffset1 * subPixelOffset1;
                half subPixelOffsetFinal = subPixelOffset2*subPixelOffset2*FXAA_SUBPIXEL_QUALITY;

                finalOffset = max(finalOffset, subPixelOffsetFinal);

                //final read
                half2 finaluv = i.uv;
                // half2 finaluv = 0;
                if(isHorizontal)
                {
                    finaluv.y += finalOffset * stepLength;
                }
                else    
                {
                    finaluv.x += finalOffset * stepLength;
                }

                // return half4(finaluv, 0, 1);

                return tex2D(_MainTex, finaluv);
            }
            ENDCG
        }
    }
}
