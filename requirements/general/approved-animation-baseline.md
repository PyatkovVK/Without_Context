# Approved animation baseline

Status: Implemented and explicitly approved by the user on 2026-10-27.

## Preservation requirement

The user approved the animations in the current local build and explicitly requested that they be preserved. This is the reference behavior for future interface work. The reference is the working application approved on this date, including its local changes, rather than an earlier Git revision.

Keep the current timing, easing, opacity ranges, layout continuity and interaction feedback when adding features, changing layout, updating dependencies or refactoring presentation code. Change this approved visual behavior only when the user explicitly requests an animation change. Ordinary interface work should proceed while preserving this baseline; it does not require another confirmation.

## Approved parameters

| Surface | Current behavior to preserve |
| --- | --- |
| Left and right sidebars | Width, padding, header/brand widths and content opacity use 500 ms transitions with `CubicEaseInOut`. |
| Sidebar content | Retains its expanded layout throughout resizing. Hidden content cannot receive pointer or keyboard input. Reversing direction continues from the current animated position. |
| Main-area document views | Display one view at a time. Outgoing views hide immediately; the incoming view fades from opacity `0.94` to `1.0` over 240 ms with `CubicEaseInOut`. |
| Settings window | Content fades from `0.94` to `1.0` on opening and back to `0.94` on closing over 400 ms with `CubicEaseInOut`. The content remains visible while the window closes; repeated close actions do not start another closing sequence. |
| Modal background dimming | Uses the existing 500 ms `CubicEaseInOut` opacity transition. Modal input blocking and focus behavior remain intact. |
| Model-card expanders | Retain their 500 ms `CrossFade`, with `CubicEaseInOut` for both fade directions. |
| Field focus and trimming selection | Retain the existing 350 ms `CubicEaseInOut` border and selection-dot transitions. |
| Button hover and press | Both states use the same instantaneous dark fill, `#183047`. No brush interpolation, pulses, native gray flashes or brightness changes. Primary buttons retain their existing blue `#369AFF`. |
| General visibility fades | Retain the existing 500 ms eased fade where `SmoothVisibility` uses the default fade mode. |

Keep motion smooth and restrained. Preserve rapid reversals, delayed-callback revision checks, stable layout and accessible controls at 1000 × 720 as well as larger window sizes. Functional additions must not introduce overlapping document text, empty opaque settings windows or animated button hover effects.

## Implementation references

- [SmoothVisibility.cs](../../src/ContextGuard.App/SmoothVisibility.cs): visibility modes, opacity ranges, timings and revision guards.
- [MainWindow.axaml](../../src/ContextGuard.App/MainWindow.axaml): sidebar geometry, header widths, content visibility and modal shade.
- [App.axaml](../../src/ContextGuard.App/App.axaml): model expansion, field focus, trimming selection and stable button states.
- [SettingsWindow.axaml.cs](../../src/ContextGuard.App/SettingsWindow.axaml.cs): settings opening and closing.
- [UI checks](../../tests/ContextGuard.UiTests/Program.cs): intermediate sidebar positions, rapid reversals, exclusive views, modal behavior and stable hover/press colors.

These are active implementation files, not duplicate reference implementations. Changes unrelated to motion may edit these files while preserving the approved behavior.

## Regression review

For changes affecting presentation, verify opening and closing both sidebars, reversing them midway, switching chat/context/files/replacements, opening and closing settings, modal dimming, model-card expansion, field focus and trimming selection. Verify English and Russian interfaces and compact layouts.

Inspect button hover immediately, after 100 ms and after 650 ms, including a held press and movement between icon and label. The fill must remain constant. Existing behavioral UI checks must continue to pass. Static screenshots establish layout only; they do not prove the approved motion has been preserved.

Keep this document and [Design and animations](design-and-animations.md) consistent. Record a future explicitly requested animation change here, including its reason and new approved parameters, instead of silently replacing this baseline.
