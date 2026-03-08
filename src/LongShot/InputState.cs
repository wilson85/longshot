namespace LongShot;


public class InputState
{
    public bool[] Keys { get; } = new bool[256];
    public int MouseX, MouseY, MouseDeltaX, MouseDeltaY, MouseWheelDelta;
    public bool IsLeftMouseDown, IsRightMouseDown;

    public void ResetDeltas()
    {
        MouseDeltaX = 0;
        MouseDeltaY = 0;
        MouseWheelDelta = 0;
    }
}

