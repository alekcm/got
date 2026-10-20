// IronThrone: вода глобальной карты (Built-in RP, CG).
// Два бегущих слоя поверхности (мелководье/глубина) + пена у берегов по _CoastMap.
Shader "IronThrone/CartographyWater"
{
    Properties
    {
        _ShallowTex ("Shallow Surface", 2D) = "white" {}
        _DeepTex ("Deep Surface", 2D) = "white" {}
        _CoastMap ("Coast Map (R = близость к суше)", 2D) = "black" {}
        _ShallowColor ("Shallow Color", Color) = (0.16, 0.55, 0.58, 0.92)
        _DeepColor ("Deep Color", Color) = (0.05, 0.22, 0.32, 1)
        _FoamColor ("Foam Color", Color) = (0.85, 0.95, 0.95, 1)
        _Tiling ("UV Tiling", Float) = 0.12
        _Speed ("Speed", Float) = 0.35
        _MapOrigin ("Map Origin (x,z, width, height)", Vector) = (0, 0, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _ShallowTex;
            sampler2D _DeepTex;
            sampler2D _CoastMap;
            fixed4 _ShallowColor;
            fixed4 _DeepColor;
            fixed4 _FoamColor;
            float _Tiling;
            float _Speed;
            float4 _MapOrigin;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 worldUV : TEXCOORD0;
                float2 texUV : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldUV = float2((wp.x - _MapOrigin.x) / _MapOrigin.z,
                                   (wp.z - _MapOrigin.y) / _MapOrigin.w);
                o.texUV = wp.xz * _Tiling;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y * _Speed;

                // два встречных потока — дышащая рябь
                float2 uv1 = i.texUV + float2(t * 0.06, t * 0.028);
                float2 uv2 = i.texUV * 1.7 - float2(t * 0.041, t * 0.055);
                fixed4 s1 = tex2D(_ShallowTex, uv1);
                fixed4 s2 = tex2D(_ShallowTex, uv2);
                fixed ripple = (s1.r + s2.r) * 0.5;

                float coast = tex2D(_CoastMap, i.worldUV).r; // 1 = у берега
                fixed4 water = lerp(_DeepColor, _ShallowColor, saturate(coast * 1.4));
                water.rgb *= 0.85 + 0.35 * ripple;

                // пена у берега: узкая полоса + мелкая рябь
                float foamBand = smoothstep(0.35, 0.85, coast) * (1 - smoothstep(0.9, 1.0, coast));
                float foamNoise = tex2D(_DeepTex, i.texUV * 2.3 + float2(t * 0.05, -t * 0.03)).r;
                float foam = saturate(foamBand * (0.55 + foamNoise * 0.9));
                water.rgb = lerp(water.rgb, _FoamColor.rgb, foam * 0.85);

                water.a = lerp(_DeepColor.a, _ShallowColor.a, saturate(coast * 1.4));
                water.a = saturate(water.a + foam * 0.35);

                UNITY_APPLY_FOG(i.fogCoord, water);
                return water;
            }
            ENDCG
        }
    }
    Fallback "Transparent/Diffuse"
}
