# Contexto: raíz del repositorio.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config ./
COPY contracts/protos/ contracts/protos/
COPY src/DisasterMaps.Data/DisasterMaps.Data.csproj src/DisasterMaps.Data/
RUN dotnet restore src/DisasterMaps.Data/DisasterMaps.Data.csproj --configfile nuget.config
COPY src/DisasterMaps.Data/ src/DisasterMaps.Data/
RUN dotnet publish src/DisasterMaps.Data/DisasterMaps.Data.csproj -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=5003
EXPOSE 5003
USER $APP_UID
ENTRYPOINT ["dotnet", "DisasterMaps.Data.dll"]
