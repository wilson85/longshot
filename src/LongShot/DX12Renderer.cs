using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LongShot.Engine;
using Vortice.D3DCompiler;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace LongShot;

public sealed class DX12Renderer : IDisposable
{
    private const int FrameCount = 2;
    private readonly ID3D12Device _device;
    private readonly ID3D12CommandQueue _commandQueue;
    private readonly IDXGISwapChain3 _swapChain;
    private readonly ID3D12DescriptorHeap _rtvHeap;
    private readonly ID3D12DescriptorHeap _dsvHeap;
    private readonly ID3D12Resource[] _renderTargets = new ID3D12Resource[FrameCount];
    private readonly ID3D12Resource _depthStencil;
    private readonly ID3D12CommandAllocator[] _commandAllocators = new ID3D12CommandAllocator[FrameCount];
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly ID3D12Fence _fence;
    private readonly IntPtr _fenceEvent;

    private readonly ID3D12RootSignature _rootSignature;
    private readonly ID3D12PipelineState _pipelineState;

    private readonly ID3D12Resource _vBufferCube;
    private readonly ID3D12Resource _iBufferCube;
    private readonly ID3D12Resource _vBufferSphere;
    private readonly ID3D12Resource _iBufferSphere;

    private ulong _fenceValue;
    private int _frameIndex;
    private readonly int _rtvDescriptorSize;

    private readonly int _width;
    private readonly int _height;

    [StructLayout(LayoutKind.Sequential)]
    public struct ObjectConstants
    {
        public Matrix4x4 World;
        public Matrix4x4 ViewProj;
        public Vector4 Color;
        public Vector3 CameraPos;
        public float MaterialType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex { public Vector3 Position, Normal; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateEvent(IntPtr attr, bool man, bool init, string name);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr h, uint ms);

    public DX12Renderer(GameWindow window)
    {
        _width = window.Width;
        _height = window.Height;

        DXGI.CreateDXGIFactory2(false, out IDXGIFactory4 factory);
        D3D12.D3D12CreateDevice(null, Vortice.Direct3D.FeatureLevel.Level_11_0, out _device);

        if (_device == null)
        {
            throw new InvalidOperationException("Failed to create D3D12 device.");
        }

        _commandQueue = _device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));

        var swapDesc = new SwapChainDescription1
        {
            BufferCount = FrameCount,
            Width = (uint)_width,
            Height = (uint)_height,
            Format = Format.R8G8B8A8_UNorm,
            BufferUsage = Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.FlipDiscard,
            SampleDescription = new SampleDescription(1u, 0u)
        };

        using (var tempChain = factory.CreateSwapChainForHwnd(_commandQueue, window.Handle, swapDesc))
        {
            _swapChain = tempChain.QueryInterface<IDXGISwapChain3>();
        }

        _frameIndex = (int)_swapChain.CurrentBackBufferIndex;

