# ── Build stage ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore (layer-cached)
COPY src/MccIntakeService/MccIntakeService.csproj src/MccIntakeService/
RUN dotnet restore src/MccIntakeService/MccIntakeService.csproj

# Copy everything else and publish
COPY src/ src/
RUN dotnet publish src/MccIntakeService/MccIntakeService.csproj \
    -c Release -o /app/publish --no-restore

# ── Runtime stage ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MccIntakeService.dll"]
