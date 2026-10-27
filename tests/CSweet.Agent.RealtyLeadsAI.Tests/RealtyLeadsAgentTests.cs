using CSweet.Agent.SDK;
namespace CSweet.Agent.RealtyLeadsAI.Tests;
public sealed class RealtyLeadsAgentTests
{
    [Fact] public async Task SearchUsesOnlyTypedGrantedCapability()
    {
        var runtime = new AgentTestRuntime().RegisterCapability<LeadSearch, InheritanceLead[]>(RealtyLeadsAgent.ProviderSearch, (query, ct) =>
        { Assert.Equal("TX", query.State); Assert.Equal(5, query.Limit); return Task.FromResult(Array.Empty<InheritanceLead>()); });
        var result = await runtime.ExecuteCapabilityAsync(new RealtyLeadsAgent(), RealtyLeadsAgent.Search, new LeadSearch(State: "TX", Limit: 5));
        Assert.True(result.Succeeded); Assert.Equal(0, result.Value!.Value.GetArrayLength());
    }
    [Fact] public async Task MissingGrantGivesNativeSetupGuidance()
    {
        var result = await new AgentTestRuntime().ExecuteCapabilityAsync(new RealtyLeadsAgent(), RealtyLeadsAgent.Search, new LeadSearch());
        Assert.False(result.Succeeded); Assert.Contains("account settings", result.Error);
    }
    [Fact] public async Task RejectsCredentialsAndTransportArguments()
    {
        var result = await new AgentTestRuntime().ExecuteCapabilityAsync(new RealtyLeadsAgent(), RealtyLeadsAgent.Search, new { apiKey = "synthetic", url = "http://localhost" });
        Assert.False(result.Succeeded); Assert.DoesNotContain("synthetic", result.Error);
    }
    [Fact] public async Task LookupRejectsInvalidId()
    {
        var result = await new AgentTestRuntime().ExecuteCapabilityAsync(new RealtyLeadsAgent(), RealtyLeadsAgent.Lookup, new LeadLookup("invalid")); Assert.False(result.Succeeded);
    }
    [Fact] public async Task CancelledWorkDoesNotRun()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AgentTestRuntime().ExecuteCapabilityAsync(new RealtyLeadsAgent(), RealtyLeadsAgent.Search, new LeadSearch(), cts.Token));
    }
}
