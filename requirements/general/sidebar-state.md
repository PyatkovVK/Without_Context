# Sidebar state persistence

Status: Implemented.

## Purpose

Keep the user's preferred left and right sidebar visibility between application launches.

## Behavior

- A dedicated sidebar state service stores exactly two preferences: whether the left chat sidebar and the right context/model sidebar are open.
- Each panel is independent: all four open/closed combinations survive a normal application restart.
- The latest state is saved before normal window closure finishes. Rapid toggles, opening context/model options and changing chats preserve the final visible panel state.
- Loading is asynchronous. A late startup result cannot override a panel changed by the user or update a disposed workspace.
- On first launch, both panels are open. Missing, malformed, incomplete, oversized or unsupported state files also use that default.
- The versioned JSON file is `sidebar-state.local.json` in the OS local application-data folder `WithoutContext`, outside the repository and chat storage. Reads are limited to 4 KiB; writes replace the file atomically.
- File access failures use localized messages. A failed save keeps the existing file and uses the normal close retry / close-without-saving dialog.
- This service is used only for these sidebar preferences. It does not store language, window geometry, active chat, archive view, expanded model cards, models, folder paths, drafts, credentials, context, approval or replacement maps.
- Chat export and clearing chats do not export or reset sidebar preferences. Deleting the sidebar state file restores the default on the next launch.
- Forced termination before normal closure may leave the previous saved preference.

## Acceptance criteria

For each of the four combinations, toggle the panels, close the main window normally and reopen the application. Verify each panel independently, including its toggle, new-chat/settings access and hidden controls. Repeat with rapid toggles and at minimum window size. Check that the JSON contains only a schema version and the two boolean preferences, and that unrelated settings still use their existing behavior. Missing/corrupt/oversized files must not prevent startup. Simulate delayed loading, disposal and a write failure: no stale result changes the panels and failure is not reported as a successful save.
