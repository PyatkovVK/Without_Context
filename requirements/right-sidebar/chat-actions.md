# Chat actions menu

Status: Implemented.

## Behavior

- Put export, archive/restore and delete in a compact **⋮** dropdown next to the close/collapse button in the right-sidebar header. Keep the bottom of the sidebar free for settings.
- Archive/restore and delete are single-line 34-pixel-high rows with 16-pixel icons and compact padding. Do not use descriptive second lines or navigation chevrons.
- Put the existing localized archive/restore and deletion explanations in tooltips. Keep deletion visually distinct in red.
- Keep export as a compact action above these rows inside the dropdown. All actions remain reachable at the minimum supported window size without scrolling the settings.
- Close the dropdown after an action, when switching chats, starting an operation, opening a modal or collapsing the sidebar. Its hidden trigger cannot receive input. Preserve the approved sidebar animation behavior.
- Preserve active-chat targeting, archive restoration, operation locking and the existing confirmation for deleting a chat with history. An empty chat is still deleted immediately.

## Acceptance criteria

Verify the header dropdown, row heights and readable labels in English and Russian, including a restored chat and a compact window. Export, archive, restore, cancel deletion and confirm deletion through these actions. Confirm the dropdown dismisses on action and sidebar collapse, actions remain unavailable during a main operation, and the existing storage behavior is preserved.
