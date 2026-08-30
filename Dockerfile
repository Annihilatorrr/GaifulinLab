# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY GaifulinLab.slnx ./
COPY src/GaifulinLab.Domain/GaifulinLab.Domain.csproj src/GaifulinLab.Domain/
COPY src/GaifulinLab.Application/GaifulinLab.Application.csproj src/GaifulinLab.Application/
COPY src/GaifulinLab.Infrastructure/GaifulinLab.Infrastructure.csproj src/GaifulinLab.Infrastructure/
COPY src/GaifulinLab.Contracts/GaifulinLab.Contracts.csproj src/GaifulinLab.Contracts/
COPY src/GaifulinLab.Web.Client/GaifulinLab.Web.Client.csproj src/GaifulinLab.Web.Client/
COPY src/GaifulinLab.Web/GaifulinLab.Web.csproj src/GaifulinLab.Web/
RUN dotnet restore src/GaifulinLab.Web/GaifulinLab.Web.csproj

COPY src/ src/
RUN dotnet publish src/GaifulinLab.Web/GaifulinLab.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish ./
RUN mkdir -p /app/data/media && chown -R app:app /app/data

USER app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "GaifulinLab.Web.dll"]
