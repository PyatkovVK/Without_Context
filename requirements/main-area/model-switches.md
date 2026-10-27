# Header model switches

Status: Implemented.

## Behavior

- Do not show a separate settings gear in the top-right corner of the conversation header. The model-name button still opens its editor in the right sidebar; general settings remain in the left sidebar.
- The Cleaner and Assistant indicators in the chat header are real buttons. Keep their dot, role label and current status, with localized action tooltips and accessible names. Support normal keyboard activation and the existing static hover styling.
- Clicking an active Cleaner disables AI cleaning for the current chat. Built-in local anonymization rules remain active. Cancel the current owned operation, advance cleaner/context revisions, discard the prepared message preview and revoke context approval. Preserve completed chat pairs, the draft, the current anonymized context and model settings. This pause does not start a new redaction session or silently replace its snapshot.
- Clicking an active Assistant disables request generation for the current chat, cancels its owned request and optional title request, and invalidates connection verification. Preserve model/provider/credential configuration, completed chat pairs, context and the unsent draft. Completed conversations remain readable; the composer is disabled until reconnection.
- Clicking an inactive configured role checks its connection again before enabling it. If it has no configuration, open its existing editor. Re-enabling the cleaner requires context review again and does not re-enable a manually disabled assistant. Reconnecting the assistant must not re-enable a manually disabled cleaner.
- Disabling remains available while that role is working or reconnecting. No second operation starts while the current operation is still cancelling. Ignore late metadata, streaming replies, preparation and title results; an unfinished reply must not become saved history.
- Toggles affect only the current chat's application-owned use of the model. Never kill the Ollama process/service, run an Ollama stop command, send an unload request, dispose another owner's client, or change system/cloud/provider configuration. A model shared with another app or chat keeps running. If one local model serves both roles, disabling one role leaves the other role's use independent.
- Pause state stays in per-chat RAM. Switching chats restores that chat's own state; no durable preference or cache is added. Explicit model/endpoint/protection configuration edits retain their existing invalidation behavior.
- Keep the approved animation baseline intact.

## Acceptance criteria

1. Use actual header clicks to switch each role off and on. Verify the off labels, action tooltips, fresh connection checks and unchanged settings/transcript/draft.
2. Verify cleaner-off previews use built-in rules with no local AI request, and require review of retained context after changing cleaner mode.
3. Disable during a delayed reconnect and a delayed assistant reply, then complete the delayed result. The role must stay disabled and the completed transcript must stay intact.
4. Re-enable one role while the other remains disabled; repeat with an external assistant. Verify independent chat state and zero OS/terminal/model-stop side effects.
5. Verify English/Russian labels, readable paused conversations, compact layout and unchanged motion and hover styling.
