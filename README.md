# Cofoundry Local

> **Early-stage and immature — help improving this project is welcome.** Please contribute code reviews, fixes, usability feedback, and improvement suggestions. **Live API validation is partial; full acceptance has not passed.**

A local multi-model AI project workspace with configurable team hierarchies, collaborative planning, human approval, and parallel text delivery. Includes a native Windows desktop application and a local web interface.

> **Experimental — partial live API validation.** On October 18, 2026, a synthetic DeepSeek Flash test exercised connection, collaborative planning, plan editing/approval, two task deliveries, review, and a direct member reply. The final team conversation was incomplete; a bounded retry failed at the network layer. Full end-to-end acceptance and other providers remain unverified. See [the test record](desktop/LIVE-API-VALIDATION.md).

[中文使用说明](README.zh-CN.md)

## Desktop editions and project controls

`共创团队.exe` starts the Chinese edition; `Cofoundry.exe` starts the English edition. Both use the same local data and cannot run simultaneously. Existing project content is preserved in its original language. Projects created in the English edition use English default roles, demo outputs, and live-model response instructions. The retained web interface is currently Chinese.

The Team page now defaults to an interactive organization chart. Click a member to edit their role, level, manager, and model connection, or add a direct report. Table editing remains available. Team changes still invalidate prior plans and results.

