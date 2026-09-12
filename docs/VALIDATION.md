# Validation — 1.0.0

Validated on September 12, 2026 using the local Windows / Visual Studio 2026 installation.

## Application icon

- Preserved the latest supplied `SinAIPrompt.png` in `src/SinAIPrompt/Assets`; SHA-256 comparison confirms it is byte-identical to the original. Generated transparent ICO frames at 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixels using Windows/.NET drawing APIs, with no downloads or runtime conversion dependency.
- The existing project icon resource embeds the new ICO in the executable; `MainWindow.xaml` explicitly uses the same resource. The native 32-pixel icon extracted from the packaged executable matches the generated ICO pixel for pixel and was visually inspected.
- `Build.ps1 -Package` passed with zero compiler warnings/errors. `Test.ps1 -Packaged` passed 16 guardrail cases and 125 native/browser checks in `work/smoke-8b1cc5415358475e9c7d6ac9ce5dd03f`; JavaScript syntax and whitespace checks passed. The existing 616-line architecture warning remains unchanged.
- Asset ownership stays in `Assets`; there are no new state owners or application algorithms. The window markup has no line-count growth. Reopening the app loads the new window/taskbar icon, and its existing Windows association notification refreshes shell icon associations. No browser refresh is involved.

## Architecture guardrail adoption

- Ingested the five local generic guardrail templates and adapted the shared policy, existing-project workflow, and routine reminder. No application feature or source refactor was performed.
- `scripts/Test-Architecture.ps1` owns measurements and declared dependency checks; its JSON owns the reviewed limits. Build and test entry points invoke the check. The checker is 95 lines, its isolated fixture suite 84, and its rules 17; `Build.ps1` grew by one line and `Test.ps1` by two. Application source growth is zero.
- Added the module/state map and review triggers in `docs/ARCHITECTURE.md` (186 lines). Moved the existing full policy there and shortened repository `AGENTS.md` from 100 to 44 lines while retaining a required link. General preferences remain in the Codex home; project budgets remain in the repository.
- The checker passed against 37 handwritten files, retaining one explicit legacy warning: `MainWindow.xaml.cs` is 616 lines and cannot grow above that baseline. No new growth exception, dependency, runtime feature, or CI service was introduced.
- All 16 checker cases passed, including warning/hard boundaries, legacy growth/reduction/rename handling, narrow output exclusions, new source directories, and Core dependency failures (including MSBuild XML namespaces).
- `Build.ps1 -Package` succeeded offline with zero compiler warnings/errors; `Test.ps1 -Packaged` passed the 16 guardrail cases plus 125 native/browser checks under `work/smoke-956ea3a5884a4e108db9c0b6b360f798`. The four changed/new PowerShell scripts and all nine browser JavaScript modules passed syntax checks; `git diff --check` passed.
- Ownership/cohesion, dense-function complexity, semantic dependency cycles, imported MSBuild properties, and future rule changes remain manual review items. The recorded warning and structural debt are not resolved by passing the gate. See the architecture map for bounded future triggers.

## Full-area annotation, clipboard, and startup

