using Vortice.D3DCompiler;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace LongShot.Rendering;


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
                input.Norm = normalize(input.Norm);
                
                float3 viewDir = normalize(CameraPos - input.WPos);
                
                // Unclamped NdotV used for detecting backfaces
                float rawNdotV = dot(input.Norm, viewDir);
                float NdotV = max(rawNdotV, 0.0);
                float fresnel = pow(1.0 - NdotV, 2.0);

                float3 finalColor = float3(0.0, 0.0, 0.0);
                float outAlpha = Color.a;
                float dist = length(CameraPos - input.WPos);

                // 0.0: TABLE FLOOR (Frosted Glass / Tron surface)
                if (MaterialType < 0.5) {
                    float3 baseGlass = float3(0.02, 0.03, 0.04);
                    
                    float3 lightDir = normalize(float3(0.5, 1.0, 0.3));
                    float3 halfVector = normalize(lightDir + viewDir);
                    float NdotH = max(dot(input.Norm, halfVector), 0.0);
                    
                    float specular = pow(NdotH, 256.0) * 0.5;
                    float3 neonGlow = float3(0.0, 0.8, 1.0) * fresnel * 0.5;
                    
                    finalColor = baseGlass + float3(specular, specular, specular) + neonGlow;
                    
                    outAlpha = 0.85; 
                    outAlpha *= saturate(1.0 - (dist / 12.0));
                }
                // 1.0: CUSHIONS & RAILS (Transparent Holographic 3D Grid)
                else if (MaterialType < 1.5) {
                    float3 uv = input.WPos * 15.0;
                    float3 grid = abs(frac(uv - 0.5) - 0.5);
                    float3 gridWidth = fwidth(uv); 
                    
                    float3 lineAlpha = smoothstep(0.03 + gridWidth, 0.03 - gridWidth, grid);
                    float lineIntensity = max(lineAlpha.x, max(lineAlpha.y, lineAlpha.z));

                    float3 neonColor = Color.rgb * 2.0; 
                    float3 darkBase = float3(0.01, 0.01, 0.02); 
                    
                    finalColor = lerp(darkBase, neonColor, lineIntensity);
                    finalColor += (neonColor * fresnel * 0.5);

                    // --- TRANSPARENCY LOGIC ---
                    // Make the spaces between the lines highly transparent (15% opacity), while grid lines remain solid
                    outAlpha = lerp(0.15, 1.0, lineIntensity);

                    // ""Transparent from behind""
                    // If the normal is pointing away from the camera, heavily fade out the opacity and color
                    // This prevents the back-walls of the cushions from cluttering the visual field!
                    if (rawNdotV < 0.0) {
                        outAlpha *= 0.15;
                        finalColor *= 0.4;
                    }
                }
                // 2.0: BILLIARD BALLS (High-gloss procedural scanlines)
                else if (MaterialType < 2.5) {
                    float3 lightDir = normalize(float3(0.5, 1.0, 0.3));
                    float3 ambient = Color.rgb * 0.25;
                    float3 diffuse = Color.rgb * max(dot(input.Norm, lightDir), 0.0) * 0.75;
                    
                    float3 halfVector = normalize(lightDir + viewDir);
                    float specular = pow(max(dot(input.Norm, halfVector), 0.0), 128.0); 
                    
                    finalColor = ambient + diffuse + float3(specular, specular, specular);
                }
                // 3.0: CUE STICK (Tron Light Beam / Holographic Cylinder)
                else if (MaterialType < 3.5) {
                    // Bright hot-white core, colored neon glowing edges
                    float3 coreColor = float3(1.0, 1.0, 1.0);
                    float3 neonColor = Color.rgb * 3.0; // Boosted for maximum glow
                    
                    finalColor = lerp(coreColor, neonColor, fresnel);
                    
                    // Add a diffused 'laser fog' glow that boosts when rendering inside other transparent objects
                    finalColor += neonColor * fresnel * 1.5;
                    
                    // Additive transparency: Bright in the center, soft at the edges
                    outAlpha = lerp(0.9, 0.2, fresnel);
                    
                    // Fade out slightly based on distance to prevent screen blow-out
                    outAlpha *= saturate(1.0 - (dist / 15.0));
                }
                // 4.0: TRAILS (Unlit neon glow)
                else if (MaterialType < 4.5) {
                    finalColor = Color.rgb * 2.0;
                }
                // 5.0: HIT MARKS (Faded, diffused impacts inside the glass)
                else {
                    finalColor = Color.rgb * 1.5; 
                    
                    // Heavily fade the opacity so it looks like it's glowing deep inside the frosted glass
                    outAlpha = Color.a * 0.4; 
                    
                    outAlpha *= saturate(1.0 - (dist / 10.0));
                }
                
                return float4(finalColor, outAlpha);
            }
        ";

    public static ID3D12PipelineState Init(ID3D12Device device, ID3D12RootSignature rootSignature, uint sampleCount = 4, bool isTransparent = false)
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

        var rsDesc = RasterizerDescription.CullNone;
        rsDesc.MultisampleEnable = sampleCount > 1;

        // CRITICAL FOR TRANSPARENCY:
        // By disabling Depth Writes for transparent objects, they will no longer 
        // occlude each other in 3D space. The Cue can now render safely inside the rails!
        var depthDesc = DepthStencilDescription.Default;
        if (isTransparent)
        {
            depthDesc.DepthWriteMask = DepthWriteMask.Zero;
        }

        return device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
        {
            InputLayout = new InputLayoutDescription(new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0), new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0)),
            RootSignature = rootSignature,
            VertexShader = vs.ToArray(),
            PixelShader = ps.ToArray(),
            RasterizerState = rsDesc,
            BlendState = blendDesc,
            DepthStencilState = depthDesc,
            DepthStencilFormat = Format.D32_Float,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = new[] { Format.R8G8B8A8_UNorm },
            SampleDescription = new SampleDescription(sampleCount, 0u)
        });
    }
}