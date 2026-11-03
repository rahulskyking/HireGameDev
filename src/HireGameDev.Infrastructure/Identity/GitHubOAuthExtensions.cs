using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using HireGameDev.Domain.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace HireGameDev.Infrastructure.Identity;

internal static class GitHubOAuthExtensions
{
    public const string AuthenticationScheme = "GitHub";
    public const string DisplayName = "GitHub";

    private const string AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
    private const string TokenEndpoint = "https://github.com/login/oauth/access_token";
    private const string UserInformationEndpoint = "https://api.github.com/user";
    private const string UserEmailsEndpoint = "https://api.github.com/user/emails";
    private const string EmailVerifiedClaimType = "email_verified";
    private const string GitHubApiVersion = "2022-11-28";
    private const string GitHubUserAgent = "HireGameDev/2.0";

    public static AuthenticationBuilder AddGitHubOAuth(
        this AuthenticationBuilder builder,
        OAuthProviderOptions gitHubOptions)
    {
        return builder.AddOAuth(AuthenticationScheme, DisplayName, options =>
        {
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.ClaimsIssuer = AuthenticationScheme;
            options.ClientId = gitHubOptions.ClientId;
            options.ClientSecret = gitHubOptions.ClientSecret;
            options.CallbackPath = "/signin-github";

            options.AuthorizationEndpoint = AuthorizationEndpoint;
            options.TokenEndpoint = TokenEndpoint;
            options.UserInformationEndpoint = UserInformationEndpoint;
            options.UsePkce = true;

            options.Scope.Add("read:user");
            options.Scope.Add("user:email");

            options.Events.OnCreatingTicket = PopulateGitHubClaimsAsync;
            options.Events.OnRemoteFailure = context =>
            {
                var message = Uri.EscapeDataString("GitHub sign-in was cancelled or could not be completed.");
                context.Response.Redirect($"/Account/Login?remoteError={message}");
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });
    }

    private static async Task PopulateGitHubClaimsAsync(OAuthCreatingTicketContext context)
    {
        if (context.Identity is null || string.IsNullOrWhiteSpace(context.AccessToken))
        {
            throw new HttpRequestException("GitHub access token was missing during OAuth ticket creation.");
        }

        // 1. Fetch GitHub user profile (/user)
        using var userRequest = CreateGitHubApiRequest(UserInformationEndpoint, context.AccessToken);
        using var userResponse = await context.Backchannel.SendAsync(
            userRequest,
            HttpCompletionOption.ResponseHeadersRead,
            context.HttpContext.RequestAborted);

        if (!userResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Failed to retrieve GitHub user profile (HTTP {(int)userResponse.StatusCode}).");
        }

        using var userDoc = JsonDocument.Parse(
            await userResponse.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));

        var root = userDoc.RootElement;
        var userId = ReadJsonScalarAsString(root, "id");
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new HttpRequestException("GitHub user profile did not include an account identifier.");
        }

        var login = ReadJsonScalarAsString(root, "login");
        var fullName = ReadJsonScalarAsString(root, "name");
        var profileUrl = ReadJsonScalarAsString(root, "html_url");

        context.Identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId, ClaimValueTypes.String, AuthenticationScheme));

        if (!string.IsNullOrWhiteSpace(login))
        {
            context.Identity.AddClaim(new Claim(HireGameDevClaimTypes.GitHubLogin, login, ClaimValueTypes.String, AuthenticationScheme));
        }

        if (!string.IsNullOrWhiteSpace(profileUrl))
        {
            context.Identity.AddClaim(new Claim(HireGameDevClaimTypes.GitHubProfileUrl, profileUrl, ClaimValueTypes.String, AuthenticationScheme));
        }

        var displayName = !string.IsNullOrWhiteSpace(fullName) ? fullName : login;
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            context.Identity.AddClaim(new Claim(ClaimTypes.Name, displayName, ClaimValueTypes.String, AuthenticationScheme));
        }

        // 2. Fetch verified primary email (/user/emails) — works even when the developer keeps GitHub emails private.
        using var emailsRequest = CreateGitHubApiRequest(UserEmailsEndpoint, context.AccessToken);
        using var emailsResponse = await context.Backchannel.SendAsync(
            emailsRequest,
            HttpCompletionOption.ResponseHeadersRead,
            context.HttpContext.RequestAborted);

        if (!emailsResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Failed to retrieve GitHub user email addresses (HTTP {(int)emailsResponse.StatusCode}).");
        }

        using var emailsDoc = JsonDocument.Parse(
            await emailsResponse.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));

        var verifiedEmail = SelectVerifiedGitHubEmail(emailsDoc.RootElement);
        if (!string.IsNullOrWhiteSpace(verifiedEmail))
        {
            context.Identity.AddClaim(new Claim(ClaimTypes.Email, verifiedEmail, ClaimValueTypes.String, AuthenticationScheme));
            context.Identity.AddClaim(new Claim(EmailVerifiedClaimType, "true", ClaimValueTypes.Boolean, AuthenticationScheme));
        }
    }

    private static HttpRequestMessage CreateGitHubApiRequest(string endpoint, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd(GitHubUserAgent);
        request.Headers.Add("X-GitHub-Api-Version", GitHubApiVersion);
        return request;
    }

    private static string? SelectVerifiedGitHubEmail(JsonElement emailsArray)
    {
        if (emailsArray.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? primaryVerified = null;
        string? fallbackVerified = null;

        foreach (var item in emailsArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var isVerified = item.TryGetProperty("verified", out var verifiedProp) &&
                             verifiedProp.ValueKind == JsonValueKind.True;
            if (!isVerified)
            {
                continue;
            }

            var email = ReadJsonScalarAsString(item, "email");
            if (string.IsNullOrWhiteSpace(email) ||
                email.EndsWith("@users.noreply.github.com", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isPrimary = item.TryGetProperty("primary", out var primaryProp) &&
                            primaryProp.ValueKind == JsonValueKind.True;

            if (isPrimary)
            {
                primaryVerified = email;
                break;
            }

            fallbackVerified ??= email;
        }

        return primaryVerified ?? fallbackVerified;
    }

    private static string? ReadJsonScalarAsString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString()?.Trim(),
            JsonValueKind.Number => prop.GetRawText(),
            _ => null
        };
    }
}
