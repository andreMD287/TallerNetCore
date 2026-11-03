# Contexto de build: raíz del repo.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config ./
COPY src/DisasterMaps.Simulator/ src/DisasterMaps.Simulator/
RUN dotnet publish src/DisasterMaps.Simulator/DisasterMaps.Simulator.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "DisasterMaps.Simulator.dll"]
