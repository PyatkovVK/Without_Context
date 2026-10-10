# Collapsing the left sidebar

Status: Implemented.

## Behavior

- The collapse button is at the top and remains available at the minimum window width.
- Both sidebar toggles use vector icons 24 pixels high in 44 × 44-pixel buttons. Chevrons use a centered 12 × 24-pixel drawing area matching their geometry, so the painted arrow is centered as well as its button. Collapsed panels are 72 pixels wide so the buttons are not clipped.
- In the collapsed state, the entire button and hover background are centered inside the panel with at least 10 pixels of clearance from each side. The arrow and plus button must not extend past the panel edge.
- Header and brand widths resize with the panel. Invisible branding must not reserve a minimum width that pushes the toggle outside the panel.
- The panel width changes smoothly; content fades in and out.
- Width, padding and content opacity transitions last 500 ms with smooth acceleration and deceleration. Expanded content retains its layout while fading, so headings, controls and the footer do not jump before resizing finishes.
- The collapsed state retains buttons for expanding the sidebar and creating a new chat, with a settings icon at the bottom. Settings remain accessible without expanding the panel.
- Rapid repeated clicks correctly reverse the transition.
- Collapsing preserves the active chat, context, draft and archive.
- The final open/closed state survives normal application restart, independently of the right sidebar. See [sidebar state persistence](../general/sidebar-state.md).
- In a narrow window, the panel uses a compact width; the application title may be abbreviated, while the collapse button remains visible.

## Acceptance criteria

Collapse and expand both panels repeatedly, including rapid clicks and a 1000 × 720 window. Check intermediate frames for continuous resizing, stable layout and no overlapping new-chat controls. Hover the collapsed toggles, plus button and settings icon: their entire colored background remains within the panel, and the fill stays constant. Check that the painted chevrons are centered within their buttons. Open and close settings from the collapsed panel; it remains collapsed and the conversation is preserved. Invisible controls do not accept keyboard or pointer input.
