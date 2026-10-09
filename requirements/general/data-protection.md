# Data protection

Status: Implemented within the supported rules.

## Requirements

- Source files remain unchanged.
- The selected profile, custom values and, when enabled, local cleaner run before sending.
- Only explicitly enabled and validated local Ollama can receive original text. Cleaner failure does not trigger a fallback to a cloud model.
- Remote requests use HTTPS, credentials are sent in headers, and redirects and the system proxy are disabled.
- Assistant and title-refinement requests contain only anonymized data.
- Generated titles are anonymized again before display, storage and export.
- Original values are restored only for displaying replies on this device.
- Different chat sessions keep original values, replacement maps, context and history isolated.
- Editing reviewed text revokes approval. Late results after clearing or deletion cannot restore state.
- Secrets and request contents are excluded from diagnostic messages.
- Local data, exports, screenshots, SDKs, dependencies and builds are excluded from the repository.

## Limitation

Automatic anonymization may miss a sensitive fragment. The profile determines which categories are checked; the user reviews the result before sending. Local storage is not encryption.

## Acceptance criteria

Use fictional keys, contacts and internal addresses only; inspect outbound request bodies, titles, JSON and Markdown.
