using System.IO;
using System.Text;
using System.Text.Json;
using SinAIPrompt.Core;

namespace SinAIPrompt;

internal static class PromptProjectMoveSelfTest
{
    internal static Task Run(string folder, Action<bool, string> check) => Task.Run(() =>
    {
        string root = Path.Combine(folder, "project-moves-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var settings = new Settings();
        string project = PromptProjects.Create(root, "人物與世界 🌌");
        PromptProjects.Remember(settings, project); PromptProjects.Remember(settings, project + Path.DirectorySeparatorChar);
        settings.ProjectFolders.Add(Path.Combine(root, "Project - Missing"));
        var restored = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings))!;
        check(Path.GetFileName(project) == "Project - 人物與世界 🌌" && PromptProjects.Known(restored).SequenceEqual(new[] { project }),
            "Project creation uses a physical prefixed Unicode folder and its deduplicated registry survives settings serialization");
        check(Rejected(() => PromptProjects.Create(root, "人物與世界 🌌")) && Rejected(() => PromptProjects.Create(root, "../escape")),
            "Project creation rejects existing names and names containing folder traversal");
        check(PromptProjects.DisplaySuffix(Path.Combine(project, "Notes", "Prompt.HTML")) == " (人物與世界 🌌)" &&
            PromptProjects.DisplaySuffix(Path.Combine(project, "project - Inner", "Prompt.html")) == " (Inner)" &&
            PromptProjects.DisplaySuffix(null) == "" && PromptProjects.DisplaySuffix(Path.Combine(root, "Prompt.html")) == "" &&
            PromptProjects.DisplaySuffix(Path.Combine(project, "picture.png")) == "",
            "Project labels use the nearest project ancestor for HTML only, without requiring a saved file or directory lookup");

        string source = Path.Combine(root, "Sources"); Directory.CreateDirectory(source);
        string first = Path.Combine(source, "角色 #1.html"), second = Path.Combine(source, "Mixed.html");
        const string original = "<html>\r\n<p>已儲存</p>\r\n<a href=\"../reference.html\">reference</a>\r\n</html>";
        File.WriteAllText(first, original, TextFiles.EncodingFor("UTF-16 LE"));
        byte[] mixed = [.. new UTF8Encoding(true).GetPreamble(), .. Encoding.UTF8.GetBytes("<p>One</p>\r\n<p>Two</p>\n<p>Three</p>\r")];
        File.WriteAllBytes(second, mixed);
        string assets = Path.Combine(source, "角色 #1"), nested = Path.Combine(assets, "Nested"); Directory.CreateDirectory(nested);
        Directory.CreateDirectory(Path.Combine(assets, "Empty"));
        string image = Path.Combine(nested, "影像 #1.png"); byte[] imageBytes = [1, 7, 4, 2, 250, 0, 12]; File.WriteAllBytes(image, imageBytes);
        File.SetAttributes(first, File.GetAttributes(first) | FileAttributes.ReadOnly | FileAttributes.Hidden);
        File.SetAttributes(image, File.GetAttributes(image) | FileAttributes.ReadOnly);
        var open = TextFiles.Open(first); open.Text += "\n<p>Unsaved buffer remains open</p>";
        string unsaved = open.Text; FileAttributes attributes = File.GetAttributes(first);
        var snapshots = new[] { PromptProjectMove.Read(first, project), PromptProjectMove.Read(second, project) };
        snapshots[0] = snapshots[0] with { Html = snapshots[0].Html.Replace("../reference.html", "../../reference.html", StringComparison.Ordinal) };
        var progress = new List<(int, int)>(); var moved = PromptProjectMove.Commit(snapshots, (done, total) => progress.Add((done, total)));
        var disk = TextFiles.Open(snapshots[0].DestinationPath);
        check(moved.Count == 2 && !File.Exists(first) && !File.Exists(second) && !Directory.Exists(assets) && progress.SequenceEqual(new[] { (1, 4), (2, 4), (3, 4), (4, 4) }),
            "A project batch moves every selected HTML and matching image folder and reports staging/publication progress");
        check(disk.Text.Contains("../../reference.html") && disk.EncodingName == "UTF-16 LE" && disk.NewLine == "\r\n" && File.GetAttributes(disk.Path!) == attributes && moved[0].IsReadOnly,
            "Rewritten project HTML retains its encoding, BOM, newline convention and read-only/hidden attributes");
        check(open.Path == first && open.Text == unsaved && open.Dirty && !disk.Text.Contains("Unsaved buffer") && moved[0].Html == disk.Text && moved[0].Fingerprint == disk.Fingerprint,
            "Project disk transactions preserve detached unsaved buffers and return normalized saved HTML with the actual fingerprint");
        check(File.ReadAllBytes(snapshots[1].DestinationPath).SequenceEqual(mixed), "An unchanged project move preserves original bytes including mixed line endings");
        string movedImage = Path.Combine(snapshots[0].DestinationAssets!, "Nested", "影像 #1.png");
        check(File.ReadAllBytes(movedImage).SequenceEqual(imageBytes) && File.GetAttributes(movedImage).HasFlag(FileAttributes.ReadOnly) && Directory.Exists(Path.Combine(snapshots[0].DestinationAssets!, "Empty")) && moved.All(result => result.CleanupWarning == null),
            "Project assets retain nested Unicode names, exact bytes, empty folders and file locks without recovery debris");

        string collisionSource = Path.Combine(root, "Collision sources"); Directory.CreateDirectory(collisionSource);
        string a = Write(collisionSource, "A.html"), b = Write(collisionSource, "B.html");
        string collisionProject = PromptProjects.Create(root, "Collision");
        var collisionBatch = new[] { PromptProjectMove.Read(a, collisionProject), PromptProjectMove.Read(b, collisionProject) };
        File.WriteAllText(collisionBatch[1].DestinationPath, "Existing destination");
        check(Rejected(() => PromptProjectMove.Commit(collisionBatch)) && File.Exists(a) && File.Exists(b) && !File.Exists(collisionBatch[0].DestinationPath) && File.ReadAllText(collisionBatch[1].DestinationPath) == "Existing destination",
            "A collision late in a project batch rejects the entire batch before moving any source");
        File.Delete(collisionBatch[1].DestinationPath); Directory.CreateDirectory(Path.Combine(collisionProject, "B"));
        check(Rejected(() => PromptProjectMove.Commit(collisionBatch)) && File.Exists(a) && File.Exists(b), "Project preflight also rejects corresponding asset-folder collisions even when the source has no assets");
        string duplicateFolder = Path.Combine(root, "Duplicate source"); Directory.CreateDirectory(duplicateFolder);
        string duplicate = Write(duplicateFolder, "A.html");
        check(Rejected(() => PromptProjectMove.Commit([collisionBatch[0], PromptProjectMove.Read(duplicate, collisionProject)])) && File.Exists(a) && File.Exists(duplicate),
            "Project preflight detects two selected files that would have the same destination name");

        string rollbackProject = PromptProjects.Create(root, "Rollback");
        Directory.CreateDirectory(Path.Combine(collisionSource, "A")); File.WriteAllText(Path.Combine(collisionSource, "A", "saved.png"), "Saved asset");
        var rollback = new[] { PromptProjectMove.Read(a, rollbackProject), PromptProjectMove.Read(b, rollbackProject) };
        byte[] aBefore = File.ReadAllBytes(a), bBefore = File.ReadAllBytes(b);
        bool rejected = Rejected(() => PromptProjectMove.Commit(rollback, (done, _) =>
        {
            if (done == 3) File.WriteAllText(rollback[1].DestinationPath, "Concurrent destination");
        }));
        check(rejected && File.ReadAllBytes(a).SequenceEqual(aBefore) && File.ReadAllBytes(b).SequenceEqual(bBefore) && File.ReadAllText(Path.Combine(collisionSource, "A", "saved.png")) == "Saved asset" &&
            !File.Exists(rollback[0].DestinationPath) && !Directory.Exists(rollback[0].DestinationAssets) && File.ReadAllText(rollback[1].DestinationPath) == "Concurrent destination",
            "A publication failure rolls back every source pair without overwriting a concurrently created destination");
        check(!Directory.EnumerateDirectories(root, ".sin-project-*", SearchOption.AllDirectories).Any(), "Successful moves and successful rollbacks remove their staging/recovery folders");

        string changedProject = PromptProjects.Create(root, "Changed source"); var changed = PromptProjectMove.Read(a, changedProject);
        File.AppendAllText(a, "\nExternal edit");
        check(Rejected(() => PromptProjectMove.Commit([changed])) && File.ReadAllText(a).EndsWith("External edit") && !File.Exists(changed.DestinationPath),
            "A project move rejects externally modified HTML without losing the newer disk contents");
        changed = PromptProjectMove.Read(a, changedProject);
        check(Rejected(() => PromptProjectMove.Commit([changed], (done, _) =>
        {
            if (done == 1) File.WriteAllText(Path.Combine(changed.SourceAssets!, "saved.png"), "External asset edit");
        })) && File.ReadAllText(Path.Combine(changed.SourceAssets!, "saved.png")) == "External asset edit" && File.Exists(a) && !File.Exists(changed.DestinationPath),
            "Project asset verification catches changes made during staging before reserving any originals");

        foreach (bool editAsset in new[] { false, true })
        {
            string editedProject = PromptProjects.Create(root, editAsset ? "Edited destination asset" : "Edited destination HTML");
            var edited = new[] { PromptProjectMove.Read(a, editedProject), PromptProjectMove.Read(b, editedProject) };
            byte[] saved = File.ReadAllBytes(a);
            bool kept = Rejected(() => PromptProjectMove.Commit(edited, (done, _) =>
            {
                if (done != 3) return;
                File.AppendAllText(editAsset ? Path.Combine(edited[0].DestinationAssets!, "saved.png") : edited[0].DestinationPath, " New external destination edit");
                File.WriteAllText(edited[1].DestinationPath, "Concurrent collision");
            }));
            check(kept && File.ReadAllBytes(a).SequenceEqual(saved) && File.Exists(b) && File.Exists(edited[0].DestinationPath) && Directory.Exists(edited[0].DestinationAssets) &&
                File.ReadAllText(editAsset ? Path.Combine(edited[0].DestinationAssets!, "saved.png") : edited[0].DestinationPath).Contains("New external destination edit"),
                "Rollback retains a published pair edited externally in its " + (editAsset ? "image folder" : "HTML") + " while restoring original source files");
        }

        if (!string.Equals(Path.GetPathRoot(root), Path.GetPathRoot(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
        {
            string otherVolume = Path.Combine(Path.GetTempPath(), "SinAI-project-move-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(otherVolume);
            try
            {
                string crossSource = Write(otherVolume, "Cross volume.html"), crossAssets = Path.Combine(otherVolume, "Cross volume"); Directory.CreateDirectory(crossAssets);
                File.WriteAllBytes(Path.Combine(crossAssets, "image.png"), imageBytes);
                string crossProject = PromptProjects.Create(root, "Other volume"); var cross = PromptProjectMove.Read(crossSource, crossProject);
                var result = PromptProjectMove.Commit([cross]);
                check(result.Count == 1 && !File.Exists(crossSource) && !Directory.Exists(crossAssets) && File.ReadAllBytes(Path.Combine(cross.DestinationAssets!, "image.png")).SequenceEqual(imageBytes),
                    "Project moves copy and verify HTML plus image folders across volumes before deleting originals");
            }
            finally { Directory.Delete(otherVolume, true); }
        }
    });

    static string Write(string folder, string name) { string path = Path.Combine(folder, name); File.WriteAllText(path, "<p>Saved " + name + "</p>"); return path; }
    static bool Rejected(Action action)
    {
        try { action(); return false; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return true; }
    }
}
