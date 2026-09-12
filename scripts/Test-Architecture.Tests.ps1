$ErrorActionPreference = 'Stop'
$checker = Join-Path $PSScriptRoot 'Test-Architecture.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('SinAIPrompt-architecture-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$fixtureRoot = (Resolve-Path -LiteralPath $fixtureRoot).Path
$rulesPath = Join-Path $fixtureRoot 'rules.json'
$rules = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'architecture-rules.json') -Raw | ConvertFrom-Json
$passed = 0
function Write-Fixture([string]$Path, [string]$Text) {
    $destination = Join-Path $fixtureRoot $Path
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    [IO.File]::WriteAllText($destination, $Text)
}
function Write-Lines([string]$Path, [int]$Count, [string]$Newline = "`n") {
    Write-Fixture $Path ((1..$Count | ForEach-Object { '// line' }) -join $Newline)
}
function Check([string]$Name, [string]$ExpectedFailure = '', [string]$ExpectedWarning = '') {
    $errorText = ''; $output = @()
    try { $output = @(& $checker -Root $fixtureRoot -RulesPath $rulesPath 3>&1) }
    catch { $errorText = $_.Exception.Message }
    if ($ExpectedFailure) {
        if (!$errorText.Contains($ExpectedFailure)) { throw "$Name failed: expected '$ExpectedFailure'; got '$errorText'." }
    }
    elseif ($errorText) { throw "$Name failed: $errorText" }
    if ($ExpectedWarning -and !($output -join "`n").Contains($ExpectedWarning)) { throw "$Name did not report the review warning." }
    $script:passed++
    Write-Output "PASS $Name"
}
try {
    $rules | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $rulesPath -Encoding utf8
    $entry = $rules.entryFiles[0]; $legacy = $rules.legacy[0].path
    $core = 'src/SinAIPrompt.Core/SinAIPrompt.Core.csproj'
    Write-Fixture $core '<Project Sdk="Microsoft.NET.Sdk" />'
    Write-Lines $entry 200
    Write-Lines $legacy 616
    Check 'Current legacy baseline passes with a visible review warning' -ExpectedWarning '616 lines'
    Write-Lines 'src/Sample.cs' 800
    Check 'Module hard boundary is inclusive' -ExpectedWarning '800 lines'
    Write-Lines 'src/Sample.cs' 801
    Check 'Oversized new module fails' '801 lines exceeds 800'
    Write-Lines 'src/Sample.cs' 500 "`r`n"
    Write-Lines $entry 300
    Check 'Entry hard boundary is inclusive' -ExpectedWarning '300 lines'
    Write-Lines $entry 301
    Check 'Oversized entry file fails' '301 lines exceeds 300'
    Write-Lines $entry 200
    Write-Lines $legacy 617
    Check 'Legacy growth fails below the general module limit' '617 lines exceeds 616'
    Write-Lines $legacy 615
    Check 'Legacy reduction requests a reviewed baseline reduction' -ExpectedWarning 'tighten the recorded'
    Write-Lines $legacy 616
    Move-Item -LiteralPath (Join-Path $fixtureRoot $legacy) -Destination (Join-Path $fixtureRoot 'src/Renamed.cs')
    Check 'Renaming cannot silently erase baseline history' 'Legacy path missing'
    Move-Item -LiteralPath (Join-Path $fixtureRoot 'src/Renamed.cs') -Destination (Join-Path $fixtureRoot $legacy)
    Write-Lines 'app/Generated.cs' 900
    Write-Lines 'work/Fixture.cs' 900
    Write-Lines 'src/SinAIPrompt/obj/Generated.cs' 900
    Check 'Known output and test profiles are excluded'
    Write-Lines 'src/New/Generated.cs' 900
    Check 'A generated-looking name is not an automatic exclusion' '900 lines exceeds 800'
    Write-Lines 'src/New/Generated.cs' 2
    Write-Lines 'extra/NewFeature.js' 801
    Check 'Source outside the usual folders is still checked' '801 lines exceeds 800'
    Write-Lines 'extra/NewFeature.js' 1
    Write-Fixture $core '<Project><ItemGroup><ProjectReference Include="../SinAIPrompt/SinAIPrompt.csproj" /></ItemGroup></Project>'
    Check 'Core-to-shell project dependency fails' 'Core must not reference'
    Write-Fixture $core '<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003"><ItemGroup><ProjectReference Include="../SinAIPrompt/SinAIPrompt.csproj" /></ItemGroup></Project>'
    Check 'MSBuild XML namespaces do not hide project dependencies' 'Core must not reference'
    Write-Fixture $core '<Project><PropertyGroup><UseWPF>true</UseWPF></PropertyGroup></Project>'
    Check 'WPF cannot be enabled in Core' 'Core must not enable UseWPF'
    Write-Fixture $core '<Project><ItemGroup><Reference Include="Microsoft.Web.WebView2.Core" /></ItemGroup></Project>'
    Check 'Core UI assembly dependency fails' 'Core must not reference UI assembly'
    Write-Fixture $core '<Project />'
    Check 'Restored valid fixtures pass'
    Write-Output "Architecture checker: $passed cases passed."
}
finally {
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$fixtureRoot.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
        !(Split-Path $fixtureRoot -Leaf).StartsWith('SinAIPrompt-architecture-')) {
        throw 'Refusing to remove a fixture directory outside the task TEMP location.'
    }
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
}
