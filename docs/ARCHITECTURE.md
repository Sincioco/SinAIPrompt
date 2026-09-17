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
| Documents and window UI | `SinAIPrompt/MainWindow.xaml.cs`: document list, active document, lazy editor instances, autosave queues and navigation state | Calls Core, `EditorView`, search pane and document ordering. Native navigation, 100-document startup, save/conflict checks. |
| Find/Replace | `SearchBar.xaml/.cs` owns the left Find pane, query/options/debounce/status and virtualized result list; `EditorSearch.cs` adapts one editor; `Web/document-search.js` owns visible text mapping, excerpts, highlights and smooth match navigation, with a disposable `search-worker.js` matcher; Core `SearchEngine.cs` owns source-text matching | No search operation switches view modes. Explicit editor callbacks and request/result values; no window reference in search UI/model. Browser highlights never enter saved HTML. Source behavior passed before extraction; visual/native checks cover regex, timeouts, inline formatting, replacement and Ctrl+F. |
| Document order | `DocumentOrder.cs` owns sorting subscriptions and cached metadata loading; each `Document` persists pin/created/modified values and Settings persists sort mode | Reorders existing models without editor creation. Missing legacy timestamps load on a worker after window construction. A narrow callback restores selection after moves. Native order/persistence and 100-document lazy-loading checks. |
| Prompt Explorer | `PromptExplorer.xaml/.cs` owns the working folder, loaded branches, selection cancellation, folder visibility and debounced refresh; Core `PromptDirectory.cs` reads directory metadata, using `ImageReferences.cs` for local references | Explicit open/rename/delete/new/path callbacks and immutable path/HTML snapshots. Enumeration and usage checks run on workers and never create editors. `ExplorerFileOperations.cs` owns Windows location commands, recycle calls and folder eligibility; the existing FileActions integration flushes editors and pauses autosave. Native checks cover live usage colors, valid-reference rejection, recycle/cancel, previews and navigation. |
| Navigation layout and file previews | `NavigationLayout.cs` owns pane visibility/width; `ExplorerPreview.cs` owns one disposable, read-only image/text/PDF preview; `ExplorerIcons.cs` caches three frozen Windows Shell stock icons | Layout extracted under passing existing navigation/startup checks. Previews are outside the document/save/recovery collection; the existing host delegates selection and preserves its last active document. PDF uses installed WebView2 with scripts, external requests and downloads disabled. No new window reference or application dependency in these owners. |
| Image-file rename | `ImageFileRename.cs` owns destination validation and the file/parent-save transaction; `Web/asset-references.js` rewrites matching local image sources relative to the explicit parent HTML | Existing `HtmlFileRename.cs` coordinates open copies, paused autosaves and conflicts; `HtmlEditorHost.cs` is the parser/live-editor adapter. Checks cover encoded/absolute/base-relative URLs, unsaved edits, collision rejection and rollback. No parent editor is created for an unopened file. |
| Markdown and list numbering | `Web/markdown.js` owns safe Markdown conversion and empty-document paste; `list-numbering.js` owns current-list operations. `EditorClipboard` reads file/text clipboard data and `MarkdownExport.cs` coordinates one editor's existing asset export path | No libraries or runtime downloads. Browser checks cover common structures, nested numbering and Undo; native checks paste a real local Markdown file and export independent PNG assets. |
| File operations and annotation hosting | `FileActions.cs`, `HtmlFileActions.cs`, `HtmlFileRename.cs`, `AnnotationHost.cs`: partial `MainWindow` implementations sharing that window's state | Existing legacy integration boundaries, not independent state owners. Native file/asset and modal-layout checks. |
| Visual/source editing adapter | `EditorView.cs` + `HtmlEditorHost.cs`: one document's WPF source view, WebView lifecycle, synchronization, mapping, native messages | Calls the window and platform adapters. Existing reverse coupling must not spread. Source/visual, immediate save, image, typing tests. |
| Document data and persistence | `SinAIPrompt.Core/Documents.cs`: `Document`, settings/session records, `TextFiles`, `Store`, numbering, search | Core uses .NET APIs; no dependency on the WPF app or WebView. Save/conflict, recovery, numbering checks. |
| Platform services | `HtmlAssets.cs`, `AnnotationClipboard.cs`, `FileAssociations.cs`, `StorageLocation.cs`; `Dialogs.cs` builds native dialogs | Narrow Windows/file operations. Clipboard, PNG, storage and native dialog tests. |
| Screen capture | `ScreenCapture.cs` owns Win32 monitor/window enumeration and physical-pixel capture; each `ScreenCaptureDialog` owns its picker, cancellable countdown and window restoration; `CaptureRegionWindow` owns selection over one frozen bitmap | The host passes only its owner window and receives an in-memory PNG. Capture/encoding run on workers; the browser reuses annotation and image storage. `ScreenCaptureSelfTest` covers actual window pixels, picker initialization, cursor option, delay, cancellation, scaling and negative coordinates. |
| Browser editor | `SinAIPrompt/Web/editor.js`: live document, caret/selection, pending synchronization and exports | Uses `document.js`, annotation UI, highlighting, and bridge. Browser integration suite. |
| Document styling and formatting | `Web/document-styles.js` owns Modern presets and CSS. `ribbon.js` owns gallery/painter and marker color state; `word-styles.js` owns paragraph operations; `text-formatting.js` owns a document's pending insertion font | Editor supplies document/selection/change callbacks. `ribbon-self-test.js` covers paragraph scope, spacing, fonts, undo, Enter, painter, clipboard and selected highlight visibility. No imports back into the editor. |
| Code and link insertion | `Web/code-blocks.js` owns the temporary code form and full/preview/collapsed markup; `link-insertion.js` owns one insert-link form. `LinkPreview.cs` owns bounded asynchronous HTTP metadata/image reads and PNG conversion | Narrow document/command callbacks, no imports back to editor. `source-highlighting.js` remains a pure lexical formatter. Browser checks cover code modes, persistence, Undo, escaping and link cancellation; offline native HTTP fixtures validate preview extraction and fallback. |
| Image content identity | `HtmlAssets.cs` hashes normalized PNG bytes on a worker, reusing matching assets without a persistent index. `EditorClipboard.cs` owns Windows clipboard formats; custom fragments carry document identity and original image references | Current image bytes determine the hash after crop/annotation. Standard clipboard HTML remains portable with embedded images. Native/browser checks cover reuse, renamed files, replaced content, crop hashes and same-document cut/paste. No shared mutable hash cache or new startup scan. |
| Image insertion and actions | `Web/image-insertion.js` owns one editor's storage preference and insertion workflow. `image-actions.js` owns temporary dropdown/fullscreen chrome; `image-selection.js` retains selection and resize gesture state. `ImageExternalViewer.cs` prepares decoded preview PNGs in application storage | Narrow callbacks to editor commands, selection, annotation and native requests. Settings persists one default mode; explicit region capture overrides it. Existing reuse/save/clipboard behavior remains covered; browser tests exercise the dropdown, all resize corners, storage defaults and fullscreen cleanup. |
| YouTube playback and fullscreen | `Web/youtube.js` owns static card markup and one runtime player per editor; `LinkPreview.cs` retrieves bounded metadata/optional local thumbnails. `EditorFullscreen.cs` owns one window-layout memento and an explicit expand callback | Runtime player iframe lives in the trusted outer page; the editable document keeps its script-disabled sandbox. Native host closes playback when hidden. Existing annotation host is reused solely to expand the browser; no new MainWindow state or algorithms. Offline metadata tests plus browser/native fullscreen and live YouTube playback checks. |
| Color palettes | `Web/color-picker.js` owns each temporary popup; callers own color values. `annotation-colors.js` adapts existing inspector values/events | Annotation retains scene/history ownership. Native color inputs replaced without moving annotation state. Browser palette/transparency checks. |
| Annotation crops | `Web/annotation-crop.js` owns crop controls, inset/radius calculations and baking one image layer; `annotation-ui.js` retains scene, selection and undo history | Explicit object arguments; no import back into the UI or editor. Existing crop behavior passed before extraction; browser checks cover synchronized/independent radii, reset, baked pixels, positioning and undo. |
| Settings and editor chrome | `SettingsDialog.cs` owns tabbed settings controls; `Settings.ShowToolbar` persists visibility. `ToolbarVisibility` filters blank-menu double clicks with an explicit settings object and apply callback. `EditorPathStatus` owns one editor's hovered/selected image source and displayed path | The native host binds the status text and receives image-status messages. Browser/native checks cover settings save, toolbar visibility, local image paths and document fallback. No new state in MainWindow. |
| Document copies and revert | `DocumentCopies.cs` owns duplicate naming and copy creation, using the existing browser export/asset relocation path. `FileActions.cs` coordinates selected document, paused autosaves, progress and revert confirmation | Saved copies own their own image folders; drafts embed image copies. Native checks cover current unsaved text, names, collisions, separate/inline images, original-file preservation, cancel and confirmed revert. |
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

