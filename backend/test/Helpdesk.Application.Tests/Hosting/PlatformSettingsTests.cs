using System;
using System.Collections.Generic;
using System.Data.Common;
using Helpdesk.Hosting;
using Shouldly;
using Xunit;

namespace Helpdesk.Hosting;

public class PlatformSettingsTests
{
    private static Dictionary<string, string> Run(Dictionary<string, string> environment)
    {
        var result = new Dictionary<string, string>(environment);
        PlatformSettings.Apply(name => result.GetValueOrDefault(name), (name, value) => result[name] = value);
        return result;
    }

    private static Dictionary<string, string> Parse(string connectionString)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in builder.Keys)
        {
            parts[key] = Convert.ToString(builder[key])!;
        }

        return parts;
    }

    [Fact]
    public void A_database_url_becomes_the_connection_string()
    {
        var parts = Parse(PlatformSettings.ToNpgsql("postgresql://helpdesk:s3cret@dpg-abc123-a:5432/helpdesk_db"));

        parts["Host"].ShouldBe("dpg-abc123-a");
        parts["Port"].ShouldBe("5432");
        parts["Database"].ShouldBe("helpdesk_db");
        parts["Username"].ShouldBe("helpdesk");
        parts["Password"].ShouldBe("s3cret");
    }

    [Fact]
    public void A_password_with_awkward_characters_survives_the_trip()
    {
        // percent-encoded in the address, quoted in the connection string: a ; or = must not split the setting
        var parts = Parse(PlatformSettings.ToNpgsql("postgres://us%40er:p%3Bw%3Dd%22%27%20x@db.example.test:6543/my%20db"));

        parts["Username"].ShouldBe("us@er");
        parts["Password"].ShouldBe("p;w=d\"' x");
        parts["Database"].ShouldBe("my db");
        parts["Port"].ShouldBe("6543");
    }

    [Fact]
    public void The_port_defaults_and_ssl_mode_is_carried_over()
    {
        var parts = Parse(PlatformSettings.ToNpgsql("postgres://u:p@host.example.test/db?sslmode=require"));

        parts["Port"].ShouldBe("5432");
        parts["SSL Mode"].ShouldBe("Require");
        Parse(PlatformSettings.ToNpgsql("postgres://u:p@h/db?sslmode=verify-full"))["SSL Mode"].ShouldBe("VerifyFull");
        Parse(PlatformSettings.ToNpgsql("postgres://u:p@h/db")).ContainsKey("SSL Mode").ShouldBeFalse();
    }

    [Fact]
    public void An_address_without_a_password_is_accepted()
    {
        var parts = Parse(PlatformSettings.ToNpgsql("postgres://u@h/db"));

        parts["Username"].ShouldBe("u");
        parts.ContainsKey("Password").ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("mysql://u:p@h/db")]
    [InlineData("postgres:///db")]
    [InlineData("postgres://u:p@h/db?sslmode=sometimes")]
    public void Something_that_is_not_a_postgres_address_is_refused_with_a_clear_message(string url)
    {
        Should.Throw<FormatException>(() => PlatformSettings.ToNpgsql(url));
    }

    [Fact]
    public void The_platforms_settings_fill_in_what_was_not_set()
    {
        var result = Run(new()
        {
            ["DATABASE_URL"] = "postgres://u:p@h:5432/db",
            ["RENDER_EXTERNAL_URL"] = "https://helpdesk-api.onrender.com/",
        });

        Parse(result["ConnectionStrings__Default"])["Host"].ShouldBe("h");
        result["App__SelfUrl"].ShouldBe("https://helpdesk-api.onrender.com");
        result["AuthServer__Authority"].ShouldBe("https://helpdesk-api.onrender.com");
        result["OpenIddict__Applications__Helpdesk_Swagger__RootUrl"].ShouldBe("https://helpdesk-api.onrender.com");
    }

    [Fact]
    public void What_was_set_explicitly_always_wins()
    {
        var result = Run(new()
        {
            ["DATABASE_URL"] = "postgres://u:p@h/db",
            ["ConnectionStrings__Default"] = "Host=mine",
            ["RENDER_EXTERNAL_URL"] = "https://render.example",
            ["App__SelfUrl"] = "https://my.example",
        });

        result["ConnectionStrings__Default"].ShouldBe("Host=mine");
        result["App__SelfUrl"].ShouldBe("https://my.example");
        result["AuthServer__Authority"].ShouldBe("https://render.example"); // only the ones that were missing
    }

    [Fact]
    public void Without_a_platform_nothing_is_touched()
    {
        Run(new()).ShouldBeEmpty();
        Run(new() { ["DATABASE_URL"] = "", ["RENDER_EXTERNAL_URL"] = "  " }).Keys.ShouldBe(new[] { "DATABASE_URL", "RENDER_EXTERNAL_URL" }, ignoreOrder: true);
    }
}
