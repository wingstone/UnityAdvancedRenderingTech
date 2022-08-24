Shader "ARP/TerrainShader"
{
    Properties
    {
        _MainTex ("Main Tex", 2D) = "white" {}
        _HightMap ("Height Map", 2D) = "white" {}
        _Hight ("Height", Range(1, 1000)) = 100
        _Lod ("Lod ", Float) = 0
        _LodOther ("Lod Offset", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM

            #pragma multi_compile_instancing
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint vid : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float3 color : TEXCOORD1;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _HightMap;
            float _Hight;
            
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _LodOther)
                UNITY_DEFINE_INSTANCED_PROP(float, _Lod)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert (appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);

                v2f o = (v2f)0;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.uv = worldPos.xz/2048 + 0.5;

                int lod = (int)(UNITY_ACCESS_INSTANCED_PROP(Props, _Lod)+0.5);
                float4 lodOther = UNITY_ACCESS_INSTANCED_PROP(Props, _LodOther);
                int leftLod = (int)(lodOther.x+0.5);
                int rightLod = (int)(lodOther.y+0.5);
                int bottomLod = (int)(lodOther.z+0.5);
                int upLod = (int)(lodOther.w+0.5);

                float uvExtent = 1/pow(2, lod)/16;
                float extent = 2048/pow(2, lod)/16;

                o.color = lod/7.0f;

                if(leftLod < lod && v.vid <= 16) 
                {
                    int mod = (v.vid)%(pow(2, lod-leftLod));
                    if( mod !=0)
                    {
                        worldPos.z += extent*mod;
                        o.uv.y += uvExtent*mod;
                    } 
                    o.color = 1;
                }

                if(rightLod < lod && v.vid >= 17*16) 
                {
                    int mod = (v.vid-17*16)%(pow(2, lod-rightLod));
                    if( mod !=0)
                    {
                        worldPos.z -= extent*mod;
                        o.uv.y -= uvExtent*mod;
                    }
                    o.color = 1;
                }

                if(bottomLod < lod && v.vid%17 <= 0) 
                {
                    int mod = (v.vid/17)%(pow(2, lod-bottomLod));
                    if( mod !=0)
                    {
                        worldPos.x += extent*mod;
                        o.uv.x += uvExtent*mod;
                    }
                    o.color = 1;
                }

                if(upLod < lod && v.vid%17 >= 16) 
                {
                    int mod = (v.vid/17)%(pow(2, lod-upLod));
                    if( mod !=0)
                    {
                        worldPos.x -= extent*mod;
                        o.uv.x -= uvExtent*mod;
                    }
                    o.color = 1;
                }


                worldPos.y = tex2Dlod(_HightMap, float4(o.uv, 0, 0)).x*_Hight;
                o.vertex = UnityWorldToClipPos(worldPos);
                // o.color = (float)v.vid/256;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                // sample the texture
                float4 col = tex2D(_MainTex, i.uv);
                col.rgb = i.color;

                return col;
            }
            ENDCG
        }
    }
}
