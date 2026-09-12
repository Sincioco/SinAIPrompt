using System.Runtime.InteropServices;

namespace SinAIPrompt;

// Windows pointer sensitivity, changed only for choosing the first capture point.
// No preference is written to disk; every exit restores the original value.
internal sealed class CapturePointerSpeed : IDisposable
{
    readonly int original;
    bool changed;
    internal int Current { get; private set; }
    internal CapturePointerSpeed()
    {
        int value = 10;
        if (Read(0x70, 0, ref value, 0)) original = Current = value;
    }
    internal void Adjust(int delta)
    {
        if (original == 0) return;
        int value = Math.Clamp(Current + delta, 1, 20);
        if (Write(0x71, 0, (nint)value, 0)) { Current = value; changed = Current != original; }
    }
    internal void Restore()
    {
        if (changed && Write(0x71, 0, (nint)original, 0)) { Current = original; changed = false; }
    }
    public void Dispose() => Restore();
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] static extern bool Read(uint action, uint parameter, ref int value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] static extern bool Write(uint action, uint parameter, nint value, uint flags);
}
