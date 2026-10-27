# Design and animations

Status: Implemented.

The current local build's animations were explicitly approved by the user on 2026-10-27. Preserve the [approved animation baseline](approved-animation-baseline.md), including its timings and interaction behavior, during future interface changes.

## Requirements

- The product name is Without Context.
- The dark interface has three areas: chats on the left, conversation in the center, and context and models on the right.
- The appearance follows the design reference supplied by the user.
- Both sidebars collapse, with smooth width and opacity changes.
- Sidebar transitions use a 500 ms eased motion and retain content layout throughout. Rapid reversals must continue from the current animated position without snapping or stale visibility changes.
- Main-area views never overlap outgoing and incoming documents. Use a subtle entry fade from 94% to full opacity over 240 ms to avoid bright/dark flashes.
- Settings content stays visible during its 400 ms transition; it must not open or close as an empty opaque rectangle. Modal background dimming uses a 500 ms eased transition.
- Buttons use a dedicated template with a fixed dark hover color (#183047). Hover and pressed states share that color; moving within a button or keeping the pointer over it must not trigger color interpolation, pulses, gray flashes or brightness changes. Primary actions keep their existing blue fill during hover and press.
- Model headers use the same dark color for hover and press. Field focus and trimming indicators retain their subtle transitions. Disabled buttons retain their normal dark palette.
- Keep width and view transitions separate from hover styling. Hover feedback must remain static even during sidebar resizing.
- Settings dim the background and block its input and scrolling; the window has no overall scrollbar.
- Action buttons, especially the chat menu and collapse buttons, remain accessible at the minimum 1000 × 720 size.
- Sidebar toggles have 24-pixel icons and 44 × 44-pixel hit areas.
- English is the default language; all new labels also have Russian localization.
- Target platforms: Windows, macOS and Linux Mint. Builds and startup for visual review are verified on Windows; other platforms require separate validation.

## Acceptance criteria

Check full and compact windows, both collapsed sidebars, chat row menus, Russian localization and settings. Rapid repeated clicks do not leave panels or views in an incorrect state. Inspect hover immediately, after 100 ms and after 650 ms; the fill must stay identical, including during a held press and movement between a button's icon and label.
