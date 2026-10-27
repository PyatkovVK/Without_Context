# Compact context trimming

Status: Implemented.

## Behavior

- Keep all four modes: Minimal, Moderate (default), Maximum and Custom. Selecting a mode changes the existing context-reduction setting and revokes prepared context and approval as before.
- Display preset choices as compact single-line radio rows with smaller indicators and spacing. Keep the explanation of each mode in a localized tooltip.
- Show the current character and history limits below the choices. Character limits do not promise exact token counts.
- Custom reveals two mutually exclusive choices: **Clean code** and **Clean text by description**. Keep description selected initially to preserve the existing behavior.
- Code cleanup uses the ordinary local protection rules to replace detected keys, tokens and credentials. Other sensitive values follow the selected protection profile and custom replacement list. It needs no instructions or downloaded model and does not request an editorial rewrite. Preserve the collected anonymized code within the full collection budget; file exclusions, character limits, local entity validation when enabled, and review remain mandatory. Source files are unchanged.
- Show the instruction editor and its character counter only for **Clean text by description**. Use a smaller editor (76–120 pixels tall) with the existing 1,000-character limit.
- Switching away from Custom or to code cleanup hides the editor and preserves its session-only text for a later return. Language changes preserve both choices and instructions. New chats copy this configuration without context, approval or sensitive session state; it is not persisted to disk.
- Switching cleanup choices cancels stale preparation and revokes prepared context and approval. Editing hidden description text while code cleanup is active does not invalidate the code result.
- Applying description instructions still requires an enabled downloaded local cleaner and nonempty instructions. Until those prerequisites are available, use Moderate trimming automatically and show a small localized explanation instead of a blocking setup error. Code cleanup remains available independently. Preserve the requested mode and session-only instructions; the displayed limits describe the effective mode. Core validation remains fail-closed if directly asked to execute an invalid description operation.

## Acceptance criteria

At the normal window size, preset trimming occupies less than 220 pixels and has no empty disabled editor. Verify all four controls change the actual setting; Custom reveals both cleanup choices, and only description reveals the editor. Check source-code credentials and sensitive values are replaced without rewriting ordinary code or making a summary request. Switching variants revokes approval and retains description text; new chats copy only configuration. Verify both languages and that the section remains reachable by scrolling at minimum window size.
