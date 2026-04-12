#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")" && pwd)"
MODEL_ROOT="${WH40K_TRANSLATION_MODEL_ROOT:-$ROOT_DIR/models}"
TOKENIZER_ROOT="${WH40K_TRANSLATION_TOKENIZER_ROOT:-$ROOT_DIR/tokenizers}"
PYTHON_BIN="${PYTHON_BIN:-python3}"

mkdir -p \
  "$MODEL_ROOT/opus-mt-en-ru" \
  "$MODEL_ROOT/opus-mt-ru-en" \
  "$TOKENIZER_ROOT/opus-mt-en-ru" \
  "$TOKENIZER_ROOT/opus-mt-ru-en"

"$PYTHON_BIN" -m pip install -r "$ROOT_DIR/requirements.txt"

"$PYTHON_BIN" - <<'PY'
import sys

try:
  import torch  # noqa: F401
except Exception as exc:
  raise SystemExit(
    "download_models.sh: Python package 'torch' is required for ct2-transformers-converter. "
    "Install requirements again or run: python -m pip install torch"
  ) from exc
PY

ct2-transformers-converter \
  --force \
  --model Helsinki-NLP/opus-mt-en-ru \
  --quantization int8 \
  --output_dir "$MODEL_ROOT/opus-mt-en-ru"

ct2-transformers-converter \
  --force \
  --model Helsinki-NLP/opus-mt-ru-en \
  --quantization int8 \
  --output_dir "$MODEL_ROOT/opus-mt-ru-en"

"$PYTHON_BIN" - <<PY
from pathlib import Path
from transformers import AutoTokenizer

models = {
  "Helsinki-NLP/opus-mt-en-ru": Path(r"$TOKENIZER_ROOT") / "opus-mt-en-ru",
  "Helsinki-NLP/opus-mt-ru-en": Path(r"$TOKENIZER_ROOT") / "opus-mt-ru-en",
}

for source, target_dir in models.items():
    tokenizer = AutoTokenizer.from_pretrained(source)
    tokenizer.save_pretrained(target_dir)
    print(f"saved tokenizer for {source} -> {target_dir}")
PY
