# HireGameDev — Design System v1.0 ("Volt & Obsidian")

Derived from your look-and-feel brief: neon yellow + obsidian, professional and clean, bright game-dev accent without the gamer-clutter look. This file is the contract; UI code must not hard-code colors, radii or type sizes outside these tokens.

## 1. Brand position

Reads like a **tool for working developers**, not an esports site. One loud color, everything else quiet. If a screen has more than one yellow element competing for attention, the design is wrong.

## 2. Color tokens

The accent behaves differently in each theme, and that is intentional: **neon yellow on white fails contrast**, so light mode uses a deeper gold for anything carrying text, and reserves pure volt for fills and dark-on-yellow labels.

### Dark (obsidian)

| Token | Value | Use |
| --- | --- | --- |
| `--bg` | `#0B0C0E` | page |
| `--bg-elev-1` | `#141619` | cards, panels |
| `--bg-elev-2` | `#1B1E22` | inputs, nested panels, hover |
| `--border` | `#262A30` | hairlines |
| `--border-strong` | `#3A4046` | focus-adjacent, table rules |
| `--text` | `#ECEEF1` | body |
| `--text-muted` | `#9BA3AE` | secondary |
| `--accent` | `#D6FF3D` | primary buttons, active states |
| `--accent-hover` | `#E4FF6B` | hover |
| `--accent-ink` | `#0B0C0E` | **text on accent, always** |
| `--accent-soft` | `rgba(214,255,61,.12)` | selected rows, tag fills |
| `--success` `#4ADE80` · `--warn` `#FBBF24` · `--danger` `#F87171` · `--info` `#7DD3FC` | semantic | status only, never decorative |

### Light

| Token | Value | Use |
| --- | --- | --- |
| `--bg` | `#FFFFFF` | page |
| `--surface` | `#F7F8FA` | cards on white |
| `--surface-2` | `#EEF0F3` | inputs, chips |
| `--border` | `#E3E6EA` | hairlines |
| `--text` | `#14161A` | body |
| `--text-muted` | `#5A6270` | secondary |
| `--accent` | `#E9B400` | links, labels, icon-active (gold — passes AA on white for large text) |
| `--volt` | `#D6FF3D` | **fills only**: badge backgrounds, match-meter, selected chip fill with dark ink on top |
| `--volt-ink` | `#14161A` | text on volt fills |
| `--focus` | `#14161A` outer + `--volt` inner ring | see §6 |

Dark is the default for the app shell and marketing hero; light is the default for long forms (job post, onboarding) because most candidates fill profiles on a phone in daylight. Auto = `prefers-color-scheme`.

## 3. Typography

- **Space Grotesk** 500/600/700 — headings, numerals in dashboards, section labels. `letter-spacing: -0.01em` above 24px, `-0.02em` for hero sizes.
- **Inter** 400/500/600 — body, labels, tables, buttons. `font-feature-settings: "cv05","ss01"` for a slightly friendlier `a`/`g`; `tnum` for all money and count columns.
- Scale (mobile → desktop, one scale, no magic numbers): `12 / 13 / 14 / 16 / 18 / 22 / 28 / 36 / 48`. Body 16, small 14, labels 13 (uppercase + `0.04em` tracking only for tiny section labels), H3 18, H2 22, H1 28→36, hero 36→48.
- Line height: 1.5 body, 1.25 headings, 1.1 numerals. Max prose width 68ch.
- **Self-host both fonts** (`@fontsource/inter`, `@fontsource/space-grotesk`), `font-display: swap`, latin subset woff2 only. No CDN `<link>` to Google Fonts: this app is used on flaky mobile connections and the build sandbox has no access to font CDNs anyway.

## 4. Space, radius, elevation

- Spacing scale 4px base: `4 8 12 16 20 24 32 48 64 96`. Card padding 20 (16 mobile), grid gap 16, section gap 32/48.
- Radius: `6` inputs/badges · `12` cards/buttons · `16` modals/hero panels · `999` chips/avatars/pills.
- Elevation, restrained — max two levels in light mode, and in dark mode prefer a hairline border over a shadow:
  - `--shadow-1: 0 1px 2px rgba(16,18,22,.06), 0 1px 1px rgba(16,18,22,.04)`
  - `--shadow-2: 0 10px 30px -12px rgba(16,18,22,.18)` (menus, dialogs)
