# Delete confirmation

Status: Implemented.

## Purpose

Prevent accidental loss of a conversation with message history while keeping removal of empty chats quick.

## Behavior

- Apply the same policy to the row's three-dot menu, recent and archived chats, and the right-sidebar delete button.
- A chat with no messages is deleted immediately when the user chooses delete. An unsent draft or prepared context alone does not count as message history. Empty chats are not automatically removed when switching chats.
- A chat with message history opens a centered modal showing its title, an explanation of permanent deletion, and "Cancel" and "Delete this chat" buttons.
- Do not change the active chat, release its session or remove its stored file before confirmation.
- Dim the background and block its input, scrolling, navigation and other chat actions while the modal is open.
- Focus "Cancel" initially. Escape cancels. Pressing Enter on the default action must not delete the conversation.
- Cancel preserves messages, context, title, archive membership, credentials and drafts.
- Confirmation deletes the original target even if it was inactive, releases its session and removes its local file. Exported copies remain.
- Deleting the active chat selects another available chat or creates an empty chat if none remain.
- Confirmation is invalid after workspace disposal; background title results cannot recreate a deleted chat.
- All modal text has English and Russian localization.

## Acceptance criteria

Delete an empty recent chat without a prompt. Open confirmation for active, inactive and archived chats with history. Cancel by button and Escape and verify all state and files remain. Confirm and verify only the target is removed and exported copies remain. Check the dimmed background, focus, both languages and deletion while a title request is pending.
