# Architecture and controlled growth

Reviewed September 12, 2026 against the actual C#/WPF/WebView2 implementation.
This map describes the current system, including debt; it is not a claim that its
existing partial classes are independent modules.

## Current owners and dependencies

Paths below are relative to `src/`; unqualified native filenames are under
`SinAIPrompt/`, and `Web/` is that project's browser directory.

| Responsibility | Current owner and state | Dependencies / validation |
| --- | --- | --- |
| Application lifetime | `SinAIPrompt/App.xaml.cs`: windows, single-instance pipe, profile store, settings, recovery scheduling | Composes WPF windows and Core. Startup, session, and storage smoke checks. |
| Documents and window UI | `SinAIPrompt/MainWindow.xaml.cs`: document list, active document, lazy editor instances, autosave queues, search and navigation state | Calls Core and `EditorView`. Native navigation, 100-document startup, save/conflict checks. |
| File operations and annotation hosting | `FileActions.cs`, `HtmlFileActions.cs`, `HtmlFileRename.cs`, `AnnotationHost.cs`: partial `MainWindow` implementations sharing that window's state | Existing legacy integration boundaries, not independent state owners. Native file/asset and modal-layout checks. |
| Visual/source editing adapter | `EditorView.cs` + `HtmlEditorHost.cs`: one document's WPF source view, WebView lifecycle, synchronization, mapping, native messages | Calls the window and platform adapters. Existing reverse coupling must not spread. Source/visual, immediate save, image, typing tests. |
| Document data and persistence | `SinAIPrompt.Core/Documents.cs`: `Document`, settings/session records, `TextFiles`, `Store`, numbering, search | Core uses .NET APIs; no dependency on the WPF app or WebView. Save/conflict, recovery, numbering checks. |
| Platform services | `HtmlAssets.cs`, `AnnotationClipboard.cs`, `FileAssociations.cs`, `StorageLocation.cs`; `Dialogs.cs` builds native dialogs | Narrow Windows/file operations. Clipboard, PNG, storage and native dialog tests. |
| Browser editor | `SinAIPrompt/Web/editor.js`: live document, caret/selection, pending synchronization and exports | Uses `document.js`, annotation UI, highlighting, and bridge. Browser integration suite. |
| Office-style formatting | `Web/ribbon.js` owns gallery/painter UI state; `word-styles.js` owns measured presets and paragraph operations; `text-formatting.js` owns a document's pending insertion font | Editor supplies document/selection/change callbacks. `ribbon-self-test.js` covers paragraph scope, spacing, fonts, undo, Enter, painter and clipboard. No imports back into the editor. |
| Color palettes | `Web/color-picker.js` owns each temporary popup; callers own color values. `annotation-colors.js` adapts existing inspector values/events | Annotation retains scene/history ownership. Native color inputs replaced without moving annotation state. Browser palette/transparency checks. |
| Text clipboard and Office fonts | `EditorClipboard.cs` owns Windows HTML/text exchange and isolated test clipboard; `EditorFonts.cs` owns an immutable cached catalog of existing local Aptos faces | `Web/editor-clipboard.js` operates on an explicit document selection. Font catalog reads run off the UI thread; a local WebView mapping serves existing Office fonts without copying, downloading or exporting them. Actual local-font loads and Unicode clipboard round trips tested. |
| Annotation interaction | `Web/annotation-ui.js`: one dialog's scene, selection, gesture, zoom, history, inspector | Uses model, templates, clipboard, and bridge. Mouse/keyboard, crop, copy/paste, modal tests. |
| Annotation representation | `Web/annotation-model.js`: geometry, movement, SVG/PNG rendering; no persistent scene ownership | Explicit scene/object arguments; image/escaping utilities. Geometry and render behavior covered through browser tests. |
| Exchange and utilities | `Web/templates.js`, `annotation-clipboard.js`, `document.js`, `source-highlighting.js` | Focused data operations. `bridge.js` owns pending native requests and small browser dialogs; it must not become a feature/state hub. |
| Guardrail tooling | `scripts/Test-Architecture.ps1` owns traversal, measurements, and rule evaluation; `architecture-rules.json` owns reviewed limits | No application/runtime dependency. `Test-Architecture.Tests.ps1` owns isolated fixtures and pass/fail checks. |

