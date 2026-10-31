namespace CSweet.Agent.RealtyLeadsAI.Tests;

public sealed class ReplyRenderingTests
{
    private static InheritanceLead Lead(string id, string address, string city = "Sacramento", string county = "Sacramento", string state = "CA",
        double? score = 26, string? died = null, int? heirs = null, int? reachable = null, string? owner = "R. Alvarez", string status = "New") =>
        new(id, "p1", address, owner, status, score, "d1", "M. Alvarez", city, county, state, died, heirs, reachable);

    private static ResearchResult Result(ResearchBrief brief, params InheritanceLead[] leads) => new(brief, leads, 0);
    private static readonly string Id1 = "019e7177-ad03-7f0b-9703-d0dce07c5739";
    private static readonly string Marker = "[lead-brief:8088e0c2-a722-410f-814e-10ec325f65ae]";

    [Theory]
    [InlineData("2180.0 MANGRUM AVE, SACRAMENTO, CA, 95822-3962", "2180 Mangrum Ave · 95822")]
    [InlineData("1336.0 63RD, SACRAMENTO, CA, 95819-4312", "1336 63rd · 95819")]
    [InlineData("5431 State Route 55, Liberty, NY", "5431 State Route 55")]
    [InlineData("STONY PT LN, HENDERSON, NY", "Stony Pt Ln")]
    [InlineData("12 N MAIN ST SW, AUSTIN, TX, 78701", "12 N Main St SW · 78701")]
    [InlineData("  ", "Not supplied")]
    public void AddressesAreShortenedForReading(string raw, string expected) => Assert.Equal(expected, LeadBriefRenderer.ShortAddress(raw));

    [Fact]
    public void AddressesStayEscapedAndProviderTextNeverBecomesALink()
    {
        var shown = LeadBriefRenderer.ShortAddress("[click](https://evil.example) |<b>, SACRAMENTO, CA");
        Assert.DoesNotContain("[click]", shown); Assert.DoesNotContain("<b>", shown); Assert.Contains("&#124;", shown);
        Assert.Null(LeadBriefRenderer.LeadLink("not-a-uuid")); Assert.Null(LeadBriefRenderer.LeadLink("https://evil.example"));
        Assert.Equal($"[Open ↗](https://realtyleads.ai/leads/{Id1} \"Lead {Id1}\")", LeadBriefRenderer.LeadLink(Id1));
    }

    [Theory]
    [InlineData("2026-08-14", "Aug 14, 2026")]
    [InlineData("2026-08-14T00:00:00Z", "Aug 14, 2026")]
    [InlineData("yesterday", null)]
    [InlineData(null, null)]
    public void DeathDatesAreHumanOrOmitted(string? raw, string? expected) => Assert.Equal(expected, LeadBriefRenderer.DeathDate(raw));

    [Theory]
    [InlineData(3, 2, "3 · 2 reachable")]
    [InlineData(1, 0, "1 · none reachable")]
    [InlineData(2, 5, "2 · 2 reachable")]
    [InlineData(0, 0, "—")]
    [InlineData(null, null, "—")]
    public void HeirCountsRead(int? total, int? contactable, string expected) => Assert.Equal(expected, LeadBriefRenderer.Heirs(total, contactable));

    [Fact]
    public void OnePlaceReplyFoldsSharedFactsIntoTheHeaderAndLinksEachLead()
    {
        var brief = new ResearchBrief("CA", null, "Sacramento", null, null, 5, null);
        var reply = LeadBriefRenderer.ChatReply(Result(brief,
            Lead(Id1, "2180.0 MANGRUM AVE, SACRAMENTO, CA, 95822-3962", score: 26, died: "2026-08-14", heirs: 3, reachable: 2),
            Lead("019fc9f1-5e91-7e7d-b765-08c438dba824", "3064 ALTOS AVE, SACRAMENTO, CA, 95815-1114", score: 18)), Marker);
        var lines = reply.Split('\n');
        Assert.Equal($"{Marker}: ../documents?artifact=8088e0c2-a722-410f-814e-10ec325f65ae", lines[0]);
        Assert.Equal("**2 inheritance leads · Sacramento, CA** — all status New · scores 18–26 · [Full brief][lead-brief:8088e0c2-a722-410f-814e-10ec325f65ae]", lines[2]);
        Assert.Equal("| Address | Owner | Deceased | Heirs | Score | |", lines[4]);
        Assert.Contains("| 2180 Mangrum Ave · 95822 | R. Alvarez | M. Alvarez · d. Aug 14, 2026 | 3 · 2 reachable | **26** | [Open ↗](https://realtyleads.ai/leads/" + Id1, reply);
        Assert.Contains("| 3064 Altos Ave · 95815 | R. Alvarez | M. Alvarez | — | **18** |", reply);
        Assert.DoesNotContain("| City", reply); Assert.DoesNotContain("Lead ID", reply);
        Assert.EndsWith(LeadBriefRenderer.Footer, reply);
        Assert.Equal(Guid.Parse("8088e0c2-a722-410f-814e-10ec325f65ae"), PublishedBrief.ParseMarker(reply));
    }

