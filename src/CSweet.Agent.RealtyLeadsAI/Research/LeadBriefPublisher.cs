using CSweet.Agent.SDK;

namespace CSweet.Agent.RealtyLeadsAI;

/// <summary>A published brief. The marker is embedded in the chat reply so delegation review can recognise delivery deterministically.</summary>
public sealed record PublishedBrief(Guid ArtifactId, Guid? RevisionId)
{
    public const string MarkerPrefix = "[lead-brief:";
    public string Marker => $"{MarkerPrefix}{ArtifactId:D}]";
    public static Guid? ParseMarker(string? text)
    {
        if (text is null) return null;
        var start = text.IndexOf(MarkerPrefix, StringComparison.Ordinal);
        if (start < 0) return null;
        var end = text.IndexOf(']', start);
        return end > start && Guid.TryParseExact(text[(start + MarkerPrefix.Length)..end], "D", out var id) ? id : null;
    }
}

/// <summary>Persists a lead brief as a revisioned artifact with a stable idempotency key, so retries never duplicate it.</summary>
public sealed class LeadBriefPublisher
{
    public async Task<PublishedBrief?> PublishAsync(ResearchResult result, string idempotencyKey, Guid? conversationId, Guid? workItemId,
        AgentRuntimeContext context, CancellationToken ct)
    {
        var title = $"Lead brief: {LeadBriefRenderer.Scope(result.Brief)} ({result.Leads.Count} lead(s))";
        var content = LeadBriefRenderer.Brief(result, DateTimeOffset.UtcNow);
        try
        {
            var document = await context.Platform.Artifacts.CreateAsync(new(title, content, LeadBriefRenderer.DocumentType, idempotencyKey,
                OriginConversationId: conversationId, OriginWorkItemId: workItemId)
            { WorkstreamId = context.Identity?.AssignedProject?.WorkstreamId,
              TeamId = Guid.TryParse(context.Identity?.TeamContext?.TeamId, out var team) ? team : context.Identity?.AssignedProject?.TeamId }, ct);
            return new(document.Id, document.LatestRevisionId);
        }
        catch (PlatformCapabilityException e) when (e.Code is PlatformCapabilityErrorCode.Denied or PlatformCapabilityErrorCode.NotFound)
        {
            // Artifact storage is optional for a chat answer: the results are still returned, explicitly unsaved.
            return null;
        }
    }
}
