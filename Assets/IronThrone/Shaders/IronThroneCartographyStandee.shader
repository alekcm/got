// IronThrone: стендee-спрайты глобальной карты (Built-in RP, CG), версия 2.
// Наклонные квады (52°) с текстурой атласа; освещение — light-ramp игры
// (снизу тень, сверху тёплый свет). Свойства с именами Shader Graph игры:
// FadeTintColor (Color_daefe717...), Self Illumination (Vector1_F645C8DF).
Shader "IronThrone/CartographyStandee"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Sprite", 2D) = "white" {}
        _LightRamp ("Light Ramp", 2D) = "white" {}
        Color_daefe717fd214acebc87c6e09bd36812 ("FadeTintColor", Vector) = (1, 1, 1, 1)
        Vector1_F645C8DF ("Self Illumination", Range(0, 1)) = 0.22
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        LOD 100
        Cull Off
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _LightRamp;
            float4 Color_daefe717fd214acebc87c6e09bd36812;
            float Vector1_F645C8DF;
            float _Cutoff;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                clip(col.a - _Cutoff);
                col *= Color_daefe717fd214acebc87c6e09bd36812;

                // ramp по вертикали спрайта: низ — мягкая тень, верх — тёплый свет
                float3 ramp = tex2D(_LightRamp, float2(saturate(i.uv.y * 0.85 + 0.15), 0.5)).rgb * 1.75;
                col.rgb *= lerp(ramp, float3(1, 1, 1), saturate(Vector1_F645C8DF));

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    Fallback "Transparent/Cutout/VertexLit"
}