Prompt 34's region shortcut reuses the capture dialog's countdown and restoration.
`CaptureMagnifier.cs` owns only its frozen-bitmap preview and pointer-monitor corner
placement; `CaptureRegionWindow` still owns marquee geometry and the resulting crop.
The direct flow captures all virtual-desktop monitors without cursor composition or
darkening, and returns PNG data through the existing editor/storage owners. No
startup scans, additional platform dependencies, or guardrail exceptions were added.

## Enforced now

Run `./scripts/Test-Architecture.ps1 -Report` for measured file sizes.
`Build.ps1` runs the checker before compilation. `Test.ps1` runs its fixture tests;
both normal and `-Packaged` validation enforce the repository rules. There is no
existing CI configuration to connect; these local entry points are the gate.

| Classification | Warning above | Failure above |
| --- | ---: | ---: |
| Application entry/composition (`App.xaml.cs`) | 200 physical lines | 300 |
| Other handwritten modules, markup, tooling, and tests | 500 | 800 |
| Legacy `MainWindow.xaml.cs` | 500 | **564 (no growth; reviewed reductions from 616 and 580)** |

Physical lines include blank lines and comments; a final newline does not create
an extra line. Files without a final newline are counted too. Tests use the same
file budget for now. `MainWindow.xaml.cs` is classified by its actual window-UI
role, not the word "Main" in its name; its mixed responsibilities require the
stricter 564-line legacy ceiling recorded with a stable ID and review trigger.

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

