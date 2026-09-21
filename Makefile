.PHONY: setup test-setup test integration build clean restore frontend \
       stack-init stack-build stack-up stack-down stack-configure stack-healthy stack-rebuild stack-clean \
       test-unit test-integration test-integration-rerun test-integration-only test-all \
       coverage-report container-build container-build-test

SOLUTION := src/Seedarr.sln
UNIT_TEST := src/NzbDrone.Core.Test/Seedarr.Core.Test.csproj
INTEGRATION_TEST := src/NzbDrone.Integration.Test/Seedarr.Integration.Test.csproj
AUTOMATION_TEST := src/NzbDrone.Automation.Test/Seedarr.Automation.Test.csproj
CONSOLE := src/NzbDrone.Console/Seedarr.Console.csproj
FRONTEND := src/Seedarr.Frontend
COMPOSE := podman-compose
SERVICES := seedarr leecharr sonarr radarr prowlarr
DEPS := leecharr sonarr radarr prowlarr

SEEDARR_API_KEY := 1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d
LEECHARR_API_KEY := 2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e
SONARR_API_KEY := 4b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e
RADARR_API_KEY := 5c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f
PROWLARR_API_KEY := 3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f

# --- Build targets (called by upstream CI: make setup) ---

setup:
	dotnet restore $(SOLUTION)
	@if [ -f $(FRONTEND)/package.json ]; then cd $(FRONTEND) && npm ci; fi
	@command -v podman-compose > /dev/null 2>&1 || pip install podman-compose 2>/dev/null || true

test-setup:
	dotnet build $(SOLUTION) --configuration Release

build: setup test-setup

publish:
	dotnet publish $(CONSOLE) --configuration Release --output _output

frontend:
	@if [ -f $(FRONTEND)/package.json ]; then cd $(FRONTEND) && npm run build; fi

restore:
	dotnet restore $(SOLUTION)

clean:
	dotnet clean $(SOLUTION) 2>/dev/null || true
	rm -rf _output _temp

# --- Tests (called by upstream CI: make test / make integration) ---

test:
	dotnet test $(UNIT_TEST) --configuration Release --no-build \
		--settings .runsettings \
		-maxcpucount:4 \
		--logger "console;verbosity=normal" \
		--logger "trx;LogFileName=test-results.trx" \
		--collect:"XPlat Code Coverage"

# integration brings up the full stack and runs all test suites.
integration: stack-clean stack-init stack-build stack-up stack-healthy stack-configure
	@echo ""
	@echo "Running .NET integration tests..."
	dotnet test $(INTEGRATION_TEST) --no-build \
		--settings .runsettings \
		-maxcpucount:4 \
		--logger "console;verbosity=normal" \
		--logger "trx;LogFileName=integration-test-results.trx" \
		--collect:"XPlat Code Coverage"
	@echo ""
	@echo "Running automation tests..."
	SEEDARR_URL=http://localhost:9898 SEEDARR_API_KEY=$(SEEDARR_API_KEY) \
	LEECHARR_URL=http://localhost:7889 LEECHARR_API_KEY=$(LEECHARR_API_KEY) \
	SONARR_URL=http://localhost:8989 SONARR_API_KEY=$(SONARR_API_KEY) \
	RADARR_URL=http://localhost:7878 RADARR_API_KEY=$(RADARR_API_KEY) \
	PROWLARR_URL=http://localhost:9696 PROWLARR_API_KEY=$(PROWLARR_API_KEY) \
	dotnet test $(AUTOMATION_TEST) --no-build \
		--settings .runsettings \
		-maxcpucount:4 \
		--logger "console;verbosity=normal" \
		--logger "trx;LogFileName=automation-results.trx"
	@echo ""
	@echo "Extracting automation coverage..."
	@podman stop --time 30 seedarr 2>/dev/null || true
	@sleep 3
	@podman cp seedarr:/coverage/coverage.xml coverage-automation.xml 2>/dev/null && \
		echo "Automation coverage extracted: coverage-automation.xml" || \
		echo "Warning: no automation coverage file found (coverage may not have been enabled)"
	$(MAKE) coverage-report

test-unit: test

# --- Integration test stack ---

stack-init:
	@mkdir -p config/seedarr config/leecharr config/sonarr config/radarr config/prowlarr data/downloads data/movies data/series
	@if [ ! -f config/seedarr/config.xml ]; then cp tests/config/seedarr/config.xml config/seedarr/config.xml; fi
	@if [ ! -f config/leecharr/config.xml ]; then cp tests/config/leecharr/config.xml config/leecharr/config.xml; fi
	@if [ ! -f config/sonarr/config.xml ]; then cp tests/config/sonarr/config.xml config/sonarr/config.xml; fi
	@if [ ! -f config/radarr/config.xml ]; then cp tests/config/radarr/config.xml config/radarr/config.xml; fi
	@if [ ! -f config/prowlarr/config.xml ]; then cp tests/config/prowlarr/config.xml config/prowlarr/config.xml; fi
	@chmod -R 777 config data 2>/dev/null || true

