# Desktop UI refresh

## Recovery point

The original implementation was preserved remotely before UI changes. Tag: `baseline-before-ui-20261017`, commit: `b485208`.

## Design references

- [Open WebUI](https://github.com/open-webui/open-webui): a dedicated workspace and persistent navigation for model interactions.
- [LibreChat](https://github.com/LibreChat-AI/LibreChat): separating conversations from model configuration.
- [shadcn/ui dashboard](https://ui.shadcn.com/examples/dashboard): restrained neutral surfaces, visible selection, and a small number of emphasized actions.

These are design references, not imported components or copied source. The Windows client remains a native WinForms application.

## Changes

- A light sidebar with a visible current section and a larger project selector.
- An overview that explains the next action based on project state.
- A readable project brief with an explicit editing mode.
- Collapsible discussion messages, activity history, and advanced execution limits.
- Custom JSON configuration displayed only for the custom protocol.
- Explicit display scaling for native pixel dimensions and dialog layout.
- Neutral cards, improved spacing, and more readable task/team tables.

The application UI remains Chinese. The repository overview and commit messages use English.

## Usability follow-up

- Cancelling an action preserves unsaved controls instead of rebuilding the page.
- The unsaved indicator updates immediately; Ctrl+S uses the same save action as the visible button.
- Background updates preserve the page scroll position and disclosure state, and only rebuild when project data changes.
- Discussions and deliverables offer a separate resizable reading window and a copy action.
- Execution includes a progress bar; invalid team levels use a specific, readable error.
- Dialogs are centered and constrained after display scaling; sidebar overflow is reduced.

The native self-test includes a regression check for unsaved content preservation after a cancelled operation.
