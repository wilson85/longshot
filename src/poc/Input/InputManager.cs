using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LongShot.Input;

public sealed class InputManager
{
    public InputState State { get; } = new InputState();

    private IntPtr _hwnd;
    public bool IsCursorCaptured { get; private set; } = false;

    // --- Win32 API Imports ---
    [DllImport("user32.dll")] private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
    [DllImport("user32.dll")] private static extern int ShowCursor(bool bShow);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool ClipCursor(ref RECT lpRect);
    [DllImport("user32.dll")] private static extern bool ClipCursor(IntPtr lpRect);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE { public ushort usUsagePage; public ushort usUsage; public uint dwFlags; public IntPtr hwndTarget; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER { public uint dwType; public uint dwSize; public IntPtr hDevice; public IntPtr wParam; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWMOUSE { public ushort usFlags; public ushort usButtonFlags; public ushort usButtonData; public uint ulRawButtons; public int lLastX; public int lLastY; public uint ulExtraInformation; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int l, t, r, b; }

    // --- Windows Message Constants ---
    private const uint WM_INPUT = 0x00FF;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_MOUSEWHEEL = 0x020A;
    private const uint RID_INPUT = 0x10000003;

    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        CaptureCursor(true);

        var rid = new RAWINPUTDEVICE[1];
        rid[0].usUsagePage = 0x01; // Generic Desktop Controls
        rid[0].usUsage = 0x02;     // Mouse
        rid[0].dwFlags = 0;
        rid[0].hwndTarget = _hwnd;

        RegisterRawInputDevices(rid, 1, (uint)Unsafe.SizeOf<RAWINPUTDEVICE>());
    }

    public void CaptureCursor(bool capture)
    {
        IsCursorCaptured = capture;

        // Prevent infinite hangs if the internal Windows display counter gets desynced
        int safety = 0;

        if (capture)
        {
            while (ShowCursor(false) >= 0 && safety++ < 100) { }

            if (GetWindowRect(_hwnd, out RECT rect))
            {
                ClipCursor(ref rect);
            }
        }
        else
        {
            while (ShowCursor(true) < 0 && safety++ < 100) { }
            ClipCursor(IntPtr.Zero);
        }
    }

    public void ProcessMessage(uint msg, IntPtr w, IntPtr l)
    {
        // WM_INPUT (Raw hardware movement - avoids OS mouse ballistics)
        if (msg == WM_INPUT)
        {
            uint dataSize = 0;
            uint headerSize = (uint)Unsafe.SizeOf<RAWINPUTHEADER>();
            GetRawInputData(l, RID_INPUT, IntPtr.Zero, ref dataSize, headerSize);

            if (dataSize > 0)
            {
                // PERFORMANCE FIX: We use stackalloc and unsafe pointers to read the struct directly. 
                // This removes ALL garbage collection allocations when moving the mouse!
                unsafe
                {
                    byte* rawInputPtr = stackalloc byte[(int)dataSize];

                    if (GetRawInputData(l, RID_INPUT, (IntPtr)rawInputPtr, ref dataSize, headerSize) == dataSize)
                    {
                        RAWINPUTHEADER* header = (RAWINPUTHEADER*)rawInputPtr;

                        if (header->dwType == 0) // RIM_TYPEMOUSE
                        {
                            RAWMOUSE* rawMouse = (RAWMOUSE*)(rawInputPtr + headerSize);

                            State.MouseDeltaX += rawMouse->lLastX;
                            State.MouseDeltaY += rawMouse->lLastY;
                        }
                    }
                }
            }
        }

        // WM_KEYDOWN
        if (msg == WM_KEYDOWN && (ulong)w < 256)
        {
            State.Keys[(ulong)w] = true;

            if ((ulong)w == 27) // Virtual Key Code for Escape
            {
                CaptureCursor(!IsCursorCaptured);
            }
        }

        // WM_KEYUP
        if (msg == WM_KEYUP && (ulong)w < 256)
        {
            State.Keys[(ulong)w] = false;
        }

        // WM_MOUSEWHEEL
        if (msg == WM_MOUSEWHEEL)
        {
            State.MouseWheelDelta += (short)((ulong)w >> 16);
        }

        // WM_MOUSEMOVE
        if (msg == WM_MOUSEMOVE)
        {
            int x = (short)((long)l & 0xFFFF);
            int y = (short)(((long)l >> 16) & 0xFFFF);

            State.MouseX = x;
            State.MouseY = y;

            if (IsCursorCaptured && GetWindowRect(_hwnd, out RECT rect))
            {
                int centerX = rect.l + (rect.r - rect.l) / 2;
                int centerY = rect.t + (rect.b - rect.t) / 2;

                // If cursor gets too close to the window border, snap it back to center
                // so we never lose focus.
                if (Math.Abs(x - (centerX - rect.l)) > 100 || Math.Abs(y - (centerY - rect.t)) > 100)
                {
                    SetCursorPos(centerX, centerY);
                }
            }
        }

        // Mouse Button States
        if (msg == WM_LBUTTONDOWN) State.IsLeftMouseDown = true;
        if (msg == WM_LBUTTONUP) State.IsLeftMouseDown = false;
        if (msg == WM_RBUTTONDOWN) State.IsRightMouseDown = true;
        if (msg == WM_RBUTTONUP) State.IsRightMouseDown = false;

        // Auto-recapture mouse if user clicks inside the window
        if ((msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN) && !IsCursorCaptured)
        {
            CaptureCursor(true);
        }
    }
}