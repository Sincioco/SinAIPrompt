param([switch]$Packaged)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (!$Packaged) { & .\Build.ps1 }
    $exe = if ($Packaged) { Join-Path $PSScriptRoot 'app\Sin - AI Prompt.exe' } else { Join-Path $PSScriptRoot 'src\SinAIPrompt\bin\Release\net10.0-windows\Sin - AI Prompt.exe' }
    $testData = Join-Path $PSScriptRoot ('work\smoke-' + [Guid]::NewGuid().ToString('N'))
    $testProcess = Start-Process -FilePath $exe -ArgumentList ('--self-test --data-dir "' + $testData + '"') -WindowStyle Hidden -PassThru
    if (!$testProcess.WaitForExit(60000)) { throw "Tests still running: PID $($testProcess.Id). Results: $testData" }
    $results = Join-Path $testData 'ui-test-results.txt'
    if (Test-Path -LiteralPath $results) { Get-Content -LiteralPath $results }
    if ($testProcess.ExitCode -ne 0) { throw "Integration checks failed. See $testData" }
    Write-Output "Native and browser integration passed. Screenshots: $testData"
} finally { Pop-Location }
