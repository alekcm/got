// IronThrone: вода карты приключений (Built-in RP).
// Два слоя (глубина/отмель) со встречным движением UV — спокойная «картографическая» вода.
Shader "IronThrone/SoqMapWater"
{
    Properties
    {
        _ShallowTex ("Shallow", 2D) = "white" {}
        _DeepTex ("Deep", 2D) = "white" {}
        _Color ("Water color", Color) = (0.16, 0.32, 0.42, 0.92)
        _Tiling ("Tiling", Float) = 0.25
        _Speed ("Speed", Float) = 0.02
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
            float4 _Color;
            float _Tiling;
            float _Speed;

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
                float t = _Time.y * _Speed;
                fixed4 shallow = tex2D(_ShallowTex, i.uv * _Tiling + float2(t, t * 0.6));
                fixed4 deep = tex2D(_DeepTex, i.uv * _Tiling * 0.63 - float2(t * 0.7, t * 0.35));
                fixed4 col = lerp(deep, shallow, 0.55) * _Color;
                col.a = _Color.a;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    Fallback "Unlit/Transparent"
}
