# Stage 1: Build frontend
FROM node:20-alpine AS frontend
WORKDIR /build/src/Seedarr.Frontend
COPY src/Seedarr.Frontend/package.json src/Seedarr.Frontend/package-lock.json ./
RUN npm ci
COPY src/Seedarr.Frontend/ ./
RUN npm run build

# Stage 2: Build backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /build

# Copy solution and project files first for layer caching
COPY src/Seedarr.sln src/Seedarr.sln
COPY src/Directory.Build.props src/Directory.Build.props
COPY src/stylecop.json src/stylecop.json
COPY src/NzbDrone.Console/Seedarr.Console.csproj src/NzbDrone.Console/
COPY src/NzbDrone.Host/Seedarr.Host.csproj src/NzbDrone.Host/
COPY src/NzbDrone.Core/Seedarr.Core.csproj src/NzbDrone.Core/
COPY src/NzbDrone.Common/Seedarr.Common.csproj src/NzbDrone.Common/
COPY src/NzbDrone.SignalR/Seedarr.SignalR.csproj src/NzbDrone.SignalR/
COPY src/Seedarr.Http/Seedarr.Http.csproj src/Seedarr.Http/
COPY src/Seedarr.Api.V1/Seedarr.Api.V1.csproj src/Seedarr.Api.V1/

RUN dotnet restore src/NzbDrone.Console/Seedarr.Console.csproj

# Copy full source and publish
COPY src/ src/

# Copy frontend build output into wwwroot before publish
COPY --from=frontend /build/src/NzbDrone.Host/wwwroot/ src/NzbDrone.Host/wwwroot/

RUN dotnet publish src/NzbDrone.Console/Seedarr.Console.csproj \
    -c Release \
    -o /app \
    -p:RunAnalyzers=false \
    --no-restore

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# hadolint ignore=DL3008
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

RUN mkdir -p /config /data

WORKDIR /app

COPY --from=backend /app ./
COPY --from=frontend /build/src/NzbDrone.Host/wwwroot/ ./wwwroot/

ENV SEEDARR__APP_DATA=/config

EXPOSE 9898

VOLUME ["/config", "/data"]

ENTRYPOINT ["dotnet", "Seedarr.Console.dll", "--data=/config"]
