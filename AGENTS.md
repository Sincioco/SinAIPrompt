# Sin - AI Prompt

Native Windows HTML editor cloned from Sin - Notepad. Keep its WPF shell, tabs/document list,
file conflict protection, atomic saves, autosave, and recovery. HTML editing runs in WebView2
with native JavaScript modules, no framework or bundler. Document scripts must not execute
inside the editor. PMT is a read-only reference for RTE and Diagram 2 behavior.

Keep modules focused. Do not add dependencies or tests without a concrete need.

## Commits

Every Codex-created commit subject starts exactly with `Sin and Codex: `.
Nontrivial commits include a detailed body with Summary, Changes, Validation, and Known limitations.
Describe actual behavior and only checks performed. Commit coherent validated work; never
rewrite pushed history or discard unrelated work. Exclude binaries, profiles, documents, and credentials.

## Verification

Build the .NET solution, syntax-check JavaScript, run the relevant browser and native integration
smoke tests, and run `git diff --check`. Use isolated profiles under ignored `work/` or TEMP.
Package with `Build.ps1 -Package`. At handoff identify restart/rebuild requirements.

No databases, third-party libraries, npm packages, downloaded tools, or network package restores.
Use only Windows and basic Visual Studio 2026 components. Reference the installed Microsoft
WebView2 assemblies in Visual Studio, never NuGet. NuGet.Config clears all package sources.
Keep internal JSON in the configurable application storage folder, outside user HTML folders.
