#!/bin/bash
set -e

# Disable linters not supported in dispatch.yaml inputs or incompatible with repo
if [ -n "$GITHUB_ENV" ]; then
	{
		echo "VALIDATE_TYPESCRIPT_PRETTIER=false"
		echo "VALIDATE_MARKDOWN_PRETTIER=false"
		echo "VALIDATE_SPELL_CODESPELL=false"
		echo "VALIDATE_TYPESCRIPT_ES=false"
		echo "VALIDATE_MARKDOWN=false"
		echo "VALIDATE_NATURAL_LANGUAGE=false"
	} >>"$GITHUB_ENV"
fi

echo 0
