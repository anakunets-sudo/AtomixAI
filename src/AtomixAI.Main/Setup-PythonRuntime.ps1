# Восстанавливает embedded Python 3.10 + зависимости оркестратора
# в src/AtomixAI.Main/PythonRuntime (папка не хранится в git).
#
# Запуск: powershell -ExecutionPolicy Bypass -File Setup-PythonRuntime.ps1
# Вызывается автоматически MSBuild-таргетом EnsurePythonRuntime, если python.exe отсутствует.

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$pythonVersion = '3.10.11'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$runtimeDir = Join-Path $root 'PythonRuntime'
$pythonExe = Join-Path $runtimeDir 'python.exe'

if (Test-Path $pythonExe) {
    Write-Host "[Setup-PythonRuntime] python.exe уже существует, восстановление не требуется."
    exit 0
}

$zipUrl = "https://www.python.org/ftp/python/$pythonVersion/python-$pythonVersion-embed-amd64.zip"
$zipPath = Join-Path $env:TEMP "python-$pythonVersion-embed-amd64.zip"
$getPipPath = Join-Path $env:TEMP 'get-pip.py'

Write-Host "[Setup-PythonRuntime] Скачивание $zipUrl ..."
Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing

Write-Host "[Setup-PythonRuntime] Распаковка в $runtimeDir ..."
New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
Expand-Archive -Path $zipPath -DestinationPath $runtimeDir -Force

Write-Host "[Setup-PythonRuntime] Установка pip ..."
Invoke-WebRequest -Uri 'https://bootstrap.pypa.io/pip/get-pip.py' -OutFile $getPipPath -UseBasicParsing
& $pythonExe $getPipPath --no-warn-script-location
if ($LASTEXITCODE -ne 0) { throw "get-pip.py завершился с кодом $LASTEXITCODE" }

Write-Host "[Setup-PythonRuntime] Установка зависимостей из Orchestrator\requirements.txt ..."
& $pythonExe -m pip install --no-warn-script-location -r (Join-Path $root 'Orchestrator\requirements.txt')
if ($LASTEXITCODE -ne 0) { throw "pip install завершился с кодом $LASTEXITCODE" }

Write-Host "[Setup-PythonRuntime] Готово: $pythonExe"
