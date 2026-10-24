namespace HireGameDev.Infrastructure.Identity;

public sealed class ExternalAuthOptions
{
    public const string SectionName = "Authentication";

    public OAuthProviderOptions Google { get; set; } = new();

    public OAuthProviderOptions GitHub { get; set; } = new();
}

public sealed class OAuthProviderOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);
}
