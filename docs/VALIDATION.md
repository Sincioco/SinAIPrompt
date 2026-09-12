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

## Prompt 34: media, capture, image defaults and title state (September 12)

- Source integration passed: `work/prompt34-tests-3.log`, profile `work/smoke-96443f98d07f4d35a86c644bcbd68283`.
- Offline package built with zero compiler warnings/errors: `work/prompt34-package.log`.
- Packaged integration passed **462 native/browser checks**, plus all **16 architecture-checker fixtures**: `work/prompt34-packaged-tests.log`, profile `work/smoke-06ab772477eb4ae2ad688dac1be5fe16`.
- All **33 JavaScript modules** passed `node --check`; `git diff --check` passed. Architecture checks cover **100 handwritten files**, with the existing 564-line MainWindow review warning and no exceptions or changed limits.
- Native/browser checks cover the three-second direct region shortcut, undimmed frozen desktop, opposite-corner magnifier geometry, separate-PNG insertion overriding embed defaults, settings mutual exclusion/persistence, dirty-title updates, image dropdown, four proportional resize handles/Undo, image and video fullscreen hosting/restoration, reusable external-view PNGs, YouTube metadata and optional local thumbnails, inert document scripts, saved-card round trips, and absence of runtime player markup from saved HTML.
- Live UI validation used the official YouTube player sample (`M7lc1UVf-VE`) in an isolated profile under `work/prompt34-live`: fetched title/channel/thumbnail, played inline, played fullscreen, returned with Esc, and opened the same video in the default Chrome browser. The temporary browser tab was closed and the fixture saved before closing the test app. Playback is an actual live check, not merely iframe creation.

Ownership review: insertion storage policy moved from the editor coordinator into
`image-insertion.js`; existing image reuse/save paths and regression checks remain.
The image action menu and transient fullscreen view are in `image-actions.js`, with
resize state retained by `image-selection.js`. `youtube.js` owns one player per
editor, and `EditorFullscreen.cs` owns/restores window presentation through a small
existing-host callback. `CaptureMagnifier.cs` owns only its frozen preview geometry.
The native bridge delegates platform work; MainWindow gained no lines or feature
state. No new dependencies, architecture exceptions, or startup scans were added.
The established native partial-class coupling and browser/native bridge remain debt.

Limits: YouTube playback and remote thumbnails require a network connection and
remain subject to YouTube's video restrictions. Local thumbnails are stored as PNG
data inside the HTML. Saved cards act as normal thumbnail/text links outside the
app; scripts are never added to user documents. Capture uses visible desktop pixels,
including protected/off-screen limitations of the existing Windows capture primitive.
Magnifier geometry covers multiple-monitor/negative-coordinate cases in checks;
physical dragging across every possible mixed-DPI monitor arrangement is manual
acceptance. The external image action's PNG preparation is validated without changing
the user's default viewer. No clean-machine VM was used.

The .NET package was rebuilt; restart loads bundled CSS/modules with WebView caching
disabled. No manual compilation or Ctrl+F5 is needed for the relaunched package.

Changed source-file physical growth (new modules start at zero):

| File | Before | After | Delta |
| --- | ---: | ---: | ---: |
| src/SinAIPrompt.Core/Documents.cs | 213 | 214 | 1 |
| src/SinAIPrompt/CaptureMagnifier.cs | 0 | 49 | 49 |
| src/SinAIPrompt/CaptureRegionWindow.cs | 85 | 96 | 11 |
| src/SinAIPrompt/DocumentCommandSelfTest.cs | 198 | 209 | 11 |
| src/SinAIPrompt/EditorFullscreen.cs | 0 | 44 | 44 |
| src/SinAIPrompt/HtmlAssetsSelfTest.cs | 38 | 41 | 3 |
| src/SinAIPrompt/HtmlEditorHost.cs | 283 | 295 | 12 |
| src/SinAIPrompt/ImageExternalViewer.cs | 0 | 23 | 23 |
| src/SinAIPrompt/LinkPreview.cs | 122 | 154 | 32 |
| src/SinAIPrompt/LinkPreviewSelfTest.cs | 48 | 57 | 9 |
| src/SinAIPrompt/MainWindow.xaml.cs | 564 | 564 | 0 |
| src/SinAIPrompt/ScreenCapture.cs | 201 | 201 | 0 |
| src/SinAIPrompt/ScreenCaptureDialog.cs | 142 | 165 | 23 |
| src/SinAIPrompt/ScreenCaptureSelfTest.cs | 162 | 211 | 49 |
| src/SinAIPrompt/SettingsDialog.cs | 111 | 118 | 7 |
| src/SinAIPrompt/UiSelfTest.cs | 184 | 185 | 1 |
| src/SinAIPrompt/Web/bridge.js | 37 | 38 | 1 |
| src/SinAIPrompt/Web/editor.css | 87 | 97 | 10 |
| src/SinAIPrompt/Web/editor.js | 168 | 176 | 8 |
| src/SinAIPrompt/Web/image-actions.js | 0 | 37 | 37 |
| src/SinAIPrompt/Web/image-insertion.js | 0 | 25 | 25 |
| src/SinAIPrompt/Web/image-selection.js | 59 | 60 | 1 |
| src/SinAIPrompt/Web/link-insertion.js | 39 | 53 | 14 |
| src/SinAIPrompt/Web/media-self-test.js | 0 | 53 | 53 |
| src/SinAIPrompt/Web/ribbon.css | 62 | 62 | 0 |
| src/SinAIPrompt/Web/ribbon.js | 139 | 139 | 0 |
| src/SinAIPrompt/Web/self-test.js | 277 | 281 | 4 |
| src/SinAIPrompt/Web/youtube.js | 0 | 67 | 67 |

