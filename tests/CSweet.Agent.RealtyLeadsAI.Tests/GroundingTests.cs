using System.Text.Json;
namespace CSweet.Agent.RealtyLeadsAI.Tests;

public sealed class GroundingTests
{
    [Theory]
    [InlineData("I would like any lead information you have for New York", "NY")]
    [InlineData("find leads in texas", "TX")]
    [InlineData("leads for TX please", "TX")]
    [InlineData("north   carolina leads", "NC")]
    public void StateNamedInWordsGroundsTheAbbreviation(string text, string state)
    {
        var brief = Grounding.GroundedSearch(new("Search", new(State: state, Limit: 5)), text);
        Assert.NotNull(brief);
        Assert.Equal(state, brief!.State);
        Assert.Null(brief.City); Assert.Null(brief.County);
    }

    [Theory]
    [InlineData("Search TX", "CA")]                       // abbreviation invented
    [InlineData("leads in Newark", "NY")]                 // "New York" is not a whole-word match of Newark
    [InlineData("find leads", "TX")]                      // no geography at all
    [InlineData("leads in Yorkshire", "NY")]
    public void StateNotNamedAndNoPlaceIsRefused(string text, string state) =>
        Assert.Null(Grounding.GroundedSearch(new("Search", new(State: state, Limit: 5)), text));

    [Fact]
    public void QuotedCityOrCountyGroundsAnInferredState()
    {
        var brief = Grounding.GroundedSearch(new("Search", new(State: "TX", City: "Austin", Limit: 5)), "leads in Austin");
        Assert.Equal(("TX", "Austin"), (brief!.State, brief.City));
        Assert.Null(Grounding.GroundedSearch(new("Search", new(State: "TX", City: "Austin", Limit: 5)), "leads in Dallas"));
        Assert.Null(Grounding.GroundedSearch(new("Search", new(State: "TX", County: "Travis", Limit: 5)), "leads in Texas"));
    }

    [Fact]
    public void UnknownStateCodesAreRefused()
    {
        Assert.Null(Grounding.GroundedSearch(new("Search", new(State: "XX", City: "Austin", Limit: 5)), "leads in Austin XX"));
        Assert.Null(Grounding.GroundedSearch(new("Search", new(State: "Texas", Limit: 5)), "leads in Texas"));
    }

    [Fact]
    public void LimitIsClampedToTheChatMaximumNeverRefused()
    {
        Assert.Equal(ResearchBrief.MaximumChatResults, Grounding.GroundedSearch(new("Search", new(State: "NY", Limit: 50)), "New York leads")!.Limit);
        Assert.Equal(3, Grounding.GroundedSearch(new("Search", new(State: "NY", Limit: 3)), "3 New York leads")!.Limit);
        Assert.Null(Grounding.GroundedSearch(new("Search", new(State: "NY", Limit: 0)), "New York leads"));
    }

    [Theory]
    [InlineData("{\"action\":\"Search\",\"search\":{\"state\":\"NY\",\"limit\":5},\"id\":null}")]
    [InlineData("```json\n{\"action\":\"Search\",\"search\":{\"state\":\"NY\",\"limit\":5},\"id\":null}\n```")]
    [InlineData("<think>The user wants New York. {not json} </think>Here you go: {\"action\":\"Search\",\"search\":{\"state\":\"NY\",\"limit\":5},\"id\":null}")]
    [InlineData("{\"action\":\"Search\",\"search\":{\"state\":\"NY\",\"city\":\"Ne{w} \\\"York\\\"\",\"limit\":5},\"id\":null} trailing")]
    [InlineData("Thinking: the template is {\"action\":\"Search|Lookup|Clarify\",\"search\":null,\"id\":null}. Draft: { state: \"NY\" }. Final:\n{\"action\":\"Search\",\"search\":{\"state\":\"NY\",\"limit\":5},\"id\":null}")]
    [InlineData("a stray { brace that never closes ... then {\"action\":\"Search\",\"search\":{\"state\":\"NY\",\"limit\":5},\"id\":null}")]
    [InlineData("{\"action\":\"Search\",\"search\":{\"state\":\"NY\",\"limit\":5},\"id\":null}\n\nNote: {\"example\":true}")]
    public void ModelAnswerIsParsedFromFencesThinkBlocksAndProse(string text)
    {
        var intent = ModelResearchPlanner.Parse(text);
        Assert.Equal("Search", intent.Action);
        Assert.Equal("NY", intent.Search!.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("CLARIFY")]
    [InlineData("{\"action\":\"Search\"")]
    public void UnparseableModelAnswersAreJsonErrors(string text) =>
        Assert.ThrowsAny<JsonException>(() => ModelResearchPlanner.Parse(text));

    [Fact]
    public void OversizedJsonIsRejectedEvenWhenBalanced()
    {
        var huge = "{\"action\":\"Search\",\"search\":{\"state\":\"NY\",\"status\":\"" + new string('a', ModelResearchPlanner.MaximumModelJsonLength) + "\"}}";
        Assert.ThrowsAny<JsonException>(() => ModelResearchPlanner.Parse(huge));
    }
}
