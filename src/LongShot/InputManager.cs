using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace LongShot;

public sealed class InputManager
{
    public InputState State { get; } = new InputState();

    private IntPtr _hwnd;
    public bool IsCursorCaptured { get; private set; } = false;

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

    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        CaptureCursor(true);

        var rid = new RAWINPUTDEVICE[1];
        rid[0].usUsagePage = 0x01; // Generic Desktop Controls
        rid[0].usUsage = 0x02;     // Mouse
        rid[0].dwFlags = 0;
        rid[0].hwndTarget = _hwnd;
        RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    public void CaptureCursor(bool capture)
    {
        IsCursorCaptured = capture;
        if (capture)
        {
            while (ShowCursor(false) >= 0) { }
            if (GetWindowRect(_hwnd, out RECT rect))
            {
                ClipCursor(ref rect);
            }
        }
        else
        {
            while (ShowCursor(true) < 0) { }
            ClipCursor(IntPtr.Zero);
        }
    }

    public void ProcessMessage(uint msg, IntPtr w, IntPtr l)
    {
        // WM_INPUT (Raw hardware movement - avoids OS mouse ballistics)
        if (msg == 0x00FF)
        {
            uint dataSize = 0;
            uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
            GetRawInputData(l, 0x10000003, IntPtr.Zero, ref dataSize, headerSize);

            if (dataSize > 0)
            {
                IntPtr rawInputPtr = Marshal.AllocHGlobal((int)dataSize);
                if (GetRawInputData(l, 0x10000003, rawInputPtr, ref dataSize, headerSize) == dataSize)
                {
                    RAWINPUTHEADER header = Marshal.PtrToStructure<RAWINPUTHEADER>(rawInputPtr);
                    if (header.dwType == 0) // RIM_TYPEMOUSE
                    {
                        IntPtr mousePtr = IntPtr.Add(rawInputPtr, (int)headerSize);
                        RAWMOUSE rawMouse = Marshal.PtrToStructure<RAWMOUSE>(mousePtr);

                        State.MouseDeltaX += rawMouse.lLastX;
                        State.MouseDeltaY += rawMouse.lLastY;
                    }
                }
                Marshal.FreeHGlobal(rawInputPtr);
            }
        }

        // WM_KEYDOWN
        if (msg == 0x0100 && (ulong)w < 256)
        {
            State.Keys[(ulong)w] = true;
            if ((ulong)w == (uint)ConsoleKey.Escape)
            {
                CaptureCursor(!IsCursorCaptured);
            }
        }

        // WM_KEYUP
        if (msg == 0x0101 && (ulong)w < 256)
        {
            State.Keys[(ulong)w] = false;
        }

        // WM_MOUSEWHEEL
        if (msg == 0x020A)
        {
            State.MouseWheelDelta += (short)((ulong)w >> 16);
        }

        // WM_MOUSEMOVE
        if (msg == 0x0200)
        {
            int x = (short)((long)l & 0xFFFF), y = (short)(((long)l >> 16) & 0xFFFF);
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

        // WM_LBUTTONDOWN / UP
        if (msg == 0x0201) State.IsLeftMouseDown = true;
        if (msg == 0x0202) State.IsLeftMouseDown = false;

        // WM_RBUTTONDOWN / UP
        if (msg == 0x0204) State.IsRightMouseDown = true;
        if (msg == 0x0205) State.IsRightMouseDown = false;

        // Auto-recapture mouse if user clicks inside the window
        if ((msg == 0x0201 || msg == 0x0204) && !IsCursorCaptured)
        {
            CaptureCursor(true);
        }
    }
}
