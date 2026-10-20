// IronThrone: рельеф глобальной карты (Built-in RP, CG).
// Сплейтинг: две control-текстуры (A = слои 0-3, B = слои 4-7),
// индексы слоёв в _LayersA/_LayersB. Рельеф из Texture2DArray (128x128 слои).
Shader "IronThrone/CartographyTerrain"
{
    Properties
    {
        _ControlA ("Splat A (слои 0-3)", 2D) = "black" {}
        _ControlB ("Splat B (слои 4-7)", 2D) = "black" {}
        _TerrainArray ("Terrain Texture2DArray", 2DArray) = "" {}
        _LayersA ("Layer Indices A", Vector) = (0, 1, 2, 3)
        _LayersB ("Layer Indices B", Vector) = (4, 5, 6, 7)
        _Tiling ("UV Tiling", Float) = 0.15
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _MapOrigin ("Map Origin (x,z, width, height)", Vector) = (0, 0, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _ControlA;
            sampler2D _ControlB;
            UNITY_DECLARE_TEX2DARRAY(_TerrainArray);
            float4 _LayersA;
            float4 _LayersB;
            float _Tiling;
            fixed4 _Tint;
            float4 _MapOrigin; // x,z = origin, z,w = size

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 worldUV : TEXCOORD0;   // UV карты для control-текстур
                float2 texUV : TEXCOORD1;     // UV тайлов рельефа
                float height : TEXCOORD2;     // высота вершины (подсветка)
                UNITY_FOG_COORDS(3)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldUV = float2((wp.x - _MapOrigin.x) / _MapOrigin.z,
                                   (wp.z - _MapOrigin.y) / _MapOrigin.w);
                o.texUV = wp.xz * _Tiling;
                o.height = wp.y;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 sampleLayer(float layerIndex, float2 uv)
            {
                return UNITY_SAMPLE_TEX2DARRAY(_TerrainArray, float3(uv, layerIndex));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 wa = tex2D(_ControlA, i.worldUV);
                float4 wb = tex2D(_ControlB, i.worldUV);

                fixed4 col = fixed4(0, 0, 0, 1);
                float wsum = 0;
                float4 idxA = _LayersA;
                float4 idxB = _LayersB;

                col += sampleLayer(idxA.x, i.texUV) * wa.x; wsum += wa.x;
                col += sampleLayer(idxA.y, i.texUV) * wa.y; wsum += wa.y;
                col += sampleLayer(idxA.z, i.texUV) * wa.z; wsum += wa.z;
                col += sampleLayer(idxA.w, i.texUV) * wa.w; wsum += wa.w;
                col += sampleLayer(idxB.x, i.texUV) * wb.x; wsum += wb.x;
                col += sampleLayer(idxB.y, i.texUV) * wb.y; wsum += wb.y;
                col += sampleLayer(idxB.z, i.texUV) * wb.z; wsum += wb.z;
                col += sampleLayer(idxB.w, i.texUV) * wb.w; wsum += wb.w;

                // морское дно: там, где весов нет
                fixed4 seabed = fixed4(0.10, 0.16, 0.18, 1);
                col = lerp(seabed, col / max(wsum, 0.0001), saturate(wsum * 4));

                // свет: солнце + мягкая подсветка высот
                float3 sun = normalize(float3(0.35, 0.8, 0.45));
                float ndl = 0.82 + 0.18 * sun.y;
                float heightLight = saturate(i.height * 0.35);
                col.rgb *= _Tint.rgb * (ndl + heightLight * 0.12);

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
