# Contexto: raíz del repositorio.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config ./
COPY src/DisasterMaps.Web/DisasterMaps.Web.csproj src/DisasterMaps.Web/
RUN dotnet restore src/DisasterMaps.Web/DisasterMaps.Web.csproj --configfile nuget.config
COPY src/DisasterMaps.Web/ src/DisasterMaps.Web/
RUN dotnet publish src/DisasterMaps.Web/DisasterMaps.Web.csproj -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=5001
EXPOSE 5001
USER $APP_UID
ENTRYPOINT ["dotnet", "DisasterMaps.Web.dll"]
