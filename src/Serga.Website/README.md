# Serga.Website

Standalone .NET 10 ASP.NET Core Razor Pages marketing website. No database, authentication, project references, frontend framework, package dependencies or build pipeline beyond .NET.

Run from the repository root:

```sh
dotnet run --project src/Serga.Website
```

Open http://localhost:5086. The separate TenantPlatform app keeps its existing configuration and port.

Build with `dotnet build src/Serga.Website/Serga.Website.csproj` or `dotnet build TenantPlatform.sln`.

The homepage is split into Razor partials in `Pages/Shared`. Design tokens and responsive styles live in `wwwroot/css/site.css`. Native details navigation works without JavaScript; optional JavaScript adds menu dismissal and desktop/mobile state synchronization. The official Serga logo is copied into this project’s assets/images/branding directory; other visuals use HTML/CSS or SVG. Product views are illustrative, not interactive application screens.

Product pages, Platform and Pricing are standalone Razor pages that describe the intended Serga product vision. Pricing is not finalized. The contact/demo form is a clearly labeled, disabled visual prototype; it sends and stores nothing. No contact details, testimonials or prices have been invented. Illustrative mockups use example names and data supplied in the brief, not customer references. The former /products/subscriptions route redirects permanently to /products/revenue. The header and footer Sign in links use `Serga:ApplicationUrl` from appsettings.json (default: https://serga.magida.org). Override it with the `Serga__ApplicationUrl` environment variable. Integration categories describe potential connections, not released integrations.

Future hosting: serga.com → this project; app.serga.com → TenantPlatform.Web. No deployment or DNS changes are included.

## Docker deployment

`docker/compose.yml` builds the independent `serga-website` service using
`docker/website/Dockerfile`. It publishes only Serga.Website with .NET 10 SDK
and runs the published output in the .NET 10 ASP.NET runtime as a non-root user.
The website has no database, project references or dependencies on other services.
It joins the frontend network and is reachable there as `http://serga-website:8080`.

Run the existing `bash scripts/deploy.sh` to deploy all services to the Linux VM.
Open http://192.168.123.105:8081 for the website; TenantPlatform remains on port 8080.
Compose sets Production and defaults `Serga__ApplicationUrl` to
http://192.168.123.105:8080. Set `Serga__ApplicationUrl` in `docker/.env` to override it.
The website serves HTTP directly for this deployment; DNS, TLS and reverse proxy
configuration are outside this setup. Production exception handling remains enabled.

To validate on a machine with Docker:

```sh
docker compose --env-file docker/.env -f docker/compose.yml config --quiet
docker compose --env-file docker/.env -f docker/compose.yml config --services
docker compose --env-file docker/.env -f docker/compose.yml build serga-website
```
