[Begin_ResourceLayout]
   
// These DO NOT have engine tags, so they WILL show up as sliders for you to tweak!
    cbuffer Parameters : register(b0)
    {
        float FresnelPower      : packoffset(c0.x); [Default(4.0)]
        float BaseOpacity       : packoffset(c0.y); [Default(0.2)]
        float EmissiveIntensity : packoffset(c0.z); [Default(15.0)]
    };

    // These HAVE engine tags, so Evergine hides them and fills them in automatically!
    cbuffer PerDrawCall : register(b1)
    {
        float4x4 WorldViewProj  : packoffset(c0); [WorldViewProjection]
        float4x4 World          : packoffset(c4); [World]
    };

    cbuffer PerCamera : register(b2)
    {
        float3 CameraPosition   : packoffset(c0); [CameraPosition]
    };

[End_ResourceLayout]

[Begin_Pass:Default]
	[Profile 10_0]
	[Entrypoints VS=VS PS=PS]
	struct VS_IN
    {
    	float4 position : POSITION;
        float3 normal   : NORMAL;
        float3 tangent  : TANGENT;   // <--- WE MISSED THIS!
        float4 color    : COLOR;   // Your Cyan and Slate vertex colors!
    };

    // What goes TO the pixel shader
    struct PS_IN
    {
        float4 pos      : SV_POSITION;
        float4 color    : COLOR;
        float3 normal   : NORMAL;
        float3 viewDir  : TEXCOORD0;
    };

    // VERTEX SHADER: Calculates positions and angles
    PS_IN VS(VS_IN input)
    {
        PS_IN output = (PS_IN)0;

        output.pos = mul(input.position, WorldViewProj);
        output.color = input.color;
        
        // Convert normals and positions to World Space for accurate lighting
        output.normal = normalize(mul(input.normal, (float3x3)World));
        float3 worldPos = mul(input.position, World).xyz;
        output.viewDir = normalize(CameraPosition - worldPos);

        return output;
    }

    // PIXEL SHADER: Calculates the glowing transparent colors
    float4 PS(PS_IN input) : SV_Target
    {
        float3 baseColor = input.color.rgb;

        // 1. Fresnel Math (Calculates the glancing edges)
        float dotNV = max(0.0, dot(normalize(input.normal), normalize(input.viewDir)));
        float fresnel = pow(1.0 - dotNV, FresnelPower);

        // 2. Emissive Glow (Multiplies your vertex color by the intensity slider)
        float3 emissive = baseColor * EmissiveIntensity;

        // 3. Alpha Transparency (Center is clear, edges are completely solid)
        float alpha = BaseOpacity + fresnel;

        return float4(baseColor + emissive, alpha);
    }

[End_Pass]