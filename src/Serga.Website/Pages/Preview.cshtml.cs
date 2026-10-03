using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace Serga.Website.Pages;
public sealed class PreviewModel : PageModel
{
    public string Title { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string Category { get; private set; } = "";
    public string Note { get; private set; } = "More details are on the way. Explore the homepage for an introduction to the connected Serga platform.";
    public IActionResult OnGet(string section, string? slug)
    {
        var pages = new Dictionary<string, (string Title, string Description, string Category)>(StringComparer.OrdinalIgnoreCase)
        {
            ["products/services"] = ("Serga Services", "Deliver and manage services across organizations, locations and users.", "Products"),
            ["products/contracts"] = ("Serga Contracts", "Know what you’ve agreed to — and what happens next.", "Products"),
            ["products/subscriptions"] = ("Serga Subscriptions", "Manage recurring business without spreadsheets.", "Products"),
            ["products/leasing"] = ("Serga Leasing", "Every leased asset. Every agreement. Always under control.", "Products"),
            ["solutions/property-management"] = ("Property Management", "Connect tenants with the services available in their building.", "Solutions"),
            ["solutions/software-service-providers"] = ("Software & Service Providers", "Manage customers, subscriptions and recurring services.", "Solutions"),
            ["solutions/procurement-finance"] = ("Procurement & Finance", "Stay ahead of contracts, costs and renewals.", "Solutions"),
            ["solutions/operations"] = ("Operations", "Keep leased equipment and agreements under control.", "Solutions"),
            ["platform"] = ("One platform. One business context.", "Organizations, people, locations, agreements, services, assets and workflows — connected.", "Platform"),
            ["pricing"] = ("A platform that fits your business.", "Pricing information will be published here as the Serga offering takes shape.", "Pricing"),
            ["contact"] = ("Let’s make the connection.", "Bring services, agreements and assets together with Serga.", "Contact / Book a demo")
        };
        var key = slug is null ? section : $"{section}/{slug}";
        if (!pages.TryGetValue(key, out var page)) return NotFound();
        (Title, Description, Category) = page;
        if (key.Equals("contact", StringComparison.OrdinalIgnoreCase)) Note = "Demo booking and contact details are not available in this first preview. This page does not submit a request. Please return once contact arrangements are published.";
        return Page();
    }
}
