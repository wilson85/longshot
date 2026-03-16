using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LongShot.Engine;
using LongShot.Table;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace LongShot.Rendering;

public enum MeshType { Cube, Sphere, Circle, Quad, Cylinder, TableRails, DynamicTrail }

public sealed class DX12Renderer : IDisposable
{
    private const int FrameCount = 2;
    private const uint SampleCount = 4;

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
    private readonly ID3D12PipelineState _pipelineStateOpaque;
    private readonly ID3D12PipelineState _pipelineStateTransparent;
    private readonly ID3D12PipelineState _pipelineStateReflection;
    private readonly ID3D12PipelineState _pipelineStateTrail;

    private readonly ID3D12Resource _vBufferCube, _iBufferCube;
    private readonly ID3D12Resource _vBufferSphere, _iBufferSphere;
    private readonly ID3D12Resource _vBufferQuad, _iBufferQuad;
    private readonly ID3D12Resource _vBufferCircle, _iBufferCircle;
    private readonly ID3D12Resource _vBufferCylinder, _iBufferCylinder;
    private ID3D12Resource _vBufferRails, _iBufferRails;

    private readonly int _iCountCube, _iCountSphere, _iCountQuad, _iCountCircle, _iCountCylinder;
    private int _iCountRails;

    private ulong _fenceValue = 0;
    private readonly ulong[] _fenceValues = new ulong[FrameCount];
    private int _frameIndex;
    private readonly int _rtvDescriptorSize;
    private readonly int _width, _height;

