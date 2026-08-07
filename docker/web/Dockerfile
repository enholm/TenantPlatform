FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /source

COPY TenantPlatform.sln ./

COPY src/TenantPlatform.Web/TenantPlatform.Web.csproj \
     src/TenantPlatform.Web/

COPY src/TenantPlatform.Core/TenantPlatform.Core.csproj \
     src/TenantPlatform.Core/

COPY src/TenantPlatform.Infrastructure/TenantPlatform.Infrastructure.csproj \
     src/TenantPlatform.Infrastructure/

RUN dotnet restore src/TenantPlatform.Web/TenantPlatform.Web.csproj

COPY src/ src/

RUN dotnet publish \
    src/TenantPlatform.Web/TenantPlatform.Web.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "TenantPlatform.Web.dll"]