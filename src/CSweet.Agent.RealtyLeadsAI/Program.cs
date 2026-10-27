using CSweet.Agent.SDK;
using CSweet.Agent.RealtyLeadsAI;
using Microsoft.Extensions.Hosting;

if (args.Contains("--self-test", StringComparer.Ordinal))
{
    var runtime = new AgentTestRuntime().RegisterCapability<LeadSearch, InheritanceLead[]>(
        RealtyLeadsAgent.ProviderSearch, (_, _) => Task.FromResult(Array.Empty<InheritanceLead>()));
    var result = await runtime.ExecuteCapabilityAsync(new RealtyLeadsAgent(), RealtyLeadsAgent.Search, new LeadSearch(Limit: 1));
    Console.WriteLine(result.Succeeded ? "Synthetic RealtyLeadsAI capability self-test passed." : "Synthetic capability self-test failed.");
    Environment.ExitCode = result.Succeeded ? 0 : 1;
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.AddCSweetAgent<RealtyLeadsAgent>();
await builder.Build().RunAsync();
