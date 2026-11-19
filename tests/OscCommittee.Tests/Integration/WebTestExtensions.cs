using System.Net;
using System.Text.RegularExpressions;

namespace OscCommittee.Tests.Integration;

public static class WebTestExtensions
{
    private static readonly Regex TokenPattern = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"",
        RegexOptions.Compiled);

    public static string UniqueEmail(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}@test.local";

    public static async Task<string> GetAntiforgeryTokenAsync(this HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = TokenPattern.Match(html);

        if (!match.Success)
        {
            throw new InvalidOperationException($"No antiforgery token was rendered on {path}.");
        }

        return match.Groups["token"].Value;
    }

    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient client,
        string path,
        Dictionary<string, string> fields) =>
        await client.PostAsync(path, new FormUrlEncodedContent(fields));

    public static async Task<HttpResponseMessage> LoginAsync(
        this HttpClient client,
        string email,
        string password)
    {
        var token = await client.GetAntiforgeryTokenAsync("/account/login");

        return await client.PostFormAsync("/account/login", new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "true",
            ["__RequestVerificationToken"] = token
        });
    }

    public static async Task<HttpResponseMessage> RegisterAsync(
        this HttpClient client,
        string displayName,
        string email,
        string password)
    {
        var token = await client.GetAntiforgeryTokenAsync("/account/register");

        return await client.PostFormAsync("/account/register", new Dictionary<string, string>
        {
            ["DisplayName"] = displayName,
            ["Email"] = email,
            ["Password"] = password,
            ["ConfirmPassword"] = password,
            ["__RequestVerificationToken"] = token
        });
    }

    public static async Task<HttpClient> SignedInClientAsync(
        this OscAppFactory app,
        string email,
        string password)
    {
        var client = app.CreateClient();
        using var response = await client.LoginAsync(email, password);

        if (response.StatusCode != HttpStatusCode.OK || response.RequestMessage?.RequestUri?.AbsolutePath == "/account/login")
        {
            var body = await response.Content.ReadAsStringAsync();
            client.Dispose();
            throw new InvalidOperationException(
                $"Sign-in failed for {email}: {response.StatusCode} at {response.RequestMessage?.RequestUri}. {body[..Math.Min(body.Length, 2000)]}");
        }

        return client;
    }
}