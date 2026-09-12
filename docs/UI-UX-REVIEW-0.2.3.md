# FrameForge 0.2.3 UI / UX review

## Flat, responsive buttons

The shared button template uses a flat fill and a thin border, with no shadow, raised edge, or vertical displacement. Hover subtly tints the surface. Pressing darkens it and applies a small uniform scale inside a fixed hit target; a 140 ms release tint confirms activation. Content remains above the state overlays so labels stay crisp.

Keyboard focus retains a visible outline. When Windows client-area animation is disabled, the scale and release animation are suppressed while the pressed color remains.

## Spacing and alignment

- Ordinary buttons have a 36-DIP minimum height and 4-DIP margins, creating 8-DIP gaps between neighbors.
- Text fields and combo boxes use matching minimum heights and margins.
- Left and right sidebars are wider, with consistent 12-DIP content insets.
- Tool icons occupy a fixed column and captions share a common left alignment.
- Footer buttons use consistent type size and padding and align with the capture/tool rows.
- The capture search field and folder button share a row height; the compact clear control fits inside the search field.
- Color swatches use explicit 28-DIP square faces and even gaps.
- Recording and scrolling session controls use equal-width columns.
- Preferences, resize, recording options, and video export forms fit their content, with scrolling when the available screen height is limited.
- Preferences puts Cancel and Save at the bottom.
- Shortcut dialog actions remain pinned below the scrolling form. Shortcut fields explicitly inherit the shared TextBox style, and their tab order precedes the bottom actions.
- Resize dimensions have separate labels and align in two columns.

## Verification

The existing application/UI regression run passed 64 checks, including minimum-window layout, enabled/disabled actions, capture overlays and cancellation, clipboard copying, recording, live scrolling, and tray behavior. The minimum-size render and the running editor were inspected visually.

Real Windows input opened and closed Preferences, recording settings, and resize. Their buttons and form fields fit without clipping. These reviews used an isolated capture library and did not alter the user's preferences.

The shortcut layout was adjusted after the main regression run. In the final packaged app, its input padding, initial Tab target, and pinned action row during scrolling were verified with real Windows input. Final installer verification passed all 13 checks; see TEST-REPORT.md.

The rendered test-window PNG has transparent areas that some image viewers display as black; the running window was also inspected and renders normally. Reduced-motion behavior was reviewed in source; the Windows setting was not changed during testing.
