using Vortice.D3DCompiler;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace LongShot;

public class TrailShader
{
    const string shaderCode = @"
            struct VS_IN { float3 Pos : POSITION; float3 Norm : NORMAL; };
            struct PS_IN { float4 Pos : SV_POSITION; float3 WPos : WORLDPOS; float3 Norm : NORMAL; };
            
            cbuffer CB0 : register(b0) { 
                row_major float4x4 World; 
                row_major float4x4 ViewProj; 
                float4 Color; 
                float3 CameraPos;
                float MaterialType;
            };
            
            PS_IN VS(VS_IN input) {
                PS_IN output;
                float4 wp = mul(float4(input.Pos, 1.0), World);
                output.Pos = mul(wp, ViewProj);
                output.WPos = wp.xyz;
                output.Norm = normalize(mul(input.Norm, (float3x3)World));
                return output;
            }
            float4 PS(PS_IN input) : SV_Target {
                // FIX: Re-normalize the interpolated normal to prevent specular shattering!
                input.Norm = normalize(input.Norm);
                
                float3 viewDir = normalize(CameraPos - input.WPos);
                float NdotV = max(dot(input.Norm, viewDir), 0.0);
                float fresnel = pow(1.0 - NdotV, 2.0);

                float3 finalColor = float3(0.0, 0.0, 0.0);
                float outAlpha = Color.a;
                float dist = length(CameraPos - input.WPos);

                // 0.0: TRON GRID (Table Floor)
                if (MaterialType < 0.5) {
                    float2 uv = input.WPos.xz * 15.0;
                    float2 grid = abs(frac(uv - 0.5) - 0.5);
                    
                    // Anti-aliasing magic: calculate exactly how fast the grid is shrinking per-pixel
                    float2 gridWidth = fwidth(uv); 
                    
                    // Smoothly blend the edges of the line
                    float2 lineAlpha = smoothstep(0.03 + gridWidth, 0.03 - gridWidth, grid);
                    float lineIntensity = max(lineAlpha.x, lineAlpha.y);

                    float3 neonCyan = float3(0.0, 0.8, 1.0); // Bright Tron Blue
                    float3 darkBase = float3(0.02, 0.02, 0.03); // Deep space background
                    
                    finalColor = lerp(darkBase, neonCyan * 2.0, lineIntensity);
                    
                    // Fade into the abyss in the distance
                    finalColor *= saturate(1.0 - (dist / 8.0));
                }
                // 1.0: CUSHIONS & CUE STICK (Dark with glowing edge)
                else if (MaterialType < 1.5) {
                    float3 baseMetal = float3(0.1, 0.1, 0.1);
                    float3 neonEdge = Color.rgb * 2.0;
                    finalColor = baseMetal + (neonEdge * fresnel);
                }
                // 2.0: BILLIARD BALLS (High-gloss procedural scanlines)
                else if (MaterialType < 2.5) {
                    // Create a virtual light source shining down at an angle
                    float3 lightDir = normalize(float3(0.5, 1.0, 0.3));
                    
                    // 1. Ambient: Base color in the shadows
                    float3 ambient = Color.rgb * 0.25;
                    
                    // 2. Diffuse: How the light wraps around the sphere
                    float NdotL = max(dot(input.Norm, lightDir), 0.0);
                    float3 diffuse = Color.rgb * NdotL * 0.75;
                    
                    // 3. Specular: The sharp, glossy white reflection dot
                    float3 halfVector = normalize(lightDir + viewDir);
                    float NdotH = max(dot(input.Norm, halfVector), 0.0);
                    // The power (128.0) determines how sharp and tiny the reflection is. 
                    // Higher = glossier.
                    float specular = pow(NdotH, 128.0); 
                    
                    finalColor = ambient + diffuse + float3(specular, specular, specular);
                }
                // 3.0: TRAILS
                else {
                    finalColor = Color.rgb * 2.0;
                }
                
                return float4(finalColor, outAlpha);
            }
        ";

    public static ID3D12PipelineState Init(ID3D12Device device, ID3D12RootSignature rootSignature)
    {
        var vs = Compiler.Compile(shaderCode, "VS", "Shader", "vs_5_0");
        var ps = Compiler.Compile(shaderCode, "PS", "Shader", "ps_5_0");

        var blendDesc = new BlendDescription { AlphaToCoverageEnable = false, IndependentBlendEnable = false };
        blendDesc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.SourceAlpha,
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.InverseSourceAlpha,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All
        };

        return device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
        {
            InputLayout = new InputLayoutDescription(new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0), new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0)),
            RootSignature = rootSignature,
            VertexShader = vs.ToArray(),
            PixelShader = ps.ToArray(),
            RasterizerState = RasterizerDescription.CullNone,
            BlendState = blendDesc,
            DepthStencilState = DepthStencilDescription.Default,
            DepthStencilFormat = Format.D32_Float,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = new[] { Format.R8G8B8A8_UNorm },
            SampleDescription = new SampleDescription(1u, 0u)
        });
    }
}
