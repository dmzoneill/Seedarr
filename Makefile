.PHONY: setup test-setup test build clean restore frontend

SOLUTION := src/Seedarr.sln
CONSOLE := src/NzbDrone.Console/Seedarr.Console.csproj
FRONTEND := src/Seedarr.Frontend

setup:
	dotnet restore $(SOLUTION)
	@if [ -f $(FRONTEND)/package.json ]; then cd $(FRONTEND) && npm ci; fi

test-setup:
	dotnet build $(SOLUTION) --no-restore --configuration Release

test:
	dotnet test $(SOLUTION) --no-restore --configuration Release --filter "Category!=IntegrationTest" --logger "trx;LogFileName=test-results.trx" --collect:"XPlat Code Coverage"
	@if [ -f $(FRONTEND)/package.json ]; then cd $(FRONTEND) && npm test; fi

integration-test:
	dotnet test $(SOLUTION) --no-restore --configuration Release --filter "Category=IntegrationTest" --logger "trx;LogFileName=integration-results.trx"

build:
	dotnet build $(SOLUTION) --no-restore --configuration Release

publish:
	dotnet publish $(CONSOLE) --configuration Release --output _output

frontend:
	@if [ -f $(FRONTEND)/package.json ]; then cd $(FRONTEND) && npm run build; fi

clean:
	dotnet clean $(SOLUTION)
	rm -rf _output _temp

restore:
	dotnet restore $(SOLUTION)
