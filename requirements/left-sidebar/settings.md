# Settings

Status: Implemented.

## Purpose

Open general preferences and manage local data.

## Behavior

- The "Settings" button is at the bottom of the left sidebar.
- When the sidebar is collapsed, a centered 44 × 44-pixel icon button remains at the bottom with a localized "Settings" tooltip and accessible name. It opens the same modal without expanding the sidebar.
- Settings open in a modal window. The background is dimmed, and scrolling and input in the main window are blocked.
- The window has no overall scrollbar: settings fit within its content area.
- English and Russian are available. Switching requires no restart and does not translate documents, replies or task titles.
- Settings include the anonymization profile, custom replacement values, context limit and local restoration of original values in replies.
- The chat storage path is displayed and can be selected and copied.
- Exporting all chats and clearing all chats and credentials are available.
- Changing protection rules requires preparing and reviewing context again.
- "Done" and Escape close the window with a smooth transition.

## Acceptance criteria

Open settings from both the expanded sidebar and its collapsed icon, attempt to scroll the background, switch the language and close the window. The current conversation and sidebar state are preserved, and the background becomes interactive again. Only the settings control for the current sidebar state accepts input.
