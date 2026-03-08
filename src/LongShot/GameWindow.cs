using System.Runtime.InteropServices;

namespace LongShot;

public sealed class GameWindow : IDisposable
{
    public IntPtr Handle { get; }
    public int Width { get; }
    public int Height { get; }

    public InputManager InputManager { get; } = new InputManager();

    private readonly WndProcDelegate _wndProc;
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr p, IntPtr m, IntPtr inst, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr h, uint mMin, uint mMax, uint rm);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr h, string text);
    [DllImport("user32.dll")] private static extern bool AdjustWindowRect(ref RECT r, uint style, bool menu);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string mod);

    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr h; public uint m; public IntPtr w; public IntPtr l; public uint t; public POINT p; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int l, t, r, b; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WNDCLASSEX { public uint cb; public uint st; public WndProcDelegate proc; public int cbC, cbW; public IntPtr inst, icon, cur, bg; public string menu, cls; public IntPtr smIcon; }

    public GameWindow(int width, int height, string title)
    {
        Width = width;
        Height = height;
        _wndProc = WindowProc;

        var wc = new WNDCLASSEX { cb = (uint)Marshal.SizeOf<WNDCLASSEX>(), st = 3, proc = _wndProc, inst = GetModuleHandle(null), cls = "DX12Class_" + Guid.NewGuid().ToString("N") };
        RegisterClassEx(ref wc);

        var rect = new RECT { r = width, b = height };
        AdjustWindowRect(ref rect, 0x10CF0000, false);

        Handle = CreateWindowEx(0, wc.cls, title, 0x10CF0000, 100, 100, rect.r - rect.l, rect.b - rect.t, IntPtr.Zero, IntPtr.Zero, wc.inst, IntPtr.Zero);
        ShowWindow(Handle, 5);

        InputManager.Initialize(Handle);
    }

    public void SetTitle(string title) => SetWindowText(Handle, title);

    public bool ProcessMessages()
    {
        InputManager.State.ResetDeltas();
        while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, 1))
        {
            if (msg.m == 0x0012) return false; // WM_QUIT
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        return true;
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr w, IntPtr l)
    {
        if (msg == 0x0010) // WM_CLOSE
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        InputManager.ProcessMessage(msg, w, l);
        return DefWindowProc(hWnd, msg, w, l);
    }

    public void Dispose()
    {
        // Destroy window handle natively if necessary
    }
}