Iteration 1 keeps date-prefixed draft naming in `DocumentFactory`, path-copy text
in `FileActions`, and Save/Save All/Rename in the existing native file operations.
The ribbon sends the existing document command message; no additional global
state, application entry-point logic, or dependency is introduced. The Editor
button reuses the existing selected-image/blank annotation entry point. New blank
canvases ask for image storage when applied, so opening the editor is immediate.

Preset data lives in `document-styles.js`. The 17:06 request removes the Office
choice and uses Modern for new and existing documents; known style presets migrate
when loaded. Saved/exported HTML includes its CSS without a reference to the sample.
Modern uses Segoe UI, 16px text, 1.65 line spacing, heading dividers and paragraph/list
spacing. Arbitrary imported CSS and character formatting can still override it.
Paragraph/content changes retain native Undo; stylesheet initialization does not
create a document-wide Undo operation.

The Insert Link regression is fixed in the shared form dialog: Cancel bypasses
constraint validation, while Apply still validates the address. Its browser
check covers both empty and invalid addresses, cancellation without a content
change, and successful insertion at the saved selection.

The 12:02 enhancements revise Modern Title/Heading/Heading2 to 34/22/28 points,
matching the numeric toolbar's units. Existing saved formatting is preserved
until the user reapplies a preset or switches document mode. Ribbon controls and
the Document List header remain in their existing UI owners.

Screen capture uses Windows GDI and DWM APIs, with no downloaded components or
new dependency. Window capture brings the chosen window forward and captures
its visible desktop pixels; it does not reconstruct obscured/off-screen content
or bypass protected surfaces. Region selection overlays a frozen capture, so
its controls and selection cursor cannot appear in the PNG. Include Cursor is
off by default and composites the current Windows pointer, with its hotspot,
before region selection. Capture and insertion do not write to the clipboard.
Browser integration checks cover captured-image cancellation and applying at
the saved caret through the normal storage choice. Live mixed-DPI/multi-monitor,
HDR and protected-window behavior remain hardware/manual validation areas;
scaled/negative-coordinate geometry is covered automatically. MainWindow's
616-line baseline, dependency directions and guardrail limits are unchanged.

The 12:14 iteration changes Modern Heading2 to 18 points (Title remains 34 and
Heading 22). The Styles grid caps at its intrinsic five-tile width, with Editor
immediately to its right. Settings UI moved out of the shared dialog utility
into its own owner; the window entry/coordination file remains at 616 lines.
Formatting report preferences are recorded permanently in `AGENTS.md`.

`CaptureGallery` owns visual choices grouped by monitor. Monitor previews are
snapshots taken before the picker appears and retained for that invocation;
refresh updates the window list without capturing the picker inside itself.
`WindowThumbnail` owns each Windows DWM thumbnail relationship, clips it to the
scroll viewport, and unregisters it when unloaded/disposed. Live previews do not
activate source windows or send blocking capture messages to other apps. The
picker covers the owner's content area. Native tests inspect real thumbnail
pixels, monitor cards, selected capture, cancellation and restoration. Some
windows can provide unavailable/blank previews through DWM; actual capture still
uses the previously documented visible-desktop behavior.

Crop baking replaces a layer's source PNG and removes its reversible crop data;
it preserves canvas placement and opacity. Reset All Crops removes only current
reversible masks/radii. Undo retains pre-bake pixels for the current annotation
session; Apply To Document persists the new layer source. This does not overwrite
an independently imported image file on disk. The white workspace is a visual
surface; output transparency and chosen canvas colors retain their meaning.
Draft duplicates embed their copied assets instead of creating a new unsaved
asset-folder convention. Saved duplicates use the existing separate-image flow.
Existing browser/native adapter coupling and settings/storage integration debt
remain; no guardrail limits or exclusions were changed.

The 12:54 iteration extracted search from MainWindow after adding and passing
source-search behavior checks. The reviewed window baseline fell from 616 to 580
lines under the same `main-window-ui` record; limits for other modules and all
exclusions are unchanged. Fixture tests now derive boundary cases from that
record so a reviewed reduction does not leave hard-coded obsolete test sizes.

