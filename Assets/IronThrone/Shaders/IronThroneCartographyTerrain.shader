// IronThrone: террейн глобальной карты (Built-in RP, CG), версия 2.
// 16 биом-классов: веса из 4 control-текстур (RGBA x4), тайлы — Texture2DArray
// из Resources/Art/Terrain (собирается в рантайме, без битого импорта ленты).
// Региональный tint + шум из _TintNoise, освещение через light-ramp игры.
Shader "IronThrone/CartographyTerrain"
{
    Properties
    {
        _ControlA ("Classes 0-3 Weights", 2D) = "white" {}
        _ControlB ("Classes 4-7 Weights", 2D) = "white" {}
        _ControlC ("Classes 8-11 Weights", 2D) = "white" {}
        _ControlD ("Classes 12-15 Weights", 2D) = "white" {}
        _TintNoise ("Tint RGB + Noise A", 2D) = "white" {}
        [NoScaleOffset] _TerrainArray ("Terrain Tiles (16 layers)", 2DArray) = "" {}
        _LightRamp ("Light Ramp", 2D) = "white" {}
        _TileWorld ("World Units Per Tile", Float) = 7
        _MapOrigin ("Map Origin (x,z,width,height)", Vector) = (0, 0, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _ControlA;
            sampler2D _ControlB;
            sampler2D _ControlC;
            sampler2D _ControlD;
            sampler2D _TintNoise;
            sampler2D _LightRamp;
            UNITY_DECLARE_TEX2DARRAY(_TerrainArray);
            float _TileWorld;
            float4 _MapOrigin;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 worldUV : TEXCOORD0;
                float2 worldXZ : TEXCOORD1;
                float3 normal : TEXCOORD2;
                UNITY_FOG_COORDS(3)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldUV = float2((wp.x - _MapOrigin.x) / _MapOrigin.z,
                                   (wp.z - _MapOrigin.y) / _MapOrigin.w);
                o.worldXZ = wp.xz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float3 SampleLayer(float2 uvw, int layer)
            {
                return UNITY_SAMPLE_TEX2DARRAY(_TerrainArray, float3(uvw, layer)).rgb;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 ca = tex2D(_ControlA, i.worldUV);
                float4 cb = tex2D(_ControlB, i.worldUV);
                float4 cc = tex2D(_ControlC, i.worldUV);
                float4 cd = tex2D(_ControlD, i.worldUV);
                float2 uvw = i.worldXZ / _TileWorld;

                float3 col = float3(0, 0, 0);
                col += ca.r * SampleLayer(uvw, 0);
                col += ca.g * SampleLayer(uvw, 1);
                col += ca.b * SampleLayer(uvw, 2);
                col += ca.a * SampleLayer(uvw, 3);
                col += cb.r * SampleLayer(uvw, 4);
                col += cb.g * SampleLayer(uvw, 5);
                col += cb.b * SampleLayer(uvw, 6);
                col += cb.a * SampleLayer(uvw, 7);
                col += cc.r * SampleLayer(uvw, 8);
                col += cc.g * SampleLayer(uvw, 9);
                col += cc.b * SampleLayer(uvw, 10);
                col += cc.a * SampleLayer(uvw, 11);
                col += cd.r * SampleLayer(uvw, 12);
                col += cd.g * SampleLayer(uvw, 13);
                col += cd.b * SampleLayer(uvw, 14);
                col += cd.a * SampleLayer(uvw, 15);

                // региональный оттенок + шумовая яркость
                float4 tn = tex2D(_TintNoise, i.worldUV);
                col *= tn.rgb * (0.92 + 0.16 * tn.a);

                // свет через light-ramp игры: N·L -> тёплые вершины, тени холодные
                float3 n = normalize(i.normal);
                float ndl = saturate(dot(n, normalize(float3(0.35, 0.85, 0.4))));
                float3 light = tex2D(_LightRamp, float2(ndl, 0.5)).rgb * 1.9;
                col *= light;

                fixed4 outc = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, outc);
                return outc;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
