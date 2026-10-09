# Storage and export

Status: Implemented.

## Behavior

- Chats are stored in a separate application-data folder, WithoutContext/chats, outside the repository.
- Windows uses %LOCALAPPDATA%/WithoutContext/chats; settings display the actual path.
- JSON filenames use opaque identifiers rather than task titles or user folder names.
- Storage includes titles, message timestamps, archive membership, completed anonymized conversations and permitted anonymized context.
- Credentials, original values and paths, replacement maps, model settings, custom secret lists, drafts, incomplete replies and approval are not stored.
- Writes are serialized with atomic file replacement. A late write cannot recreate a deleted chat.
- After restarting, a conversation can continue once its model is configured and stored context is reviewed.
- Single-chat export is in the right sidebar; exporting all chats is in settings. The user selects a folder; JSON and Markdown files include the stored title.
- Exported copies are not removed when deleting a chat. Files contain plain text without encryption.
- Limits: 200 chats, 512 messages per chat, 8 MiB per file and 64 MiB in total.
- Version 2 adds the title; older version 1 files without it remain supported.
- Read, save and export errors use safe messages; failures are not reported as success.
- Normal closure waits for saving. On failure, the user chooses whether to keep the application open or close without saving.

## Acceptance criteria

Save a chat, restart, check its title and archive membership, export and delete it. Files must not contain the fictional original secrets; exported copies remain.
