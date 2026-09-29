using System.Net;

// Opt-in, test-only snapshots of actual Razor output; no application database or login required.
static class UiPreview
{
    public static void Write(string name, string html)
    {
        var directory = Environment.GetEnvironmentVariable("LEASING_UI_PREVIEW");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        foreach (var theme in new[] { "light", "dark" })
        {
            var title = WebUtility.HtmlEncode(name);
            File.WriteAllText(Path.Combine(directory, $"{name}-{theme}.html"), $$"""
                <!doctype html><html lang="nb" data-bs-theme="{{theme}}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
                <title>{{title}} · {{theme}}</title><link rel="stylesheet" href="bootstrap.min.css"><link rel="stylesheet" href="bootstrap-icons.css"><link rel="stylesheet" href="app.css"><link rel="stylesheet" href="leasing.css"></head>
                <body><main style="padding:24px"><div class="leasing-workspace">{{html}}</div></main></body></html>
                """);
        }
    }
}