Dependency direction is host -> Core and browser editor/UI -> model/data/platform
adapters. Core must not reference the host or UI frameworks. Browser utilities
must not import `editor.js` or reach into another dialog's mutable state. Existing
`App.Current`, `window.editor`, and partial-class access are integration debt, not
permission to add broad new globals or reverse dependencies.

## Enforced now

Run `./scripts/Test-Architecture.ps1 -Report` for measured file sizes.
`Build.ps1` runs the checker before compilation. `Test.ps1` runs its fixture tests;
both normal and `-Packaged` validation enforce the repository rules. There is no
existing CI configuration to connect; these local entry points are the gate.

| Classification | Warning above | Failure above |
| --- | ---: | ---: |
| Application entry/composition (`App.xaml.cs`) | 200 physical lines | 300 |
| Other handwritten modules, markup, tooling, and tests | 500 | 800 |
| Legacy `MainWindow.xaml.cs` | 500 | **616 (no growth)** |

Physical lines include blank lines and comments; a final newline does not create
an extra line. Files without a final newline are counted too. Tests use the same
file budget for now. `MainWindow.xaml.cs` is classified by its actual window-UI
role, not the word "Main" in its name; its mixed responsibilities require the
stricter 616-line legacy ceiling recorded with a stable ID and review trigger.

The checker covers `.cs`, `.js`, `.xaml`, `.css`, `.html`, `.ps1`, `.csproj`,
`.props`, and `.targets` anywhere in the checkout, including new directories.
It skips only root `.git`, `.vs`, `app`, `work`, `Data`, `TestResults`, and `bin`/
`obj` directories beneath `src`. These are metadata, packaged/generated output,
or runtime/test profiles. Markdown/JSON/configuration data and binary icons are
not source-line inputs. Generated-looking filenames get no exemption. Source
directory links require explicit classification instead of following them.

Missing/renamed legacy paths fail until their history is deliberately reconciled.
Reductions prompt a reviewed downward baseline update; nothing rewrites baselines
automatically. Do not change limits, classifications, exclusions, or baseline
records merely to make a task pass. Preserve the baseline's identity across moves.

The checker also rejects direct Core project references to the WPF host, explicit
Core WPF/WinForms enablement, and explicit Core UI/WebView assembly references.
It reads project XML, not evaluated MSBuild imports or semantic C# dependencies.
Warnings identify required review; failures stop the build/test entry point.

## Reviewed manually now

File counts cannot establish cohesion, state ownership, dependency-cycle freedom,
or readability. Check the relevant callers and actual state access in the diff.
Do not shorten lines or remove comments to pass. The existing browser/native UI
code has dense lines, so a small physical count is particularly weak evidence.

Use roughly 60 lines as a function target and more than 80 as a review trigger,
not a function-size gate. There is no semantic function/complexity analyzer here.
JavaScript import direction/cycles, C# type-level coupling, partial-class coupling,
MSBuild-imported dependencies, new language classification, and changes to the
rules themselves remain manual review items. Passing checks do not approve them.

## Recorded debt and later triggers

| Evidence at adoption | Next trigger and smallest appropriate action |
| --- | --- |
| `MainWindow.xaml.cs`: 616 lines; search, commands, lifecycle, and UI share window fields | On the next substantive affected feature, capture its behavior first and extract only that responsibility with narrow inputs; tighten the baseline after review. Do not move the entire window into a controller or more coupled partials. |
| `annotation-ui.js`: 214 lines, about 26 KB; a single dialog function holds interaction, rendering updates, and inspector wiring | Before another substantial interaction/inspector feature, separate only its relevant behavior/state boundary. Existing model, clipboard, and templates modules remain the owners of their responsibilities. |
| `Documents.cs`: 211 lines; records, atomic file I/O, recovery storage, numbering, and search coexist | Extract the relevant persistence/search responsibility when a change actually requires it. Preserve HTML, JSON, encoding, and recovery behavior with existing coverage. |
| `App.xaml.cs`: 149 lines; storage relocation is detailed feature work in the bootstrap class | Move relocation into its profile-storage owner when that feature next changes; keep startup/lifecycle coordination here. |
| `HtmlEditorHost.cs` has a `MainWindow` owner; window partials share mutable state; native bridge dispatch is growing | Add new behavior to its owner and use the smallest needed callbacks/arguments at integration points. Avoid a generic command manager or unrestricted state bag. |
| Browser/native smoke suites contain long scenario functions | Split by coherent scenario only when extending the affected area; do not multiply test files just to lower counts. |

