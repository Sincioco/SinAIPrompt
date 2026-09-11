$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot
$output = Join-Path $projectRoot 'app'
$built = Join-Path $projectRoot 'src\SinAIPrompt\bin\Release\net10.0-windows'
$dotnetRoot = Split-Path (Get-Command dotnet).Source
function Latest-Runtime($relative) {
    $item = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot $relative) -Directory | Where-Object Name -Like '10.0.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (!$item) { throw "Installed .NET 10 runtime missing: $relative. No runtime downloads are permitted." }
    return $item
}
$core = Latest-Runtime 'shared\Microsoft.NETCore.App'
$desktop = Latest-Runtime 'shared\Microsoft.WindowsDesktop.App'
$hostFxr = Latest-Runtime 'host\fxr'
New-Item -ItemType Directory -Force -Path $output | Out-Null
# Assemble from the runtimes already installed by Visual Studio, without downloadable runtime packs.
foreach ($runtime in @($core,$desktop)) {
    Get-ChildItem -LiteralPath $runtime.FullName | Where-Object { $_.Name -notlike '*.deps.json' -and $_.Name -notlike '*.runtimeconfig.json' } | Copy-Item -Destination $output -Recurse -Force
}
Get-ChildItem -LiteralPath $built | Where-Object { $_.Name -notin @('data-location.json','data-location.json.bak') } | Copy-Item -Destination $output -Recurse -Force
Copy-Item -LiteralPath (Join-Path $hostFxr.FullName 'hostfxr.dll') -Destination $output -Force
$depsPath = Join-Path $output 'Sin - AI Prompt.deps.json'
$deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
$target = $deps.targets.PSObject.Properties[$deps.runtimeTarget.name].Value
foreach ($pair in @(@($core,'Microsoft.NETCore.App'),@($desktop,'Microsoft.WindowsDesktop.App'))) {
    $runtimeDeps = Get-Content -LiteralPath (Join-Path $pair[0].FullName ($pair[1]+'.deps.json')) -Raw | ConvertFrom-Json
    foreach ($entry in $runtimeDeps.targets.PSObject.Properties[$runtimeDeps.runtimeTarget.name].Value.PSObject.Properties) { $target | Add-Member -NotePropertyName $entry.Name -NotePropertyValue $entry.Value -Force }
    foreach ($entry in $runtimeDeps.libraries.PSObject.Properties) { $deps.libraries | Add-Member -NotePropertyName $entry.Name -NotePropertyValue $entry.Value -Force }
}
$runtimeTarget = '.NETCoreApp,Version=v10.0/win-x64'
$deps.runtimeTarget.name = $runtimeTarget
$deps.targets = @{ $runtimeTarget = $target }
[IO.File]::WriteAllText($depsPath, ($deps | ConvertTo-Json -Depth 100), (New-Object Text.UTF8Encoding($false)))
$configPath = Join-Path $output 'Sin - AI Prompt.runtimeconfig.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$config.runtimeOptions.PSObject.Properties.Remove('frameworks')
$config.runtimeOptions | Add-Member -NotePropertyName includedFrameworks -NotePropertyValue @(@{name='Microsoft.NETCore.App';version=$core.Name},@{name='Microsoft.WindowsDesktop.App';version=$desktop.Name}) -Force
[IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding($false)))
$info = @{ packagedAt=[DateTime]::UtcNow.ToString('o'); dotnetRuntime=$core.Name; desktopRuntime=$desktop.Name; components='Installed .NET 10 and Visual Studio 2026 Microsoft WebView2'; packageDownloads=0 } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $output 'build-info.json'), $info, (New-Object Text.UTF8Encoding($false)))
Write-Output 'Offline self-contained package assembled from installed Microsoft runtimes. Uses the Windows WebView2 runtime.'
