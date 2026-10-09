# Recent chats

Status: Implemented.

## Purpose

Select a conversation and see its topic.

## Behavior

- The list contains chats that are not archived. New chats appear at the top.
- Each row displays an icon, title, time of the latest message and a three-dot menu button.
- The active row is highlighted. Long titles are truncated with an ellipsis; a tooltip shows the full title.
- Selecting a row opens that chat's conversation, context, model settings and draft.
- Switching chats and list actions are unavailable during a main operation.
- Background title generation does not block the list.
- Chats are stored locally and restored after restarting the application.

## Acceptance criteria

Switch between two conversations with different tasks: their messages and sensitive data remain isolated. Open the menu of an inactive chat and archive it: the current conversation remains active.