Add compiler-backed dependency/function analysis only if concrete drift warrants
it and installed Windows/Visual Studio tooling can provide it without downloads.
Add CI invocation when CI exists. Revisit test budgets, languages, generated-file
classifications, and module contracts as those needs appear, with explicit review.
Do not add speculative frameworks, compile-time infrastructure, or repository-wide
refactors now.

The Office-style ribbon preserves HTML block types and uses explicit paragraph
styles (plus accessible heading roles). It does not implement Word's layout engine
or character-linked styles; the requested happy path applies styles to whole
paragraphs. Presets were measured from the running Word instance on this PC.
Existing locally cached Office fonts are optional data sources for matching that
instance; they are not distributed application components. Other PCs fall back to
installed Aptos or Windows fonts. Saved HTML retains font names, not private cache
paths or runtime font mappings. Existing browser/native integration debt and the
616-line window baseline remain unchanged; no guardrail exceptions were added.

## Template adoption

Source: the five Markdown templates in
`D:\Sin - Notepad - Contents\2026-09-12-0851 Codex Architecture Guardrails`.
The shared policy, existing-project workflow, and routine reminder informed this
setup. Their numeric defaults were adapted as review tripwires, not treated as
official requirements or measurements of architecture quality. The source folder
is a reference only; builds do not depend on it.

The new-project prompt remains a future starting-point reference, not a request
to scaffold or rebuild this app. SMILE/compiler-expansion advice does not apply
to this C#/JavaScript project. No new product features or broad refactor are part
of this adoption.

## Architecture and controlled-growth policy

### Objective

Keep individual behaviors understandable, testable, and changeable within a small,
coherent set of modules. Minimize avoidable future refactoring and agent context
requirements without sacrificing correctness, performance, or readability.

### Before substantial implementation

- Read applicable repository instructions, relevant architecture documentation,
  implementation, callers, and tests.
- Identify the responsibility being added, its state owner, the appropriate module,
  its dependencies, and its validation.
- Reuse an existing cohesive module where appropriate.
- Establish a focused boundary before adding substantial logic that does not
  belong in an existing module.
- Scale planning to the task. Do not write a design document for a trivial change.

### Structure

- Keep Program/Main/bootstrap files limited to startup, wiring, lifecycle
  coordination, delegation, and shutdown.
- Do not put feature algorithms, detailed UI behavior, persistence implementations,
  rendering implementations, or simulation rules in the entry point.
- Give each module a coherent responsibility, explicit ownership of its state,
  and a small public surface.
- Pass only the state and dependencies a module needs.
- Do not introduce dependency cycles, reverse dependencies into entry points,
  broad mutable globals, or generic dumping grounds.
- Do not replace a giant Program file with a giant Controller, Manager, shared
  state bag, or tightly coupled family of files.
- Prefer the simplest adequate design. Do not introduce speculative frameworks,
  unnecessary dependencies, excessive tiny files, or interfaces without a
  concrete purpose.

### Controlled growth

- Follow the repository's file-size and complexity guardrails.
- Treat size thresholds as review triggers, not architectural grades.
- Do not compress statements, delete useful comments, or split files arbitrarily
  to satisfy line-count checks.
- For oversized legacy files, respect reviewed no-growth baselines.
- Do not silently raise limits, reset baselines, expand exclusions, disable checks,
  or grant yourself exceptions.
- A smaller file does not justify adding unrelated responsibilities or moving
  the same monolith into another file.

### Existing code

- Add substantial new behavior in its proper owner.
- Make only the smallest local extraction necessary for the task.
- Preserve behavior and public formats unless changes are authorized.
- Add behavior-capturing tests before risky extraction.
- Keep structural changes distinguishable from behavior changes.
- Do not start a repository-wide refactor as a side effect of a feature.
- Record unrelated architectural debt rather than fixing all of it now.

### Language and runtime constraints

- Verify actual capabilities instead of assuming proposed features exist.
- Report limitations that prevent sound modularity or state ownership.
- Do not silently work around those limitations by centralizing state, and do
  not expand the language/compiler without authorization.

### Completion

- Run relevant tests and available architecture checks.
- Review the diff for new coupling, hidden state, and unnecessary churn.
- Report ownership decisions, changed-file growth, check results, actual
  validation performed, exceptions, and remaining risks.
- Do not treat a passing build or reduced Program line count as proof that the
  architecture is healthy.
