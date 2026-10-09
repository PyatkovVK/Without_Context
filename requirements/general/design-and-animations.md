# Design and animations

Status: Implemented.

## Requirements

- The product name is Without Context.
- The dark interface has three areas: chats on the left, conversation in the center, and context and models on the right.
- The appearance follows the design reference supplied by the user.
- Both sidebars collapse, with smooth width and opacity changes.
- Buttons, input focus, model cards and view changes have smooth transitions.
- Settings dim the background and block its input and scrolling; the window has no overall scrollbar.
- Action buttons, especially the chat menu and collapse buttons, remain accessible at the minimum 1000 × 720 size.
- English is the default language; all new labels also have Russian localization.
- Target platforms: Windows, macOS and Linux Mint. Builds and startup for visual review are verified on Windows; other platforms require separate validation.

## Acceptance criteria

Check full and compact windows, both collapsed sidebars, chat row menus, Russian localization and settings. Rapid repeated clicks do not leave panels or views in an incorrect state.
