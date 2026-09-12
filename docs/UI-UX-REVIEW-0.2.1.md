# FrameForge 0.2.1 UI / UX review

Reviewed the main capture/editor window, recent library, sidebar navigation, command availability, and keyboard behavior on the available Windows desktop.

| Finding | Change | Verification |
|---|---|---|
| “Folder” did not describe the destination or action. | Renamed to **Open capture folder**, with an explanation and actual directory in its tooltip; opening it confirms the action in the status bar. | A real click opened the isolated capture Library in Windows File Explorer. |
| Thick scrollbars competed with the sidebar controls. | Replaced them with overlay scrollbars: 4-DIP visible thumbs, 12-DIP grab areas, no arrow buttons, stable content width. | Real wheel scrolling and thumb dragging worked; the bar hid after focus moved to capture search. |
| Settings and Exit required scrolling past the annotation tools. | Pinned Shortcuts, Preferences, Help, Hide to tray, and Exit in a fixed footer. | Footer remained in place while the tools scrolled. |
| The library search appeared as an unlabeled empty input. | Added a visible search prompt, accessible name, clear button, and a specific no-results message. | Typed a nonmatching query, checked the empty state, and cleared it to restore the saved sample. |
| Empty-editor and selection-only actions appeared usable when they had nothing to act on. | Document, selection, Undo, and Redo controls now reflect their available state. | Opening the sample enabled image actions; drawing enabled selection actions and Undo; Escape disabled selection actions while keeping Undo. |
| Save, Export, zoom controls, and full-desktop capture were insufficiently explained. | Added contextual tooltips and renamed Screen to All screens. Long document titles truncate with a full-title tooltip. | Inspected command descriptions and the editor layout. |
| Several property fields and color swatches lacked useful accessible names. | Named capture delay, stroke width, text size, annotation text, and palette colors. | Covered by source review and compiled UI inspection. |
| Ctrl+Y intercepted redo while typing. | Text fields retain their own redo behavior. | Reviewed the same text-focus guard used for text-field undo/copy/paste. |

Scrollbars also remain visible during keyboard focus and dragging. High-contrast mode keeps a visible system-color thumb; that fallback is implemented but was not separately exercised in a Windows high-contrast session.

The release regression suite includes compact-window checks at the supported minimum dimensions and checks that image commands enable only after an image is open. See [test report](TEST-REPORT.md) for the final results.

All UI smoke-test content was synthetic and stored beneath the project's ignored artifacts directory. No personal capture library was used for these edits.

## Folder-action follow-up (0.2.2)

The 0.2.1 folder test covered the isolated test directory only. It missed Windows package redirection affecting the real LocalAppData library. A later test reproduced an unavailable-folder error for that logical path. Version 0.2.2 resolves the physical path from an open filesystem handle before handing it to Explorer; the real existing library was then opened and inspected successfully.