## YouTube, tabs and compact ribbon — September 12, 2026 (19:21 request)

Implemented from `D:\Sin - AI Prompt - Contents\2026-09-12 1921 - YouTube.html`
and inspected its embedded reference images. Changes remain uncommitted for review.

- YouTube URLs pasted alone become full-width, centered cards immediately. The
  thumbnail has a centered play control; the frame/title selects editing chrome.
- Blue frame outline, two proportional resize handles, bottom editing toolbar,
  five alignment/wrap choices, URL/title editing, external opening and Copy/Delete.
  Clipboard cut/paste and native Undo preserve cards and neighboring wrapped videos.
- Crowded tabs retain readable widths and expose horizontal buttons/wheel scrolling.
  Pin and modified markers use their measured width. Tabs can be shown independently
  of the existing Document List / Prompt Explorer pane; session visibility persists.
- View > Wrap Toolbar / Ribbon keeps the existing wrapping by default. Turning it
  off moves whole groups into More ribbon tools, retaining palette/Styles submenus.
  Link and Paste Code now have large local SVG icons in Tools.

Validation performed:

- `Test.ps1`: source build and native/browser suite passed; final source log
  `work/youtube-tests-final2.log`, profile `work/smoke-cdbda57da63a491f917d3fdb9e5d8732`.
- `Build.ps1 -Package`: succeeded with zero compiler warnings/errors, using only
  installed Microsoft components. Log: `work/youtube-package.log`.
- `Test.ps1 -Packaged`: **503 native/browser checks passed**, including final selection
  ownership changes, all four tab/sidebar combinations, native ribbon View commands,
  overflow submenus, real-pointer video resize, URL/title edit and cancellation,
  clipboard insert/cut/replacement/Undo, unchanged adjacent wrapped video, fullscreen
  expansion/restoration, sandboxing and 100-document lazy startup. Log:
  `work/youtube-packaged-tests.log`; profile `work/smoke-1003e5bb2abf41f981de67a977c6c886`.
- All **35 JavaScript modules** passed `node --check`; **16 architecture fixture
  checks** passed. Architecture checker passed for **104 handwritten source files**,
  with two review notices for MainWindow (562 exceeds the 500-line review trigger;
  it is below its unchanged 564-line maximum). `git diff --check` passed.
- Inspected the updated application with an isolated `work/youtube-live` sample:
  full-width video thumbnail, centered play control and compact ribbon rendering.
  User acceptance remains manual. The saved card acts as ordinary links outside
  this app; no player scripts are added to the document.

Ownership and limits:

Video selection/gestures live in `video-selection.js`; card metadata/player and the
synchronous native-edit adapter remain in `youtube.js`. Tab width/scrolling lives in
`TabStripLayout`; visibility stays in `NavigationLayout`. Ribbon overflow has one
small owner and retains the original controls. MainWindow decreased **564 → 562**
lines. No dependency, architecture exception, baseline increase or exclusion change.
Existing host/native partial coupling remains debt. The specific WebView2 noneditable
figure workaround and validation are documented in ARCHITECTURE.md.

