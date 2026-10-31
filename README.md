# RealtyLeadsAI Lead Research Analyst

Version 0.2.1 on SDK 5.8.0. An individual-contributor agent that turns a bounded
research brief into a **lead brief** using the two RealtyLeadsAI read capabilities a
human has explicitly granted. It has no provider key, HTTP client, MCP transport,
connection selector, outreach or write authority.

## How work reaches it

| Channel | What happens | Model involved? |
|---|---|---|
| Manager delegation carrying a `research-brief.v1` line | Accepted or clarified deterministically; on execution runs **one** search, publishes a lead brief artifact, and closes the delegation when its own brief marker appears in the recorded evidence | No |
| Human chat with a `research-brief.v1` line | Same bounded run; brief saved when `platform.artifact.create.v1` is granted | No |
| Human chat in plain language | The approved model returns a constrained intent; every filter must be quoted from the request or the analyst asks for scope | Intent only; provider data never enters a prompt |
| Typed capabilities `realtyleads.research.search.v1` / `lookup.v1` | Validated wrappers over the granted provider reads for other agents with their own grants | No |

### The research brief grammar

```
research-brief.v1 state=TX county="Travis" minScore=70 limit=10 purpose="Q4 acquisitions"
```

`state` (two letters) is required. Optional: `county`, `city`, `status`, `minScore` (0–100),
`limit` (1–100, default 10), `purpose`. Quoted values cannot contain quotes, newlines or control
characters; unknown keys, repeated keys and trailing prose are rejected with the exact defect.
Nothing in a brief is ever sent to a model.

### The lead brief

`realtyleads.lead-brief.v1` artifacts record the criteria actually used, the generation time,
duplicate counts, and all eleven reviewed fields per lead (lead ID, property ID, address, owner
name, status, score, deceased ID, deceased name, city, county, state). Every provider value is
escaped literal data; missing values read "Not supplied". Chat replies show at most ten rows and
point to the brief for the rest.

## Guarantees carried over from 0.1.0

- No provider read without explicit geography (a two-letter state).
- Empty results are reported honestly; the search is never broadened or retried.
- Credential-shaped input is refused before any model or provider use, and never echoed.
- Malformed model output produces a safe clarification, never a guessed query.
- Access or model failures produce fixed messages; nothing is fabricated.

## Code layout

```
src/CSweet.Agent.RealtyLeadsAI/
  RealtyLeadsAgent.cs            routing, delegation protocol, typed capabilities
  Research/ResearchBrief.cs      brief grammar, typed provider records
  Research/ResearchIntentPlanner.cs  bounded model interpretation + grounding
  Research/LeadResearchService.cs    the only provider-calling code
  Research/LeadBriefRenderer.cs      escaped chat table, full brief, lookup record
  Research/LeadBriefPublisher.cs     idempotent artifact creation + delivery marker
```

## Grants

The manifest requests the two provider reads (dependency `realtyleads`, plugin ≥ 0.2.0),
`platform.llm.chat-stream.v1`, `platform.artifact.create.v1`, and the delegation/tracking set
the shared runtime needs to route a manager's assignment (`work.delegation.create/read/respond/
publish.v1`, `work.tracking.read/pending/processed.v1`). `work.delegation.create.v1` is
required by the runtime's dispatch; the analyst overrides the classifier so it never delegates.
Declarations grant nothing: an approved model and an explicit native read grant are required.

Run `dotnet test` and `dotnet run --project src/CSweet.Agent.RealtyLeadsAI -- --self-test`.
