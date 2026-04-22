namespace Nexus.Models;

public class LinkedAccounts
{
    public List<MicrosoftAccountToken> MicrosoftAccounts { get; set; } = [];
    public List<GitHubAccountToken> GitHubAccounts { get; set; } = [];
}

public class MicrosoftAccountToken
{
    /// <summary>User's UPN / email, used as a stable identifier.</summary>
    public string Login { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PersonalAccessToken { get; set; } = "";
    /// <summary>The ADO org URL provided at link time, e.g. https://dev.azure.com/myorg</summary>
    public string OrgUrl { get; set; } = "";
    public List<AdoMonitoredProject> MonitoredProjects { get; set; } = [];
}

public class GitHubAccountToken
{
    public string PersonalAccessToken { get; set; } = "";
    public string Login { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    /// <summary>
    /// Repositories to monitor in owner/repo format. Empty list means monitor nothing (explicit opt-in required).
    /// </summary>
    public List<string> MonitoredRepos { get; set; } = [];
}
