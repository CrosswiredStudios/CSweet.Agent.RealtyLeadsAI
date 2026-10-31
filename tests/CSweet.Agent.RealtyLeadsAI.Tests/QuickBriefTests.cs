namespace CSweet.Agent.RealtyLeadsAI.Tests;

public sealed class QuickBriefTests
{
    [Theory]
    [InlineData("I would like any lead information you have for New York", "NY", 5)]
    [InlineData("find leads in texas", "TX", 5)]
    [InlineData("give me 8 leads for Ohio", "OH", 8)]
    [InlineData("Show me what inheritance leads you have in West Virginia", "WV", 5)]
    [InlineData("leads in Virginia please", "VA", 5)]
    [InlineData("Research NJ leads", "NJ", 5)]
    [InlineData("How many leads do you have in Oregon?", "OR", 5)]
    public void StateOnlyRequestsAreInterpretedWithoutAModel(string text, string state, int limit)
    {
        var brief = QuickBrief.TryParse(text);
        Assert.NotNull(brief);
        Assert.Equal((state, limit), (brief!.State, brief.Limit));
        Assert.True(brief.City is null && brief.County is null && brief.Status is null && brief.MinScore is null);
    }

    [Theory]
    [InlineData("leads in Austin TX")]                        // another place name: the planner must ground the city
    [InlineData("leads in TX with score over 70")]            // a filter the fast path does not model
    [InlineData("leads in Travis County")]                    // county without state
    [InlineData("leads in New York and New Jersey")]          // two states
    [InlineData("15 leads in Texas")]                         // above the chat maximum
    [InlineData("leads in the 78701 area of Texas")]          // a ZIP code
    [InlineData("don't search New York")]                     // negation
    [InlineData("Thanks for the New York leads")]             // acknowledgement
    [InlineData("email the Texas leads")]                     // outreach
    [InlineData("lead information in the area")]             // 'in' is not Indiana
    [InlineData("what can you do?")]                          // no state at all
    [InlineData("look up lead 019fd073-4b80-7583-b7cf-cf454be3fd51 in NY")]
    public void AnythingRicherGoesToThePlanner(string text) => Assert.Null(QuickBrief.TryParse(text));

    [Fact]
    public void WordLikeAbbreviationsNeedCapitals()
    {
        Assert.Null(UsStates.FindSingleNamed("leads in the area or nearby"));
        Assert.Equal("OR", UsStates.FindSingleNamed("leads in OR"));
        Assert.Equal("TX", UsStates.FindSingleNamed("leads in tx"));
        Assert.Equal("WV", UsStates.FindSingleNamed("West Virginia leads"));
        Assert.Null(UsStates.FindSingleNamed("Virginia and Maryland"));
    }
}