        _rtvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, FrameCount));
        _rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        _dsvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.DepthStencilView, 1));

        var rtvHandle = _rtvHeap.GetCPUDescriptorHandleForHeapStart();
        for (int i = 0; i < FrameCount; i++)
        {
            _renderTargets[i] = _swapChain.GetBuffer<ID3D12Resource>((uint)i);
            _device.CreateRenderTargetView(_renderTargets[i], null, rtvHandle);
            _commandAllocators[i] = _device.CreateCommandAllocator(CommandListType.Direct);
            rtvHandle.Ptr += (nuint)_rtvDescriptorSize;
        }

        _depthStencil = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default),
            HeapFlags.None,
            ResourceDescription.Texture2D(Format.D32_Float, (uint)_width, (uint)_height, 1, 1, 1, 0, ResourceFlags.AllowDepthStencil),
            ResourceStates.DepthWrite,
            new ClearValue(Format.D32_Float, 1.0f, 0));

        _device.CreateDepthStencilView(_depthStencil, null, _dsvHeap.GetCPUDescriptorHandleForHeapStart());

        _commandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, _commandAllocators[0], null);
        _commandList.Close();

        _fence = _device.CreateFence(0, FenceFlags.None);
        _fenceEvent = CreateEvent(IntPtr.Zero, false, false, null);

        _rootSignature = _device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.AllowInputAssemblerInputLayout, new[] { new RootParameter1(new RootConstants(0, 0, 40), ShaderVisibility.All) }));

        string shaderCode = @"
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
                float3 viewDir = normalize(CameraPos - input.WPos);
                float NdotV = max(dot(abs(input.Norm), viewDir), 0.0);
                float fresnel = pow(1.0 - NdotV, 2.5); 

                float3 finalColor = float3(0.0, 0.0, 0.0);
                float dist = length(CameraPos - input.WPos);
                float outAlpha = Color.a;

                // 0.0: TABLE FLOOR
                if (MaterialType < 0.5) {
                    float2 grid = abs(frac(input.WPos.xz * 15.0) - 0.5);
                    if (grid.x < 0.03 || grid.y < 0.03) {
                        finalColor = Color.rgb * 1.5; 
                        outAlpha = max(outAlpha, 0.8);
                    } else {
                        finalColor = float3(0.005, 0.005, 0.015); 
                    }
                    finalColor *= saturate(1.0 - (dist / 8.0));
                }
                // 1.0: CUSHIONS & CUE STICK
                else if (MaterialType < 1.5) {
                    finalColor = Color.rgb * 0.1 + (Color.rgb * fresnel * 2.0);
                }
                // 2.0: BILLIARD BALLS & REFLECTIONS
                else if (MaterialType < 2.5) {
                    float scanline = sin(input.WPos.y * 100.0) * 0.5 + 0.5;
                    finalColor = Color.rgb * 0.15 + (Color.rgb * fresnel * 2.5) + (Color.rgb * scanline * 0.6);
                }
                // 3.0: TRAILS
                else {
                    finalColor = Color.rgb * 1.5;
                }
                
                return float4(finalColor, outAlpha);
            }
        ";

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

        _pipelineState = _device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
        {
            InputLayout = new InputLayoutDescription(new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0), new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0)),
            RootSignature = _rootSignature,
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

        GenerateCube(out var cubeVerts, out var cubeInds);
        GenerateSphere(out var sphVerts, out var sphInds, 30, 30, BilliardsEngine.StandardBallRadius);

        _vBufferCube = CreateBuffer(cubeVerts);
        _iBufferCube = CreateBuffer(cubeInds);

        _vBufferSphere = CreateBuffer(sphVerts);
        _iBufferSphere = CreateBuffer(sphInds);
    }

    private ID3D12Resource CreateBuffer<T>(T[] data) where T : unmanaged
    {
        int size = data.Length * Unsafe.SizeOf<T>();
        var res = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer((ulong)size), ResourceStates.GenericRead);
        unsafe { void* ptr; res.Map(0, &ptr); fixed (T* src = data) Buffer.MemoryCopy(src, ptr, size, size); res.Unmap(0); }
        return res;
    }

    public void Render(Camera camera, BilliardsEngine engine, MatchManager match)
    {
        var allocator = _commandAllocators[_frameIndex];
        allocator.Reset();

        _commandList.Reset(allocator, _pipelineState);
        _commandList.SetGraphicsRootSignature(_rootSignature);

        _commandList.RSSetViewport(new Viewport(0, 0, _width, _height));
        _commandList.RSSetScissorRect(new RectI(0, 0, _width, _height));

        _commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(_renderTargets[_frameIndex], ResourceStates.Present, ResourceStates.RenderTarget));

        var rtv = _rtvHeap.GetCPUDescriptorHandleForHeapStart();
        rtv.Ptr += (nuint)(_frameIndex * _rtvDescriptorSize);
        var dsv = _dsvHeap.GetCPUDescriptorHandleForHeapStart();

        _commandList.ClearRenderTargetView(rtv, new Color4(0.01f, 0.01f, 0.03f, 1.0f));
        _commandList.ClearDepthStencilView(dsv, ClearFlags.Depth, 1.0f, 0);
        _commandList.OMSetRenderTargets(rtv, dsv);
        _commandList.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);

        var vp = camera.ViewMatrix * camera.ProjectionMatrix;

        // PLANAR REFLECTIONS
        foreach (var ball in engine.ActiveBalls)
        {
            var pos = engine.GetBallPosition(ball.Id);
            var color = ball.Type == BallType.Cue ? new Vector4(1.0f, 1.0f, 1.0f, 1.0f) :
                        ball.Type == BallType.Normal ? new Vector4(1.0f, 0.0f, 0.6f, 1.0f) : new Vector4(0.6f, 0.0f, 1.0f, 1.0f);

            var refPos = new Vector3(pos.X, -pos.Y, pos.Z);
            var refWorld = Matrix4x4.CreateScale(1, -1, 1) * Matrix4x4.CreateTranslation(refPos);

            var refColor = color * 0.25f;
            refColor.W = 1.0f;
            DrawMesh(_vBufferSphere, _iBufferSphere, 30 * 30 * 6, refWorld, vp, camera.Position, refColor, 2.0f);
        }

        // BALLS
        foreach (var ball in engine.ActiveBalls)
        {
            var pos = engine.GetBallPosition(ball.Id);
            var color = ball.Type == BallType.Cue ? new Vector4(1.0f, 1.0f, 1.0f, 1.0f) :
                        ball.Type == BallType.Normal ? new Vector4(1.0f, 0.0f, 0.6f, 1.0f) : new Vector4(0.6f, 0.0f, 1.0f, 1.0f);

            DrawMesh(_vBufferSphere, _iBufferSphere, 30 * 30 * 6, Matrix4x4.CreateTranslation(pos), vp, camera.Position, color, 2.0f);
        }

        // OPAQUE CUSHIONS
        var cCushion = new Vector4(0.0f, 0.8f, 1.0f, 1.0f);
        DrawMesh(_vBufferCube, _iBufferCube, 36, Matrix4x4.CreateScale(BilliardsEngine.CushionWidth, 0.06f, BilliardsEngine.TableLength) * Matrix4x4.CreateTranslation(-BilliardsEngine.TableWidth / 2 - BilliardsEngine.CushionWidth / 2, 0.03f, 0), vp, camera.Position, cCushion, 1.0f);
        DrawMesh(_vBufferCube, _iBufferCube, 36, Matrix4x4.CreateScale(BilliardsEngine.CushionWidth, 0.06f, BilliardsEngine.TableLength) * Matrix4x4.CreateTranslation(BilliardsEngine.TableWidth / 2 + BilliardsEngine.CushionWidth / 2, 0.03f, 0), vp, camera.Position, cCushion, 1.0f);
        DrawMesh(_vBufferCube, _iBufferCube, 36, Matrix4x4.CreateScale(BilliardsEngine.TableWidth + BilliardsEngine.CushionWidth * 2, 0.06f, BilliardsEngine.CushionWidth) * Matrix4x4.CreateTranslation(0, 0.03f, -BilliardsEngine.TableLength / 2 - BilliardsEngine.CushionWidth / 2), vp, camera.Position, cCushion, 1.0f);
        DrawMesh(_vBufferCube, _iBufferCube, 36, Matrix4x4.CreateScale(BilliardsEngine.TableWidth + BilliardsEngine.CushionWidth * 2, 0.06f, BilliardsEngine.CushionWidth) * Matrix4x4.CreateTranslation(0, 0.03f, BilliardsEngine.TableLength / 2 + BilliardsEngine.CushionWidth / 2), vp, camera.Position, cCushion, 1.0f);

        // TRANSLUCENT TABLE FLOOR
        DrawMesh(_vBufferCube, _iBufferCube, 36, Matrix4x4.CreateScale(BilliardsEngine.TableWidth, 0.05f, BilliardsEngine.TableLength) * Matrix4x4.CreateTranslation(0, -0.025f, 0), vp, camera.Position, new Vector4(0.0f, 1.0f, 1.0f, 0.4f), 0.0f);

        // TRANSPARENT ENERGY TRAILS
        foreach (var ball in engine.ActiveBalls)
        {
            var color = ball.Type == BallType.Cue ? new Vector4(1.0f, 1.0f, 1.0f, 1.0f) :
                        ball.Type == BallType.Normal ? new Vector4(1.0f, 0.0f, 0.6f, 1.0f) : new Vector4(0.6f, 0.0f, 1.0f, 1.0f);

            int trailIndex = 0;
            float totalTrailPoints = ball.Trail.Length;

            foreach (var trailPos in ball.Trail)
            {
                float age = trailIndex / totalTrailPoints;
                float intensity = Math.Clamp(ball.LastSpeed * 0.5f, 0.2f, 1.5f);

                Vector4 trailColor = color * intensity;
                trailColor.W = age * 0.5f;

                float dotScale = (BilliardsEngine.StandardBallRadius * 0.25f) * (0.2f + age * 0.8f);
                Vector3 bottomOffset = new Vector3(0, -BilliardsEngine.StandardBallRadius + dotScale, 0);

                Matrix4x4 trailTransform = Matrix4x4.CreateScale(dotScale / BilliardsEngine.StandardBallRadius) * Matrix4x4.CreateTranslation(trailPos + bottomOffset);

                DrawMesh(_vBufferSphere, _iBufferSphere, 30 * 30 * 6, trailTransform, vp, camera.Position, trailColor, 3.0f);
                trailIndex++;
            }
        }

        //  AIMING CUE STICK
        if (match.Mode == GameStateMode.Aim || match.Mode == GameStateMode.Power)
        {
            var cuePos = engine.GetBallPosition(0);
            var stickRot = Matrix4x4.CreateRotationY(camera.Yaw);

            float visualOffset = 0.5f + match.CueStickOffset;
            var stickOffset = Vector3.Transform(new Vector3(0, 0, visualOffset), stickRot);

            DrawMesh(_vBufferCube, _iBufferCube, 36, Matrix4x4.CreateScale(0.015f, 0.015f, 1.0f) * stickRot * Matrix4x4.CreateTranslation(cuePos + stickOffset), vp, camera.Position, new Vector4(1.0f, 0.5f, 0.0f, 0.8f), 1.0f);
        }

        _commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(_renderTargets[_frameIndex], ResourceStates.RenderTarget, ResourceStates.Present));
        _commandList.Close();

        _commandQueue.ExecuteCommandList(_commandList);
        _swapChain.Present(1, PresentFlags.None);

        _commandQueue.Signal(_fence, ++_fenceValue);
        if (_fence.CompletedValue < _fenceValue)
        {
            _fence.SetEventOnCompletion(_fenceValue, _fenceEvent);
            WaitForSingleObject(_fenceEvent, 0xFFFFFFFF);
        }

        _frameIndex = (int)_swapChain.CurrentBackBufferIndex;
    }

    private void DrawMesh(ID3D12Resource vb, ID3D12Resource ib, int iCount, Matrix4x4 w, Matrix4x4 vp, Vector3 camPos, Vector4 col, float matType)
    {
        var consts = new ObjectConstants { World = w, ViewProj = vp, Color = col, CameraPos = camPos, MaterialType = matType };
        _commandList.SetGraphicsRoot32BitConstants(0, ref consts);
        _commandList.IASetVertexBuffers(0, new VertexBufferView(vb.GPUVirtualAddress, (uint)vb.Description.Width, (uint)Unsafe.SizeOf<Vertex>()));
        _commandList.IASetIndexBuffer(new IndexBufferView(ib.GPUVirtualAddress, (uint)ib.Description.Width, Format.R16_UInt));
        _commandList.DrawIndexedInstanced((uint)iCount, 1, 0, 0, 0);
    }

    private static void GenerateCube(out Vertex[] v, out ushort[] i)
    {
        Vector3[] p = { new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f), new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f) };
        Vector3[] n = { -Vector3.UnitZ, Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitX, -Vector3.UnitY, Vector3.UnitY };
        int[][] f = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 }, new[] { 0, 1, 5, 4 }, new[] { 3, 7, 6, 2 } };
        v = new Vertex[24]; i = new ushort[36];
        for (int face = 0; face < 6; face++)
        {
            for (int vert = 0; vert < 4; vert++) v[face * 4 + vert] = new Vertex { Position = p[f[face][vert]], Normal = n[face] };
            i[face * 6 + 0] = (ushort)(face * 4 + 0); i[face * 6 + 1] = (ushort)(face * 4 + 2); i[face * 6 + 2] = (ushort)(face * 4 + 1);
            i[face * 6 + 3] = (ushort)(face * 4 + 0); i[face * 6 + 4] = (ushort)(face * 4 + 3); i[face * 6 + 5] = (ushort)(face * 4 + 2);
        }
    }

    private static void GenerateSphere(out Vertex[] v, out ushort[] ind, int lat, int lon, float r)
    {
        var vl = new List<Vertex>(); var il = new List<ushort>();
        for (int y = 0; y <= lat; y++)
        {
            float phi = (y / (float)lat) * MathF.PI;
            for (int x = 0; x <= lon; x++)
            {
                float theta = (x / (float)lon) * MathF.PI * 2;
                Vector3 p = new Vector3(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta));
                vl.Add(new Vertex { Position = p * r, Normal = p });
            }
        }
        for (int y = 0; y < lat; y++)
        {
            for (int x = 0; x < lon; x++)
            {
                int i0 = y * (lon + 1) + x, i1 = i0 + lon + 1;
                il.Add((ushort)i0); il.Add((ushort)(i0 + 1)); il.Add((ushort)i1);
                il.Add((ushort)(i1)); il.Add((ushort)(i0 + 1)); il.Add((ushort)(i1 + 1));
            }
        }
        v = vl.ToArray(); ind = il.ToArray();
    }

    public void Dispose()
    {
        _commandQueue?.Dispose();
        _device?.Dispose();
        _swapChain?.Dispose();
        // Cleanup remaining DX12 COM objects
    }
}