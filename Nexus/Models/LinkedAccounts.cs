namespace Nexus.Models;

public class LinkedAccounts
{
    public List<MicrosoftAccountToken> MicrosoftAccounts { get; set; } = [];
    public List<GitHubAccountToken> GitHubAccounts { get; set; } = [];
}

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
}
