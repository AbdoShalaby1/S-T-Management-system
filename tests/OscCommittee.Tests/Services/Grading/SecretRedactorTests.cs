using OscCommittee.Web.Services.Grading;
using Xunit;

namespace OscCommittee.Tests.Services.Grading;

public sealed class SecretRedactorTests
{
    [Fact]
    public void SecretsInAppSettingsAreReplacedInTheSentBundleOnly()
    {
        const string bundle = """
            ## /Program.cs

            ````csharp
            var password = "keep-me";
            ````

            ## /appsettings.json

            ````json
            {
              "ConnectionStrings": {
                "Default": "Server=.;Password=hunter2"
              },
              "ApiKey": "xyz-secret",
              "Logging": {
                "LogLevel": "Warning"
              }
            }
            ````
            """;

        var redacted = SecretRedactor.RedactBundle(bundle);

        Assert.Contains("keep-me", redacted);
        Assert.DoesNotContain("hunter2", redacted);
        Assert.DoesNotContain("xyz-secret", redacted);
        Assert.Contains("***", redacted);
        Assert.Contains("Warning", redacted);
        Assert.Contains("\"LogLevel\"", redacted);
    }

    [Fact]
    public void NonAppSettingsFilesAreUntouched()
    {
        const string bundle = """
            ## /appsettings.json

            ````json
            { "Token": "hidden" }
            ````

            ## /notes.txt

            ````text
            token = "visible"
            ````
            """;

        var redacted = SecretRedactor.RedactBundle(bundle);

        Assert.DoesNotContain("hidden", redacted);
        Assert.Contains("visible", redacted);
    }

    [Fact]
    public void TruncatedJsonFallsBackToLineRedaction()
    {
        const string bundle = """
            ## /appsettings.Development.json

            ````json
            {
              "Password": "hunter2",
              "ConnectionStrings": { "Default": "Server=.;Database=x" },
              "Token": "abc"
            ````
            """;

        var redacted = SecretRedactor.RedactBundle(bundle);

        Assert.DoesNotContain("hunter2", redacted);
        Assert.DoesNotContain("Database=x", redacted);
        Assert.DoesNotContain("abc", redacted);
        Assert.Contains("***", redacted);
    }

    [Fact]
    public void ConnectionStringValuesAreRedactedEvenWhenTheKeyLooksInnocent()
    {
        const string json = """
            {
              "ConnectionStrings": {
                "Default": "Server=.;Database=OscCommittee"
              },
              "Logging": { "LogLevel": "Warning" }
            }
            """;

        var redacted = SecretRedactor.RedactJson(json);

        Assert.DoesNotContain("OscCommittee", redacted);
        Assert.Contains("Warning", redacted);
    }

    [Fact]
    public void BundlesWithoutAppSettingsAreReturnedUnchanged()
    {
        const string bundle = """
            ## /Program.cs

            ````csharp
            class A { }
            ````
            """;

        Assert.Equal(bundle, SecretRedactor.RedactBundle(bundle));
    }
}