- Release build and `Build.ps1 -Package` completed with zero warnings/errors. The packaged native/browser suite passed 125 checks in `work/smoke-11c8686fbcf143e29f1690ad83d611ad`; all nine JavaScript modules passed syntax checks and `git diff --check` passed.
- Annotation covers the entire client area over the native menu, document list, and status bar. Native bounds and browser bounds were checked; Apply, Cancel, and Escape restore the shell. The packaged screenshot was inspected. Middle-button behavior remains unchanged.
- Native mouse/keyboard checks cover marquee intersection, reverse direction, Shift-add selection, SVG and PNG format choices, editable paste with fresh object IDs, one-step paste undo, and cancellation preserving clipboard contents. The tests use native `DataObject` payloads in an isolated clipboard, leaving the user's Windows clipboard untouched. SVG is supplied as `image/svg+xml` and text; PNG is supplied as PNG bytes and a Windows bitmap.
- A restored 100-document window creates exactly one editor for its saved active document. Tests verify that other saved files are read only when selected, unsaved recovery text survives, and unvisited documents can be saved, renamed with their image folders, or closed without initializing their editors.
- Packaged process launch with 100 documents and approximately 19 MB of recovery JSON took 557, 462, and 471 ms to obtain the first native window, titled for document 100. This measures first-window creation, not full HTML rendering, and is not a cold-machine guarantee. Very large recovery files and active images can still increase startup time.
- Earlier diagnostic runs with a 48 MB recovery file measured about 130 ms to read it as text versus about 80 ms streamed. Windows file-association registration now runs in the background; hidden source editors are populated on demand. Temporary timing instrumentation was removed.
- WebView cache is disabled for the editor so packaged CSS and modules are refreshed on launch. Rebuild/package and relaunch were performed; the user does not need to compile or press Ctrl+F5.

## Rename, resize, and wheel zoom

- Release build and `Build.ps1 -Package` completed with zero warnings/errors; `Test.ps1 -Packaged`, syntax checks for all eight JavaScript modules, and `git diff --check` passed. Inspected the packaged annotation screenshot and resulting renamed files under the isolated test profile.
- Native rename checks cover moving the matching PNG folder, updating encoded image references in saved and open HTML, preserving unsaved edits, immediate image visibility, occupied-folder rejection, rollback after a read-only HTML rewrite failure, and case-only rename while editing source.
- Browser mouse checks cover all four proportional corner handles with the opposite corner anchored, all four independent side handles, and wheel zoom in/out without changing object dimensions. Existing crop, line endpoints, undo/redo, drag-size, and typing responsiveness checks remain in the smoke suite.
- Rename performs disk work off the UI thread and shows an indeterminate progress bar in its dialog. Only image URLs pointing into the matching local folder are rewritten; inline images, external URLs, other folders, and displayed text remain unchanged.
- Renaming an image folder reloads the open visual document to discard undo entries containing obsolete URLs; unsaved text is preserved. Wheel zoom keeps its pointer anchor within the available scroll range.

## Image and typing fixes

Validated September 12, 2026 with PowerShell 7 and the installed Visual Studio 2026 components.

- `Build.ps1 -Package` succeeded with zero build warnings/errors and no package downloads. `Test.ps1 -Packaged` passed the browser/native smoke suite.
- All eight JavaScript modules passed `node --check`; `git diff --check` passed.
- Reproduced the first-save image failure through the real Windows Save dialog in an isolated profile: the PNG existed, but the browser could not decode its relative URL. The same check passed after refreshing the sandboxed document's resource mapping. Automated coverage checks first-save mapping, preserved caret position, image decoding, and visibility after Save As.
- Reproduced annotation shrinking with native browser mouse input. Regression checks now move the image toward all four corners and verify constant displayed and pixel dimensions; existing resize, crop, endpoint, and undo/redo checks pass.
- Typing with 2.4 MB of image metadata takes no full-document snapshots during the input burst and synchronizes once after a 150 ms pause. The packaged run measured a maximum of 0.3 ms per character inside the browser input operation; this measures input handling, not end-to-end display latency.
- Native checks verify the hidden source TextBox is not rebuilt during visual editing, immediate Save includes pending input, deferred synchronization queues autosave, and switching to source view cancels pending updates before source edits.
- Inspected editor and annotation screenshots from the native test profile. Tests use isolated profiles under ignored `work/`.

Reopen the desktop application to use the rebuilt package. No user compilation or browser Ctrl+F5 is required. Changing the asset folder reloads the sandboxed document to apply WebView2's mapping; its contents and caret survive, but earlier visual undo history resets at that reload. Ordinary saves in the same folder do not reload.

## Initial release checks

