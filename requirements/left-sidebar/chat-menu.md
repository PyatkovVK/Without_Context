# Chat menu

Status: Implemented.

## Purpose

Manage a specific chat directly from the list.

## Behavior

- Every row has a three-dot button with a localized "Chat actions" tooltip.
- In recent chats, the menu contains "Archive chat" and "Delete this chat".
- In the archive, "Restore chat" replaces the archive action.
- Actions apply to the row whose menu is open, regardless of the active conversation.
- Archiving or deleting an inactive chat preserves the active chat and its draft.
- After deleting or archiving the active chat, an available chat becomes active. If no recent chats remain, a new chat is created.
- Restoring the active archived chat returns it to recent chats.
- Deletion releases the session and removes its local file. Separately exported copies remain.
- A chat with no message history is deleted immediately. A chat with history opens a confirmation modal; cancellation preserves the entire chat. The same rule applies to the delete button in the right sidebar.
- The delete action uses a distinct warning color. Deletion is irreversible within the application.
- Actions are unavailable during a main operation. The menu closes after selecting an item.
- Commands belonging to a deleted row become unavailable; results from its background title refinement are ignored.

## Acceptance criteria

Check archiving, restoring and deleting through the three-dot menu for active and inactive rows, including the last chat and application restart. Verify immediate empty-chat deletion, cancellation for a chat with history and deletion only after explicit confirmation.
