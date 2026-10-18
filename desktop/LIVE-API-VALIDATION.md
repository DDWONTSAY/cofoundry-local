# Partial live API validation — October 18, 2026

Status: **partial validation, not full acceptance**.

## Scope and isolation

- Provider: DeepSeek, model `deepseek-flash`, official `/chat/completions` endpoint.
- Adapter: the application's existing custom JSON protocol, with `thinking.type` set to `disabled`, `stream: false`, and the ordinary `choices.0.message.content` response path.
- Synthetic project: a fictional library welcome notice, two team members and two text tasks. Both members used the same model; this does not validate collaboration across different providers.
- The temporary credential was injected into the test process only. It was never supplied to the local provider-save endpoint or persisted in project files. The temporary workspace was removed after each run. No existing user project was sent.
- Original cap: 12 requests, each at most 1,000 output tokens (connection check: 64). Separately authorized retry: at most two requests, each at most 128 output tokens. No further live calls were made.

## Observed results

The connection check, two proposal/review rounds, plan synthesis, refusal to execute before approval, manual plan editing, approval, two task outputs, final review, and a direct member reply passed the smoke-test assertions. The final whole-team command produced only one of two expected replies and failed its completion assertion. Its original underlying error was not retained, so no cause is claimed for that first failure.

The separate two-member retry failed on both requests with `fetch failed`, before an HTTP response was available. This does not establish a credential, account-balance, or provider-model failure; the transport cause remains unresolved. The human acceptance endpoint was not reached in the first run.

Eleven successful responses reported a total of 10,717 tokens. This is observed response usage, not a billing reconciliation; failed requests did not return usage.

## Follow-up changes and limits

- Distinguish questions, supplemental instructions, and proposed plan changes in the desktop composer.
- Keep questions and plan-change suggestions out of later task requirements. Preserve manual plan approval.
- Record inclusion of instructions in subsequent task requests without claiming fulfillment.
- Exclude failed instructions from new task contexts; retain partial replies for inspection.
- Add actionable HTTP, transport, invalid-JSON, and truncated-output errors without echoing provider response bodies.

These follow-up behaviors have automated mock regression coverage. They have not passed a complete new live workflow. Live stability, other providers, cross-provider teams, pause/recovery under real network failure, and desktop interaction acceptance remain outstanding. No credential or raw private test output is included in this record.
