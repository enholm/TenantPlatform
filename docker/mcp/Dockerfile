FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /source

COPY TenantPlatform.sln ./

COPY src/TenantPlatform.Mcp/TenantPlatform.Mcp.csproj \
     src/TenantPlatform.Mcp/

COPY src/TenantPlatform.Core/TenantPlatform.Core.csproj \
     src/TenantPlatform.Core/

COPY src/TenantPlatform.Application/TenantPlatform.Application.csproj \
     src/TenantPlatform.Application/

COPY src/TenantPlatform.Infrastructure/TenantPlatform.Infrastructure.csproj \
     src/TenantPlatform.Infrastructure/

RUN dotnet restore src/TenantPlatform.Mcp/TenantPlatform.Mcp.csproj

COPY src/ src/

RUN dotnet publish \
    src/TenantPlatform.Mcp/TenantPlatform.Mcp.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

USER $APP_UID

WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "TenantPlatform.Mcp.dll"]
