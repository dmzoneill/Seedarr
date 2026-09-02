.PHONY: setup test-setup test integration build clean restore frontend \
       stack-up stack-down stack-configure stack-healthy stack-rebuild \
       test-unit test-integration test-integration-rerun test-integration-only test-all \
       coverage-report

SOLUTION := src/Seedarr.sln
UNIT_TEST := src/NzbDrone.Core.Test/Seedarr.Core.Test.csproj
INTEGRATION_TEST := src/NzbDrone.Integration.Test/Seedarr.Integration.Test.csproj
AUTOMATION_TEST := src/NzbDrone.Automation.Test/Seedarr.Automation.Test.csproj
CONSOLE := src/NzbDrone.Console/Seedarr.Console.csproj
FRONTEND := src/Seedarr.Frontend
COMPOSE := podman-compose
SERVICES := seedarr sonarr radarr lidarr prowlarr transmission
DEPS := sonarr radarr lidarr prowlarr transmission

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
		--logger "trx;LogFileName=test-results.trx" \
		--collect:"XPlat Code Coverage"

# integration brings up the full stack and runs all test suites.
# Webhook auth uses standard X-Api-Key (same as all other endpoints).
integration: stack-clean stack-build stack-up stack-healthy stack-configure
	@echo ""
	@echo "Running .NET integration tests..."
	dotnet test $(INTEGRATION_TEST) --no-build \
		--settings .runsettings \
		-maxcpucount:4 \
		--logger "trx;LogFileName=integration-test-results.trx" \
		--collect:"XPlat Code Coverage"
	@echo ""
	@echo "Running automation tests..."
	$(eval SEEDARR_API_KEY := $(shell podman exec seedarr sh -c "grep -o '<ApiKey>[^<]*</ApiKey>' /config/config.xml 2>/dev/null" | sed 's/<[^>]*>//g'))
	SEEDARR_URL=http://localhost:9898 SEEDARR_API_KEY=$(SEEDARR_API_KEY) dotnet test $(AUTOMATION_TEST) --no-build \
		--settings .runsettings \
		-maxcpucount:4 \
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

stack-build:
	$(COMPOSE) build

stack-rebuild:
	$(COMPOSE) build --no-cache seedarr

stack-up:
	$(COMPOSE) up -d $(DEPS)
	@echo "Waiting for dependency services..."
	@for i in $$(seq 1 120); do \
		if curl -sf http://localhost:8989/ping > /dev/null 2>&1 && \
		   curl -sf http://localhost:7878/ping > /dev/null 2>&1 && \
		   curl -sf http://localhost:8686/ping > /dev/null 2>&1 && \
		   curl -sf http://localhost:9696/ping > /dev/null 2>&1 && \
		   curl -sf http://localhost:9091/transmission/web/ > /dev/null 2>&1; then \
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
	echo "Timeout waiting for Seedarr"; exit 1

stack-configure:
	@podman exec radarr mkdir -p /config/movies 2>/dev/null || true
	@podman exec radarr chown abc:users /config/movies 2>/dev/null || true
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
	SEEDARR_URL=http://localhost:9898 dotnet test $(AUTOMATION_TEST) --no-build \
		--logger "trx;LogFileName=automation-test-results.trx"

test-integration-only:
	SEEDARR_URL=http://localhost:9898 dotnet test $(AUTOMATION_TEST) --no-build \
		--logger "trx;LogFileName=automation-test-results.trx"

# --- Combined ---

test-all: test integration
