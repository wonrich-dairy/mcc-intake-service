# ── Build stage ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the csproj of the service and of every project it references, then restore (layer-cached).
# A missing referenced project fails the build with NETSDK1004.
COPY src/MccIntakeService/MccIntakeService.csproj src/MccIntakeService/
COPY src/Wonrich.Auth/Wonrich.Auth.csproj src/Wonrich.Auth/
COPY src/Wonrich.QualityPanel/Wonrich.QualityPanel.csproj src/Wonrich.QualityPanel/
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