Visual search maps text nodes once per content revision, uses binary lookup for
match ranges, and paints highlights without inserting markup. Since 15:20 all matches are highlighted; range construction yields between batches of 2,000.
Matching runs in a worker that is terminated after 750 ms; .NET source regex uses
a 250 ms timeout on a worker. Count/navigation remain available beyond the paint
limit. Regex follows JavaScript syntax in visual mode and .NET syntax in source
mode; zero-length matches are skipped. Replace All retains inline markup using
native editing operations; browser Undo follows those individual edits.

Ribbon Rename was opening WPF modal UI inside a WebView callback. The bridge now
yields back to the dispatcher before handling messages, allowing callbacks needed
by the rename to complete. The regression runs the actual ribbon button, native
dialog, image-folder rename, encoded references, and unsaved-edit preservation.

Newest First means last modification time; Date Created also orders newest first.
Pins remain above either order and persist with session recovery. Drag reordering
selects Manual Order. Unknown timestamps in old recovery records are populated
from file metadata without reading HTML; unavailable files use a fallback date.

Markdown paste autoformats an empty editor, including clipboard .md/.markdown
files and their relative images, using Modern. Supported content includes common
headings, emphasis, links/images, lists, quotes, fenced code and pipe tables. Raw
HTML input is escaped. Export keeps the HTML document open and writes UTF-8 .md
plus separate PNGs through the existing asset path. Markdown cannot preserve
arbitrary fonts, colors, canvas editing metadata, or page layout; this is a focused
implementation rather than a full CommonMark parser. Numbering changes use an
explicit li value and an undoable replacement of the current list, avoiding
Chromium's loss of outer ol start attributes during list merging.


The 15:20 iteration keeps numbering in `list-numbering.js`, Find in the existing
search UI/browser/Core owners, and capture/annotation state in their existing
owners. `image-selection.js` owns the selected inline image, four corner handles,
its resize gesture and a temporary drag shield over the iframe. The editor only
wires selection, change and path-status callbacks. The shield prevents pointer
routing into the child frame during a drag; handles never enter saved HTML.

List operations select from inside the first item to inside the last item so
Chromium cannot absorb the following heading. Bullet conversion includes the
whole list. Explicit numbers remain standard li values; small per-item metadata
records continuation/restart intent and distinguishes an anchor from a clone
created by Enter or Redo. A single pass updates linked section numbers. Enter at
the start of an item transfers the anchor to the preceding blank item. HTML
reopening retains this intent, and subsequent Numbering commands can continue it.
Plain paragraphs use a valid div container during native list conversion so
saved/reopened HTML cannot turn an invalid nested paragraph/list into orphan items.

Find displays paragraph excerpts and counts in a virtualized left pane. Selecting
an excerpt smoothly scrolls to its range. Runtime CSS highlights use yellow and
are removed on close or content invalidation. Source search uses corresponding
line excerpts. Image annotation fits visible artwork on opening, selects its image
layer, and starts captured images in Crop mode; capture inside an existing
annotation adds a layer through the same Windows picker and Undo history.

Markdown export offers relative references or absolute file URIs to the exported
PNGs. Both modes create independent assets beside the Markdown file and preserve
the open HTML document. Absolute references are specific to the export location
on this computer. The native export adapter owns the choice of destination base;
the browser converter still receives only a callback that saves one image.
File ribbon icons are local SVG drawings following the supplied Word reference.

Regression coverage includes real browser pointer/keyboard editing, the numbering
dialog, continuation across sections and reopening, Enter/Undo/Redo, all four
inline resize corners, native Find results/highlight cleanup, capture within the
annotation dialog, native rename selection, and both Markdown path modes.
MainWindow.xaml.cs remains at its reviewed 580-line baseline. No dependency,
guardrail exception, expanded exclusion, or new bootstrap responsibility was added.

## Prompt Explorer (16:34)

The 16:34 Prompt Explorer change separates directory metadata/navigation, read-only
previews and image-file transactions. Navigation visibility/width moved out of the
window after the existing packaged suite passed; the expanded suite also preserves
100-document lazy restoration and original Document List operations. MainWindow's
reviewed ceiling drops from 580 to 564; no exclusions or limits increased.
`FileActions` and `HtmlFileRename` remain legacy window integration points, now
delegating the new operations to their owners. There are no new partial families,
entry-point algorithms, dependency cycles or runtime packages.

Image renaming changes matching local `img[src]` references in the corresponding
parent HTML, preserving URL form and query/fragment suffixes. References in other
documents, CSS, or arbitrary user scripts are outside this parent-image workflow.
PNG/JPG/GIF previews show a still image; TXT/Markdown are read-only text, and PDF is
the built-in offline viewer. Very large/network folders remain dependent on disk
latency, with metadata work and the progress indicator separate from editing.

