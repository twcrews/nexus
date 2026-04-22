namespace Nexus.Models;

public enum ReviewerVote { None, Approved, ApprovedWithSuggestions, WaitingForAuthor, Rejected }

public record ReviewerReference(UserReference User, ReviewerVote Vote);
