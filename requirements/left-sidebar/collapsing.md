# Collapsing the left sidebar

Status: Implemented.

## Behavior

- The collapse button is at the top and remains available at the minimum window width.
- The panel width changes smoothly; content fades in and out.
- The collapsed state retains buttons for expanding the sidebar and creating a new chat.
- Rapid repeated clicks correctly reverse the transition.
- Collapsing preserves the active chat, context, draft and archive.
- In a narrow window, the panel uses a compact width; the application title may be abbreviated, while the collapse button remains visible.

## Acceptance criteria

Collapse and expand the panel repeatedly, including rapid clicks and a 1000 × 720 window. The open conversation and access to buttons are preserved.