    public DX12Renderer(IntPtr windowHandle, int width, int height)
    {
        _width = width; _height = height;
        DXGI.CreateDXGIFactory2(false, out IDXGIFactory4 factory);
        D3D12.D3D12CreateDevice(null, Vortice.Direct3D.FeatureLevel.Level_11_0, out _device);
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
            _swapChain = tempChain.QueryInterface<IDXGISwapChain3>();

        _frameIndex = (int)_swapChain.CurrentBackBufferIndex;
        _rtvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, FrameCount));
        _rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        _dsvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.DepthStencilView, 1));

        var optimizedClearValue = new ClearValue(Format.R8G8B8A8_UNorm, new Color4(0.05f, 0.1f, 0.05f, 1.0f));
        var rtvHandle = _rtvHeap.GetCPUDescriptorHandleForHeapStart();
        for (int i = 0; i < FrameCount; i++)
        {
            _renderTargets[i] = _swapChain.GetBuffer<ID3D12Resource>((uint)i);
            _msaaRenderTargets[i] = _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, (uint)_width, (uint)_height, 1, 1, SampleCount, 0, ResourceFlags.AllowRenderTarget),
                ResourceStates.ResolveSource, optimizedClearValue);
            _device.CreateRenderTargetView(_msaaRenderTargets[i], null, rtvHandle);
            _commandAllocators[i] = _device.CreateCommandAllocator(CommandListType.Direct);
            rtvHandle.Ptr += (nuint)_rtvDescriptorSize;
        }

        _depthStencil = _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
            ResourceDescription.Texture2D(Format.D32_Float, (uint)_width, (uint)_height, 1, 1, SampleCount, 0, ResourceFlags.AllowDepthStencil),
            ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 1.0f, 0));
        _device.CreateDepthStencilView(_depthStencil, null, _dsvHeap.GetCPUDescriptorHandleForHeapStart());

        _fence = _device.CreateFence(0, FenceFlags.None);
        _fenceEvent = new AutoResetEvent(false);

        _rootSignature = _device.CreateRootSignature(
            new RootSignatureDescription1(
                RootSignatureFlags.AllowInputAssemblerInputLayout, 
            new[] { new RootParameter1(new RootConstants(0, 0, 48), ShaderVisibility.All) }));

        _pipelineStateOpaque = TrailShader.Init(_device, _rootSignature, SampleCount, false);
        _pipelineStateTransparent = TrailShader.Init(_device, _rootSignature, SampleCount, true);
        _pipelineStateReflection = TrailShader.Init(_device, _rootSignature, SampleCount, true, invertWinding: true);
        _pipelineStateTrail = TrailShader.Init(_device, _rootSignature, SampleCount, true, false, PrimitiveTopologyType.Line);

        _commandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, _commandAllocators[0], null);
        var stgs = new List<ID3D12Resource>();

        // FIX: Extracting the Index staging buffer securely to dispose properly.
        _vBufferCube = CreateStaticBuffer(MeshUtils.GenerateCube, _commandList, out var s1v, out var s1i, out var i1, out _iCountCube); _iBufferCube = i1; stgs.Add(s1v); stgs.Add(s1i);
        _vBufferSphere = CreateStaticBuffer((out Vertex[] v, out ushort[] i) => MeshUtils.GenerateSphere(out v, out i, 64, 64, 0.028575f), _commandList, out var s2v, out var s2i, out var i2, out _iCountSphere); _iBufferSphere = i2; stgs.Add(s2v); stgs.Add(s2i);
        _vBufferQuad = CreateStaticBuffer(MeshUtils.GenerateQuad, _commandList, out var s3v, out var s3i, out var i3, out _iCountQuad); _iBufferQuad = i3; stgs.Add(s3v); stgs.Add(s3i);
        _vBufferCircle = CreateStaticBuffer((out Vertex[] v, out ushort[] i) => MeshUtils.GenerateCircle(out v, out i), _commandList, out var s4v, out var s4i, out var i4, out _iCountCircle); _iBufferCircle = i4; stgs.Add(s4v); stgs.Add(s4i);
        _vBufferCylinder = CreateStaticBuffer((out Vertex[] v, out ushort[] i) => MeshUtils.GenerateCylinder(out v, out i), _commandList, out var s5v, out var s5i, out var i5, out _iCountCylinder); _iBufferCylinder = i5; stgs.Add(s5v); stgs.Add(s5i);

        _commandList.Close();
        _commandQueue.ExecuteCommandList(_commandList);
        WaitForGpu();
        foreach (var s in stgs) s.Dispose();
    }

    public void LoadTableGeometry(TableLayout layout, float railWidth, float railHeight)
    {
        WaitForGpu();
        _vBufferRails?.Dispose(); _iBufferRails?.Dispose();

        MeshUtils.GenerateTableRails(layout, railWidth, railHeight, out var railV, out var railI);
        for (int i = 0; i < railV.Length; i++) railV[i].Color = Vector4.One;

        _commandAllocators[0].Reset();
        _commandList.Reset(_commandAllocators[0], null);
        _vBufferRails = CreateDefaultBuffer(railV, _commandList, out var stgV);
        _iBufferRails = CreateDefaultBuffer(railI, _commandList, out var stgI);
        _iCountRails = railI.Length;
        _commandList.Close();
        _commandQueue.ExecuteCommandList(_commandList);
        WaitForGpu();
        stgV.Dispose(); stgI.Dispose();
    }

    private delegate void MeshGen(out Vertex[] v, out ushort[] i);
    // FIX: Included stgI to prevent staging index buffer leak.
    private ID3D12Resource CreateStaticBuffer(MeshGen gen, ID3D12GraphicsCommandList cmd, out ID3D12Resource stgV, out ID3D12Resource stgI, out ID3D12Resource idx, out int cnt)
    {
        gen(out var v, out var i);
        cnt = i.Length;
        for (int n = 0; n < v.Length; n++) v[n].Color = Vector4.One;
        var vb = CreateDefaultBuffer(v, cmd, out stgV);
        idx = CreateDefaultBuffer(i, cmd, out stgI);
        return vb;
    }

    private ID3D12Resource CreateDefaultBuffer<T>(T[] data, ID3D12GraphicsCommandList cmdList, out ID3D12Resource stagingBuffer) where T : unmanaged
    {
        int size = data.Length * Unsafe.SizeOf<T>();
        var buf = _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None, ResourceDescription.Buffer((ulong)size), ResourceStates.CopyDest);
        stagingBuffer = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer((ulong)size), ResourceStates.GenericRead);
        unsafe
        {
            void* ptr;
            stagingBuffer.Map(0, &ptr);
            fixed (T* src = data) { Buffer.MemoryCopy(src, ptr, size, size); }
            stagingBuffer.Unmap(0);
        }
        cmdList.CopyBufferRegion(buf, 0, stagingBuffer, 0, (ulong)size);
        cmdList.ResourceBarrier(ResourceBarrier.BarrierTransition(buf, ResourceStates.CopyDest, ResourceStates.VertexAndConstantBuffer));
        return buf;
    }

    private void WaitForGpu()
    {
        _fenceValues[_frameIndex] = ++_fenceValue;
        _commandQueue.Signal(_fence, _fenceValues[_frameIndex]);
        _fence.SetEventOnCompletion(_fenceValues[_frameIndex], _fenceEvent.SafeWaitHandle.DangerousGetHandle());
        _fenceEvent.WaitOne();
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
        _commandList.SetPipelineState(_pipelineStateReflection);
        _commandList.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
        foreach (ref readonly var item in queue.Items)
        {
            if (item.Material != MaterialType.Ball) continue;
            var color = item.Color * 0.25f; color.W = 1f;
            DrawItem(in item, item.World * Matrix4x4.CreateScale(1, -1, 1), vp, camPos, color, MaterialType.Ball);
        }
    }

    void OpaquePass(RenderQueue queue, Matrix4x4 vp, Vector3 camPos)
    {
        _commandList.SetPipelineState(_pipelineStateOpaque);
        _commandList.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
        foreach (ref readonly var item in queue.Items)
        {
            if (item.Color.W < 1.0f) continue;
            DrawItem(in item, item.World, vp, camPos, item.Color, item.Material);
        }
    }

    void TransparentPass(RenderQueue queue, Matrix4x4 vp, Vector3 camPos)
    {
        foreach (ref readonly var item in queue.Items)
        {
            if (item.Color.W >= 1.0f) continue;

            // FIX: We must check if the actual topology requires a line strip.
            // Cubes, Quads, and Circles used as Trails/Particles still need TriangleList!
            if (item.Mesh == MeshType.DynamicTrail)
            {
                _commandList.SetPipelineState(_pipelineStateTrail);
                _commandList.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.LineStrip);
            }
            else
            {
                _commandList.SetPipelineState(_pipelineStateTransparent);
                _commandList.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
            }
            DrawItem(in item, item.World, vp, camPos, item.Color, item.Material);
        }
    }

    void DrawItem(in RenderItem item, Matrix4x4 world, Matrix4x4 vp, Vector3 camPos, Vector4 color, MaterialType material)
    {
        uint stride = (uint)Unsafe.SizeOf<Vertex>();
        if (item.Mesh == MeshType.DynamicTrail) stride = (uint)Unsafe.SizeOf<TrailPoint>();

        ID3D12Resource? vb = item.Mesh switch
        {
            MeshType.Cube => _vBufferCube,
            MeshType.Sphere => _vBufferSphere,
            MeshType.Circle => _vBufferCircle,
            MeshType.Quad => _vBufferQuad,
            MeshType.Cylinder => _vBufferCylinder,
            MeshType.TableRails => _vBufferRails,
            MeshType.DynamicTrail => item.CustomBuffer,
            _ => null
        };

        if (vb == null) return;

        var consts = new ObjectConstants { World = world, ViewProj = vp, GlobalColor = color, CameraPosAndMaterial = new Vector4(camPos, (float)material) };
        _commandList.SetGraphicsRoot32BitConstants(0, ref consts);
        _commandList.IASetVertexBuffers(0, new VertexBufferView(vb.GPUVirtualAddress, (uint)vb.Description.Width, stride));

        if (item.Mesh == MeshType.DynamicTrail)
        {
            _commandList.DrawInstanced((uint)item.CustomIndexCount, 1, 0, 0);
        }
        else
        {
            ID3D12Resource? ib = item.Mesh switch
            {
                MeshType.Cube => _iBufferCube,
                MeshType.Sphere => _iBufferSphere,
                MeshType.Circle => _iBufferCircle,
                MeshType.Quad => _iBufferQuad,
                MeshType.Cylinder => _iBufferCylinder,
                MeshType.TableRails => _iBufferRails,
                _ => null
            };
            int count = item.Mesh switch
            {
                MeshType.Cube => _iCountCube,
                MeshType.Sphere => _iCountSphere,
                MeshType.Circle => _iCountCircle,
                MeshType.Quad => _iCountQuad,
                MeshType.Cylinder => _iCountCylinder,
                MeshType.TableRails => _iCountRails,
                _ => 0
            };
            if (ib != null)
            {
                _commandList.IASetIndexBuffer(new IndexBufferView(ib.GPUVirtualAddress, (uint)ib.Description.Width, Format.R16_UInt));
                _commandList.DrawIndexedInstanced((uint)count, 1, 0, 0, 0);
            }
        }
    }

    void BeginFrame()
    {
        _commandAllocators[_frameIndex].Reset();
        _commandList.Reset(_commandAllocators[_frameIndex], _pipelineStateOpaque);
        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.RSSetViewport(new Viewport(0, 0, _width, _height));
        _commandList.RSSetScissorRect(new RectI(0, 0, _width, _height));
        _commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(_msaaRenderTargets[_frameIndex], ResourceStates.ResolveSource, ResourceStates.RenderTarget));
        var rtv = _rtvHeap.GetCPUDescriptorHandleForHeapStart();
        rtv.Ptr += (nuint)(_frameIndex * _rtvDescriptorSize);
        _commandList.OMSetRenderTargets(rtv, _dsvHeap.GetCPUDescriptorHandleForHeapStart());
        _commandList.ClearRenderTargetView(rtv, new Color4(0.05f, 0.1f, 0.05f, 1.0f));
        _commandList.ClearDepthStencilView(_dsvHeap.GetCPUDescriptorHandleForHeapStart(), ClearFlags.Depth, 1.0f, 0);
    }

    void EndFrame()
    {
        _commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(_msaaRenderTargets[_frameIndex], ResourceStates.RenderTarget, ResourceStates.ResolveSource));
        _commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(_renderTargets[_frameIndex], ResourceStates.Present, ResourceStates.ResolveDest));
        _commandList.ResolveSubresource(_renderTargets[_frameIndex], 0, _msaaRenderTargets[_frameIndex], 0, Format.R8G8B8A8_UNorm);
        _commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(_renderTargets[_frameIndex], ResourceStates.ResolveDest, ResourceStates.Present));
        _commandList.Close();
        _commandQueue.ExecuteCommandList(_commandList);
        _swapChain.Present(1, PresentFlags.None);
        _fenceValues[_frameIndex] = ++_fenceValue;
        _commandQueue.Signal(_fence, _fenceValues[_frameIndex]);
        _frameIndex = (int)_swapChain.CurrentBackBufferIndex;
        if (_fence.CompletedValue < _fenceValues[_frameIndex])
        {
            _fence.SetEventOnCompletion(_fenceValues[_frameIndex], _fenceEvent.SafeWaitHandle.DangerousGetHandle());
            _fenceEvent.WaitOne();
        }
    }

    public void Dispose()
    {
        WaitForGpu();
        _swapChain?.Dispose(); _commandQueue?.Dispose(); _device?.Dispose(); _fence?.Dispose(); _fenceEvent?.Dispose();
        _rtvHeap?.Dispose(); _dsvHeap?.Dispose(); _depthStencil?.Dispose(); _commandList?.Dispose(); _rootSignature?.Dispose();
        _pipelineStateOpaque?.Dispose(); _pipelineStateTransparent?.Dispose(); _pipelineStateReflection?.Dispose(); _pipelineStateTrail?.Dispose();
        for (int i = 0; i < FrameCount; i++)
        {
            _renderTargets[i]?.Dispose(); _msaaRenderTargets[i]?.Dispose(); _commandAllocators[i]?.Dispose();
        }
        _vBufferCube?.Dispose(); _iBufferCube?.Dispose(); _vBufferSphere?.Dispose(); _iBufferSphere?.Dispose();
        _vBufferQuad?.Dispose(); _iBufferQuad?.Dispose(); _vBufferCircle?.Dispose(); _iBufferCircle?.Dispose();
        _vBufferCylinder?.Dispose(); _iBufferCylinder?.Dispose(); _vBufferRails?.Dispose(); _iBufferRails?.Dispose();
    }
}