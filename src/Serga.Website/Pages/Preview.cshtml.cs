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
            ["products/services"] = ("Serga Services", "Make services available to the right customers, users and locations — and manage every request from one place.", "Products"),
            ["products/contracts"] = ("Serga Contracts", "Keep every agreement, owner, obligation and deadline in one place. Never let a renewal or notice period surprise you again.", "Products"),
            ["products/revenue"] = ("Serga Revenue", "Subscription & recurring revenue management. Manage the recurring products and services you sell to customers — with subscriptions, licenses, pricing and renewals in one place.", "Products"),
            ["products/leasing"] = ("Serga Leasing", "Know what you lease, where it is, what it costs and when the agreement ends. Keep the asset and its contract together throughout its lifecycle.", "Products"),
            ["solutions/property-management"] = ("Property Management", "Give tenants one place to discover and order the services available in their building.", "Solutions"),
            ["solutions/software-service-providers"] = ("Software & Service Providers", "Manage subscriptions, pricing, renewals and recurring revenue — from customer agreement to invoice.", "Solutions"),
            ["solutions/procurement-finance"] = ("Procurement & Finance", "See contracts, costs and renewal commitments before they become surprises.", "Solutions"),
            ["solutions/operations"] = ("Operations", "Connect operational assets with the agreements, suppliers and services behind them.", "Solutions"),
            ["platform"] = ("One platform. One business context.", "Organizations, people, locations, agreements, services, assets and workflows — connected.", "Platform"),
            ["pricing"] = ("A platform that fits your business.", "Pricing information will be published here as the Serga offering takes shape.", "Pricing"),
            ["contact"] = ("Let’s make the connection.", "Bring services, agreements and assets together with Serga.", "Contact / Book a demo")
        };
        var key = slug is null ? section : $"{section}/{slug}";
        if (key.Equals("products/subscriptions", StringComparison.OrdinalIgnoreCase)) return RedirectPermanent("/products/revenue");
        if (!pages.TryGetValue(key, out var page)) return NotFound();
        (Title, Description, Category) = page;
        if (key.Equals("products/revenue", StringComparison.OrdinalIgnoreCase)) Note = "This is the product direction for Serga Revenue. The illustrative overview uses example data; capability availability will be confirmed as the product develops.";
        if (key.Equals("contact", StringComparison.OrdinalIgnoreCase)) Note = "Demo booking and contact details are not available in this first preview. This page does not submit a request. Please return once contact arrangements are published.";
        return Page();
    }
}
