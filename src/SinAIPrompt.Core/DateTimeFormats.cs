using System.Globalization;

namespace SinAIPrompt.Core;

public static class DateTimeFormats
{
    public const int Count = 6;
    public const int Default = 2;
    public static int NormalizeChoice(int choice) => choice >= 0 && choice < Count ? choice : Default;

    public static string Format(DateTime value, int choice)
    {
        string pattern = NormalizeChoice(choice) switch
        {
            0 => "dddd, MMMM d, yyyy 'at' h:mm tt",
            1 => "M/d/yyyy",
            2 => "M/d/yyyy h:mm tt",
            3 => "yyyyMMddHHmm",
            4 => "yyyy-MM-dd HHmm",
            _ => "yyyy-MM-dd' - 'HHmm"
        };
        return value.ToString(pattern, CultureInfo.GetCultureInfo("en-US"))
            .Replace("AM", "am", StringComparison.Ordinal).Replace("PM", "pm", StringComparison.Ordinal);
    }
}
