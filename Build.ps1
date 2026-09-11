param([switch]$Package, [string]$VisualStudioWebViewPath)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (!$VisualStudioWebViewPath) {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path -LiteralPath $vswhere) {
            $vsPath = & $vswhere -latest -version '[18.0,19.0)' -property installationPath
            if ($vsPath) { $VisualStudioWebViewPath = Join-Path $vsPath 'Common7\IDE\PrivateAssemblies' }
        }
    }
    if (!$VisualStudioWebViewPath -or !(Test-Path -LiteralPath (Join-Path $VisualStudioWebViewPath 'Microsoft.Web.WebView2.Core.dll'))) {
        throw 'Visual Studio 2026 is required. Specify -VisualStudioWebViewPath pointing to its Common7\IDE\PrivateAssemblies folder. No packages will be downloaded.'
    }
    dotnet build SinAIPrompt.sln -c Release --nologo "-p:VisualStudioWebViewPath=$VisualStudioWebViewPath" --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if ($Package) {
        & (Join-Path $PSScriptRoot 'scripts\Package-Offline.ps1')
        $shortcutShell = New-Object -ComObject WScript.Shell
        $shortcut = $shortcutShell.CreateShortcut((Join-Path $PSScriptRoot 'SinAIPrompt.lnk'))
        $shortcut.TargetPath = Join-Path $PSScriptRoot 'app\Sin - AI Prompt.exe'
        $shortcut.WorkingDirectory = Join-Path $PSScriptRoot 'app'
        $shortcut.Description = 'Sin - AI Prompt HTML editor'
        $shortcut.Save()
        Write-Output 'Ready: app\Sin - AI Prompt.exe (or double-click SinAIPrompt.lnk)'
    }
} finally { Pop-Location }
