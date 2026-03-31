using Vortice.D3DCompiler;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace LongShot.Rendering
{
    public class TrailShader
    {
        const string shaderCode = @"
            struct VS_IN { 
                float3 Pos : POSITION; 
                float3 Norm : NORMAL; 
                float4 Color : COLOR; // Added per-vertex color
            };

            struct PS_IN { 
                float4 Pos : SV_POSITION; 
                float3 WPos : WORLDPOS; 
                float3 Norm : NORMAL; 
                float3 LNorm : LOCALNORM; 
                float4 VertColor : COLOR; // Passed to pixel shader
            };
            
            cbuffer CB0 : register(b0) { 
                row_major float4x4 World; 
                row_major float4x4 ViewProj; 
                float4 GlobalColor; // Renamed from Color to avoid confusion with vertex color
                float3 CameraPos;
                float MaterialType;
            };
            
            PS_IN VS(VS_IN input) {
                PS_IN output;
                float4 wp = mul(float4(input.Pos, 1.0), World);
                output.Pos = mul(wp, ViewProj);
                output.WPos = wp.xyz;
                output.Norm = normalize(mul(input.Norm, (float3x3)World));
                output.LNorm = input.Norm;
                output.VertColor = input.Color; // Pass through the vertex color
                
                #ifdef INVERT_WINDING
                output.Norm = -output.Norm;
                #endif
                
                return output;
            }
            
            float4 PS(PS_IN input) : SV_Target {
                input.Norm = normalize(input.Norm);
                float3 viewDir = normalize(CameraPos - input.WPos);
                float rawNdotV = dot(input.Norm, viewDir);
                float NdotV = max(rawNdotV, 0.0);
                float fresnel = pow(1.0 - NdotV, 2.0);

                float3 finalColor = float3(0.0, 0.0, 0.0);
                float outAlpha = GlobalColor.a;
                float dist = length(CameraPos - input.WPos);

                // 0.0: TABLE FLOOR
                if (MaterialType < 0.5) {
                    float3 baseGlass = float3(0.02, 0.03, 0.04);
                    float3 lightDir = normalize(float3(0.5, 1.0, 0.3));
                    float3 halfVector = normalize(lightDir + viewDir);
                    float specular = pow(max(dot(input.Norm, halfVector), 0.0), 256.0) * 0.5;
                    float3 neonGlow = float3(0.0, 0.8, 1.0) * fresnel * 0.5;
                    finalColor = baseGlass + specular + neonGlow;
                    outAlpha = 0.85 * saturate(1.0 - (dist / 12.0));
                }
                // 1.0: CUSHIONS & RAILS
                else if (MaterialType < 1.5) {
                    float3 uv = input.WPos * 15.0;
                    float3 grid = abs(frac(uv - 0.5) - 0.5);
                    float3 gridWidth = fwidth(uv); 
                    float3 lineAlpha = smoothstep(0.03 + gridWidth, 0.03 - gridWidth, grid);
                    float lineIntensity = max(lineAlpha.x, max(lineAlpha.y, lineAlpha.z));
                    float3 neonColor = GlobalColor.rgb * 2.0; 
                    finalColor = lerp(float3(0.01, 0.01, 0.02), neonColor, lineIntensity) + (neonColor * fresnel * 0.5);
                    outAlpha = lerp(0.15, 1.0, lineIntensity);
                    if (rawNdotV < 0.0) { outAlpha *= 0.15; finalColor *= 0.4; }
                }
                // 2.0: BILLIARD BALLS
                else if (MaterialType < 2.5) {
                    float3 lightDir = normalize(float3(0.5, 1.0, 0.3));
                    float3 ballColor = GlobalColor.rgb;
                    float maxAxis = max(abs(input.LNorm.x), max(abs(input.LNorm.y), abs(input.LNorm.z)));
                    float spotAlpha = smoothstep(0.98, 0.98, maxAxis);
                    ballColor = lerp(ballColor, float3(0.85, 0.1, 0.15), spotAlpha);
                    float diffuse = max(dot(input.Norm, lightDir), 0.0);
                    float specular = pow(max(dot(input.Norm, normalize(lightDir + viewDir)), 0.0), 128.0); 
                    finalColor = (ballColor * 0.25) + (ballColor * diffuse * 0.75) + specular;
                }
                // 3.0: CUE STICK
                else if (MaterialType < 3.5) {
                    float3 neonColor = GlobalColor.rgb * 3.0;
                    finalColor = lerp(float3(1.0, 1.0, 1.0), neonColor, fresnel) + (neonColor * fresnel * 1.5);
                    outAlpha = lerp(0.9, 0.2, fresnel) * saturate(1.0 - (dist / 15.0));
                }
                // 4.0: TRAILS / LASERS / PARTICLES
                else if (MaterialType < 4.5) {
                    // Mix GlobalColor (from RenderItem) with VertColor (from Mesh)
                    // This supports Quads/Cubes (White vertices + colored RenderItem) 
                    // AND dynamic per-vertex colored trails simultaneously!
                    finalColor = (GlobalColor.rgb * input.VertColor.rgb) * 2.5; // Multiply for intense neon bloom
                    outAlpha = GlobalColor.a * input.VertColor.a;
                }
                // 5.0: HIT MARKS
                else {
                    finalColor = GlobalColor.rgb * 1.5; 
                    outAlpha = GlobalColor.a * 0.4 * saturate(1.0 - (dist / 10.0));
                }
                
                return float4(finalColor, outAlpha);
            }
        ";

        public static ID3D12PipelineState Init(
            ID3D12Device device,
            ID3D12RootSignature rootSignature,
            uint sampleCount = 4,
            bool isTransparent = false,
            bool invertWinding = false,
            PrimitiveTopologyType topologyType = PrimitiveTopologyType.Triangle,
            bool isDynamicTrail = false) // NEW: Handles the TrailPoint layout mismatch
        {
            string currentShaderCode = shaderCode;
            if (invertWinding) currentShaderCode = "#define INVERT_WINDING 1\n" + currentShaderCode;

            var vs = Compiler.Compile(currentShaderCode, "VS", "Shader", "vs_5_0");
            var ps = Compiler.Compile(currentShaderCode, "PS", "Shader", "ps_5_0");

            var blendDesc = isTransparent ? BlendDescription.AlphaBlend : BlendDescription.Opaque;

            var rsDesc = RasterizerDescription.CullNone;
            if (invertWinding) rsDesc.FrontCounterClockwise = true;
            rsDesc.MultisampleEnable = sampleCount > 1;

            var depthDesc = DepthStencilDescription.Default;
            if (isTransparent) depthDesc.DepthWriteMask = DepthWriteMask.Zero;

            // Dynamically select the layout offsets based on struct type mapped to the vertex buffer
            var inputLayout = isDynamicTrail ? new InputLayoutDescription(
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 16, 0),    // TrailPoint.Direction is at offset 16
                new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 28, 0)   // TrailPoint.Color is at offset 28
            ) : new InputLayoutDescription(
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),    // Vertex.Normal is at offset 12
                new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 24, 0)   // Vertex.Color is at offset 24
            );

            return device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
            {
                InputLayout = inputLayout,
                RootSignature = rootSignature,
                VertexShader = vs.ToArray(),
                PixelShader = ps.ToArray(),
                RasterizerState = rsDesc,
                BlendState = blendDesc,
                DepthStencilState = depthDesc,
                DepthStencilFormat = Format.D32_Float,
                PrimitiveTopologyType = topologyType,
                RenderTargetFormats = new[] { Format.R8G8B8A8_UNorm },
                SampleDescription = new SampleDescription(sampleCount, 0u)
            });
        }
    }
}