- .NET Release build: zero warnings, zero errors; package sources disabled.
- Final packaged integration run: 64 checks passed under Windows PowerShell 5.1.
- Final source-tree whitespace check passed after removing a trailing blank line in the stylesheet. The initial commit body reported this check as passed prematurely; the final tree was rechecked against Git's empty tree.
- Build and offline packaging through Windows PowerShell 5.1, without npm, downloaded tools, or NuGet packages.
- Native WPF/WebView2 integration checks for HTML creation, formatting and font size, PMT language highlighting, code editing and indentation, image storage, draggable shapes and endpoints, resize, crop and individual corner radii, annotation undo/redo, blank canvas and multiple pasted image layers, template JSON exchange, source editing, standalone export, and session recovery.
- Native file checks for external-change protection, Save As across folders with separate PNG assets, document navigation, and numbering collisions.
- Settings storage relocation, copying all profile JSON, preserving the original folder, and rejecting an occupied target before partial copying.
- JavaScript modules loaded successfully in the real Windows WebView2 runtime; no browser runtime exceptions in the completed smoke tests.
- Regression coverage confirms font-size changes after CSS formatting and successful image insertion after editing a noneditable code block. Image resize remains undoable.
- Visual inspection of captured editor and annotation screenshots at a 1280-pixel native window width.
- Packaged app run with DOTNET_ROOT and DOTNET_ROOT_X64 pointing to a nonexistent folder and multilevel lookup disabled. The host trace reported a self-contained application and loaded `app\coreclr.dll` from the packaged folder.

The tests and screenshots are generated under ignored `work` by `Test.ps1`; user data is not included in the repository.

## Manual usage checks

1. Launch the root shortcut. Create and save an HTML file, format selected text, and edit its source.
2. Paste code, select a language, then double-click the block to edit it.
3. Paste an image as a separate PNG and verify the asset folder beside the HTML.
4. Annotate the image, draw and resize shapes, drag an arrow endpoint, crop with handles and numeric controls, and apply.
5. Reopen annotation to verify the original image and objects remain editable.
6. Insert a blank image canvas, paste several images, and save a multi-object template. Export/import its PMT JSON.
7. Export standalone HTML and open it with the separate-image folder temporarily unavailable; images should remain visible.
8. Move Application storage to a persistent drive, restart, and verify settings, documents, and templates return.

## Scope and limits

Windows x64 only. The runtime supplied with Windows is required; the app never installs it automatically. PMT database/entity/relationship/mapping objects and rich-text canvas objects are excluded. Document scripts remain inert during editing. Annotation PNG rendering is capped at 100 megapixels to avoid memory exhaustion. Arbitrary external document assets must be available to export them; this is unrelated to the fully offline application build.

No separate clean-machine VM was available. Offline packaging was checked locally with machine-runtime discovery disabled and local runtime loading verified in the .NET host trace.

## Prompt 33, image identity, Explorer actions and highlight visibility (September 12)

Source build and integration: `work/prompt33-tests-13.log`, isolated profile `work/smoke-6f0288d175de49adb63ddefd3f513582`.

- Release build: zero compiler warnings/errors.
- Native/browser integration passed, including actual Recycle Bin operations on disposable fixtures, cancel and unsaved-state handling, valid-reference rejection and preview closure.
- Image reuse, renamed image reuse, overwritten content, permanent crop hashes, same-document clipboard references and selection borders passed.
- Toolbar, Modern styles, eight-language token/source preservation, full/preview/collapsed code, required captions, edit/Undo, Markdown export, URL-only links and cancellation passed.
- Offline HTTP fixtures cover YouTube and page metadata, PNG conversion, size limits and unavailable-thumbnail fallback. Real websites can deny previews.
- Highlight screenshot visually inspected: selected text retains yellow; toolbar marker is yellow. Drag event checks preserve native image movement; manual pointer-drag acceptance remains with the user.
- JavaScript syntax: 29 modules passed. Architecture: 93 handwritten files; 16 checker fixtures passed. Existing MainWindow warning reviewed at its unchanged 564-line ceiling. No exceptions or dependency additions.
- `git diff --check` passed. `Build.ps1 -Package` assembled the offline package, and `Test.ps1 -Packaged` passed all 430 checks (`work/prompt33-packaged-tests.log`, isolated profile `work/smoke-240df23738104e7cb159d6509883f890`).