Functional departments are optional, editable member attributes (for example, Engineering, Design, Research, or Quality). The organization chart uses department columns and role-level rows; solid lines retain the configured reporting relationships. Project details groups recipient cards by department and shows each member's manager and model. Existing teams are not reassigned automatically: blank departments appear as Department not assigned. Three levels can represent department leadership, specialist leads, and contributors; use a fourth only when needed. This is a visual organization aid, not an additional authorization layer: human approval and direct member instructions remain available. The grouping follows the specialization principle described in [PMI's organizational alternatives](https://www.pmi.org/learning/library/organizational-alternatives-project-management-techniques-strategies-5690).

Details & instructions shows the current plan, task progress, deliverables, activity, and a persistent instruction thread. Address the whole team or one member. Instructions received during execution are included in subsequent model calls; replies are queued until the current run finishes. Sending an instruction does not modify or approve the plan. Scope changes must be incorporated into a revised plan and approved. Replies consume the normal project API call budget. Each project supports up to 100 instructions of up to 8,000 characters each.

The desktop composer distinguishes **Question**, **Instruction**, and **Plan change**. Questions stay in the conversation; instructions are included in subsequent relevant task requests; plan-change messages request advice only and must be manually incorporated and approved. Processing details record how many subsequent task requests included an instruction, not whether a model fulfilled it. In-flight requests and completed results are unchanged. Failed instructions are excluded from new task contexts. Legacy messages retain instruction behavior.

The conversation uses distinct sender colors, timestamps, and queued/replying/completed/error states. Send with Ctrl+Enter and continue typing while replies update. Each member receives their relevant conversation history (the latest 20 instructions and their own replies). Responses appear when a call finishes; token streaming is not supported. Pause stops current work and cancels queued instructions; send a new message to resume the conversation.

Opening project details switches to a dedicated three-column workspace. The left column contains the current goal, live plan progress, and clickable team cards. Select Whole project for team instructions or a member card for a direct conversation with that member's configured model. The center contains message bubbles and a fixed composer; switching recipients preserves separate unsent drafts. The right column shows available planning outputs, task results, reviews, and instruction replies, with an independent member filter and a full-text reader. Its filter never changes the message recipient. Use Back to workspace to return to project settings and approval.

Both editions support Ctrl+S in editable brief, plan, and team pages, preserve unsaved edits when actions are cancelled, retain expanded discussion sections during updates, and offer a separate reader for long text.

The desktop uses soft backgrounds and rounded controls with opaque corner painting. Action rows wrap within their cards, and team bulk editing keeps readable column widths with horizontal scrolling and a frozen member-name column. In smaller detail windows, output history collapses automatically and can be reopened with Output history. Recipient and status labels remain visible alongside the color cues.

## Status

This repository preserves a development baseline. Backend tests and native UI construction/binding self-tests have passed; the desktop window has been observed loading existing projects. Full desktop interaction, lifecycle, and live provider acceptance are still in progress. See [acceptance tracking](desktop/ACCEPTANCE.md).

Kaspersky previously detected and deleted the locally compiled executable. The exact detection cause remains unresolved. A successful build or self-test does not establish antivirus clearance. See the [event record](desktop/STATUS.md).

## Workflow

1. Define the project goal, constraints, and acceptance criteria.
2. Configure API connections and assign models to team members.
3. Set roles, reporting relationships, and hierarchy depth (1–12 levels; 3 or 4 recommended).
4. Let members propose independently, review each other's ideas, and produce a combined plan.
5. Edit the plan and explicitly approve its current revision.
6. Execute tasks concurrently as their dependencies become ready.
7. Review the results and give final human acceptance.

Demo mode runs offline with clearly labeled example outputs. Live mode calls the configured providers. Editing a plan invalidates its approval and clears prior results; export anything you need to retain first.

## Run

### Windows desktop

Requirements for a source build: Windows 10/11 x64, .NET Framework 4.8, and Node.js 22+ on PATH.

```powershell
powershell -File desktop/build.ps1
```

If local PowerShell policy does not allow scripts, run `desktop\build.cmd` instead. This does not require changing the machine's execution policy.

Launch `共创团队.exe` and keep its adjacent configuration, `server.mjs`, `lib/`, `public/`, and `runtime/node.exe` together. The desktop connects to or starts the local server and shares data with the web interface. Compiled executables and the copied runtime are excluded from Git.

### Local web interface

```sh
node server.mjs
```

Open `http://127.0.0.1:4317`. The service binds to loopback and should not be exposed to the public internet. No third-party runtime packages are required.

## Provider support

- Chat Completions compatible HTTP JSON APIs.
- Anthropic Messages APIs.
- Custom JSON request/header templates and a dot-separated response text path.

Custom templates support `{{model}}`, `{{messages}}`, `{{system}}`, `{{prompt}}`, `{{apiKey}}`, and `{{maxTokens}}`. Use the key placeholder instead of embedding credentials in templates.

This version supports synchronous text responses. Streaming, OAuth, complex request signatures, asynchronous polling, media, and tool execution need additional adapters. It produces text deliverables; it does not browse, execute generated code, edit external files, or publish artifacts autonomously.

## Local data and limits

Projects are stored in `data/workspace.json`. API keys are encrypted with AES-256-GCM using `data/local.key`; access to both files permits decryption. This is not an operating-system credential vault. Live runs send relevant project content to the API endpoints you configure.

The default is 3 concurrent tasks, 80 cumulative API calls per project, and a 120-second request timeout. Failed attempts count toward the limit. There is no currency budget accounting. Cancellation cannot undo provider-side processing or charges.

The highest-ranked first member synthesizes the plan; the final team member supplies a review. Reporting relationships do not introduce additional approval gates, and the reviewer may also execute tasks. Human acceptance remains final.

## Validation

```sh
node --test tests/workflow.test.mjs
```

Tests cover approval gating and invalidation, discussion-to-delivery flow, task dependencies, pause/resume, credential handling, provider adapters, and request origin checks. They use demo and mocked local APIs, not paid provider calls.

After a Windows build, run `共创团队.exe --self-test` and inspect `desktop-self-test.txt`. This verifies native page construction, grid bindings, and JSON round-tripping; it is not a substitute for interactive acceptance.

## History and recovery

Use `git log --oneline` to inspect saved versions. To inspect a previous revision without replacing current work:

```sh
git worktree add --detach ../cofoundry-recovery <commit>
```

Rebuild from that checkout as needed. Git excludes project data, keys, logs, runtimes, and executables. Source history does not back up project data. Pause tasks and stop the service before privately copying the entire `data/` directory; stop the service again before restoring it. Never upload that directory to the repository.

## Layout

| Path | Purpose |
| --- | --- |
| `desktop/` | Native Windows client and build script |
| `server.mjs` | Local service and project orchestration |
| `lib/providers.mjs` | API protocol adapters |
| `lib/domain.mjs` | Team and plan validation |
| `lib/store.mjs` | Persistence and key encryption |
| `public/` | Local web interface |
| `tests/` | Backend and adapter acceptance tests |
