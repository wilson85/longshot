using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace LongShot.Rendering;

public sealed class DX12Renderer : IDisposable
{
    private const int FrameCount = 2;
    private const uint SampleCount = 4; // 4x MSAA for perfectly smooth geometry edges

    private readonly ID3D12Device _device;
    private readonly ID3D12CommandQueue _commandQueue;
    private readonly IDXGISwapChain3 _swapChain;
    private readonly ID3D12DescriptorHeap _rtvHeap;
    private readonly ID3D12DescriptorHeap _dsvHeap;
    private readonly ID3D12Resource[] _renderTargets = new ID3D12Resource[FrameCount];
    private readonly ID3D12Resource[] _msaaRenderTargets = new ID3D12Resource[FrameCount];
    private readonly ID3D12Resource _depthStencil;
    private readonly ID3D12CommandAllocator[] _commandAllocators = new ID3D12CommandAllocator[FrameCount];
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly ID3D12Fence _fence;
    private readonly AutoResetEvent _fenceEvent;

    private readonly ID3D12RootSignature _rootSignature;

    // We now have TWO Pipeline States: one for solids, one for holograms/glass
    private readonly ID3D12PipelineState _pipelineStateOpaque;
    private readonly ID3D12PipelineState _pipelineStateTransparent;

    private readonly ID3D12Resource _vBufferCube;
    private readonly ID3D12Resource _iBufferCube;
    private readonly ID3D12Resource _vBufferSphere;
    private readonly ID3D12Resource _iBufferSphere;
    private readonly ID3D12Resource _vBufferQuad;
    private readonly ID3D12Resource _iBufferQuad;
    private readonly ID3D12Resource _vBufferCircle;
    private readonly ID3D12Resource _iBufferCircle;
    private readonly ID3D12Resource _vBufferCylinder;
    private readonly ID3D12Resource _iBufferCylinder;

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

