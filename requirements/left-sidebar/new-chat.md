# New chat

Status: Implemented.

## Purpose

Start an independent conversation while preserving existing chats.

## Behavior

- The "New chat" button is at the top of the left sidebar. Its text is centered horizontally and vertically, with a "+" icon beside it.
- The collapsed sidebar provides a "+" button with a tooltip.
- A new chat immediately becomes active and receives the temporary title "Chat N".
- Current model, protection and trimming settings are copied into the new session. Context, replacement maps, approval, conversation history and drafts start empty.
- Sending requires preparing and approving context for this chat.
- Creating a chat is unavailable during preparation, sending, connection checks or export.

## Acceptance criteria

Create a second chat: the first retains its conversation, context and draft; the second contains none of them. The button remains available after collapsing the sidebar.