stack-build:
	$(COMPOSE) build

stack-rebuild:
	$(COMPOSE) build --no-cache seedarr

stack-up: stack-init
	$(COMPOSE) up -d $(DEPS)
	@echo "Waiting for dependency services..."
	@for i in $$(seq 1 120); do \
		if curl -sf http://localhost:8989/ping > /dev/null 2>&1 && \
		   curl -sf http://localhost:7878/ping > /dev/null 2>&1 && \
		   curl -sf http://localhost:9696/ping > /dev/null 2>&1 && \
		   curl -sf http://localhost:7889/ping > /dev/null 2>&1; then \
			echo "Dependencies healthy after $${i}s"; \
			break; \
		fi; \
		sleep 1; \
	done
	$(COMPOSE) up --no-deps -d seedarr

stack-down:
	$(COMPOSE) down 2>/dev/null || true

stack-clean:
	@$(COMPOSE) down 2>/dev/null || true
	@podman rm -f $(SERVICES) arr-configure 2>/dev/null || true
	@podman unshare rm -rf config 2>/dev/null || rm -rf config 2>/dev/null || true
	@$(COMPOSE) down -v 2>/dev/null || true

stack-healthy:
	@echo "Waiting for Seedarr..."
	@for i in $$(seq 1 120); do \
		if curl -sf http://localhost:9898/api/v1/system/status > /dev/null 2>&1; then \
			echo "Seedarr healthy after $${i}s"; \
			exit 0; \
		fi; \
		sleep 1; \
	done; \
	echo "Timeout waiting for Seedarr"; podman logs --tail 100 seedarr 2>&1 || true; exit 1

stack-configure:
	@$(COMPOSE) rm -f configure 2>/dev/null || true
	@$(COMPOSE) up --no-deps configure 2>&1 | tail -60

# --- Integration tests (requires podman-compose stack) ---

coverage-report:
	@INTEGRATION_COV=$$(find . -name "coverage.cobertura.xml" -path "*/TestResults/*" 2>/dev/null | head -1); \
	AUTOMATION_COV=coverage-automation.xml; \
	REPORTS=""; \
	[ -n "$$INTEGRATION_COV" ] && REPORTS="$$INTEGRATION_COV"; \
	[ -f "$$AUTOMATION_COV" ] && REPORTS="$${REPORTS:+$$REPORTS;}$$AUTOMATION_COV"; \
	if [ -n "$$REPORTS" ]; then \
		dotnet reportgenerator -reports:"$$REPORTS" -targetdir:coverage-report -reporttypes:Html 2>/dev/null && \
		echo "Coverage report: coverage-report/index.html" || \
		echo "Install reportgenerator: dotnet tool install -g dotnet-reportgenerator-globaltool"; \
	else \
		echo "No coverage files found"; \
	fi

test-integration: integration

test-integration-rerun: stack-healthy stack-configure
	@echo ""
	@echo "Running .NET integration tests..."
	dotnet test $(INTEGRATION_TEST) --no-build \
		--logger "trx;LogFileName=integration-test-results.trx"
	@echo ""
	@echo "Running automation tests..."
	SEEDARR_URL=http://localhost:9898 SEEDARR_API_KEY=$(SEEDARR_API_KEY) \
	LEECHARR_URL=http://localhost:7889 LEECHARR_API_KEY=$(LEECHARR_API_KEY) \
	SONARR_URL=http://localhost:8989 SONARR_API_KEY=$(SONARR_API_KEY) \
	RADARR_URL=http://localhost:7878 RADARR_API_KEY=$(RADARR_API_KEY) \
	PROWLARR_URL=http://localhost:9696 PROWLARR_API_KEY=$(PROWLARR_API_KEY) \
	dotnet test $(AUTOMATION_TEST) --no-build \
		--logger "trx;LogFileName=automation-test-results.trx"

test-integration-only:
	SEEDARR_URL=http://localhost:9898 SEEDARR_API_KEY=$(SEEDARR_API_KEY) \
	LEECHARR_URL=http://localhost:7889 LEECHARR_API_KEY=$(LEECHARR_API_KEY) \
	SONARR_URL=http://localhost:8989 SONARR_API_KEY=$(SONARR_API_KEY) \
	RADARR_URL=http://localhost:7878 RADARR_API_KEY=$(RADARR_API_KEY) \
	PROWLARR_URL=http://localhost:9696 PROWLARR_API_KEY=$(PROWLARR_API_KEY) \
	dotnet test $(AUTOMATION_TEST) --no-build \
		--logger "trx;LogFileName=automation-test-results.trx"

# --- Combined ---

test-all: test integration

container-build:
	podman build -t seedarr:latest -f Containerfile .

container-build-test:
	podman build --target test --build-arg COVERAGE_TOOLS=true -t seedarr:test -f Containerfile .