    public DX12Renderer(IntPtr windowHandle, int width, int height)
    {
        _width = width;
        _height = height;

        DXGI.CreateDXGIFactory2(false, out IDXGIFactory4 factory);
        D3D12.D3D12CreateDevice(null, Vortice.Direct3D.FeatureLevel.Level_11_0, out _device);

        if (_device == null) throw new InvalidOperationException("Failed to create D3D12 device.");

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

        using (var tempChain = factory.CreateSwapChainForHwnd(_commandQueue, windowHandle, swapDesc))
        {
            _swapChain = tempChain.QueryInterface<IDXGISwapChain3>();
        }

        _frameIndex = (int)_swapChain.CurrentBackBufferIndex;

        _rtvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, FrameCount));
        _rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        _dsvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.DepthStencilView, 1));

        var rtvHandle = _rtvHeap.GetCPUDescriptorHandleForHeapStart();

        // CRITICAL FIX: MSAA Targets require an Optimized Clear Value upon creation!
        var clearColor = new Color4(0.05f, 0.1f, 0.05f, 1.0f);
        var optimizedClearValue = new ClearValue(Format.R8G8B8A8_UNorm, clearColor);

        for (int i = 0; i < FrameCount; i++)
        {
            _renderTargets[i] = _swapChain.GetBuffer<ID3D12Resource>((uint)i);

            _msaaRenderTargets[i] = _device.CreateCommittedResource(
                new HeapProperties(HeapType.Default), HeapFlags.None,
                ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, (uint)_width, (uint)_height, 1, 1, SampleCount, 0, ResourceFlags.AllowRenderTarget),
                ResourceStates.ResolveSource, optimizedClearValue);

            _device.CreateRenderTargetView(_msaaRenderTargets[i], null, rtvHandle);

            _commandAllocators[i] = _device.CreateCommandAllocator(CommandListType.Direct);
            rtvHandle.Ptr += (nuint)_rtvDescriptorSize;
        }

        _depthStencil = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default), HeapFlags.None,
            ResourceDescription.Texture2D(Format.D32_Float, (uint)_width, (uint)_height, 1, 1, SampleCount, 0, ResourceFlags.AllowDepthStencil),
            ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 1.0f, 0));

        _device.CreateDepthStencilView(_depthStencil, null, _dsvHeap.GetCPUDescriptorHandleForHeapStart());

        _commandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, _commandAllocators[0], null);
        _commandList.Close();

        _fence = _device.CreateFence(0, FenceFlags.None);
        _fenceEvent = new AutoResetEvent(false);

        _rootSignature = _device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.AllowInputAssemblerInputLayout, new[] { new RootParameter1(new RootConstants(0, 0, 40), ShaderVisibility.All) }));

        // Generate the two unique rendering states based on transparency
        _pipelineStateOpaque = TrailShader.Init(_device, _rootSignature, SampleCount, false);
        _pipelineStateTransparent = TrailShader.Init(_device, _rootSignature, SampleCount, true);

        GenerateCube(out var cubeVerts, out var cubeInds);
        GenerateSphere(out var sphVerts, out var sphInds, 100, 100, 0.028575f);
        GenerateQuad(out var quadVerts, out var quadInds);
        GenerateCircle(out var circVerts, out var circInds);
        GenerateCylinder(out var cylVerts, out var cylInds);

        _vBufferQuad = CreateBuffer(quadVerts);
        _iBufferQuad = CreateBuffer(quadInds);
        _vBufferCircle = CreateBuffer(circVerts);
        _iBufferCircle = CreateBuffer(circInds);
        _vBufferCube = CreateBuffer(cubeVerts);
        _iBufferCube = CreateBuffer(cubeInds);
        _vBufferSphere = CreateBuffer(sphVerts);
        _iBufferSphere = CreateBuffer(sphInds);
        _vBufferCylinder = CreateBuffer(cylVerts);
        _iBufferCylinder = CreateBuffer(cylInds);
    }

    private static void GenerateCylinder(out Vertex[] v, out ushort[] ind, int segments = 32)
    {
        var vl = new List<Vertex>();
        var il = new List<ushort>();

        vl.Add(new Vertex { Position = new Vector3(0, 0.5f, 0), Normal = Vector3.UnitY });
        vl.Add(new Vertex { Position = new Vector3(0, -0.5f, 0), Normal = -Vector3.UnitY });

        int capTopStart = vl.Count;
        for (int s = 0; s <= segments; s++)
        {
            float angle = ((float)s / segments) * MathF.PI * 2f;
            vl.Add(new Vertex { Position = new Vector3(MathF.Cos(angle) * 0.5f, 0.5f, MathF.Sin(angle) * 0.5f), Normal = Vector3.UnitY });
        }

        int capBotStart = vl.Count;
        for (int s = 0; s <= segments; s++)
        {
            float angle = ((float)s / segments) * MathF.PI * 2f;
            vl.Add(new Vertex { Position = new Vector3(MathF.Cos(angle) * 0.5f, -0.5f, MathF.Sin(angle) * 0.5f), Normal = -Vector3.UnitY });
        }

        int sideTopStart = vl.Count;
        for (int s = 0; s <= segments; s++)
        {
            float angle = ((float)s / segments) * MathF.PI * 2f;
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);
            vl.Add(new Vertex { Position = new Vector3(cos * 0.5f, 0.5f, sin * 0.5f), Normal = new Vector3(cos, 0, sin) });
        }

        int sideBotStart = vl.Count;
        for (int s = 0; s <= segments; s++)
        {
            float angle = ((float)s / segments) * MathF.PI * 2f;
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);
            vl.Add(new Vertex { Position = new Vector3(cos * 0.5f, -0.5f, sin * 0.5f), Normal = new Vector3(cos, 0, sin) });
        }

        for (int s = 0; s < segments; s++)
        {
            il.Add((ushort)0);
            il.Add((ushort)(capTopStart + s + 1));
            il.Add((ushort)(capTopStart + s));

            il.Add((ushort)1);
            il.Add((ushort)(capBotStart + s));
            il.Add((ushort)(capBotStart + s + 1));

            il.Add((ushort)(sideTopStart + s));
            il.Add((ushort)(sideTopStart + s + 1));
            il.Add((ushort)(sideBotStart + s));

            il.Add((ushort)(sideBotStart + s));
            il.Add((ushort)(sideTopStart + s + 1));
            il.Add((ushort)(sideBotStart + s + 1));
        }

        v = vl.ToArray();
        ind = il.ToArray();
    }

    private static void GenerateCircle(out Vertex[] v, out ushort[] i, int segments = 32)
    {
        v = new Vertex[segments + 1];
        i = new ushort[segments * 3];
        v[0] = new Vertex { Position = Vector3.Zero, Normal = Vector3.UnitY };
        for (int s = 0; s < segments; s++)
        {
            float angle = ((float)s / segments) * MathF.PI * 2f;
            v[s + 1] = new Vertex { Position = new Vector3(MathF.Cos(angle) * 0.5f, 0, MathF.Sin(angle) * 0.5f), Normal = Vector3.UnitY };
        }
        for (int s = 0; s < segments; s++)
        {
            i[s * 3] = 0; i[s * 3 + 1] = (ushort)(s + 1); i[s * 3 + 2] = (ushort)(s == segments - 1 ? 1 : s + 2);
        }
    }

    private ID3D12Resource CreateBuffer<T>(T[] data) where T : unmanaged
    {
        int size = data.Length * Unsafe.SizeOf<T>();
        var res = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer((ulong)size), ResourceStates.GenericRead);
        unsafe { void* ptr; res.Map(0, &ptr); fixed (T* src = data) { Buffer.MemoryCopy(src, ptr, size, size); } res.Unmap(0); }
        return res;
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
            for (int vert = 0; vert < 4; vert++) v[(face * 4) + vert] = new Vertex { Position = p[f[face][vert]], Normal = n[face] };
            i[(face * 6) + 0] = (ushort)((face * 4) + 0); i[(face * 6) + 1] = (ushort)((face * 4) + 2); i[(face * 6) + 2] = (ushort)((face * 4) + 1);
            i[(face * 6) + 3] = (ushort)((face * 4) + 0); i[(face * 6) + 4] = (ushort)((face * 4) + 3); i[(face * 6) + 5] = (ushort)((face * 4) + 2);
        }
    }

    private static void GenerateQuad(out Vertex[] v, out ushort[] i)
    {
        v = new Vertex[4] {
            new Vertex { Position = new Vector3(-0.5f, 0, -0.5f), Normal = Vector3.UnitY },
            new Vertex { Position = new Vector3( 0.5f, 0, -0.5f), Normal = Vector3.UnitY },
            new Vertex { Position = new Vector3(-0.5f, 0,  0.5f), Normal = Vector3.UnitY },
            new Vertex { Position = new Vector3( 0.5f, 0,  0.5f), Normal = Vector3.UnitY }
        };
        i = new ushort[6] { 0, 1, 2, 2, 1, 3 };
    }

    private static void GenerateSphere(out Vertex[] v, out ushort[] ind, int lat, int lon, float r)
    {
        var vl = new List<Vertex>(); var il = new List<ushort>();
        for (int y = 0; y <= lat; y++)
        {
            float phi = y / (float)lat * MathF.PI;
            for (int x = 0; x <= lon; x++)
            {
                float theta = x / (float)lon * MathF.PI * 2;
                Vector3 p = new Vector3(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta));
                vl.Add(new Vertex { Position = p * r, Normal = p });
            }
        }
        for (int y = 0; y < lat; y++)
        {
            for (int x = 0; x < lon; x++)
            {
                int i0 = (y * (lon + 1)) + x, i1 = i0 + lon + 1;
                il.Add((ushort)i0); il.Add((ushort)(i0 + 1)); il.Add((ushort)i1);
                il.Add((ushort)i1); il.Add((ushort)(i0 + 1)); il.Add((ushort)(i1 + 1));
            }
        }
        v = vl.ToArray(); ind = il.ToArray();
    }

    public void Dispose()
    {
        _commandQueue?.Dispose(); _device?.Dispose(); _swapChain?.Dispose(); _fenceEvent?.Dispose();
    }

    public void Render(Camera camera, RenderQueue queue)
    {
        BeginFrame();

        var vp = camera.ViewMatrix * camera.ProjectionMatrix;

        ReflectionPass(queue, vp, camera.Position);
        OpaquePass(queue, vp, camera.Position);
        TransparentPass(queue, vp, camera.Position);

        EndFrame();
    }

    void ReflectionPass(RenderQueue queue, Matrix4x4 vp, Vector3 camPos)
    {
        // Use transparent PSO for reflections so they don't break depth testing underneath the table
        _commandList.SetPipelineState(_pipelineStateTransparent);

        foreach (ref readonly var item in queue.Items)
        {
            if (item.Material != MaterialType.Ball) continue;

            var world = item.World * Matrix4x4.CreateScale(1, -1, 1);
            var color = item.Color * 0.25f;
            color.W = 1f;

            DrawItem(item.Mesh, world, vp, camPos, color, 2f);
        }
    }

    void OpaquePass(RenderQueue queue, Matrix4x4 vp, Vector3 camPos)
    {
        // Use Opaque PSO for solid objects to properly write depth
        _commandList.SetPipelineState(_pipelineStateOpaque);

        foreach (ref readonly var item in queue.Items)
        {
            if (item.Material != MaterialType.Ball)
            {
                continue;
            }

            DrawItem(item.Mesh, item.World, vp, camPos, item.Color, (float)item.Material);
        }
    }

    void TransparentPass(RenderQueue queue, Matrix4x4 vp, Vector3 camPos)
    {
        // Use Transparent PSO to prevent glass/holograms from occluding each other
        _commandList.SetPipelineState(_pipelineStateTransparent);

        foreach (ref readonly var item in queue.Items)
        {
            if (item.Material == MaterialType.Ball)
            {
                continue;
            }

            DrawItem(item.Mesh, item.World, vp, camPos, item.Color, (float)item.Material);
        }
    }

    void DrawItem(MeshType mesh, Matrix4x4 world, Matrix4x4 vp, Vector3 camPos, Vector4 color, float material)
    {
        switch (mesh)
        {
            case MeshType.Cube: DrawMesh(_vBufferCube, _iBufferCube, (int)(_iBufferCube.Description.Width / 2), world, vp, camPos, color, material); break;
            case MeshType.Sphere: DrawMesh(_vBufferSphere, _iBufferSphere, (int)(_iBufferSphere.Description.Width / 2), world, vp, camPos, color, material); break;
            case MeshType.Circle: DrawMesh(_vBufferCircle, _iBufferCircle, (int)(_iBufferCircle.Description.Width / 2), world, vp, camPos, color, material); break;
            case MeshType.Quad: DrawMesh(_vBufferQuad, _iBufferQuad, 6, world, vp, camPos, color, material); break;
            case MeshType.Cylinder: DrawMesh(_vBufferCylinder, _iBufferCylinder, (int)(_iBufferCylinder.Description.Width / 2), world, vp, camPos, color, material); break;
        }
    }

    void BeginFrame()
    {
        var allocator = _commandAllocators[_frameIndex];
        allocator.Reset();

        // Start the frame expecting standard opaque objects
        _commandList.Reset(allocator, _pipelineStateOpaque);
        _commandList.SetGraphicsRootSignature(_rootSignature);

        _commandList.RSSetViewport(new Viewport(0, 0, _width, _height));
        _commandList.RSSetScissorRect(new RectI(0, 0, _width, _height));
        _commandList.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);

        // Transition our off-screen MSAA target to RenderTarget status
        _commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(_msaaRenderTargets[_frameIndex], ResourceStates.ResolveSource, ResourceStates.RenderTarget));

        var rtvHandle = _rtvHeap.GetCPUDescriptorHandleForHeapStart();
        rtvHandle.Ptr += (nuint)(_frameIndex * _rtvDescriptorSize);
        var dsvHandle = _dsvHeap.GetCPUDescriptorHandleForHeapStart();

        _commandList.OMSetRenderTargets(rtvHandle, dsvHandle);
        _commandList.ClearRenderTargetView(rtvHandle, new Color4(0.05f, 0.1f, 0.05f, 1.0f));
        _commandList.ClearDepthStencilView(dsvHandle, ClearFlags.Depth, 1.0f, 0);
    }

    void EndFrame()
    {
        // 1. Prepare MSAA target to be read from
        _commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(_msaaRenderTargets[_frameIndex], ResourceStates.RenderTarget, ResourceStates.ResolveSource));

        // 2. Prepare SwapChain target to be written to
        _commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(_renderTargets[_frameIndex], ResourceStates.Present, ResourceStates.ResolveDest));

        // 3. Command the GPU to compress the 4x MSAA image down into the final 1x image!
        _commandList.ResolveSubresource(_renderTargets[_frameIndex], 0, _msaaRenderTargets[_frameIndex], 0, Format.R8G8B8A8_UNorm);

        // 4. Prepare SwapChain target to be presented to the monitor
        _commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(_renderTargets[_frameIndex], ResourceStates.ResolveDest, ResourceStates.Present));

        _commandList.Close();
        _commandQueue.ExecuteCommandList(_commandList);
        _swapChain.Present(1, PresentFlags.None);
        _commandQueue.Signal(_fence, ++_fenceValue);

        if (_fence.CompletedValue < _fenceValue)
        {
            _fence.SetEventOnCompletion(_fenceValue, _fenceEvent.SafeWaitHandle.DangerousGetHandle());
            _fenceEvent.WaitOne();
        }

        _frameIndex = (int)_swapChain.CurrentBackBufferIndex;
    }
}