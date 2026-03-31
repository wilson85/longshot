using System.Numerics;
using ImGuiNET;

namespace LongShot;

public static class ImGuiManager
{
    private const bool ENABLED = false;
    private const float FLT_MAX = 3.402823466e+38F;

    public static void Initialize(int width, int height)
    {
        if (!ENABLED)
        {
            return;
        }
        ImGui.CreateContext();
        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new Vector2(width, height);
        io.Fonts.AddFontDefault();

        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
    }

    public static void UpdateInput(InputState input, float deltaTime, bool isCursorCaptured)
    {
        if (!ENABLED)
        {
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        io.DeltaTime = deltaTime;

        // If the cursor is captured/hidden by the game, ImGui shouldn't use it.
        if (isCursorCaptured)
        {
            io.AddMousePosEvent(-FLT_MAX, -FLT_MAX); // Hide ImGui cursor
        }
        else
        {
            io.AddMousePosEvent(input.MouseX, input.MouseY);
        }

        io.AddMouseButtonEvent(0, input.IsLeftMouseDown);
        io.AddMouseButtonEvent(1, input.IsRightMouseDown);
    }

    public static void Shutdown()
    {
        if (!ENABLED)
        {
            return;
        }

        ImGui.DestroyContext(); 
    }
}