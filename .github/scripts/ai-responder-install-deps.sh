#!/usr/bin/env bash
set -euo pipefail

python3 -m pip install --only-binary :all: --no-deps \
  --require-hashes -r .github/ai-responder-requirements.txt
