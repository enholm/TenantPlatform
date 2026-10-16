FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /source

COPY src/Serga.Website/Serga.Website.csproj src/Serga.Website/

RUN dotnet restore src/Serga.Website/Serga.Website.csproj

COPY src/Serga.Website/ src/Serga.Website/

RUN dotnet publish \
    src/Serga.Website/Serga.Website.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

USER $APP_UID

ENTRYPOINT ["dotnet", "Serga.Website.dll"]
