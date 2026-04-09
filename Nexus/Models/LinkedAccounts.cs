namespace Nexus.Models;

public class LinkedAccounts
{
    public List<DummyAccountToken> DummyAccounts { get; set; } = [];
    public List<MicrosoftAccountToken> MicrosoftAccounts { get; set; } = [];
    public List<GitHubAccountToken> GitHubAccounts { get; set; } = [];
}

public record DummyAccountToken(string AccountName);

public class MicrosoftAccountToken
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}

public class GitHubAccountToken
{
    public string AccessToken { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }
}
