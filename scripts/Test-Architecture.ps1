param(
    [string]$Root = (Split-Path $PSScriptRoot),
    [string]$RulesPath = (Join-Path $PSScriptRoot 'architecture-rules.json'),
    [switch]$Report
)
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path -LiteralPath $Root).Path
$rules = Get-Content -LiteralPath $RulesPath -Raw | ConvertFrom-Json
if ($rules.version -ne 1) { throw 'Unsupported architecture rules version.' }
foreach ($kind in @('entry', 'module')) {
    $limit = $rules.limits.$kind
    if ($limit.warning -lt 1 -or $limit.maximum -lt $limit.warning) {
        throw "Invalid $kind line limits."
    }
}

# Count handwritten C#, browser code/markup, and PowerShell tooling, including tests.
# Ignore only known root output/profile folders and .NET output under src.
$extensions = @('.cs', '.js', '.xaml', '.css', '.html', '.ps1', '.csproj', '.props', '.targets')
$outputFolders = @('.git', '.vs', 'app', 'work', 'Data', 'TestResults')
function Find-SourceFiles([string]$Directory, [string]$Relative = '') {
    foreach ($item in Get-ChildItem -LiteralPath $Directory -Force) {
        $path = if ($Relative) { "$Relative/$($item.Name)" } else { $item.Name }
        if ($item.PSIsContainer) {
            if (!$Relative -and $item.Name -in $outputFolders) { continue }
            if ($path -like 'src/*' -and $item.Name -in @('bin', 'obj')) { continue }
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Classify the source directory link explicitly before scanning: $path"
            }
            Find-SourceFiles $item.FullName $path
        }
        elseif ($item.Extension -in $extensions) {
            [pscustomobject]@{ Path = $path; FullName = $item.FullName }
        }
    }
}
$files = @(Find-SourceFiles $Root)
$paths = @($files.Path)
$legacy = @{}
foreach ($baseline in $rules.legacy) {
    if (!$baseline.id -or !$baseline.reason -or !$baseline.nextReview -or $baseline.maximum -lt 1) {
        throw 'Each legacy baseline needs an ID, positive limit, reason, and review trigger.'
    }
    if ($legacy.ContainsKey($baseline.path)) { throw "Duplicate legacy path: $($baseline.path)" }
    $legacy[$baseline.path] = $baseline
    if ($baseline.path -notin $paths) {
        throw "Legacy path missing: $($baseline.path). Review its deletion/rename and carry its baseline history; do not silently drop it."
    }
}
foreach ($entry in $rules.entryFiles) {
    if ($entry -notin $paths) { throw "Entry file missing or excluded: $entry" }
}

$failures = [Collections.Generic.List[string]]::new()
$warnings = 0
$measurements = foreach ($file in $files) {
    $kind = if ($file.Path -in $rules.entryFiles) { 'entry' } else { 'module' }
    $limit = $rules.limits.$kind
    $count = [IO.File]::ReadAllLines($file.FullName).Length
    $maximum = $limit.maximum
    if ($legacy.ContainsKey($file.Path)) { $maximum = $legacy[$file.Path].maximum }
    if ($count -gt $maximum) {
        $failures.Add("$($file.Path): $count lines exceeds $maximum. Review ownership/extraction; do not raise the limit to pass.")
    }
    elseif ($count -gt $limit.warning) {
        $warnings++
        Write-Warning "$($file.Path): $count lines; review threshold $($limit.warning), enforced maximum $maximum."
    }
    if ($legacy.ContainsKey($file.Path) -and $count -lt $maximum) {
        $warnings++
        Write-Warning "$($file.Path): reduced to $count lines. Review and tighten the recorded $maximum-line baseline."
    }
    [pscustomobject]@{ File = $file.Path; Kind = $kind; Lines = $count; Maximum = $maximum }
}

# This verifies declared project dependencies, not C# semantic dependencies.
$coreProject = Join-Path $Root 'src/SinAIPrompt.Core/SinAIPrompt.Core.csproj'
if (!(Test-Path -LiteralPath $coreProject)) { throw 'Core project missing; review the dependency rule if the project moved.' }
[xml]$core = Get-Content -LiteralPath $coreProject -Raw
foreach ($reference in $core.SelectNodes('//*[local-name()="ProjectReference"]')) {
    $target = [IO.Path]::GetFullPath((Join-Path (Split-Path $coreProject) $reference.Include))
    $shell = [IO.Path]::GetFullPath((Join-Path $Root 'src/SinAIPrompt/SinAIPrompt.csproj'))
    if ($target -eq $shell) { $failures.Add('Core must not reference the WPF application project.') }
}
foreach ($setting in $core.SelectNodes('//*[local-name()="UseWPF" or local-name()="UseWindowsForms"]')) {
    if ($setting.InnerText.Trim() -eq 'true') { $failures.Add("Core must not enable $($setting.Name).") }
}
foreach ($reference in $core.SelectNodes('//*[local-name()="Reference"]')) {
    if ($reference.Include -match '^(PresentationFramework|PresentationCore|WindowsBase|System\.Windows\.Forms|Microsoft\.Web\.WebView2)([.,]|$)') {
        $failures.Add("Core must not reference UI assembly $($reference.Include).")
    }
}
if ($Report) { $measurements | Sort-Object Lines -Descending | Format-Table -AutoSize }
if ($failures.Count) { throw ("Architecture checks failed:`n" + ($failures -join "`n")) }
Write-Output "Architecture checks passed: $($files.Count) handwritten files; $warnings review warning(s)."
