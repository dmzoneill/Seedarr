#!/bin/sh
set -e
if [ "${COVERAGE_ENABLED}" = "1" ]; then
    mkdir -p /coverage
    exec dotnet-coverage collect \
        --output /coverage/coverage.xml \
        --output-format xml \
        -- dotnet /app/Seedarr.Console.dll --data=/config
else
    exec dotnet Seedarr.Console.dll --data=/config
fi