The numbering test's fixed 20 ms dialog delay occasionally expired before the
close handler applied its operation under load. Its helper now waits for the actual
dialog close event before checking results; numbering implementation is unchanged.

## Prompt 33 and image/file workflow additions

The existing editor orchestrator delegates code and link dialogs to focused owners.
Expandable code uses native HTML details/summary and saves the full source once;
preview text is omitted from Markdown export. Large snippets retain a plain-text
fallback. The lexer cannot reproduce compiler/project-specific symbol coloring.
Palette values were compared with the running Visual Studio 2026 Light editors
and installed color definitions; Java uses IntelliJ Default.

Image identity belongs to the asset writer, so permanent crops and annotation
updates cannot retain a stale hash. The clipboard holds the same-document fragment;
there is no application-wide fragment cache. Unused-file checks parse local image,
source-set, link and inline/style-sheet URL references in supplied HTML; they do not
execute scripts or fetch linked stylesheets. Folder deletion checks current parent
HTML (disk only if unopened) and other open documents. Unopened unrelated documents
are outside this document-owned folder workflow. Shell recycling preserves a folder
as a unit; no permanent deletion fallback exists. Full paths must stay within the
parent's asset directory, and linked ancestor folders are rejected.

The existing window file-operation integration pauses autosaves, flushes affected
editors and closes deleted document/preview views. Native filesystem and reference
algorithms live outside MainWindow. Its reviewed 564-line baseline is unchanged;
no guardrail exceptions, new dependencies, cycles or entry-point feature state.

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

## YouTube cards and compact navigation (19:21)

`video-selection.js` owns one editor's selected video, temporary border/toolbar,
resize gesture and edit dialog. It receives selection, command, image-clear and
playback-stop callbacks; it never imports the editor orchestrator. `youtube.js`
retains card markup, URL recognition, asynchronous metadata lookup and the runtime
player. Paste inserts the card immediately and only enriches the same unchanged,
still-open card when metadata returns. No startup work or global image/video cache
was added. Stored HTML remains script-free card markup; playback stays outside the
script-disabled document iframe.

Chromium's native commands descend into populated noneditable figures and can merge
adjacent floating figures. The bounded `editVideoAtoms` transaction temporarily makes
existing cards empty inline atoms during synchronous replacement, cut or paste, then
restores their original children/styles on the nodes retained by native Undo. There
is no separate history stack or persistent transaction state. Browser checks capture
resize/Undo, cut/Undo, replacement beside another wrapped card, unchanged neighboring
markup, and editing alongside an active player. These operations remain sensitive to
future changes in the installed WebView2 editing engine and should retain coverage.

`NavigationLayout` now owns independent tab/sidebar visibility and persisted width.
`TabStripLayout` owns tab widths, scroll buttons and wheel scrolling, with only the
WPF controls passed to it. Nullable saved tab visibility preserves old layouts.
`ribbon-overflow.js` moves existing group elements into a popup when wrapping is off;
there are no cloned controls or duplicate handlers. Nested palette and Styles popups
remain within that popup so they preserve their anchor and the editor selection.
The normal wrapping preference remains the default.

MainWindow delegates layout and wires menu/preferences; it shrank from 564 to 562
lines. The reviewed maximum remains 564. No limits/exclusions were changed, no
exception was granted, and no dependencies, cycles or reverse imports were added.
Existing native partial-class and host/EditorView coupling remain recorded debt.

## Document Content (20:54)

The native ribbon/navigation layout stays in `EditorChromeLayout` and `RibbonWebView`.
One existing WebView renders the full-width ribbon and inset document; a Windows
window region leaves the native navigation/Find panes visible beneath the ribbon.
The region is temporarily restored for browser dialogs/popovers and reset for
fullscreen/reparenting. The installed composition-control alternative threw
PlatformNotSupportedException during actual startup, so it is not used. No SDK,
package, runtime target change, extra browser, or downloaded component was added.

`DocumentContents` owns the active outline list and delayed refresh. Its browser
adapter `document-outline.js` reads Title/Heading/Heading2 nodes and navigates by
transient indexes without adding IDs to saved HTML. PromptExplorer retains the
exclusive view-mode preference. Inactive documents are not opened or parsed.

Core `DocumentAccess` owns Windows ReadOnly attributes; each Document caches the
state for binding. `DocumentLock` coordinates saving and confirmation with a narrow
save callback and synchronizes other open copies. EditorView and document-access.js
reflect that state on source/visual controls. Native mutators, replacement, saves
and autosave respect it; Save As can create an unlocked copy. Session recovery
retains any pre-existing unsaved changes in another locked copy.

Core `UnusedImages` scans only on request, with explicit folder/snapshot inputs.
The native cleanup dialog owns progress, review, confirmation and a fresh reference
check before recycling. Existing reference parsing and Windows recycle operations
are reused. No startup scan or persistent image index was introduced.

