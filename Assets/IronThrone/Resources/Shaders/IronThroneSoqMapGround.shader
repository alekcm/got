// IronThrone: рельеф карты приключений Songs of Conquest (Built-in RP).
// 16 классов рельефа в Texture2DArray + веса из четырёх control-текстур.
// Веса считаются билинейно между центрами тайлов, поэтому границы биомов
// получаются мягкими и « organic», как в SoQ, а не клетчатыми.
// Плюс карта берега: у воды подмешивается мокрый песок, у суши — пена.
Shader "IronThrone/SoqMapGround"
{
    Properties
    {
        _ControlA ("Classes 0-3", 2D) = "black" {}
        _ControlB ("Classes 4-7", 2D) = "black" {}
        _ControlC ("Classes 8-11", 2D) = "black" {}
        _ControlD ("Classes 12-15", 2D) = "black" {}
        _Coast ("Coast (R land, G distance)", 2D) = "white" {}
        [NoScaleOffset] _TerrainArray ("Terrain (16 layers)", 2DArray) = "" {}
        _Brightness ("Brightness", Float) = 1
        _SandColor ("Shore color", Color) = (0.72, 0.66, 0.45, 1)
        _WaterColor ("Seabed color", Color) = (0.10, 0.22, 0.28, 1)
        _ShoreWidth ("Shore width", Range(0.01, 1)) = 0.45
        _TileScale ("Texture repeats per map", Float) = 14
        _MacroTex ("Macro variation", 2D) = "gray" {}
        _DetailTex ("Fine grain", 2D) = "gray" {}
        _MacroScale ("Macro repeats", Float) = 8
        _DetailScale ("Fine repeats", Float) = 70
        _MacroStrength ("Macro strength", Range(0, 0.4)) = 0.10
        _DetailStrength ("Fine strength", Range(0, 0.3)) = 0.07
        _ForestShade ("Forest darkening", Range(0, 0.5)) = 0.30
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
            sampler2D _Coast;
            UNITY_DECLARE_TEX2DARRAY(_TerrainArray);
            float _Brightness;
            float4 _SandColor;
            float4 _WaterColor;
            float _ShoreWidth;
            float _TileScale;
            sampler2D _MacroTex;
            sampler2D _DetailTex;
            float _MacroScale;
            float _DetailScale;
            float _MacroStrength;
            float _DetailStrength;
            float _ForestShade;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 mapUV : TEXCOORD0;
                float2 tileUV : TEXCOORD1;
                float2 macroUV : TEXCOORD2;
                float2 detailUV : TEXCOORD3;
                UNITY_FOG_COORDS(4)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.mapUV = v.uv;
                // фактура тайла привязана к сетке мира и повторяется каждые
                // _TileScale карты: соседние тайлы продолжают рисунок, а не
                // начинают его заново (иначе карта выглядит лоскутным одеялом)
                o.tileUV = v.uv * _TileScale;
                // два дополнительных масштаба: крупные пятна света/тени и мелкое
                // зерно. Они не дают земле выглядеть однотонной заливкой ни
                // вблизи, ни при взгляде на карту целиком — так же, как в SoQ,
                // где поверх тайлов лежит ещё и общая «погода» карты
                o.macroUV = v.uv * _MacroScale;
                o.detailUV = v.uv * _DetailScale;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float3 Layer(float2 uv, int index)
            {
                return UNITY_SAMPLE_TEX2DARRAY(_TerrainArray, float3(uv, index)).rgb;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 ca = tex2D(_ControlA, i.mapUV);
                float4 cb = tex2D(_ControlB, i.mapUV);
                float4 cc = tex2D(_ControlC, i.mapUV);
                float4 cd = tex2D(_ControlD, i.mapUV);

                float sum = ca.r + ca.g + ca.b + ca.a
                          + cb.r + cb.g + cb.b + cb.a
                          + cc.r + cc.g + cc.b + cc.a
                          + cd.r + cd.g + cd.b + cd.a;
                if (sum < 0.001)
                {
                    // вне островов: дно
                    fixed4 sea = fixed4(_WaterColor.rgb, 1);
                    UNITY_APPLY_FOG(i.fogCoord, sea);
                    return sea;
                }
                float inv = 1.0 / sum;

                // четыре самых «весомых» слоя берём целиком, остальные тоже —
                // 16 выборок на пиксель для карты 96x96 это нормально
                float3 col = 0;
                col += ca.r * Layer(i.tileUV, 0);
                col += ca.g * Layer(i.tileUV, 1);
                col += ca.b * Layer(i.tileUV, 2);
                col += ca.a * Layer(i.tileUV, 3);
                col += cb.r * Layer(i.tileUV, 4);
                col += cb.g * Layer(i.tileUV, 5);
                col += cb.b * Layer(i.tileUV, 6);
                col += cb.a * Layer(i.tileUV, 7);
                col += cc.r * Layer(i.tileUV, 8);
                col += cc.g * Layer(i.tileUV, 9);
                col += cc.b * Layer(i.tileUV, 10);
                col += cc.a * Layer(i.tileUV, 11);
                col += cd.r * Layer(i.tileUV, 12);
                col += cd.g * Layer(i.tileUV, 13);
                col += cd.b * Layer(i.tileUV, 14);
                col += cd.a * Layer(i.tileUV, 15);
                col *= inv;

                // рисунок земли: крупные пятна + мелкое зерно. Яркостная
                // модуляция, а не подмешивание цвета, — оттенок класса не уезжает
                float macro = tex2D(_MacroTex, i.macroUV).r;
                col *= 1.0 + (macro - 0.5) * 2.0 * _MacroStrength;
                float detail = tex2D(_DetailTex, i.detailUV).r;
                col *= 1.0 + (detail - 0.5) * 2.0 * _DetailStrength;
                // под кронами темнее: в SoQ лес «притеняет» землю вокруг
                float forest = saturate((cd.g + cd.a) * inv);
                col *= 1.0 - forest * _ForestShade;

                // берег: у самой воды подмешиваем мокрый песок, ниже — дно.
                // поле берега сглаженное (0.5 — линия воды), поэтому край мягкий
                float4 coast = tex2D(_Coast, i.mapUV);
                float land = coast.r;
                float shore = saturate((land - 0.42) / 0.16) * saturate((0.62 - land) / 0.12);
                col = lerp(col, _SandColor.rgb * (0.75 + col * 0.55), shore * 0.6);
                float seabed = saturate((0.47 - land) / 0.30);
                col = lerp(col, _WaterColor.rgb, seabed);
                // в воде подмешиваем рисунок дна, чтобы он не был плоским пятном
                if (land < 0.47)
                {
                    float3 floorTex = Layer(float2(i.mapUV.x * 22.0, i.mapUV.y * 22.0), 5);
                    col = lerp(col, col * (0.55 + floorTex * 0.9), saturate((0.47 - land) * 3.0) * 0.35);
                }

                col *= _Brightness;
                fixed4 outc = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, outc);
                return outc;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
