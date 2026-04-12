[CmdletBinding()]
param(
    [string]$PythonBin = $env:PYTHON_BIN
)

$ErrorActionPreference = 'Stop'

$rootDir = Split-Path -Parent $PSCommandPath
$requirementsPath = Join-Path $rootDir 'requirements.txt'

$modelRoot = if ([string]::IsNullOrWhiteSpace($env:WH40K_TRANSLATION_MODEL_ROOT)) {
    Join-Path $rootDir 'models'
} else {
    $env:WH40K_TRANSLATION_MODEL_ROOT
}

$tokenizerRoot = if ([string]::IsNullOrWhiteSpace($env:WH40K_TRANSLATION_TOKENIZER_ROOT)) {
    Join-Path $rootDir 'tokenizers'
} else {
    $env:WH40K_TRANSLATION_TOKENIZER_ROOT
}

if ([string]::IsNullOrWhiteSpace($PythonBin)) {
    $venvPython = Join-Path $rootDir '.venv\Scripts\python.exe'
    if (Test-Path $venvPython) {
        $PythonBin = $venvPython
    } else {
        $PythonBin = 'python'
    }
}

$null = New-Item -ItemType Directory -Force -Path (Join-Path $modelRoot 'opus-mt-en-ru')
$null = New-Item -ItemType Directory -Force -Path (Join-Path $modelRoot 'opus-mt-ru-en')
$null = New-Item -ItemType Directory -Force -Path (Join-Path $tokenizerRoot 'opus-mt-en-ru')
$null = New-Item -ItemType Directory -Force -Path (Join-Path $tokenizerRoot 'opus-mt-ru-en')

& $PythonBin -m pip install -r $requirementsPath

$torchCheck = @'
try:
    import torch  # noqa: F401
except Exception as exc:
    raise SystemExit(
        "download_models.ps1: Python package 'torch' is required for ct2-transformers-converter. "
        "Install requirements again or run: python -m pip install torch"
    ) from exc
'@

$torchCheck | & $PythonBin -

$pythonExecutable = (& $PythonBin -c "import sys; print(sys.executable)").Trim()
$pythonDir = Split-Path -Parent $pythonExecutable

$converterCandidates = @(
    (Join-Path $pythonDir 'ct2-transformers-converter.exe'),
    (Join-Path $pythonDir 'ct2-transformers-converter')
)

$converterPath = $converterCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $converterPath) {
    $converterCommand = Get-Command 'ct2-transformers-converter.exe' -ErrorAction SilentlyContinue
    if (-not $converterCommand) {
        $converterCommand = Get-Command 'ct2-transformers-converter' -ErrorAction SilentlyContinue
    }

    if ($converterCommand) {
        $converterPath = $converterCommand.Source
    }
}

if (-not $converterPath) {
    throw "download_models.ps1: Could not find ct2-transformers-converter in the selected Python environment. Reinstall requirements and try again."
}

& $converterPath --force --model 'Helsinki-NLP/opus-mt-en-ru' --quantization int8 --output_dir (Join-Path $modelRoot 'opus-mt-en-ru')
& $converterPath --force --model 'Helsinki-NLP/opus-mt-ru-en' --quantization int8 --output_dir (Join-Path $modelRoot 'opus-mt-ru-en')

$tokenizerRootPython = (Resolve-Path -LiteralPath $tokenizerRoot).Path.Replace('\', '\\')
$tokenizerScript = @"
from pathlib import Path
from transformers import AutoTokenizer

models = {
    "Helsinki-NLP/opus-mt-en-ru": Path(r"$tokenizerRootPython") / "opus-mt-en-ru",
    "Helsinki-NLP/opus-mt-ru-en": Path(r"$tokenizerRootPython") / "opus-mt-ru-en",
}

for source, target_dir in models.items():
    tokenizer = AutoTokenizer.from_pretrained(source)
    tokenizer.save_pretrained(target_dir)
    print(f"saved tokenizer for {source} -> {target_dir}")
"@

$tokenizerScript | & $PythonBin -
