# Serga.Website

Standalone .NET 10 ASP.NET Core Razor Pages marketing website. No database, authentication, project references, frontend framework, package dependencies or build pipeline beyond .NET.

Run from the repository root:

```sh
dotnet run --project src/Serga.Website
```

Open http://localhost:5086. The separate TenantPlatform app keeps its existing configuration and port.

Build with `dotnet build src/Serga.Website/Serga.Website.csproj` or `dotnet build TenantPlatform.sln`.

The homepage is split into Razor partials in `Pages/Shared`. Design tokens and responsive styles live in `wwwroot/css/site.css`. Native details navigation works without JavaScript; optional JavaScript adds menu dismissal and desktop/mobile state synchronization. The official Serga logo is copied into this project’s assets/images/branding directory; other visuals use HTML/CSS or SVG. Product views are illustrative, not interactive application screens.

Product pages, Platform and Pricing are standalone Razor pages that describe the intended Serga product vision. Pricing is not finalized. The contact/demo form is a clearly labeled, disabled visual prototype; it sends and stores nothing. No contact details, testimonials or prices have been invented. Illustrative mockups use example names and data supplied in the brief, not customer references. The former /products/subscriptions route redirects permanently to /products/revenue. Sign in points to the intended future application host https://app.serga.com, whose availability is outside this prototype. Integration categories describe potential connections, not released integrations.

Future hosting: serga.com → this project; app.serga.com → TenantPlatform.Web. No deployment or DNS changes are included.
