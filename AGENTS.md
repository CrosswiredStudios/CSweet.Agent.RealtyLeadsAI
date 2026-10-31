# RealtyLeadsAI Researcher repository instructions

This repository contains one standalone C-Sweet protocol-v2 agent. Its purpose is:

> Lead Research Analyst: turns a bounded research brief (from a manager's delegation or a human request) into a lead brief using granted RealtyLeadsAI read capabilities.

## Invariants

- Keep `com.crosswiredstudios.agent.realtyleads` and version `0.2.0` synchronized between agent code,
  `csweet-plugin.json`, tests, and releases.
- The root manifest is the reviewed authority request. Keep `provides`, `requires`, events,
  configuration, credentials, web access, and UI contributions synchronized with implementation
  and tests.
- Request the minimum authority needed. Manifest declarations never grant access.
- Use typed callbacks and `AgentRuntimeContext.Platform`. Do not implement MCP/JSON-RPC, access
  runtime/workload/lease tokens, connect directly to databases or Docker, or handle provider
  credentials.
- Agent work is delivered at least once. Honor cancellation and use stable domain idempotency keys
  for external mutations.
- Unknown capabilities and events must fail or be ignored safely without leaking sensitive data.
- `research-brief.v1` lines and provider results never enter a model prompt. The only model use is bounded intent
  interpretation of plain-language chat, and every interpreted filter must be quoted from the request.
- Delegation acceptance, work binding, outcome review and update summaries are deterministic overrides; do not
  reintroduce the SDK's model-backed defaults.

## Verification

Run from the repository root:

```powershell
dotnet test
dotnet run --project src/CSweet.Agent.RealtyLeadsAI -- --self-test
```

Any new capability, grant, event, configuration field, credential, or network rule requires a
manifest update, a README explanation, and tests.