`RegionCaptureOptions` owns the native options form and returns an immutable value
to the existing capture workflow. Image actions reuse the existing filename dialog
and transactional reference rewrite. `video-presentation.js` owns static URL,
Inline, Card and Embed markup; video-selection.js retains temporary selection UI
and native editing/Undo. Cards show available title, author, provider and thumbnail;
no fictional description or publication date is inserted.

MainWindow is 563 lines, within its unchanged 564-line ceiling. New behavior is in
focused owners, with one layout composition call and existing integration points.
No limits, exclusions, baselines or dependencies changed. Existing partial-class
host coupling remains debt; this iteration does not add a new partial family.

## Ribbon Changes (22:04)

`EditorSurface` owns the visited-editor visual children and selected content. It switches
visibility instead of detaching and reattaching native browser windows on every document
selection. The existing editor dictionary still owns creation and lifetime; disposal
releases the visual child, and annotation explicitly transfers the active child. No
unvisited document is initialized, and there is no additional browser or shared document
state. `EditorChromeLayout` retains inactive navigation insets; `HtmlEditorHost` suppresses
identical inset messages. `ribbon-overflow.js` preserves control identities and moves groups
only when their destination changes, prioritizing the full Styles gallery over Tools.

`annotation-pan.js` owns only a view translation and its pointer gesture. It never changes
scene coordinates, selection or history, and leaves middle-button behavior intact.
`CaptureRegionWindow` retains ownership of selection, including sticky click/click capture.
`CapturePointerSpeed` owns temporary Windows pointer sensitivity and restores it on first
click, cancellation, deactivation and closure without writing a Windows preference file.
`CaptureMagnifier` owns pixel-grid rendering and the speed label; the existing capture
dialog owns the larger translucent countdown. FileActions reuses save/lock operations
before producing Copy for AI Use text; Dialogs preserves extensions for document/image
rename, while unsaved draft names and folders remain whole names.

Validation covers the existing document/annotation behaviors before the host change plus
new rapid-switch, layout, pan, capture, rename and save/lock regressions. The MainWindow
implementation remains 563 lines against its unchanged 564-line ceiling. No guardrail
exception, dependency, startup scan, history replacement or new global state was added.
First visits still pay the existing lazy WebView initialization cost; this change removes
repeated teardown and layout churn for visited documents. Windows pointer sensitivity is
a temporary system setting while initial-point capture is active, restored on normal exits.

## Splash Screen (22:51)

`BrandingWindow` owns shared splash/About presentation and dismissal, with one cached,
frozen local bitmap. It owns the About links and compiler-generated build date. App
startup only creates/shows/disposes it around session restoration and yields once to
paint; there is no minimum display timer. MainWindow delegates Help/About in one line.
All seven supplied images live permanently in `Assets/Splash Screens`; only the chosen
2400-by-1440 artwork is embedded. The canonical PNG/ICO pair holds the V2 application icon.

`EditorChromeLayout` retains the last measured ribbon height for an uninitialized editor
and observes settled layout with a cached tuple, avoiding repeated browser/native writes.
`HtmlEditorHost` remains the per-editor measurement owner and supplies initial visibility
and wrapping preferences in the local editor URL. Browser bootstrap creates the ribbon
before awaiting local fonts. No inactive editor initialization or shared document state
was added. PromptExplorer explicitly chooses a text font for its navigation menu.
`ribbon-overflow.js` uses native popover-invoker toggling to avoid dismiss/reopen races.
Annotation reuses the existing region-capture request and image-layer insertion path.

Regression coverage includes first-visit height reservation, hidden initial ribbon,
navigation resize/toggle, physical-pointer overflow toggles, annotation region capture
and cancellation, and About dismissal. MainWindow grew 563 to 564 lines, exactly its
unchanged reviewed ceiling. No limits, exclusions, dependencies or exceptions changed.
Existing host/partial-class coupling remains debt. First visits still initialize their
WebView lazily; this task preserves space during that work rather than preloading files.

## Markdown Support (23:49 request; September 13 implementation)

`MarkdownImport` owns reading a source and publishing a new sibling HTML file, using an
explicit conversion callback. Existing `MarkdownExport` provides the narrow per-editor
Markdown conversion adapter; `Web/markdown.js` remains the only Markdown parser and uses
the existing Modern CSS owner. Conversion uses a detached DOM, never the live document.
Writes run on a worker and publish by a non-overwriting move, with numbered names on
collision. The existing FileActions owner now contains OpenPaths coordination extracted
from MainWindow, plus the small status-bar progress indicator and file-drop routing.

Explorer selection and Markdown drops create read-only previews instead of converted
files. `ExplorerPreview` retains the disposable preview lifetime and native request
restrictions. Its local `preview.html`/`preview.js` shell runs only trusted application
code; the rendered document lives in an iframe without script permission. Only mapped
local resources are allowed. `file-drop.js` forwards real Windows file objects using the
installed WebView2 AdditionalObjects API, verified by reflection and actual file drops.
No arbitrary page can send a file-open command through the sandboxed document.

