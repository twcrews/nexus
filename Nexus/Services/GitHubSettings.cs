namespace Nexus.Services;

public class GitHubSettings
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string? Organization { get; set; }
}
