namespace SinAIPrompt.Core;

// Physical folders are the project identity; Settings owns only their known paths.
public static class PromptProjects
{
    public const string Prefix = "Project - ";

    public static string Create(string parentFolder, string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Enter a project name.");
        string folderName = Prefix + name;
        TextFiles.ValidateFileName(folderName);
        string parent = Normalize(parentFolder), path = Path.Combine(parent, folderName);
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("The parent folder no longer exists.");
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("A project with that name already exists in this folder.");
        Directory.CreateDirectory(path);
        return path;
    }

    public static void Remember(Settings settings, string folder)
    {
        string path = Normalize(folder);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("The project folder no longer exists.");
        if (!settings.ProjectFolders.Any(value => string.Equals(Normalize(value), path, StringComparison.OrdinalIgnoreCase)))
            settings.ProjectFolders.Add(path);
    }

    public static IReadOnlyList<string> Known(Settings settings) => settings.ProjectFolders
        .Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists)
        .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();

    static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