`DocumentContents` owns its independent pane, close button, persisted visibility and
current outline; PromptExplorer again owns only Document List/Explorer switching.
EditorChromeLayout incorporates the additional pane in its existing inset calculation.
The browser outline filter excludes nested noneditable objects, not the entire locked
document body. `editor-chrome.js` owns ribbon measurements and popup lifecycle reporting;
removed popups restore the native window cutout even when no toggle event bubbles.

PromptExplorer records the row selected during a refresh so WPF's deferred selection
event cannot reopen an old preview. Leaving Explorer cancels pending selection work.
MainWindow delegates selected-document mouse navigation, including returning from a
preview when the already-selected HTML row is clicked. The window shrank 564 to 545
lines. Its existing 564-line ceiling remains unchanged; no exceptions, dependency
additions or rule changes were made. Existing host partial-class coupling remains debt.

EditorView's existing lifecycle owns startup cancellation: disposal cancels initialization
waiters, and each asynchronous startup continuation checks disposal before using WebView2.
An aborted startup after disposal is no longer reported as a missing runtime. Real active
startup failures retain the source fallback and error dialog. `EditorStartupSelfTest`
owns a test-process-only Windows dialog observer and close-during-startup checks; native
dialogs are recorded separately from browser exceptions and fail the integration run.

The September 13 Font Color correction stays in `ribbon.js` and `ribbon.css`: the
existing button value owns its remembered ink, initially red, and the existing picker
continues to own only its popup. Explicit glyph/bar/chevron geometry replaces conflicting
size overrides. No additional state owner, file, dependency or startup work was introduced.
The ribbon regression checks rendered geometry and ink retention across caret movements.

## Popup navigation visibility (September 13, 00:02 Bugs)

`editor-chrome.js` now reports each open dialog/popover rectangle and modal state,
coalescing geometry changes on animation frames and observing open-popup resizing.
`RibbonWebView` owns those transient rectangles and unions only their occupied areas
with its existing ribbon/document region, scaling CSS coordinates by browser zoom and
monitor DPI. It never removes the whole navigation cutout just because a popup opens.
The previous boolean overlay behavior caused the three reported blank-sidebar cases.

`HtmlEditorHost` remains a narrow message adapter. `EditorChromeLayout` keeps the native
panes visible and disables them while a modal is active, without taking over popup state.
No new MainWindow state, startup work, files, dependencies or reverse dependencies were
introduced. Native regression checks cover all three actual controls in both navigation
modes, modal input blocking, bounded overlap and removal cleanup. Full-window captures
include the real WPF sidebar; browser-only screenshots cannot validate native navigation.
Existing host partial-class coupling remains debt; architecture limits are unchanged.

## Document-switch painting and V3 icon (September 13)

`EditorSurface` owns the selected child, retention of visited editors, and one temporary
ribbon fallback during a first visit. A first-paint signal from the editor replaces
that fallback after initial formatting/layout has rendered; stale completions cannot
change the current selection. The fallback is disabled and exposes no old document body.
It now switches native clipping and input eligibility instead of WPF visibility;
the previous visibility transition was still interrupting WebView2 presentation.
`RibbonWebView` owns one presentation mode and reuses its existing region calculation
to clip inactive browsers completely, including their popups. WPF stacking keeps the
selected source view or preview above retained controls. Annotation transfers the
selected editor through the existing host with its complete browser surface.

`HtmlEditorHost` explicitly stops media when an editor is deselected and checks the
actual displayed editor before completing a deferred focus request. No shared ribbon,
document state, dependency, preload, or startup scan was introduced. First visits still
initialize their editor lazily; visited documents keep their browser and ribbon state.
Regression checks cover first-visit ribbon handoff, rapid-switch visibility/layout stability, inactive input and
native clipping, source/preview display, and annotation transfer/return. MainWindow
remains unchanged at 545 lines; no limits, baselines or exceptions changed.

The canonical `Assets/SinAIPrompt.png` now permanently contains the supplied V3 artwork,
with a nine-resolution ICO generated using Windows drawing APIs. Existing executable,
WPF window and file-association resource wiring consumes that ICO; splash art is separate.

## Content View resizing and Photos image paste (September 13, 01:16)

`DocumentContents` owns its right-edge resize handle, keyboard resizing, preferred width,
and available-space limit. `Settings.ContentWidth` persists the preference with the old
220-pixel default. Headings wrap within the list viewport. The existing chrome layout
already observes the pane's measured width and updates the document inset, so no new
window coordination or sibling-column wiring is needed. The pane reserves editor space
while temporarily clamping its width when the window gets smaller.

