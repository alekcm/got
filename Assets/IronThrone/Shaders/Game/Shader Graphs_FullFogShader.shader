Shader "Shader Graphs/FullFogShader" {
	Properties {
		[NoScaleOffset] _MainTex ("MainTex", 2D) = "white" {}
		[NoScaleOffset] _ColorRamp ("ColorRamp", 2D) = "white" {}
		Vector1_85582A29 ("EdgeSharpness", Range(0.01, 50)) = 1
		Vector1_641A5FEF ("FresnelStrength", Range(0, 16)) = 3
		Vector1_4C46BB6D ("FresnelSharpness", Range(0, 0.1)) = 0
		Vector1_C033198F ("FresnelThickness", Range(0, 5)) = 0
		Vector1_9C10FC4D ("Speed", Range(0, 0.25)) = 0.25
		[NoScaleOffset] _PrimaryNoiseTex ("PrimaryNoiseTex", 2D) = "white" {}
		Vector1_6253F8 ("PrimaryNoiseScale", Range(0.01, 8)) = 1
		[NoScaleOffset] _SecondaryNoiseTex ("SecondaryNoiseTex", 2D) = "white" {}
		Vector1_D5022E9 ("SecondaryNoiseScale", Range(0.01, 8)) = 1
		[HideInInspector] _QueueOffset ("_QueueOffset", Float) = 0
		[HideInInspector] _QueueControl ("_QueueControl", Float) = -1
		[HideInInspector] [NoScaleOffset] unity_Lightmaps ("unity_Lightmaps", 2DArray) = "" {}
		[HideInInspector] [NoScaleOffset] unity_LightmapsInd ("unity_LightmapsInd", 2DArray) = "" {}
		[HideInInspector] [NoScaleOffset] unity_ShadowMasks ("unity_ShadowMasks", 2DArray) = "" {}
	}
	//DummyShaderTextExporter
	SubShader{
		Tags { "RenderType"="Opaque" }
		LOD 200

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			float4x4 unity_ObjectToWorld;
			float4x4 unity_MatrixVP;
			float4 _MainTex_ST;

			struct Vertex_Stage_Input
			{
				float4 pos : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Vertex_Stage_Output
			{
				float2 uv : TEXCOORD0;
				float4 pos : SV_POSITION;
			};

			Vertex_Stage_Output vert(Vertex_Stage_Input input)
			{
				Vertex_Stage_Output output;
				output.uv = (input.uv.xy * _MainTex_ST.xy) + _MainTex_ST.zw;
				output.pos = mul(unity_MatrixVP, mul(unity_ObjectToWorld, input.pos));
				return output;
			}

			Texture2D<float4> _MainTex;
			SamplerState sampler_MainTex;

			struct Fragment_Stage_Input
			{
				float2 uv : TEXCOORD0;
			};

			float4 frag(Fragment_Stage_Input input) : SV_TARGET
			{
				return _MainTex.Sample(sampler_MainTex, input.uv.xy);
			}

			ENDHLSL
		}
	}
	Fallback "Hidden/Shader Graph/FallbackError"
	//CustomEditor "UnityEditor.ShaderGraph.GenericShaderGraphMaterialGUI"
}