using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SinAIPrompt;

internal static class FileAssociations
{
    const string ApplicationName = "Sin - AI Prompt";
    const string ProgId = "SinAIPrompt.HtmlFile";
    const uint AssociationChanged = 0x08000000;
    const uint FlushNotification = 0x1003;

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    public static void OpenSinAIPromptDefaults()
    {
        Register();
        Thread.Sleep(500);
        OpenSettings("ms-settings:defaultapps?registeredAppUser=Sin%20-%20AI%20Prompt");
    }

    public static void Register()
    {
        string executable = Environment.ProcessPath ?? throw new IOException("The application path is unavailable.");
        using var software = Registry.CurrentUser.CreateSubKey("Software");
        Register(software, executable);
        SHChangeNotify(AssociationChanged, FlushNotification, IntPtr.Zero, IntPtr.Zero);
    }

    internal static void Register(RegistryKey software, string executable)
    {
        string icon = $"\"{executable}\",0";
        string command = $"\"{executable}\" \"%1\"";

        using (var registered = software.CreateSubKey("RegisteredApplications"))
            registered.SetValue(ApplicationName, @"Software\SinAIPrompt\Capabilities");
        using (var capabilities = software.CreateSubKey(@"SinAIPrompt\Capabilities"))
        {
            capabilities.SetValue("ApplicationName", ApplicationName);
            capabilities.SetValue("ApplicationDescription", "HTML editor with image annotation.");
            capabilities.SetValue("ApplicationIcon", icon);
            using var associations = capabilities.CreateSubKey("FileAssociations");
            associations.SetValue(".html", ProgId); associations.SetValue(".htm", ProgId);
        }
        using (var type = software.CreateSubKey(@"Classes\" + ProgId)) type.SetValue(null, "HTML Document");
        using (var defaultIcon = software.CreateSubKey(@"Classes\" + ProgId + @"\DefaultIcon")) defaultIcon.SetValue(null, icon);
        using (var open = software.CreateSubKey(@"Classes\" + ProgId + @"\shell\open\command")) open.SetValue(null, command);
        const string applicationKey = @"Classes\Applications\Sin - AI Prompt.exe";
        using (var application = software.CreateSubKey(applicationKey))
        {
            application.SetValue("FriendlyAppName", ApplicationName);
            using var supported = application.CreateSubKey("SupportedTypes");
            supported.SetValue(".html", ""); supported.SetValue(".htm", "");
        }
        using (var applicationIcon = software.CreateSubKey(applicationKey + @"\DefaultIcon")) applicationIcon.SetValue(null, icon);
        using (var applicationOpen = software.CreateSubKey(applicationKey + @"\shell\open\command")) applicationOpen.SetValue(null, command);
    }

    static void OpenSettings(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
}
