.PHONY: setup test-setup test integration build clean restore frontend \
       stack-up stack-down stack-configure stack-healthy stack-rebuild \
       test-unit test-integration test-integration-rerun test-integration-only test-all

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
		--logger "trx;LogFileName=test-results.trx" \
		--collect:"XPlat Code Coverage"

integration: stack-clean stack-build stack-up stack-healthy stack-configure
	@echo ""
	@echo "Running .NET integration tests..."
	dotnet test $(INTEGRATION_TEST) --no-build \
		--logger "trx;LogFileName=integration-test-results.trx" \
		--collect:"XPlat Code Coverage"
	@echo ""
	@echo "Running Selenium automation tests..."
	SEEDARR_URL=http://localhost:9898 dotnet test $(AUTOMATION_TEST) --no-build \
		--logger "trx;LogFileName=automation-test-results.trx"

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
	@$(COMPOSE) up --no-deps configure 2>&1 | tail -30

# --- Integration tests (requires podman-compose stack) ---

test-integration: integration

test-integration-rerun: stack-healthy stack-configure
	@echo ""
	@echo "Running .NET integration tests..."
	dotnet test $(INTEGRATION_TEST) --no-build \
		--logger "trx;LogFileName=integration-test-results.trx"
	@echo ""
	@echo "Running Selenium automation tests..."
	SEEDARR_URL=http://localhost:9898 dotnet test $(AUTOMATION_TEST) --no-build \
		--logger "trx;LogFileName=automation-test-results.trx"

test-integration-only:
	SEEDARR_URL=http://localhost:9898 dotnet test $(AUTOMATION_TEST) --no-build \
		--logger "trx;LogFileName=automation-test-results.trx"

# --- Combined ---

test-all: test integration
