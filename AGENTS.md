# Sin - AI Prompt

Native Windows HTML editor cloned from Sin - Notepad. Keep its WPF shell, tabs/document list,
file conflict protection, atomic saves, autosave, and recovery. HTML editing runs in WebView2
with native JavaScript modules, no framework or bundler. Document scripts must not execute
inside the editor. PMT is a read-only reference for RTE and Diagram 2 behavior.

Keep modules focused. Do not add dependencies or tests without a concrete need.

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
