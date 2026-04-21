namespace Nexus.Models;

public record UserReference(string Name, string? AvatarUrl)
{
    public string DisplayName
    {
        get
        {
            var comma = Name.IndexOf(',');
            if (comma <= 0) return Name;
            var last = Name[..comma].Trim();
            var first = Name[(comma + 1)..].Trim();
            return string.IsNullOrEmpty(first) ? last : $"{first} {last}";
        }
    }
}
