# Archive

Status: Implemented.

## Behavior

- The archive entry and its chat count are at the bottom of the left sidebar.
- Hide the archive entry entirely when its count is zero. Show it when the first chat is archived; hide it again after restoring or deleting the last archived chat, or clearing all chats.
- The archive shows only archived chats and provides a way to return to recent chats.
- Archiving hides a chat from recent chats while preserving its conversation, title and context.
- While the application remains open, the full live session is retained, including its draft and settings.
- After restarting, only permitted anonymized data is restored; credentials and original replacement mappings are not stored.
- Restoring returns the chat to recent chats. Deleting an archived chat removes its local file.
- Archiving the last recent chat leaves a new empty conversation.
- Clearing all chats also clears the archive.

## Acceptance criteria

Archive a conversation containing messages, restart the application, open the archive and restore the conversation. Its title and anonymized transcript are preserved. Verify that no empty archive entry is shown, including after restoring or deleting the last archived chat.
