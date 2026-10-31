using CSweet.Agent.SDK;
using CSweet.Agent.RealtyLeadsAI;
using Microsoft.Extensions.Hosting;

if (args.Contains("--self-test", StringComparer.Ordinal))
{
    var runtime = new AgentTestRuntime().RegisterCapability<LeadSearch, InheritanceLead[]>(
        RealtyLeadsAgent.ProviderSearch, (_, _) => Task.FromResult(Array.Empty<InheritanceLead>()));
    var result = await runtime.ExecuteCapabilityAsync(new RealtyLeadsAgent(), RealtyLeadsAgent.Search, new LeadSearch(Limit: 1));
    var brief = ResearchBrief.TryParse("research-brief.v1 state=TX limit=1", out var parsed, out _) && parsed is { State: "TX", Limit: 1 };
    var ok = result.Succeeded && brief;
    Console.WriteLine(ok ? "Synthetic Lead Research Analyst self-test passed." : "Synthetic self-test failed.");
    Environment.ExitCode = ok ? 0 : 1;
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.AddCSweetAgent<RealtyLeadsAgent>();
await builder.Build().RunAsync();
