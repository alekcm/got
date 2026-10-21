// IronThrone: вода карты приключений Songs of Conquest (Built-in RP).
// Плоскость на уровне моря; край воды берётся из карты берега (R = суша),
// поэтому берег получается органическим, а не квадратным. Глубина — по G
// (расстояние до суши): у берега светлая полоса и барашки, дальше — тёмная вода.
Shader "IronThrone/SoqMapWater"
{
    Properties
    {
        _ShallowTex ("Shallow", 2D) = "white" {}
        _DeepTex ("Deep", 2D) = "white" {}
        _Coast ("Coast (R land, G distance)", 2D) = "white" {}
        _Color ("Water color", Color) = (0.15, 0.34, 0.44, 0.90)
        _ShallowColor ("Shallow color", Color) = (0.30, 0.58, 0.60, 1)
        _FoamColor ("Foam color", Color) = (0.78, 0.88, 0.90, 1)
        _Tiling ("Tiling", Float) = 0.06
        _Speed ("Speed", Float) = 0.012
        _FoamWidth ("Foam width", Range(0.001, 0.4)) = 0.06
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _ShallowTex;
            sampler2D _DeepTex;
            sampler2D _Coast;
            float4 _Color;
            float4 _ShallowColor;
            float4 _FoamColor;
            float _Tiling;
            float _Speed;
            float _FoamWidth;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 coast = tex2D(_Coast, i.uv);
                float land = coast.r;      // 1 = суша, 0 = открытая вода
                float depth = coast.g;     // 0 у берега, 1 вдали

                float t = _Time.y * _Speed;
                fixed4 shallow = tex2D(_ShallowTex, i.uv * _Tiling + float2(t, t * 0.6));
                fixed4 deep = tex2D(_DeepTex, i.uv * _Tiling * 0.63 - float2(t * 0.7, t * 0.35));

                float3 col = lerp(_ShallowColor.rgb, _Color.rgb, saturate(depth * 1.25));
                col *= lerp(shallow.rgb, deep.rgb, saturate(depth)) * 1.7;

                // пена: узкая полоса вдоль берега + бегущие «барашки»
                float foam = saturate(1.0 - abs(land - 0.46) / _FoamWidth);
                foam *= 0.55 + 0.45 * sin(i.uv.x * 90.0 + t * 55.0);
                col = lerp(col, _FoamColor.rgb, saturate(foam) * 0.75);

                // альфа: воды нет там, где суша; край мягкий — по сглаженному полю берега
                float alpha = saturate((0.5 - land) * 2.2) * _Color.a;
                alpha = max(alpha, saturate(foam) * 0.85 * saturate(1.0 - land));

                fixed4 outc = fixed4(col, alpha);
                UNITY_APPLY_FOG(i.fogCoord, outc);
                return outc;
            }
            ENDCG
        }
    }
    Fallback "Unlit/Transparent"
}
