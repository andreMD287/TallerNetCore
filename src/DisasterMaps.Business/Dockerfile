# Contexto de build: raíz del repo (necesita contracts/ y nuget.config).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config ./
COPY contracts/protos/ contracts/protos/
COPY src/DisasterMaps.Business/DisasterMaps.Business.csproj src/DisasterMaps.Business/
RUN dotnet restore src/DisasterMaps.Business/DisasterMaps.Business.csproj
COPY src/DisasterMaps.Business/ src/DisasterMaps.Business/
RUN dotnet publish src/DisasterMaps.Business/DisasterMaps.Business.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=5002
EXPOSE 5002
USER $APP_UID
ENTRYPOINT ["dotnet", "DisasterMaps.Business.dll"]
