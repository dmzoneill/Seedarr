#!/usr/bin/env bash
set -euo pipefail

curl --fail --proto '=https' --tlsv1.2 --proto-redir '=https' --tlsv1.2 -sSL \
  "https://raw.githubusercontent.com/dmzoneill/dmzoneill/40c1d4928db5936027a9399be11c9d9af6acc7ab/ai-responder.py" \
  -o ai-responder.py
chmod +x ai-responder.py
