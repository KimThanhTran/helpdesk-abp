using System;
using System.Data.Common;
using System.Web;

namespace Helpdesk.Hosting;

/// <summary>
/// Reads the settings a hosting platform provides under its own names and turns them into this application's own, so a
/// platform such as Render needs no hand-typed connection string or address. Anything set explicitly always wins.
/// </summary>
public static class PlatformSettings
{
    /// <summary>
    /// <c>DATABASE_URL</c> (<c>postgres://user:password@host:5432/name</c>) becomes <c>ConnectionStrings__Default</c>, and
    /// <c>RENDER_EXTERNAL_URL</c> (the service's public address) becomes the application's own address, the token issuer
    /// and the address the sign-in clients are registered for.
    /// </summary>
    public static void Apply(Func<string, string?> get, Action<string, string> set)
    {
        SetIfMissing(get, set, "ConnectionStrings__Default", () => get("DATABASE_URL") is { Length: > 0 } url ? ToNpgsql(url) : null);

        var address = get("RENDER_EXTERNAL_URL");
        if (!string.IsNullOrWhiteSpace(address))
        {
            var trimmed = address.TrimEnd('/');
            SetIfMissing(get, set, "App__SelfUrl", () => trimmed);
            SetIfMissing(get, set, "AuthServer__Authority", () => trimmed);
            SetIfMissing(get, set, "OpenIddict__Applications__Helpdesk_Swagger__RootUrl", () => trimmed);
        }
    }

    private static void SetIfMissing(Func<string, string?> get, Action<string, string> set, string name, Func<string?> value)
    {
        if (!string.IsNullOrWhiteSpace(get(name)))
        {
            return;
        }

        if (value() is { Length: > 0 } found)
        {
            set(name, found);
        }
    }

    /// <summary>A <c>postgres://</c> or <c>postgresql://</c> address in the key=value form the database driver reads.</summary>
    public static string ToNpgsql(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("postgres" or "postgresql")
            || string.IsNullOrEmpty(uri.Host))
        {
            throw new FormatException("DATABASE_URL must look like postgres://user:password@host:5432/database.");
        }

        var credentials = uri.UserInfo.Split(':', 2);
        var builder = new DbConnectionStringBuilder
        {
            ["Host"] = uri.Host,
            ["Port"] = uri.Port > 0 ? uri.Port : 5432,
            ["Database"] = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            ["Username"] = Uri.UnescapeDataString(credentials[0]),
        };
        if (credentials.Length > 1)
        {
            builder["Password"] = Uri.UnescapeDataString(credentials[1]);
        }

        // sslmode=require|verify-full|... in the address, as the providers write it
        var mode = HttpUtility.ParseQueryString(uri.Query)["sslmode"];
        if (!string.IsNullOrEmpty(mode))
        {
            builder["SSL Mode"] = mode switch
            {
                "disable" => "Disable",
                "allow" => "Allow",
                "prefer" => "Prefer",
                "require" => "Require",
                "verify-ca" => "VerifyCA",
                "verify-full" => "VerifyFull",
                _ => throw new FormatException($"Unknown sslmode '{mode}' in DATABASE_URL."),
            };
        }

        return builder.ConnectionString;
    }
}
