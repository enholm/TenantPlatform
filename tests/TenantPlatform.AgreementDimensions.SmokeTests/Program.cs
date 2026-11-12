using System.Globalization;
using TenantPlatform.Web.Services.Agreements;

CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
await ConfigurationChecks.Run();
await AssignmentChecks.Run();
await AccessChecks.Run();
MetadataChecks.Run();
await UiChecks.Run();
Console.WriteLine($"Agreement dimension smoke checks passed ({Check.Count} checks; InMemory and offline metadata only; no database connections or SQL execution).");

static class Check
{
    public static int Count { get; private set; }
    public static void That(bool condition, string message)
    { if (!condition) throw new Exception("FAIL: " + message); Count++; Console.WriteLine("PASS: " + message); }
    public static async Task Error<T>(Func<Task> operation, string message, string? key = null) where T : Exception
    {
        try { await operation(); }
        catch (T e) { That(key is null || e.Message == key, message + (key is null ? "" : " [expected " + key + ", actual " + e.Message + "]")); return; }
        throw new Exception("FAIL: " + message + " (expected " + typeof(T).Name + ")");
    }
    public static Task Invalid(Func<Task> operation, string key, string message) => Error<AgreementValidationException>(operation, message, key);
}
