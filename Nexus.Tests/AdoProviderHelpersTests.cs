using Microsoft.TeamFoundation.Work.WebApi;
using Nexus.Models;
using Nexus.Providers;

namespace Nexus.Tests;

public class AdoProviderEscapeWiqlTests
{
    [Fact]
    public void EscapeWiql_NoQuotes_ReturnsUnchanged()
    {
        Assert.Equal("MyProject", AdoProvider.EscapeWiql("MyProject"));
    }

    [Fact]
    public void EscapeWiql_SingleQuote_IsDoubled()
    {
        Assert.Equal("O''Brien", AdoProvider.EscapeWiql("O'Brien"));
    }

    [Fact]
    public void EscapeWiql_MultipleQuotes_AllDoubled()
    {
        Assert.Equal("it''s a ''test''", AdoProvider.EscapeWiql("it's a 'test'"));
    }

    [Fact]
    public void EscapeWiql_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", AdoProvider.EscapeWiql(""));
    }

    [Fact]
    public void EscapeWiql_OnlyQuotes_AllDoubled()
    {
        Assert.Equal("''''", AdoProvider.EscapeWiql("''"));
    }
}

public class AdoProviderMapVoteTests
{
    [Theory]
    [InlineData(10, ReviewerVote.Approved)]
    [InlineData(5, ReviewerVote.ApprovedWithSuggestions)]
    [InlineData(-5, ReviewerVote.WaitingForAuthor)]
    [InlineData(-10, ReviewerVote.Rejected)]
    [InlineData(0, ReviewerVote.None)]
    [InlineData(1, ReviewerVote.None)]
    [InlineData(99, ReviewerVote.None)]
    [InlineData(-1, ReviewerVote.None)]
    public void MapVote_ReturnsExpectedVote(int vote, ReviewerVote expected)
    {
        Assert.Equal(expected, AdoProvider.MapVote(vote));
    }
}

public class AdoProviderBuildAreaPathFilterTests
{
    private static TeamFieldValues MakeFieldValues(params (string value, bool includeChildren)[] entries)
    {
        return new TeamFieldValues
        {
            Values = entries
                .Select(e => new TeamFieldValue { Value = e.value, IncludeChildren = e.includeChildren })
                .ToList()
        };
    }

    [Fact]
    public void BuildAreaPathFilter_NullFieldValues_ReturnsNull()
    {
        Assert.Null(AdoProvider.BuildAreaPathFilter(null!));
    }

    [Fact]
    public void BuildAreaPathFilter_EmptyValues_ReturnsNull()
    {
        var tfv = new TeamFieldValues { Values = [] };
        Assert.Null(AdoProvider.BuildAreaPathFilter(tfv));
    }

    [Fact]
    public void BuildAreaPathFilter_SingleValueWithoutChildren_UsesEquality()
    {
        var tfv = MakeFieldValues(("MyProject\\Team", false));
        var filter = AdoProvider.BuildAreaPathFilter(tfv);
        Assert.Equal("([System.AreaPath] = 'MyProject\\Team')", filter);
    }

    [Fact]
    public void BuildAreaPathFilter_SingleValueWithChildren_UsesUnder()
    {
        var tfv = MakeFieldValues(("MyProject\\Team", true));
        var filter = AdoProvider.BuildAreaPathFilter(tfv);
        Assert.Equal("([System.AreaPath] UNDER 'MyProject\\Team')", filter);
    }

    [Fact]
    public void BuildAreaPathFilter_MultipleValues_JoinsWithOr()
    {
        var tfv = MakeFieldValues(
            ("Proj\\A", false),
            ("Proj\\B", true));
        var filter = AdoProvider.BuildAreaPathFilter(tfv);
        Assert.Equal("([System.AreaPath] = 'Proj\\A' OR [System.AreaPath] UNDER 'Proj\\B')", filter);
    }

    [Fact]
    public void BuildAreaPathFilter_ValueWithSingleQuote_IsEscaped()
    {
        var tfv = MakeFieldValues(("O'Brien\\Team", false));
        var filter = AdoProvider.BuildAreaPathFilter(tfv);
        Assert.Equal("([System.AreaPath] = 'O''Brien\\Team')", filter);
    }

    [Fact]
    public void BuildAreaPathFilter_ResultIsWrappedInParentheses()
    {
        var tfv = MakeFieldValues(("Proj", false));
        var filter = AdoProvider.BuildAreaPathFilter(tfv);
        Assert.StartsWith("(", filter);
        Assert.EndsWith(")", filter);
    }
}