- 1px borders everywhere instead of dividers with heavy contrast.

## 5. Components (shared vocabulary for every module)

| Component | Spec |
| --- | --- |
| Button | sizes 36/44/52; `primary` = volt fill + dark ink, `secondary` = elev-2 + border, `ghost`, `danger`; radius 12; no gradients; disabled = 45% opacity + `not-allowed`; active = `translateY(1px)` |
| Input / select / textarea | 44px tall, radius 6, bg `surface-2`, border `--border`, label above (never placeholder-only), helper text below, error replaces helper with red text + red border |
| Chip (skills) | pill, 28px, removable `×`, `is_primary` = volt fill; typeahead with keyboard `↑↓ Enter Backspace` |
| Card | radius 12, padding 20, hairline border; hover on clickable cards = border-strong + `shadow-1` + 1px lift, **not** a full-card glow |
| Match meter | segmented 0–100 arc or bar, volt fill, label `Strong / Good / Partial`; below 40 renders nothing; the number is always shown as text too (never color-only) |
| Status badge | applied / viewed / shortlisted / interview / offer / hired / rejected / withdrawn — each a fixed hue from the semantic set + 10% tint fill, uppercase 12px |
| Empty state | icon-free: one line of what's missing, one primary action, one secondary. Never "Nothing here" |
| Skeleton | `surface-2` shimmer, 3 lines max, matches final layout height |
| Toaster | bottom-right desktop, top mobile; auto-dismiss 6s, errors sticky with a retry |
| Table | 44px rows, header uppercase 13px muted, `tnum`, sticky header inside scroll containers, row click = whole row is the link |

## 6. Interaction, focus, motion

- **Focus is a first-class feature** (this is a form-heavy product): `outline: 2px solid var(--volt); outline-offset: 2px;` — visible in both themes, never `outline: none`. Focus-visible only.
- Transitions: `120ms` colors, `160ms ease-out` transform, `180ms` dialogs (fade + 4px rise). Nothing animates layout width/height. `@media (prefers-reduced-motion: reduce)` kills all transitions and the shimmer.
- Hover is never the only affordance: every hover action is reachable by click/keyboard.
- Loading: optimistic where it can be reverted (save job, shortlist), pessimistic everywhere else. Never optimistic on apply.

## 7. Accessibility constraints (blocking, checked in CI)

- WCAG 2.2 AA text contrast (4.5:1 body, 3:1 large/UI). Volt is a **fill**, gold is the **light-mode text** accent, for exactly this reason.
- Status is always badge + text, never color alone.
- Target size ≥ 44×44 on mobile; 24px min gap between inline action buttons.
- Forms: one `<h*>` hierarchy per page, `aria-describedby` linking helper/error text, errors summarized in a dialog above the form with links to each field (long wizard forms).
- Keyboard order matches visual order; skip-link to main content; modals trap focus and return it.
- Run `axe` on every page in the test suite (see `TESTING.md`); zero critical violations is a merge gate.

## 8. Page-level rules for this product

- **Job cards**: title (Space Grotesk 18), studio name + verification mark, category/work-mode/city chips, salary in `tnum` gold, "posted 2 days ago" muted, save icon top-right, match meter bottom-left **only** when signed in as a candidate. Max 3 pieces of metadata per row.
- **Search page**: filters in a left rail ≥1024px, a bottom-sheet "Filters" on mobile; active filters as removable chips above the results; result count always visible; zero-result state must offer "clear filters" + "enable alerts".
- **Profile wizard**: one idea per step, progress bar with step names, autosave indicator, "why we ask" tooltip on CTC/notice period, back button never loses data.
- **Admin tables**: density toggle (comfortable/compact), reason column always visible for destructive actions.
- **Marketing hero**: dark obsidian, one volt element (the primary CTA), a real screenshot or job-card mock, and no fake counters.

## 9. File layout (agreed with the blueprint)

```
wwwroot/css/tokens.css        # all custom properties, theme blocks
           /base.css           # reset, typography, layout utilities
           /components.css     # the §5 table as code
           /pages/*.css        # page-specific only, nothing that belongs in components
```

No utility-class framework, no Bootstrap classes in markup beyond what the blueprint's grid needs; custom CSS owns every visual decision so the design stays reviewable in one place.
