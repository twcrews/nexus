namespace Nexus.Models;

public class AdoMonitoredProject
{
    /// <summary>Organization base URL, e.g. "https://dev.azure.com/myorg"</summary>
    public string OrgUrl { get; set; } = "";
    public string ProjectName { get; set; } = "";
    /// <summary>Git repo names to monitor for pull requests. Empty = no PR monitoring for this project.</summary>
    public List<string> RepoNames { get; set; } = [];
    /// <summary>Team names for work item monitoring. Empty = no work item monitoring for this project.</summary>
    public List<string> TeamNames { get; set; } = [];
}
