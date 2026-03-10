namespace LongShot;


public class InputState
{
    public bool[] Keys { get; } = new bool[256];
    private bool[] PrevKeys { get; } = new bool[256];

    public int MouseX, MouseY, MouseDeltaX, MouseDeltaY, MouseWheelDelta;
    public bool IsLeftMouseDown, IsRightMouseDown;
    private bool PrevLeftMouseDown, PrevRightMouseDown;

    /// <summary>
    /// Call this at the VERY START of your game loop, before processing OS window messages!
    /// </summary>
    public void NewFrame()
    {
        // Snapshot the previous frame's keys so we can detect single presses
        Array.Copy(Keys, PrevKeys, 256);

        PrevLeftMouseDown = IsLeftMouseDown;
        PrevRightMouseDown = IsRightMouseDown;

        MouseDeltaX = 0;
        MouseDeltaY = 0;
        MouseWheelDelta = 0;
    }

    public bool IsKeyDown(int key) => Keys[key];

    // Returns true ONLY on the exact frame the key was pressed down
    public bool IsKeyPressed(int key) => Keys[key] && !PrevKeys[key];

    // Returns true ONLY on the exact frame the key was released
    public bool IsKeyReleased(int key) => !Keys[key] && PrevKeys[key];

    public bool IsLeftMousePressed => IsLeftMouseDown && !PrevLeftMouseDown;
    public bool IsRightMousePressed => IsRightMouseDown && !PrevRightMouseDown;
}