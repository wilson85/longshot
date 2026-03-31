[Begin_ResourceLayout]

	cbuffer PerDrawCall : register(b0)
	{
		float4x4 WorldViewProj	: packoffset(c0);	[WorldViewProjection]
	};

	cbuffer Parameters : register(b1)
	{
		float3 Color			: packoffset(c0);   [Default(0.3, 0.3, 1.0)]
	};

[End_ResourceLayout]

[Begin_Pass:Default]
	[Profile 10_0]
	[Entrypoints VS=VS PS=PS]

	struct VS_IN
	{
		float4 position : POSITION0;
		float3 normal	: NORMAL0;
		float2 texCoord : TEXCOORD0;
	};

	struct PS_IN
	{
		float4 position : SV_POSITION;
		float3 normal	: NORMAL0;
		float2 texCoord : TEXCOORD0;
	};

	PS_IN VS(VS_IN input)
	{
		PS_IN output = (PS_IN)0;

		output.position = mul(input.position, WorldViewProj);
		output.normal = input.normal;
		output.texCoord = input.texCoord;

		return output;
	}

	float4 PS(PS_IN input) : SV_Target
	{
		return float4(Color,1);
	}

[End_Pass]