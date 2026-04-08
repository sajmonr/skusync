# syntax=docker/dockerfile:1.7-labs
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
COPY --parents "src/**/*.csproj" "./"
WORKDIR /src
COPY "Directory.Packages.props" "Directory.Packages.props"
RUN dotnet restore "Web.Api/Web.Api.csproj"
COPY src/ .
RUN dotnet build "Web.Api/Web.Api.csproj" -c $BUILD_CONFIGURATION

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR "/src"
RUN dotnet publish "Web.Api/Web.Api.csproj" --no-build --no-restore -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Web.Api.dll"]
