// IronThrone: спрайты карты приключений в стиле Songs of Conquest (Built-in RP).
// Один шейдер на землю, дороги, границы регионов и стенди-объекты:
//   * земля/дороги/границы — плоские квады с вертексным цветом (ramp выключен);
//   * объекты — наклонные квады с alpha-тестом и мягким ramp-освещением.
// Лежит в Resources/Shaders, поэтому попадает в билд без Always Included Shaders.
Shader "IronThrone/SoqMapSprite"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.01
        [NoScaleOffset] _LightRamp ("Light Ramp", 2D) = "white" {}
        _UseRamp ("Use light ramp", Range(0, 1)) = 0
        _RampStrength ("Ramp strength", Range(0, 2)) = 1
        _RampFlat ("Ramp flatten", Range(0, 1)) = 0.25
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
            float4 _Color;
            float _Cutoff;
            float _UseRamp;
            float _RampStrength;
            float _RampFlat;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_FOG_COORDS(1)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;
                clip(col.a - _Cutoff);

                if (_UseRamp > 0.5)
                {
                    float3 ramp = tex2D(_LightRamp, float2(saturate(i.uv.y * 0.85 + 0.15), 0.5)).rgb * 1.75;
                    col.rgb *= lerp(ramp * _RampStrength, float3(1, 1, 1), _RampFlat);
                }

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    Fallback "Unlit/Transparent Cutout"
}
