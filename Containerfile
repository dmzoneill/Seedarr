# Stage 1: Build frontend
FROM node:24-alpine AS frontend
WORKDIR /build/src/Seedarr.Frontend
COPY src/Seedarr.Frontend/package.json src/Seedarr.Frontend/package-lock.json ./
RUN npm ci --legacy-peer-deps
COPY src/Seedarr.Frontend/ ./
RUN npm run build

# Stage 2: Build backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /build

ARG COVERAGE_TOOLS=false

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
    -p:DebugType=none \
    -p:DebugSymbols=false \
    --no-restore && \
    rm -rf /app/runtimes/win* /app/runtimes/osx* /app/runtimes/maccatalyst* /app/runtimes/browser-wasm /app/clidriver

# Install coverage tools in build stage (has SDK) — only when requested
RUN mkdir -p /root/.dotnet/tools && \
    if [ "$COVERAGE_TOOLS" = "true" ]; then \
      dotnet tool install --global dotnet-coverage; \
    fi

# Stage 3: Base runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# hadolint ignore=DL3008
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/* /var/cache/apt/* /usr/share/doc/* /usr/share/man/*

RUN mkdir -p /config /data /app/fixtures

LABEL org.opencontainers.image.title="Seedarr" \
      org.opencontainers.image.description="BitTorrent Seeding Simulator" \
      org.opencontainers.image.url="https://www.seedarr.net" \
      org.opencontainers.image.source="https://github.com/dmzoneill/Seedarr" \
      org.opencontainers.image.licenses="Apache-2.0"

WORKDIR /app

COPY --from=backend /app ./
COPY --from=frontend /build/src/NzbDrone.Host/wwwroot/ ./wwwroot/
COPY version ./
COPY CHANGELOG.md ./
COPY tests/fixtures/ /app/fixtures/
COPY --chmod=755 container-entrypoint.sh /container-entrypoint.sh

ENV SEEDARR__APP_DATA=/config
ENV DOTNET_gcServer=0

EXPOSE 9898

VOLUME ["/config", "/data"]

HEALTHCHECK --interval=30s --timeout=10s --start-period=20s --retries=3 CMD curl -f http://localhost:9898/api/v1/system/status || exit 1

ENTRYPOINT ["/container-entrypoint.sh"]

# Stage 4: Test image with coverage tools
FROM runtime AS test
COPY --chmod=755 --from=backend /root/.dotnet/tools /opt/dotnet-tools
ENV PATH="$PATH:/opt/dotnet-tools"

# Stage 5: Final production release image (default target when building without --target)
FROM runtime AS release