    [Fact]
    public void StateWideReplyShowsPlaceColumnsAndCountyCount()
    {
        var brief = new ResearchBrief("NY", null, null, null, null, 5, null);
        var reply = LeadBriefRenderer.ChatReply(Result(brief,
            Lead(Id1, "STONY PT LN, HENDERSON, NY", "Henderson", "Jefferson", "NY", 32),
            Lead("019fd6c7-09c2-7d75-89f1-196c982c1cac", "519 NORTHRUP DR, UTICA, NY, 13502", "Utica", "Oneida", "NY", 26)), null);
        Assert.StartsWith("**2 inheritance leads · New York** — 2 counties · all status New · scores 26–32", reply);
        Assert.Contains("| Address | City · County | Owner | Deceased | Heirs | Score | |", reply);
        Assert.Contains("| Stony Pt Ln | Henderson · Jefferson |", reply);
        Assert.DoesNotContain("[Full brief]", reply); Assert.DoesNotContain("[lead-brief:", reply);
    }

    [Fact]
    public void MoreThanTheChatMaximumShowsTopScoresAndPointsAtTheBrief()
    {
        var brief = new ResearchBrief("TX", "Harris", null, null, null, 50, null);
        var leads = Enumerable.Range(1, 27).Select(i => Lead($"019fd0aa-1111-7000-8000-{i:D12}", $"{i} Heights Blvd, HOUSTON, TX", "Houston", "Harris", "TX", score: i)).ToArray();
        var reply = LeadBriefRenderer.ChatReply(Result(brief, leads), Marker);
        Assert.Contains("**27 inheritance leads · Harris County, TX** — all status New · scores 1–27 · showing the top 10 by score · [Full brief (all 27)]", reply);
        Assert.Equal(10, reply.Split('\n').Count(l => l.StartsWith("| ", StringComparison.Ordinal) && l.Contains("Open ↗")));
        Assert.Contains("| 27 Heights Blvd |", reply); Assert.DoesNotContain("| 17 Heights Blvd |", reply);
    }

    [Fact]
    public void EmptyAndFilteredResultsReadPlainly()
    {
        var brief = new ResearchBrief("TX", "Travis", null, "Contacted", 90, 5, null);
        var reply = LeadBriefRenderer.ChatReply(Result(brief), null);
        Assert.StartsWith("**No inheritance leads · Travis County, TX · score ≥ 90 · status Contacted**", reply);
        Assert.Contains("I did not broaden the search", reply); Assert.EndsWith(LeadBriefRenderer.Footer, reply);
    }

    [Fact]
    public void BriefDocumentKeepsRawDetailAndTheDataBlock()
    {
        var brief = new ResearchBrief("CA", null, "Sacramento", null, null, 5, "Q4");
        var lead = Lead(Id1, "2180.0 MANGRUM AVE, SACRAMENTO, CA, 95822-3962", died: "2026-08-14", heirs: 3, reachable: 2);
        var doc = LeadBriefRenderer.Brief(Result(brief, lead), new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
        Assert.Contains("# Lead brief: Sacramento, CA", doc); Assert.Contains("| 1 | 2180 Mangrum Ave · 95822 | Sacramento | Sacramento | CA |", doc);
        Assert.Contains("### 1. 2180.0 MANGRUM AVE, SACRAMENTO, CA, 95822-3962", doc);
        Assert.Contains("- Date of death: Aug 14, 2026", doc); Assert.Contains("- Heirs: 3; contactable: 2", doc);
        var data = LeadBriefRenderer.ParseData(doc);
        Assert.Equal(("2026-08-14", 3, 2), (data![0].DateOfDeath, data[0].TotalHeirs, data[0].ContactableHeirs));
    }

    [Fact]
    public void LookupRecordLinksAndStaysEscaped()
    {
        var text = LeadBriefRenderer.Record(Lead(Id1, "2180.0 MANGRUM AVE, SACRAMENTO, CA, 95822", owner: "<b>x</b>"));
        Assert.StartsWith("**2180 Mangrum Ave · 95822** · Sacramento, Sacramento County, CA · [Open in RealtyLeads ↗](https://realtyleads.ai/leads/" + Id1, text);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", text); Assert.DoesNotContain("<b>", text);
    }
}
