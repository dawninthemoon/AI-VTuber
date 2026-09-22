$ErrorActionPreference = "Stop"

$modelName = "sherpa-onnx-supertonic-3-tts-int8-2026-05-11"
$modelsDirectory = Join-Path $PSScriptRoot "..\Models"
$modelDirectory = Join-Path $modelsDirectory $modelName
$archivePath = Join-Path $modelsDirectory "$modelName.tar.bz2"
$downloadUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/$modelName.tar.bz2"

if (Test-Path -LiteralPath $modelDirectory)
{
    Write-Host "Supertonic model already exists: $modelDirectory"
    exit 0
}

New-Item -ItemType Directory -Path $modelsDirectory -Force | Out-Null

try
{
    Write-Host "Downloading Supertonic 3 model..."
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath

    Write-Host "Extracting model..."
    tar -xf $archivePath -C $modelsDirectory

    if (-not (Test-Path -LiteralPath $modelDirectory))
    {
        throw "The model archive did not contain the expected directory."
    }
}
finally
{
    if (Test-Path -LiteralPath $archivePath)
    {
        Remove-Item -LiteralPath $archivePath -Force
    }
}

Write-Host "Supertonic model is ready: $modelDirectory"
