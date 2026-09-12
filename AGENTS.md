# Sin - AI Prompt

Native Windows HTML editor cloned from Sin - Notepad. Keep its WPF shell, tabs/document list,
file conflict protection, atomic saves, autosave, and recovery. HTML editing runs in WebView2
with native JavaScript modules, no framework or bundler. Document scripts must not execute
inside the editor. PMT is a read-only reference for RTE and Diagram 2 behavior.

Keep modules focused. Do not add dependencies or tests without a concrete need.

## Architecture and growth

Before substantial edits, read [the module/state map, enforced limits, full policy,
and debt triggers](docs/ARCHITECTURE.md). Identify the behavior owner, required state,
dependencies, validation, and expected growth; keep the assessment proportional.

- Keep bootstrap code about composition/lifecycle and put features in cohesive
  owners with narrow inputs. Do not add globals, cycles, reverse dependencies,
  replacement monoliths, or coupled partial families.
- Reuse an appropriate module. Make only the smallest behavior-covered extraction
  needed for the task; separate structural changes from feature changes.
- `MainWindow.xaml.cs` has a reviewed 616-line no-growth baseline. Respect the
  checked-in limits and exclusions; never reset baselines or grant exceptions to pass.
- Size is a review tripwire, not a quality score. Do not compress code or remove
  useful comments. Record unrelated debt instead of starting a broad refactor.
- Run the architecture checker and relevant tests, review actual coupling/state,
  and report changed-file growth, checks, exceptions, and remaining risks.

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

## Report and output formatting

Permanent project preference from `D:\Sin - AI Prompt - Contents\AI Prompt - Output Formatting.html`:

- Use clear Markdown for technical work, plans, reviews, progress updates, and final reports.
- Use status symbols consistently: ✅ Completed; 🔄 In Progress; ⚠️ Issues Found;
  ❌ Failed; 🧪 Testing; 🔧 Fixing; 📋 Planned / Remaining; 🎯 Objective;
  🏗️ Architecture; 📦 Commit; 🚧 Blocked.
- Use ✅ green checkmarks for completed individual tasks. Do not use completed
  Markdown task checkboxes or strikethrough. Keep completed work visible.
- Use 🔄, 📋, or 🚧 for active, remaining, or blocked work; never mark it completed.
- Put one Markdown horizontal rule before each new substantial status/progress
  report, implementation summary, major update, or final report, immediately
  before its first heading. Do not put rules between every section.
- Organize longer reports with short, scannable Markdown sections, meaningful
  status labels, and bullets. Use headings when permitted by active instructions.
- Use inline code for identifiers and code references where appropriate, fenced
  blocks for code/commands/logs/configuration, and tables for related comparisons.
- Keep symbols semantic; do not decorate ordinary prose with random symbols.
- Final reports distinguish completed work, validation, issues, remaining work,
  and commit information. State actual results and commit hashes. Do not claim
  full completion while known issues or required work remain.
- Continue the bold ready-for-testing and interruption reporting preferences.
