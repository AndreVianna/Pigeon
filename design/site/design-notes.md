# Pigeon website: design notes

Written 2026-11-24 for Pigeon 0.7.0-beta.2. These notes go with the design at https://claude.ai/artifact/EccdyBhNDr2PiQRzVgswLC (private to its owner until shared) and the logo files in `design/site/logo/`. Structure and copy come from `product-brief.md`; the look comes from the Pigeon design system (Sleek Dark Mode: Charcoal, True black, Daylight).

The design was written without being rendered on the canvas. Frame heights are estimates with room to spare, so some frames end in empty page-colored space. Open each page once and trim the frames.

## Logo

**Update:** the owner chose the "Scroll, A Clean" mark (a pigeon with both wings raised, carrying a violet rolled message in its beak). It is now the official set in `design/site/logo/` and is used on every version 1 and version 2 site artboard. The wordmark did not change. The P-head logo described below is kept in `design/site/logo/concepts/p-head/` and on the "Logo" page of the design.

- **Mark.** A heavy geometric P that is a pigeon's head and neck: the bowl is the head, its counter is the eye, a short wedge is the beak, and one violet band crosses the neck. The band is the pigeon's iridescent collar and the message it carries. It is the only violet in the logo.
- **Why it reads at 16 px.** One solid shape with three features, each at least 1.5 px at that size. `favicon.svg` is a separate drawing on a 32-unit grid, so its straight edges (stem, band, eye) land on whole pixels at 16 and 32 px, and it carries its own charcoal tile so it holds on light and dark browser tabs.
- **Both grounds.** The body takes the ink color of the ground; the band uses the system's `accent` token for that theme (`#7361e4` on dark, `#634cd4` on light). On every ground the band differs from the ink and from the ground in lightness as well as hue (3:1 or more).
- **Wordmark.** "Pigeon" drawn from circles and stems, outlined as paths, no font needed. Weight and proportions sit beside Inter 600 without copying it.

| File | Use |
|---|---|
| `pigeon-mark.svg`, `pigeon-wordmark.svg`, `pigeon-lockup.svg` | Inline in HTML. Ink is `currentColor`; the band reads `var(--accent)`. |
| `*-on-dark.svg`, `*-on-light.svg` | As `<img>` or in other tools, where `currentColor` would turn black. |
| `favicon.svg` | Browser tab icon. Fixed colors, own ground. |

Not made: PNG or ICO favicons, an Apple touch icon, the 1200 by 630 social card.

## Artboards

| Canvas page | Artboards |
|---|---|
| Logo | Mark, lockup and wordmark on three grounds. Small sizes (64 to 16 px) and the favicon on light and dark browser tabs. |
| Home | Desktop 1440 in two frames (a frame is limited to 8000 px). Phone 390 in three frames, plus the open menu. Daylight: hero and "How it works". Variant for when the recording exists: hero version B and "From the command line" (H7). |
| Install | Desktop. Every tab panel stacked (also the layout without JavaScript). Phone in two frames, with the Linux and Codex tabs selected. |
| Security and 404 | Security and limits, desktop. Not found, desktop. |

The main Home uses hero version A and leaves H7 out, as the brief says.

## How the system is used