Watch Later opens YouTube, where the user can save the video to their account. Share
copies its public URL. The actual embedded player controls and availability are
provided by YouTube, with no guarantee of Confluence's exact control placement;
see [YouTube's supported player parameters](https://developers.google.com/youtube/player_parameters).
Playback/remote thumbnails require a connection and remain subject to video restrictions.
The app adds no account API, downloaded library or build-time network dependency.
Existing saved cards retain their older layout until edited or reinserted.

The .NET package has been rebuilt. Relaunch loads the bundled CSS/modules with
WebView caching disabled; the user does not need to compile or press Ctrl+F5.

Changed source-file growth:

| File | Before | After | Delta |
| --- | ---: | ---: | ---: |
| src/SinAIPrompt.Core/Documents.cs | 214 | 217 | 3 |
| src/SinAIPrompt/App.xaml | 49 | 49 | 0 |
| src/SinAIPrompt/HtmlEditorHost.cs | 295 | 295 | 0 |
| src/SinAIPrompt/MainWindow.xaml | 63 | 65 | 2 |
| src/SinAIPrompt/MainWindow.xaml.cs | 564 | 562 | -2 |
| src/SinAIPrompt/NavigationLayout.cs | 42 | 53 | 11 |
| src/SinAIPrompt/UiSelfTest.cs | 185 | 186 | 1 |
| src/SinAIPrompt/Web/color-picker.js | 74 | 74 | 0 |
| src/SinAIPrompt/Web/document.js | 107 | 107 | 0 |
| src/SinAIPrompt/Web/editor-clipboard.js | 34 | 38 | 4 |
| src/SinAIPrompt/Web/editor.css | 97 | 104 | 7 |
| src/SinAIPrompt/Web/editor.js | 176 | 183 | 7 |
| src/SinAIPrompt/Web/media-self-test.js | 53 | 97 | 44 |
| src/SinAIPrompt/Web/ribbon-self-test.js | 131 | 144 | 13 |
| src/SinAIPrompt/Web/ribbon.css | 62 | 70 | 8 |
| src/SinAIPrompt/Web/ribbon.js | 139 | 142 | 3 |
| src/SinAIPrompt/Web/youtube.js | 67 | 118 | 51 |
| src/SinAIPrompt/NavigationSelfTest.cs | 0 | 69 | 69 |
| src/SinAIPrompt/TabStripLayout.cs | 0 | 66 | 66 |
| src/SinAIPrompt/Web/ribbon-overflow.js | 0 | 28 | 28 |
| src/SinAIPrompt/Web/video-selection.js | 0 | 135 | 135 |

## Document Content (20:54) validation

Source integration: 523 checks passed in
`work/smoke-2ad435a9f42f44858a45236b85e32bd8`.
Build: zero compiler warnings/errors. JavaScript: all 39 modules syntax-checked.
Architecture: 117 handwritten files passed; 16 checker fixtures passed. The two
review notices concern MainWindow's existing warning threshold and its reduced
563-line size; the 564-line ceiling is unchanged.

Coverage includes the native region options/defaults and actual capture, exclusive
Content View with the active outline, Windows ReadOnly persistence/reopening/title,
visual/source editing and Replace All protection, cleanup reference snapshots and
rechecking, four YouTube presentation transitions, neighboring content, Undo and
Unlink. Existing image rename/asset rollback, save/conflict, clipboard, annotation,
search, tab layout, and 100-document lazy startup checks remain exercised.

Native visual inspection used an isolated profile under work/document-content-review.
Verified the full-width ribbon, Content View below it, URL/Inline/Card appearances
with a loaded thumbnail, and Find beside navigation beneath the same ribbon. The
isolated review app was closed. User acceptance testing remains the final check of
appearance and online YouTube behavior.

Limits: cleanup checks HTML documents in the selected folder tree plus all open
document snapshots; unopened documents outside that tree and remote stylesheets
are outside the scan. Junctions/reparse descendants are not followed; failed reads
abort the scan. Recycle operations use Windows with no permanent-delete fallback.
Native browser editing/Undo retains the previously documented WebView2 sensitivity.
ReadOnly follows the Windows file attribute, refreshed on opening/activation; it
is an editing guard, not a separate permissions system. YouTube remains dependent
on the provider/network and may omit metadata the provider does not expose.

The package was rebuilt with Build.ps1 -Package. CSS/modules use the existing
cache-disabled WebView setup; no user compilation or Ctrl+F5 is required after
relaunch. Prior unreviewed changes remain uncommitted for the requested review.

Cumulative source growth from a94a9d4 (includes the preceding uncommitted YouTube
iteration) follows. This is a growth review, not a claim that line counts establish
cohesion; ownership and coupling are described in ARCHITECTURE.md.

| File | Before | After | Delta |
| --- | ---: | ---: | ---: |
| src/SinAIPrompt.Core/DocumentAccess.cs | 0 | 19 | 19 |
| src/SinAIPrompt.Core/Documents.cs | 214 | 226 | 12 |
| src/SinAIPrompt.Core/UnusedImages.cs | 0 | 27 | 27 |
| src/SinAIPrompt/App.xaml | 49 | 49 | 0 |
| src/SinAIPrompt/Dialogs.cs | 116 | 116 | 0 |
| src/SinAIPrompt/DocumentContents.cs | 0 | 56 | 56 |
| src/SinAIPrompt/DocumentContentSelfTest.cs | 0 | 57 | 57 |
| src/SinAIPrompt/DocumentLock.cs | 0 | 18 | 18 |
| src/SinAIPrompt/EditorChromeLayout.cs | 0 | 48 | 48 |
| src/SinAIPrompt/EditorSearch.cs | 67 | 68 | 1 |
| src/SinAIPrompt/EditorView.cs | 189 | 191 | 2 |
| src/SinAIPrompt/FileActions.cs | 224 | 229 | 5 |
| src/SinAIPrompt/HtmlEditorHost.cs | 295 | 323 | 28 |
| src/SinAIPrompt/HtmlFileActions.cs | 66 | 68 | 2 |
| src/SinAIPrompt/HtmlFileRename.cs | 135 | 136 | 1 |
| src/SinAIPrompt/MainWindow.xaml | 63 | 65 | 2 |
| src/SinAIPrompt/MainWindow.xaml.cs | 564 | 563 | -1 |
| src/SinAIPrompt/NavigationLayout.cs | 42 | 53 | 11 |
| src/SinAIPrompt/NavigationSelfTest.cs | 0 | 69 | 69 |
| src/SinAIPrompt/PromptExplorer.xaml | 22 | 22 | 0 |
| src/SinAIPrompt/PromptExplorer.xaml.cs | 247 | 267 | 20 |
| src/SinAIPrompt/RegionCaptureOptions.cs | 0 | 25 | 25 |
| src/SinAIPrompt/RibbonWebView.cs | 0 | 46 | 46 |
| src/SinAIPrompt/ScreenCaptureDialog.cs | 165 | 167 | 2 |
| src/SinAIPrompt/ScreenCaptureSelfTest.cs | 211 | 217 | 6 |
| src/SinAIPrompt/SettingsDialog.cs | 118 | 120 | 2 |
| src/SinAIPrompt/TabStripLayout.cs | 0 | 66 | 66 |
| src/SinAIPrompt/UiSelfTest.cs | 185 | 187 | 2 |
| src/SinAIPrompt/UnusedImageCleanup.cs | 0 | 65 | 65 |
| src/SinAIPrompt/Web/color-picker.js | 74 | 74 | 0 |
| src/SinAIPrompt/Web/content-self-test.js | 0 | 31 | 31 |
| src/SinAIPrompt/Web/document-access.js | 0 | 18 | 18 |
| src/SinAIPrompt/Web/document-outline.js | 0 | 9 | 9 |
| src/SinAIPrompt/Web/document.js | 107 | 107 | 0 |
| src/SinAIPrompt/Web/editor-clipboard.js | 34 | 38 | 4 |
| src/SinAIPrompt/Web/editor.css | 97 | 108 | 11 |
| src/SinAIPrompt/Web/editor.js | 176 | 193 | 17 |
| src/SinAIPrompt/Web/image-actions.js | 37 | 40 | 3 |
| src/SinAIPrompt/Web/media-self-test.js | 53 | 97 | 44 |
| src/SinAIPrompt/Web/ribbon-overflow.js | 0 | 28 | 28 |
| src/SinAIPrompt/Web/ribbon-self-test.js | 131 | 144 | 13 |
| src/SinAIPrompt/Web/ribbon.css | 62 | 71 | 9 |
| src/SinAIPrompt/Web/ribbon.js | 139 | 151 | 12 |
| src/SinAIPrompt/Web/self-test.js | 281 | 285 | 4 |
| src/SinAIPrompt/Web/video-presentation.js | 0 | 31 | 31 |
| src/SinAIPrompt/Web/video-selection.js | 0 | 153 | 153 |
| src/SinAIPrompt/Web/youtube.js | 67 | 118 | 51 |

Final packaged validation passed all 525 checks in
`work/smoke-6feb380ce6a8499d95747f6abf5805d4`.
The first packaged attempt caught a clipboard test-fixture issue: a second marquee
following Undo selected two objects rather than the intended five. The independent
marquee tests remain unchanged; the clipboard-format loop now restores its fixture
through the existing Select All command and verifies five selected objects. Both
SVG and PNG clipboard checks and all annotation checks pass. This changed only the
test setup, not annotation behavior. self-test.js is now 285 lines (+4 from HEAD).
The final JavaScript syntax check and git diff --check passed.

The normal packaged application was relaunched after all older/test windows closed.
Verified its full-width ribbon and restored last document, 2026-09-12 2204 - Ribbon Changes.html.

## Ribbon Changes (September 12, 22:04)

Read the saved request and all five local reference images. Implemented the full Styles
priority/overflow layout, combined Numbering split button with a drawn chevron, removal
of the View Source strip, annotation Pan tool, save-and-lock Copy for AI Use, rename with
an uneditable extension, large translucent countdown, enlarged grid magnifier, temporary
pointer speed control, sticky click/click selection, and retained browser hosts for smoother
document switching. Ctrl+Shift+U and the native View menu still provide source editing.

Source validation passed all 543 checks in `work/smoke-17429600ee314252ba4c525d2fb50fd3`.
This includes real Windows pointer speed adjustment/restoration, capture cancellation,
first-click restoration, sticky selection completion, browser pan without geometry changes,
1500/1900-pixel single-row ribbon sizing, rapid document switching without Unloaded events,
no native browser handle changes or ribbon group mutations, and 100-document lazy startup.
The rename UI fixture was updated to enter the new extension-free name; its existing real
modal/save/image-folder/reference checks passed. JavaScript syntax: all 40 modules passed.
The offline package build passed with zero compiler warnings or errors. Architecture:
120 handwritten files and 16 guardrail fixtures passed; two existing MainWindow review
notices, no exceptions or baseline changes. `git diff --check` passed.

Changed-file growth below is relative to the start of this task, preserving the earlier
uncommitted YouTube/Document Content changes. Same-size edits also touched Dialogs.cs,
HtmlFileRename.cs, MainWindow.xaml, PromptExplorer.xaml.cs, PromptExplorerSelfTest.cs,
and Web/ribbon.js. MainWindow.xaml.cs had no change in this task (563 lines).

| File | Before | After | Growth |
| --- | ---: | ---: | ---: |
| src/SinAIPrompt/CaptureMagnifier.cs | 49 | 62 | 13 |
| src/SinAIPrompt/CapturePointerSpeed.cs | 0 | 30 | 30 |
| src/SinAIPrompt/CaptureRegionWindow.cs | 96 | 121 | 25 |
| src/SinAIPrompt/DocumentCommandSelfTest.cs | 209 | 230 | 21 |
| src/SinAIPrompt/DocumentWorkflowSelfTest.cs | 116 | 119 | 3 |
| src/SinAIPrompt/EditorChromeLayout.cs | 48 | 52 | 4 |
| src/SinAIPrompt/EditorSurface.cs | 0 | 33 | 33 |
| src/SinAIPrompt/FileActions.cs | 229 | 236 | 7 |
| src/SinAIPrompt/HtmlEditorHost.cs | 323 | 326 | 3 |
| src/SinAIPrompt/ScreenCaptureDialog.cs | 167 | 168 | 1 |
| src/SinAIPrompt/ScreenCaptureSelfTest.cs | 217 | 249 | 32 |
| src/SinAIPrompt/Web/annotation-pan.js | 0 | 21 | 21 |
| src/SinAIPrompt/Web/annotation-ui.js | 239 | 242 | 3 |
| src/SinAIPrompt/Web/editor.css | 108 | 110 | 2 |
| src/SinAIPrompt/Web/editor.js | 193 | 192 | -1 |
| src/SinAIPrompt/Web/ribbon-overflow.js | 28 | 36 | 8 |
| src/SinAIPrompt/Web/ribbon-self-test.js | 144 | 151 | 7 |
| src/SinAIPrompt/Web/ribbon.css | 71 | 72 | 1 |
| src/SinAIPrompt/Web/self-test.js | 285 | 289 | 4 |

Final packaged validation passed all 543 checks in
`work/smoke-1f45f977c7b446f58248df08fcd4f860`.
The old app closed gracefully and the rebuilt package was relaunched with its normal
profile and last saved document restored. Inspected the live native ribbon: all five
Styles visible, Tools in overflow, no source strip. Also visually inspected the actual
three-second translucent countdown and 326-by-360 magnifier, including its visible grid,
transparent crosshairs and mouse-speed label, then canceled capture without inserting.
The direct RenderTargetBitmap magnifier diagnostic was blank because its Canvas offset
lay outside the diagnostic bitmap; the live native screenshot was used for visual review.

MainWindow stayed at 563 lines. No architecture exceptions or new dependencies. The
first visit to a document still initializes its editor lazily; visited editors retain
layout/undo/selection during switches. Pointer speed restoration was verified on normal
capture/cancel paths; abrupt process termination cannot run an in-process restore handler.
No commit or push: the existing one-time user review hold remains in effect.

## Splash Screen (September 12, 22:51)

Read the saved HTML request and all five unique reference images. Copied all seven splash
assets into `src/SinAIPrompt/Assets/Splash Screens` and embedded the requested 2400-by-1440
image. Replaced the canonical PNG and regenerated the nine-size Windows ICO from V2.
Source/project SHA-256 hashes match for the V2 PNG and chosen splash artwork.

Startup shows the splash before settings/session restoration and yields for painting,
without a minimum display time. Help/About uses that artwork with author, compile date,
version and native links, covering the artwork's startup caption to prevent overlap.
The closing-menu focus race found by the first check was fixed by deferring About opening
until menu focus restoration finishes. Escape, artwork click and focus-out dismissal pass.
Email/website/repository URI and native hyperlink wiring were verified; external sites and
an email composer were not opened as part of testing.

All 562 native/browser checks passed in each final run:

- Source: `work/smoke-eb1669f3c00249e6af1968c4f052bf3c`.
- Packaged: `work/smoke-915be40c337d477f9c52d09038328634`.

New regression coverage verifies first-visit ribbon height reservation, first-render hidden
and unwrapped preferences, resize/navigation toggles at 1000/1500/1100 widths, readable
navigation menu fonts, physical-pointer overflow open/close, Region Capture image-layer
insertion inside annotation and cancellation without scene loss. Existing 100-document
lazy startup, document persistence, editor, annotation, clipboard and capture checks pass.
All 40 JavaScript modules pass syntax checks. Offline .NET build/package: zero compiler
warnings/errors. Architecture: 122 files and 16 fixture cases pass, with the existing
MainWindow size review warning; no exceptions or limit changes. `git diff --check` passes.

The old app was receiving user edits, so the input guard prevented closure. After the
user saved/closed it, the offline package was replaced and tested, then the normal app
was relaunched with its last document restored. Visually inspected native Help/About
and its outside-click dismissal, and the readable three-item navigation menu. Only the
updated normal app remains running. Bundled web resources retain cache disabling, so
no browser refresh or user recompilation is required.

Current-task source growth is measured from `work/splash-start-lines.json`, preserving
all earlier uncommitted changes. PromptExplorer.xaml.cs has a same-size font change.

| File | Before | After | Growth |
| --- | ---: | ---: | ---: |
| src/SinAIPrompt/App.xaml.cs | 149 | 156 | 7 |
| src/SinAIPrompt/BrandingSelfTest.cs | 0 | 40 | 40 |
| src/SinAIPrompt/BrandingWindow.cs | 0 | 73 | 73 |
| src/SinAIPrompt/DocumentCommandSelfTest.cs | 230 | 254 | 24 |
| src/SinAIPrompt/EditorChromeLayout.cs | 52 | 60 | 8 |
| src/SinAIPrompt/HtmlEditorHost.cs | 326 | 331 | 5 |
| src/SinAIPrompt/MainWindow.xaml | 65 | 66 | 1 |
| src/SinAIPrompt/MainWindow.xaml.cs | 563 | 564 | 1 |
| src/SinAIPrompt/NavigationSelfTest.cs | 69 | 89 | 20 |
| src/SinAIPrompt/ScreenCaptureSelfTest.cs | 249 | 279 | 30 |
| src/SinAIPrompt/SinAIPrompt.csproj | 23 | 27 | 4 |
| src/SinAIPrompt/UiSelfTest.cs | 187 | 188 | 1 |
| src/SinAIPrompt/Web/annotation-ui.js | 242 | 243 | 1 |
| src/SinAIPrompt/Web/editor.js | 192 | 196 | 4 |
| src/SinAIPrompt/Web/ribbon-overflow.js | 36 | 37 | 1 |
| src/SinAIPrompt/Web/ribbon-self-test.js | 151 | 158 | 7 |

Ownership and coupling review is recorded in ARCHITECTURE.md. MainWindow is 564 lines,
exactly its unchanged ceiling; new branding presentation stays in its 73-line owner.
No new third-party dependency or startup document scan. First visits still initialize
WebView lazily; the fix reserves ribbon space during that work. Splash duration depends
on actual startup work, with no artificial hold. No commit/push: the one-time user review
hold remains in effect; HEAD remains a94a9d4.

## Markdown Support and transient startup dialog (September 13)

Read the September 12 23:49 Markdown Support HTML request and incorporated the later
preview/drop, save-navigation and native startup-dialog bug reports. File > Open converts
Markdown into an editable sibling `Name - Converted.html`, using numbered names without
overwriting existing files. Explorer selection and Markdown drops instead show read-only
Modern HTML and create no converted file. Local relative images load from the source folder;
preview content cannot execute scripts or fetch external HTTP resources.

Content View now has its own pane beside Document List or Prompt Explorer. Locked HTML
retains its headings. Removed browser popups restore the native navigation cutout, and an
Explorer refresh cannot reopen a previously selected Markdown preview after returning to
an HTML document and saving. The native mouse regression uses the actual tunneled WPF
preview event; the file-drop checks pass real file objects through the installed WebView2
AdditionalObjects API. A preview's final drop response is intentionally not awaited after
that drop disposes its browser; the test waits for the resulting editable document instead.

The user's E_ABORT dialog exposed a missing test observation: native MessageBox windows
were not included in browser exceptions or Application.Windows. Editor disposal now cancels
initialization waiters, startup continuations stop after disposal, and canceled startup does
not report a runtime failure. Active startup errors retain the source fallback and actual
error message, without the misleading blanket instruction to install WebView2. A native
Windows event observer records editor-startup dialogs in the isolated test process and
fails the run if one occurs. Closing editors after 0, 2, 10 and 25 ms is covered.

All 582 native/browser checks passed in both final runs:

- Source: `work/smoke-eb42181bb0784205a102b491b27ce8ae`.
- Packaged: `work/smoke-6867e8a60e8447de8bd1a5e74c3d36b0`.

Both runs recorded zero unexpected native startup dialogs and zero browser exceptions.
The removed-popover navigation regression failed before its fix and passed afterwards.
The suite also covers Modern conversion, source preservation, collision naming, local
preview images, read-only sandbox behavior, HTML/Markdown drops, clicking the already
selected HTML row, saving that HTML without returning to Markdown, independent panes and
locked-document headings. Existing 100-document lazy startup checks remain passing.

PowerShell 7.6.6 built and packaged offline using installed Visual Studio 2026 components.
Compilation: zero warnings/errors. JavaScript syntax: all 43 modules passed. Architecture:
129 handwritten files and all 16 checker fixtures passed. The two existing MainWindow
review warnings remain: above the 500-line review trigger and below its recorded ceiling.
No architecture limits, exclusions or dependencies changed. `git diff --check` passed.

Task-relative source growth (earlier uncommitted tasks are excluded from these deltas):

| File | Before | After | Change |
| --- | ---: | ---: | ---: |
| MainWindow.xaml.cs | 564 | 545 | -19 |
| MainWindow.xaml | 66 | 68 | +2 |
| FileActions.cs | 236 | 279 | +43 |
| HtmlEditorHost.cs | 331 | 339 | +8 |
| ExplorerPreview.cs | 79 | 103 | +24 |
| DocumentContents.cs | 56 | 79 | +23 |
| EditorChromeLayout.cs | 60 | 63 | +3 |
| PromptExplorer.xaml.cs | 267 | 263 | -4 |
| MarkdownExport.cs | 26 | 35 | +9 |
| MarkdownImport.cs | new | 33 | +33 |
| Web/markdown.js | 138 | 143 | +5 |
| Web/editor.js | 196 | 197 | +1 |
| Web/document-outline.js | 9 | 9 | 0 |
| Web/editor-chrome.js | new | 16 | +16 |
| Web/file-drop.js | new | 13 | +13 |
| Web/preview.html | new | 4 | +4 |
| Web/preview.js | new | 9 | +9 |
| MarkdownImportSelfTest.cs | new | 100 | +100 |
| EditorStartupSelfTest.cs | new | 57 | +57 |
| DocumentContentSelfTest.cs | 57 | 68 | +11 |
| NavigationSelfTest.cs | 89 | 105 | +16 |
| PromptExplorerSelfTest.cs | 209 | 212 | +3 |
| UiSelfTest.cs | 188 | 192 | +4 |
| Web/content-self-test.js | 31 | 32 | +1 |

The new app was launched after verifying older versions were closed. Its normal profile
restored `2026-09-13 0002 - Prompt 41.html`; the rendered document, ribbon and Document List
were visually inspected with no startup dialog. Further optional live navigation was left
to the user after the input guard reported activity. The new document's instructions were
not executed because the user has not requested it. No user document was changed during
this final inspection. The native/browser suite validates the new pane and preview flows;
manual acceptance remains with the user. No rebuild or Ctrl+F5 is required: the package is
already updated and local editor/preview browser caching is disabled.

Ownership remains in existing file/preview/navigation/editor-lifecycle owners plus narrow
Markdown publication and browser chrome/drop modules. The native dialog observer is test
only. Existing host partial-class coupling remains debt; Content View still represents
editable or locked HTML documents, not the transient Markdown preview. No external
Markdown extensions or dependencies were added. Commit/push remains on the user's explicit
one-time review hold; HEAD is `a94a9d4` and earlier uncommitted work is preserved.

## Font Color icon correction (September 13)

Used the user's attached Word comparison as the visual reference. Corrected conflicting
17/20-pixel icon rules and independent bar positioning in the existing ribbon CSS. The
Font Color button now uses a compact gray A within a 16-pixel box, centered over a
16-by-4-pixel bar, with a separate 8-pixel chevron. Dark mode uses the existing light text
color. The indicator starts red and retains the last palette choice across caret moves;
Automatic remains black. The existing palette and text-formatting path are unchanged.

Added three browser regression assertions covering actual rendered icon/bar/chevron
geometry, initial red and chosen-color retention on differently colored text. Both source
and packaged integration runs passed all 585 checks, including zero native startup dialogs
and zero browser exceptions. A final gray/dark-theme CSS adjustment was included in the
packaged build and its full suite; both runs' font-color screenshots were visually reviewed.

- Source: `work/smoke-ea3840156b0a4edab7525c16c0aba925`.
- Packaged: `work/smoke-c9a69598137c438b8aa9f129f614400e`.
- Visual artifacts: `font-color-icon.png` in each run folder.

Offline build/package passed with zero compiler warnings/errors. All 43 JavaScript syntax
checks passed; architecture checked 129 source files and all 16 fixtures. Existing
MainWindow review warnings remain unchanged. `git diff --check` passed. No dependencies,
new files, extra mutable state, coupling changes or architecture exceptions were added.
Task-relative growth: ribbon.js 151 to 150 (-1), ribbon.css 72 to 76 (+4),
ribbon-self-test.js 158 to 166 (+8). MainWindow is unchanged at 545 lines.

Gracefully closed the prior app and relaunched the rebuilt package. The unsaved
`2026-09-13 0039 - Prompt 42` draft returned through session recovery. Live input was left
to the user after relaunch; the packaged screenshot provides the visual acceptance check.
No user recompilation or Ctrl+F5 is required. The existing cache-disabled WebView setup
loads the installed CSS on restart. Last-picked font color is scoped to the editor's
lifetime, as with the existing highlight indicator; it is not a new persisted preference.
No commit/push: the explicit review hold remains in effect, with HEAD at a94a9d4.

## Popup navigation visibility (September 13, 00:02 Bugs)

Read the saved HTML report and all three separate PNG references. All three show the
same defect while a popup is open: the browser's full native window paints over the WPF
navigation pane. The previous removed-popup cleanup only restored navigation afterward.

The browser now reports bounded rectangles for open dialogs/popovers, plus modal state.
The native WebView region retains the navigation cutout and unions only those rectangles.
Browser zoom and monitor DPI are included in coordinate conversion. Open popup resizing,
window resize, scrolling and removed-popup cleanup refresh the coalesced geometry.
The native pane remains visible and is disabled only for modal dialogs; ordinary overflow
and Styles popups leave it enabled. The existing layout owner applies this to navigation,
its splitter, Find and Content View without adding MainWindow state.

The new native-region regression failed before the product fix:
`work/smoke-7e0540a717914a2297f26d65c3df0c32` reported that Document List did not stay
visible with moreRibbon open. Both final runs passed all 603 native/browser checks:

- Source: `work/smoke-265af43d346f446b8da8ca6faca4d780`.
- Packaged: `work/smoke-6485e3692e004d33b095ae5168e83596`.

Coverage uses the actual toolbar overflow, List Numbering and More Styles controls in
both Document List and Prompt Explorer modes. It checks the native browser region while
the popup is open, modal input blocking, close/re-enable behavior, a popup deliberately
crossing into navigation, and removal of that popup's region. No native startup dialogs
or browser exceptions occurred. Existing annotation/fullscreen, preview, save, capture,
lazy startup and ribbon behavior also passed the full suite.

Full-desktop capture artifacts were inspected but are not accepted as full visual proof:
other foreground windows obscured the source captures and the packaged desktop captures.
The visible portion of the source sidebar remained rendered in all six cases. The new
normal app was then relaunched and its restored document/ribbon/Document List were visually
inspected directly. Further optional popup clicks were stopped by the live input guard;
manual visual acceptance remains with the user. Automated native-region checks above
validate the six popup cases independently of desktop occlusion. No user text was edited.

Build/package completed offline with zero compiler warnings/errors. All 43 JavaScript
syntax checks, architecture checks for 129 handwritten files, all 16 checker fixtures,
and `git diff --check` passed. MainWindow remains 545 lines with the same two review
warnings and unchanged 564-line ceiling. No files, dependencies or rule exceptions added.

| Current-task file | Before | After | Change |
| --- | ---: | ---: | ---: |
| RibbonWebView.cs | 46 | 61 | +15 |
| Web/editor-chrome.js | 16 | 24 | +8 |
| HtmlEditorHost.cs | 339 | 340 | +1 |
| EditorChromeLayout.cs | 63 | 65 | +2 |
| NavigationSelfTest.cs | 105 | 148 | +43 |

Ownership stays in the existing popup geometry reporter, native region owner and pane
layout owner. The adapter passes rectangles and modal state instead of a broad overlay
flag. No extra WebView, replacement controller, shared globals or per-keystroke layout
work was introduced. Existing host partial-class coupling remains architectural debt.
Popup content can intentionally cover its own portion of navigation; the rest remains
visible. Native navigation is disabled rather than covered by the HTML modal backdrop.

The older app was closed gracefully, preserving the unsaved Prompt 42 draft in recovery.
The updated package was relaunched with the user's last Splash Screen document restored.
The user needs no recompilation or Ctrl+F5; the bundled CSS/modules reload with the existing
cache-disabled WebView configuration. The user explicitly lifted the review hold during
this task and requested automatic commit/push for green work now and going forward. This
commit includes the previously validated pending YouTube, document access/content, ribbon,
splash, Markdown, startup-dialog and Font Color work along with this popup correction.
