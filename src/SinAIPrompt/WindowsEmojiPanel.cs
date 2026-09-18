using System.Runtime.InteropServices;

namespace SinAIPrompt;

// Windows owns the catalog, search, skin tones and insertion into the focused input.
// Call the documented CoreInputView API without Windows SDK/NuGet projections.
internal static class WindowsEmojiPanel
{
    internal static bool IsAvailable => Invoke(null);
    internal static bool Show() => Invoke(true);
    internal static void Hide() => Invoke(false);

    static bool Invoke(bool? show)
    {
        nint name = 0, factory = 0, view = 0, input = 0;
        int initialized = RoInitialize(0); // RO_INIT_SINGLETHREADED; WPF's UI thread.
        try
        {
            Marshal.ThrowExceptionForHR(initialized);
            const string type = "Windows.UI.ViewManagement.Core.CoreInputView";
            Marshal.ThrowExceptionForHR(WindowsCreateString(type, type.Length, out name));
            var statics = new Guid("7D9B97CD-EDBE-49CF-A54F-337DE052907F");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref statics, out factory));
            // IInspectable's six slots precede ICoreInputViewStatics.GetForCurrentView.
            Marshal.ThrowExceptionForHR(Method<GetView>(factory, 6)(factory, out view));
            var version3 = new Guid("BC941653-3AB9-4849-8F58-46E7F0353CFC");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(view, in version3, out input));
            if (show == null) return true;
            byte result;
            // ICoreInputView3: TryShow(), TryShow(kind), TryHide(). Emoji = 3.
            int hr = show.Value ? Method<ShowKind>(input, 7)(input, 3, out result) : Method<HideView>(input, 8)(input, out result);
            Marshal.ThrowExceptionForHR(hr);
            return result != 0;
        }
        catch (COMException) { return false; }
        finally
        {
            if (input != 0) Marshal.Release(input);
            if (view != 0) Marshal.Release(view);
            if (factory != 0) Marshal.Release(factory);
            if (name != 0) WindowsDeleteString(name);
            if (initialized >= 0) RoUninitialize();
        }
    }

    static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetView(nint self, out nint view);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ShowKind(nint self, int kind, out byte result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int HideView(nint self, out byte result);
    [DllImport("combase.dll")] static extern int RoInitialize(uint type);
    [DllImport("combase.dll")] static extern void RoUninitialize();
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string text, int length, out nint value);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] static extern int RoGetActivationFactory(nint name, ref Guid iid, out nint factory);
}
