# RealtyLeadsAI Researcher

Initial 0.1.0 release candidate using SDK 5.6.0. Typed inheritance-lead search and
lookup call only explicitly granted RealtyLeadsAI business capabilities. The agent
has no provider key, HTTP client, MCP transport or connection selector.

Human message events use the approved platform model to interpret one bounded
request. Missing geography or intent receives clarification. Queries are limited
to ten results in conversation, and filters must appear in the current request.
Ambiguous follow-ups ask for the scope again. Returned records are escaped and
rendered directly; provider text never enters another model prompt. Empty results,
denied access and malformed model output have fixed, safe responses. The agent
cannot perform outreach or modify provider records.

The manifest requests `platform.llm.chat-stream.v1` and subscribes to
`com.csweet.user.message.received.v1`. These declarations do not grant authority.
An approved model and explicit provider read grant are required at installation.
Use native account settings for key entry, never chat or agent configuration.

Run `dotnet test` and `dotnet run --project src/CSweet.Agent.RealtyLeadsAI -- --self-test`.
While SDK 5.6.0 publication is pending, tests need a local package feed and a fresh
package cache. This candidate's consumer protocol declaration is still under
compatibility review; do not import or release until manifest validation passes.
Full installed-host conversation acceptance is also outstanding.
