#!/usr/bin/env bash
# Run the same SonarCloud analysis as .github/workflows/sonarcloud.yml (requires SONAR_TOKEN).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if [[ -z "${SONAR_TOKEN:-}" ]]; then
	echo "SONAR_TOKEN is not set. Generate one at https://sonarcloud.io/account/security" >&2
	exit 1
fi

if ! curl -fsS -u "${SONAR_TOKEN}:" 'https://sonarcloud.io/api/authentication/validate' | grep -q '"valid":true'; then
	echo "SONAR_TOKEN is invalid or expired." >&2
	exit 1
fi

export PATH="${PATH}:${HOME}/.dotnet/tools"
command -v dotnet-sonarscanner >/dev/null 2>&1 || dotnet tool install --global dotnet-sonarscanner

SONAR_PROJECT_KEY="${SONAR_PROJECT_KEY:-dmzoneill_Seedarr}"
SONAR_ORGANIZATION="${SONAR_ORGANIZATION:-dmzoneill}"

open_issues="$(curl -fsS "https://sonarcloud.io/api/issues/search?componentKeys=${SONAR_PROJECT_KEY}&resolved=false&ps=1" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("total", "?"))' 2>/dev/null || echo '?')"
echo "SonarCloud open issues before scan: ${open_issues}"

dotnet-sonarscanner begin \
	/k:"${SONAR_PROJECT_KEY}" \
	/o:"${SONAR_ORGANIZATION}" \
	/d:sonar.token="${SONAR_TOKEN}" \
	/d:sonar.host.url="https://sonarcloud.io" \
	/d:sonar.scanner.skipJreProvisioning=true \
	/d:sonar.cs.roslyn.ignoreIssues=false \
	/d:sonar.cs.opencover.reportsPaths="TestResults/**/coverage.opencover.xml,**/coverage.opencover.xml,src/**/coverage.opencover.xml" \
	/d:sonar.javascript.lcov.reportPaths="src/Seedarr.Frontend/coverage/lcov.info" \
	/d:sonar.projectBaseDir="${ROOT}"

dotnet restore src/Seedarr.sln
dotnet build src/Seedarr.sln --configuration Release --no-restore
dotnet test src/NzbDrone.Core.Test/Seedarr.Core.Test.csproj \
	--configuration Release \
	--no-build \
	--settings .runsettings \
	--results-directory "${ROOT}/TestResults" \
	--collect:"XPlat Code Coverage;Format=opencover"

dotnet test src/Seedarr.Http.Test/Seedarr.Http.Test.csproj \
	--configuration Release \
	--no-build \
	--settings .runsettings \
	--results-directory "${ROOT}/TestResults" \
	--collect:"XPlat Code Coverage;Format=opencover"

dotnet test src/NzbDrone.Integration.Test/Seedarr.Integration.Test.csproj \
	--configuration Release \
	--no-build \
	--settings .runsettings \
	--filter "TestCategory!=Slow" \
	--results-directory "${ROOT}/TestResults" \
	--collect:"XPlat Code Coverage;Format=opencover" || true

if [[ -f src/Seedarr.Frontend/package.json ]]; then
	(
		cd src/Seedarr.Frontend
		npm ci --ignore-scripts
		mkdir -p coverage
		npm run test:coverage || true
	)
fi

dotnet-sonarscanner end /d:sonar.token="${SONAR_TOKEN}"

echo "Scan uploaded. Refresh https://sonarcloud.io/project/issues?id=${SONAR_PROJECT_KEY}&resolved=false"
