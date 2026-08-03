# ===== Build stage =====
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first so `dotnet restore` is cached unless deps change.
COPY HRM.Api/HRM.Api.csproj HRM.Api/
COPY HRM.Application/HRM.Application.csproj HRM.Application/
COPY HRM.Infrastructure/HRM.Infrastructure.csproj HRM.Infrastructure/
COPY HRM.Domain/HRM.Domain.csproj HRM.Domain/
RUN dotnet restore HRM.Api/HRM.Api.csproj

# Copy the rest of the source and publish the API.
COPY . .
RUN dotnet publish HRM.Api/HRM.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ===== Debug stage =====
# Built ONLY by docker-compose.debug.yml (build target: debug). Produces a Debug-config
# build with the .NET debugger (vsdbg) installed, so VS Code can attach via `docker exec`
# and breakpoints bind inside the container. The .pdb source paths point at /src (the
# build WORKDIR), which launch.json maps back to the host via sourceFileMap.
#
# ISSUE-360 — this stage MUST NOT be last. Docker builds the FINAL stage when no `target:`
# is given, so while `debug` sat at the bottom of this file the plain `docker compose build
# backend` silently produced it: the running API was a Debug build on the SDK base, 6.85 GB
# instead of ~400 MB, and `runtime` was never built at all. Keep `runtime` as the last stage
# so the safe artefact is the default; anything added below it inherits the same trap.
FROM build AS debug
RUN apt-get update \
    && apt-get install -y --no-install-recommends unzip procps curl \
    && rm -rf /var/lib/apt/lists/* \
    && curl -sSL https://aka.ms/getvsdbgsh | bash /dev/stdin -v latest -l /vsdbg
RUN dotnet build HRM.Api/HRM.Api.csproj -c Debug -o /app/debug
WORKDIR /app/debug
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000 \
    ASPNETCORE_ENVIRONMENT=Development
ENTRYPOINT ["dotnet", "HRM.Api.dll"]

# ===== Runtime stage — MUST REMAIN LAST (ISSUE-360; it is the default build target) =====
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# ISSUE-346: the compose healthcheck probes /health/ready, and the aspnet runtime image ships NEITHER curl NOR
# wget (verified — `command -v` finds neither in mcr.microsoft.com/dotnet/aspnet:10.0). Without this the
# healthcheck could never pass, `backend` would sit permanently unhealthy, and `frontend` would never start.
# curl only, --no-install-recommends, and the apt lists are dropped in the same layer to keep the image lean.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "HRM.Api.dll"]