Ownership and limits are recorded in `ARCHITECTURE.md`. No new feature state in bootstrap; current file-operation window integration remains documented legacy coupling. Local reference scans cover HTML attributes and inline/style CSS, not scripts or fetched external stylesheets, and do not scan unrelated unopened documents.

Physical changed-file sizes (new owners have a zero baseline):

| File | Before | After | Delta |
| --- | ---: | ---: | ---: |
| `src/SinAIPrompt.Core/ImageReferences.cs` | 0 | 51 | 51 |
| `src/SinAIPrompt.Core/PromptDirectory.cs` | 38 | 45 | 7 |
| `src/SinAIPrompt/Dialogs.cs` | 116 | 116 | 0 |
| `src/SinAIPrompt/EditorClipboard.cs` | 66 | 79 | 13 |
| `src/SinAIPrompt/ExplorerFileOperations.cs` | 0 | 49 | 49 |
| `src/SinAIPrompt/FileActions.cs` | 196 | 224 | 28 |
| `src/SinAIPrompt/HtmlAssets.cs` | 55 | 77 | 22 |
| `src/SinAIPrompt/HtmlAssetsSelfTest.cs` | 0 | 38 | 38 |
| `src/SinAIPrompt/HtmlEditorHost.cs` | 281 | 283 | 2 |
| `src/SinAIPrompt/LinkPreview.cs` | 0 | 122 | 122 |
| `src/SinAIPrompt/LinkPreviewSelfTest.cs` | 0 | 48 | 48 |
| `src/SinAIPrompt/MainWindow.xaml.cs` | 564 | 564 | 0 |
| `src/SinAIPrompt/PromptExplorer.xaml.cs` | 217 | 247 | 30 |
| `src/SinAIPrompt/PromptExplorerSelfTest.cs` | 145 | 209 | 64 |
| `src/SinAIPrompt/UiSelfTest.cs` | 182 | 184 | 2 |
| `src/SinAIPrompt/Web/annotation-model.js` | 83 | 85 | 2 |
| `src/SinAIPrompt/Web/bridge.js` | 35 | 37 | 2 |
| `src/SinAIPrompt/Web/code-block-self-test.js` | 0 | 48 | 48 |
| `src/SinAIPrompt/Web/code-blocks.js` | 0 | 64 | 64 |
| `src/SinAIPrompt/Web/document-styles.js` | 61 | 45 | -16 |
| `src/SinAIPrompt/Web/document.js` | 91 | 107 | 16 |
| `src/SinAIPrompt/Web/editor-clipboard.js` | 32 | 34 | 2 |
| `src/SinAIPrompt/Web/editor.css` | 83 | 87 | 4 |
| `src/SinAIPrompt/Web/editor.js` | 179 | 168 | -11 |
| `src/SinAIPrompt/Web/image-selection.js` | 55 | 59 | 4 |
| `src/SinAIPrompt/Web/link-insertion.js` | 0 | 39 | 39 |
| `src/SinAIPrompt/Web/markdown.js` | 137 | 138 | 1 |
| `src/SinAIPrompt/Web/ribbon-self-test.js` | 132 | 131 | -1 |
| `src/SinAIPrompt/Web/ribbon.css` | 67 | 62 | -5 |
| `src/SinAIPrompt/Web/ribbon.js` | 142 | 139 | -3 |
| `src/SinAIPrompt/Web/self-test.js` | 262 | 277 | 15 |
| `src/SinAIPrompt/Web/source-highlighting.js` | 210 | 125 | -85 |