`editor-clipboard.js` routes external, image-only HTML through the existing image-insertion
owner, which reads local pixels through `HtmlAssets`, normalizes PNGs and applies storage
preferences and hashing. It does not insert an unreadable file URL into the sandbox.
`EditorClipboard` marks application image fragments for markup preservation, both for
same-document references and portable cross-document copies, keeping their annotation
metadata and intended size. Mixed rich-text HTML continues through its existing path.
The annotation adapter uses decoded scene width when the source image has no natural
dimensions instead of retaining the browser's broken-image placeholder width.

Regressions cover image-only file HTML, both storage modes, repeated-image reuse,
cross-document annotation/size preservation, broken-image repair, pane drag/inset updates,
wrapping, width persistence and available editor space. MainWindow remains 545 lines;
no dependencies, modules, guardrail exceptions or startup scans were added. Existing
native-host coupling remains debt. Tests reproduce captured Windows Photos HTML byte
offsets and comment markers alongside its Bitmap/FileDrop alternatives, using isolated
test data rather than changing the user's Windows clipboard.

Explicit outline refreshes cancel their pending debounce timer. Screen-capture test
cleanup now awaits the editor-load promise before the next outline scenario begins,
preventing overlapping fixture restoration from replacing the next test document.

## Prompt 57 navigation and ribbon actions (September 14)

`PromptExplorer` keeps its shared folder chooser visible in both navigation modes
and owns direct mode toggling. Changing folders retains the open-document list and
active editor. `FileActions.CreateDocumentMenu` wires both tabs and document rows
to the existing `DocumentLock` save/confirmation/read-only operation.

`ribbon.js` owns the remembered highlight color, its apply action and collapsed
caret. The existing color picker accepts a separate trigger button while retaining
its own popup lifetime; font and annotation palettes keep their existing defaults.
The two split controls share CSS rules. HTML modal backdrops are transparent while
modal input blocking remains intact. The native host already disables bundled
WebView resource caching on each launch.

Newest modification remains the default order in `Settings`/`DocumentOrder`, with
explicit pins and manual dragging preserved. Existing default-order coverage now
also checks timestamp updates. Native/browser regressions cover shared navigation,
menu locking, palette/application behavior, collapsed-caret typing and backdrops.
No new modules, dependencies, MainWindow state or guardrail exceptions were added.

## Original image references and ribbon enhancements (September 15, 18:21)

`OriginalImages` owns one editor's exact-file display map and local URI resolution.
It serves asynchronous file reads through WebView2 resource responses; the map never
exposes a whole drive or starts a network service. `Web/original-images.js` prepares
transient `srcset` display URLs and removes them during serialization and clipboard
copy. Saved `src` values remain ordinary relative/file URIs. The existing insertion,
clipboard and file-drop owners retain original paths; the native host only adapts
requests and the existing Save flow. Runtime maps live and die with their editor.

Save As rebases original references after removing the old document base. Different
drives require absolute paths; insertion offers that fallback explicitly. Annotation
edits use the existing inline/separate storage flow and never overwrite the original.
Standalone exports and cross-document copies still embed portable pixels; Markdown
export resolves originals before creating its independent PNG assets. Missing
original files display as unavailable without preventing the document from opening.

Settings owns the shared ten-color history. `RecentColors` maintains unique newest-first
values; each palette owns only its popup and temporary event listeners. Browser-only
preview history uses local storage in the palette module. Font/highlight callers own
their remembered color values. `ribbon-overflow.js` moves existing Tools buttons between
the strip and overflow, retaining handlers and selection; it does not recreate controls.

Validation covers relative/absolute paths outside the document folder, cut/paste,
Save As, original-file preservation, color history persistence, actual pointer dismissal,
chevron geometry and individual Tools wrapping. Existing newest-modification ordering
remains the default, with pins and explicit sort choices preserved. MainWindow remains
545 lines. No guardrail limits, exceptions, startup scans or dependencies were added;
existing host/partial-class coupling remains recorded debt.

## Bundled pinned references (September 17)

`BundledDocuments` owns the two immutable resource names, startup restoration into an
explicit session, and non-overwriting publication of independent working copies. It
receives session/folder/name snapshots and uses Core file I/O; it never accesses a
window, application singleton, editor, or global mutable state. Exact original HTML
lives in `Assets/Documents` and is embedded, with no build-time external path.

App delegates restoration once before constructing windows. Existing document models,
paths, and unsaved recovery text survive; only missing files are created and references
are pinned. Existing DocumentOrder/session persistence own ordering and saved pin state.
FileActions wires the two Help commands to this owner, shows the existing asynchronous
load indicator, then opens/selects a new pinned document. The window stays interactive
during file I/O. Numbered destinations reserve open names and publish without replacement.

Focused native checks cover exact resource bytes, repeat startup, missing-file recovery,
edited disk and unsaved session preservation, collisions, and both actual Help handlers.
MainWindow.xaml.cs remains at 545 lines. No baseline, exclusion, dependency, or exception
was added; existing host partial-class coupling remains debt.