- Every artboard links the system's own stylesheet (`ds/visuals/components/bundle.css`) and uses its classes: announcement bar, buttons, links, eyebrow, display heading, inline code, tabs, accordion, table, list items.
- `site.css` (in the design's files) holds the token values for the three themes, copied from the system's `tokens.json`, and the site's layout. The theme is `data-polarity` on the page wrapper.
- One token is changed for the site: base text is 15 px (the system's 13.5 px is sized for app screens). Phone buttons are 44 px tall.

## Components the system did not have

- **Section rail.** A narrow mono column, ruled off from the content, holding the section number and its anchor. The page column itself is ruled on both sides. This is the system's "sidebar ruled off from the work" applied to a reading page. On the Security page the rail is the contents list.
- **Terminal transcript.** Real text; each command has its own Copy button and a prompt drawn by CSS (not copied); output is lighter in weight and tone.
- **Command block** with an optional label and a Copy button.
- **Shared-line cell grid** for steps, rules and the tried / not yet tried columns.
- **Small diagrams** built from separate nodes and wires in flex rows, which turn into columns on a phone. The violet wire is the message path.
- **Stacking table**: on a phone each row becomes a block with its column names as labels.
- **Tick lists**: a check mark for "Tried", an open circle for "Not yet tried", told apart by shape, in neutral colors.
- **Join-code window stand-in** and **recording replay panes**, both marked as placeholders.
- **Phone menu** from `<details>`, so it works without JavaScript.

## Copy changes

- Added: rail labels (01 to 09 and the anchors on Home; "Step 1 of 4", "Check", "Later", "Help", "Report" on Install), "On this page" above the Security contents, "Command line" as the terminal title, "You" and "Pigeon" in the problem diagram, "Copy" on command blocks.
- Added on Install, Linux: "On openSUSE, zypper install replaces dnf install." The brief gives it as a note beside the table.
- "MIT License" in the footer links to the license file.
- Invented and marked as a stand-in: the text inside the join-code window ("Join code for dev-laptop", "Give this code to the person at the other machine.") and the "Wakes, nobody typing" markers in the recording variant.
- Everything else is the brief's copy as written.

## For the build

- Host Inter and JetBrains Mono with the site. The design loads them from Google Fonts, which the published site must not do.
- Ship only the rules the pages use from the system's stylesheet (it is 176 KB), plus `site.css`.
- The design uses a container query on the page wrapper; a media query at the same width (720 px) does the same job on the site.
- The header is sticky. Focus uses the system's violet ring on every link and button.
- Links between artboards (for example "Download the beta") point at other artboards so the design can be played; the real targets are in the brief's links table.

## Open questions

1. The join-code window and the recording are placeholders until they are captured (brief, open questions 4 and 5).
2. Is the P-with-a-beak mark the direction to keep, and should the collar stay violet in one-color uses (print, stamps), or drop to a cut-out?
3. Should Home on desktop stay at 15 px text, or go back to the system's 13.5 px?
4. Are the rail numbers wanted? They are decoration, and they shift by one when H7 appears.
5. The tool tab labels are long for a phone; the tab list scrolls sideways. Shorter labels ("Claude", "Codex", "Antigravity", "OpenCode", "Copilot") would fit without scrolling.
6. True black is shown only for the logo. Should the site offer it, or only Charcoal and Daylight?

---

# Version 2 (from `product-brief-v2.md`)

Version 1 above stays as it is. Version 2 is on the canvas pages "Home v2", "Hook animation v2", "Install v2" and "404 v2", and adds one stylesheet, `site-v2.css`, loaded after `site.css`. It uses the original Courier mark until the owner picks a variant; the mark appears in the header, the footer, the hook scene, the join-code stand-in and the 404, always the same two shapes, so it can be swapped in one pass.

## How the four layers look

1. **Hook.** A full-width stage one tone below the page, with a faint one-pixel grid that fades out and a violet light behind the mark. Two large agent windows sit on the stage, joined by a line through the Pigeon mark. Under them: a 76 px headline, one line, one button with a violet glow, and the scroll cue. Nothing else is on the first screen (about 910 px tall at 1440).
2. **How it works.** No rules, no rail: four open panels with large type, each led by a small picture made from the hook's own pieces.
3. **Get started.** Three numbered blocks: download rows per system with the beta line directly under them, five tool cards (the "not supported yet" note takes the sixth cell), two prompts to copy.
4. **For engineers.** The page changes register: the ruled column and the index rail from version 1 return, type gets smaller, and the twelve blocks are tables, lists and code.

## The hook animation

The scene is one element with a state class (`hk--b1` to `hk--b5`, `hk--still`); the six artboards on "Hook animation v2" are those states. Violet is used for the note, for a received message, for the mark's capsule while the note passes, and for the woken window's edge.

| Time | What moves | How |
|---|---|---|
| 0.0 to 0.4 s | Scene settles in the beat 1 state: left window lit, right window at 60% opacity | opacity, 400 ms, ease-out |
| 0.4 to 2.0 s | The question writes itself in the left window | a cover the color of the bubble slides off the text one character at a time (`steps()`), caret visible |
| 2.0 to 2.5 s | Hold | |
| 2.5 to 2.8 s | The bubble dims to 35%; the note appears at the left window's edge | opacity, 300 ms |
| 2.8 to 4.3 s | The note travels along the line to the right window, with a short trail behind it | translate only, `cubic-bezier(.4, 0, .2, 1)` |
| about 3.5 s | As the note passes the mark, the capsule brightens once | opacity of a lit copy: 250 ms in, 400 ms out |
| 4.3 to 4.5 s | The note fades at the right window's edge | opacity, 200 ms, ease-in |
| 4.5 to 5.0 s | The right window comes up from 60% to 100%; its violet edge fades in | opacity, 500 ms, ease-out |
| 4.9 to 5.2 s | The question appears in the right window | opacity and a 6 px rise, 300 ms, ease-out |
| 5.9 to 6.5 s | The violet edge fades out; the window stays lit | opacity, 500 ms, ease-in |
| 6.7 to 7.7 s | The answer writes itself in the right window | as the question |
| 7.8 to 9.0 s | The answer dims, the note travels back through the mark (capsule brightens again) | as the first trip, reversed |
| 9.1 to 9.4 s | The answer appears in the left window | opacity and a 6 px rise, 300 ms |
| 9.6 to 10.0 s | Caption "Nobody typed." fades in | opacity, 400 ms |
| 10.0 to 11.4 s | Hold | |
| 11.4 to 12.0 s | Everything fades back to the beat 1 state | opacity cross-fade, 600 ms |

Rules for the build:

- Animate only `transform: translate` and `opacity`. No bounce, no overshoot, one thing at a time.
- One 12 s timeline (CSS keyframes on the scene, or the Web Animations API), so Pause and Play stop and resume everything together. The button reads "Pause", then "Play".
- Pause when the hook leaves the viewport (an `IntersectionObserver`).
- `prefers-reduced-motion: reduce`: show the still frame, run nothing, hide the Pause button. The still frame is also the markup as served, so it is what shows before the script runs and without JavaScript.
- On a phone the windows stack and the note moves along the vertical line: same timeline, translate on Y.
- The dimmed window is an idle, open chat. Never draw it closed.

## Checked and not checked

- Every version 2 page was rendered outside the canvas, with the same markup and stylesheets, in Chrome at 1440 and at 390 wide, in Charcoal; the top of Home also in Daylight; the six animation states at 1200 and one at 390. Frame heights are the measured heights plus 16 px.
- Not checked: the design canvas itself (it needs a sign-in the designer does not have). The phone frames of Home and Install and the Daylight frame are thin wrappers that show another artboard at a different width or theme; if they come up empty on the canvas, the content is still in the artboards they point to. The beat frames are complete on their own.
- Version 1 pages were not re-rendered or changed.

## Layout choices to confirm

- Headline and words sit under the scene, centered, so the picture leads. The alternative is words left, scene right.
- Home on desktop is five frames (the canvas limits a frame to 8000 px): the first three layers and Help test, then four parts of the engineers layer. On a phone it is five frames as well.
- Install v2 shows the Windows and Claude tabs. The other panels have the same copy as on the version 1 board "Install: every tab panel"; in version 2 each panel starts with the tool's long name and the tabs use the short names.
- The beta line under the download rows is normal muted text, as the brief asks. If it should stand out more, it can take the ink color.

