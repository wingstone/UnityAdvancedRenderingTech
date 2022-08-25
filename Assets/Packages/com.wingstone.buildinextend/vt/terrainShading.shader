Shader "ARP/terrainShading"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _PhysicalTex ("PhysicalTex", 2D) = "white" {}
        _PageTable ("PageTable", 2D) = "white" {}
        _CameraUV("Camera UV", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _PhysicalTex;
            sampler2D _PageTable;
            float4 _CameraUV;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uvoffset = i.uv - _CameraUV.xy;
                float len = max(uvoffset.x, uvoffset.y);
                float lod = log2(len*32);
                float4 pageTexel = tex2D(_PageTable, i.uv, lod);

                fixed4 col = tex2D(_MainTex, i.uv);
                return col;
            }
            ENDCG
        }
    }
}
