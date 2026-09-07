# AGENTS.md

This is the repository-wide instruction entry point for coding agents working on SecondDimensionWatcherReDive.

## Important Rules

- **NEVER use `npm` or `npx`**. This project uses Yarn Berry (PnP). Always use `yarn` for all frontend commands.
- **NEVER add regression checks in any form**. Do not add regression tests, test-only scripts, workflow contract assertions, or equivalent guards.
- **NEVER reference `ApplicationContext` directly** outside of `Repositories/` implementations, `Program.cs` (DI + migrations), and EF Core migration files. All data access goes through repository interfaces defined in `Framework/DataRepository/`.
- **Async method conventions in interfaces**: All interface methods returning `Task` or `Task<T>` must (1) have names ending with `Async`, (2) accept a `CancellationToken cancellationToken` parameter, and (3) must NOT have default values on `CancellationToken` in interface definitions. The parameter must be named `cancellationToken` (not `ct`).

## Agent Workflow

These instructions apply the [OpenAI prompting best practices](https://developers.openai.com/api/docs/guides/latest-model#prompting-best-practices), reviewed on 2026-09-07, to repository work.

### Initiative and Follow-through

- Treat requests to build, fix, investigate, or create a PR as instructions to do the work. Infer routine details from the conversation and repository, make reasonable assumptions, and continue until the requested outcome is complete or a concrete blocker remains.
- Carry existing authorization forward. Complete the preparation and verification needed to present a reviewable result before seeking any additional approval that is actually required. Ask a focused question only when missing information materially affects the outcome; continue independent work while waiting.
- Keep changes within the requested scope and preserve unrelated user work. Incorporate corrections and answer side questions without losing the original objective. Do not introduce approval steps or warnings for hypothetical risks.

### Instruction Following

- Follow system and developer instructions, then the user's request. Explicit user instructions take precedence over repository and skill guidance; retain the Important Rules above unless the user explicitly changes them.
- Read applicable instruction files and skills before acting. Resolve conflicting or stale guidance against the instruction hierarchy and current source code instead of silently following it.
- If a repository or skill instruction requires a pause, additional permission, or a deviation from the requested outcome, identify and link the exact file, quote the relevant rule, and explain how it applies. Distinguish an explicit requirement from your interpretation.

### Communication

- Use the user's language and lead with the result or intended action. Write concise paragraphs in plain language; use lists or tables when they make steps or comparisons easier to understand.
- Provide brief progress updates during longer work, focused on findings and next steps. Avoid stock phrases, unnecessary jargon, repeated summaries, and invented labels.
- In the final response, explain the change, relevant verification results, and any unresolved limitation. Link to changed files or the PR as appropriate. Report only work and checks actually completed.

### Subagent Delegation

- When collaboration tools are available, delegate bounded, independent tasks if parallel work can save time or improve quality. Continue useful local work while agents handle their assignments.
- Give each agent a clear scope and file ownership to avoid conflicting edits. Review and integrate their results; the coordinating agent remains responsible for the complete outcome. Keep inter-agent messages readable, with proper spacing.

### Testing and Verification

- The prohibition on adding regression checks above also applies when following external prompt guides or skills. Use existing tests, builds, static checks, and direct inspection as appropriate to the change.
- For documentation-only changes, review the diff, references, and file integrity; do not run unrelated application builds or test suites. For code changes, select relevant existing checks and complete required verification.
- Once appropriate checks pass, repeat or broaden them only for a new change, a failure, or an unresolved concern. State skipped or unavailable verification accurately and proceed to the requested deliverable.

## Build & Development Commands

```bash
# Backend
dotnet build SecondDimensionWatcherReDive.slnx                                # Build entire solution
dotnet run --project SecondDimensionWatcherReDive                             # Run backend (http://localhost:5097)
dotnet test SecondDimensionWatcherReDive.slnx                                 # Run all tests

# Frontend (in SecondDimensionWatcherReDive.Client/)
yarn install        # Install dependencies (Yarn Berry with PnP; use the version pinned in package.json)
yarn start          # Dev server on http://localhost:1234
yarn build          # Production build to dist/
yarn mock           # Mock API server on http://localhost:5097
yarn dev            # Mock server + dev server together

# Podman / Container
cd deployments && podman-compose up -d             # Full stack: PostgreSQL + qBittorrent + app
podman build -f Containerfile -t sdw-redive .      # Build container image locally
```

## Repository Reference

Before changing a subsystem, read the relevant sections of [Architecture and Configuration](docs/architecture.md). It contains the project map, data and download flows, repository contracts, plugins, background services, migrations, controllers, authentication, frontend, localization, mock server, and configuration reference. Read the full reference when the task spans subsystems or its boundaries are unclear.

Treat source code and package manifests as authoritative when descriptive documentation is stale, and update affected guidance with the implementation. Keep agent behavior rules in this file and detailed technical context in the linked reference so the instruction entry point stays within Codex's [default discovery limit](https://learn.chatgpt.com/docs/agent-configuration/agents-md#how-codex-discovers-guidance).
