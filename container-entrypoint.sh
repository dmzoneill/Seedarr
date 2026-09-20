#!/bin/sh
set -e
if [ "${COVERAGE_ENABLED}" = "1" ] && command -v dotnet-coverage >/dev/null 2>&1; then
    mkdir -p /coverage
    exec dotnet-coverage collect \
        --output /coverage/coverage.xml \
        --output-format xml \
        -- dotnet /app/Seedarr.Console.dll --data=/config "$@"
else
    exec dotnet /app/Seedarr.Console.dll --data=/config "$@"
fi
