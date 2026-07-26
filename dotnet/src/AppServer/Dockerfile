# syntax=docker/dockerfile:1.7-labs
# AppServer is a background worker, but it hosts a minimal HTTP surface to expose health
# endpoints (/_health/live, /_health/ready), so it needs the ASP.NET Core shared framework.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# curl is needed by the container HEALTHCHECK (probes /_health/live); the aspnet image doesn't
# ship it. Installed as root before dropping to the non-root app user.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
COPY --parents "src/**/*.csproj" "./"
WORKDIR /src
COPY "Directory.Packages.props" "Directory.Packages.props"
RUN dotnet restore "AppServer/AppServer.csproj"
COPY src/ .
RUN dotnet build "AppServer/AppServer.csproj" -c $BUILD_CONFIGURATION

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR "/src"
RUN dotnet publish "AppServer/AppServer.csproj" --no-build --no-restore -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Liveness only: /_health/live runs the 'self' check, never the DB/broker readiness checks, so a
# transient Postgres/RabbitMQ blip won't trigger a restart loop. The aspnet base image defaults
# ASPNETCORE_HTTP_PORTS to 8080.
HEALTHCHECK --interval=10s --timeout=3s --start-period=40s --retries=12 \
    CMD curl -fsS http://localhost:8080/_health/live || exit 1
ENTRYPOINT ["dotnet", "AppServer.dll"]
