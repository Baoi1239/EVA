param([string]$PythonPath = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$scriptDir = $PSScriptRoot
if (-not $PythonPath) {
    $launcher = Get-Command py -ErrorAction SilentlyContinue
    if ($launcher) {
        $PythonPath = & $launcher.Source -3.11 -c 'import sys; print(sys.executable)'
        if ($LASTEXITCODE -ne 0) { $PythonPath = '' }
    }
    if (-not $PythonPath) { $PythonPath = Join-Path $env:LOCALAPPDATA 'Programs\Python\Python311\python.exe' }
}
if (-not (Test-Path -LiteralPath $PythonPath -PathType Leaf)) { throw 'Python 3.11 x64 fehlt. -PythonPath angeben.' }
& $PythonPath -c 'import sys, struct; assert sys.version_info[:2] == (3,11) and struct.calcsize("P") == 8'
if ($LASTEXITCODE -ne 0) { throw 'Python 3.11 x64 erforderlich.' }
$venvDir = Join-Path $scriptDir '.venv'
if (-not (Test-Path -LiteralPath $venvDir)) {
    & $PythonPath -m venv $venvDir
    if ($LASTEXITCODE -ne 0) { throw 'Venv-Erstellung fehlgeschlagen.' }
}
$venvPython = Join-Path $venvDir 'Scripts\python.exe'
& $venvPython -c 'import sys, struct; assert sys.version_info[:2] == (3,11) and struct.calcsize("P") == 8'
if ($LASTEXITCODE -ne 0) { throw 'Vorhandene Mirroring-Venv ist nicht Python 3.11 x64.' }
& $venvPython -m pip install -r (Join-Path $scriptDir 'requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Paketinstallation fehlgeschlagen.' }
$modelDir = Join-Path $scriptDir 'models'
New-Item -ItemType Directory -Path $modelDir -Force | Out-Null
$modelPath = Join-Path $modelDir 'pose_landmarker_lite.task'
if (-not (Test-Path -LiteralPath $modelPath)) {
    $partial = "$modelPath.download"
    try {
        Invoke-WebRequest -Uri 'https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_lite/float16/1/pose_landmarker_lite.task' -OutFile $partial
        Move-Item -LiteralPath $partial -Destination $modelPath
    } finally {
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial }
    }
}
& $venvPython -m pip check
if ($LASTEXITCODE -ne 0) { throw 'Paketabhaengigkeiten sind inkonsistent.' }
Write-Host 'Mirroring-Setup erfolgreich. Der Dienst und die Kamera sind noch aus.'
