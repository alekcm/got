Shader "Shader Graphs/CartographyObjectShader_Pillaged" {
	Properties {
		[NoScaleOffset] Texture2D_65167E56 ("MainTexture", 2D) = "white" {}
		[NoScaleOffset] EffectTexture2D ("EffectTexture", 2D) = "black" {}
		[NoScaleOffset] Texture2D_14ca42914cf544e0abd355cda5d8f4fa ("SpecularTexture", 2D) = "white" {}
		[Toggle(_USEDISTORTION)] _USEDISTORTION ("UseDistortion", Float) = 1
		Vector1_6e71bd7dd45d47b1a147f613b610d8ff ("Distortion Scale", Range(0.01, 4)) = 2
		Vector1_598d6b46d2964f9381fb44ec44e1026e ("Distortion Strength", Range(0, 1)) = 0.01
		_Color ("Color", Vector) = (0,0,0,0)
		Vector1_7B082B56 ("Amount", Float) = 1
		_worldPosition ("WorldPosition", Vector) = (0,0,0,0)
		[NoScaleOffset] Texture2D_18585D9B ("NoiseTexture", 2D) = "white" {}
		[NoScaleOffset] Texture2D_7AE959FB ("PillageEffectTexture", 2D) = "white" {}
		_ColorStrength ("ColorStrength", Range(0, 1)) = 0
		[KeywordEnum(Default, Fixed, Off)] _BILLBOARDTYPE ("BillboardType", Float) = 0
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

			struct Vertex_Stage_Input
			{
				float4 pos : POSITION;
			};

			struct Vertex_Stage_Output
			{
				float4 pos : SV_POSITION;
			};

			Vertex_Stage_Output vert(Vertex_Stage_Input input)
			{
				Vertex_Stage_Output output;
				output.pos = mul(unity_MatrixVP, mul(unity_ObjectToWorld, input.pos));
				return output;
			}

			float4 _Color;

			float4 frag(Vertex_Stage_Output input) : SV_TARGET
			{
				return _Color; // RGBA
			}

			ENDHLSL
		}
	}
	Fallback "Hidden/Shader Graph/FallbackError"
	//CustomEditor "UnityEditor.ShaderGraph.GenericShaderGraphMaterialGUI"
}