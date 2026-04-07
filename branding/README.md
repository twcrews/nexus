# Nexus — Brand Guidelines

## Identity

**Nexus** is a developer dashboard that brings together work items and pull requests from across platforms into a single, focused view. The brand should feel purposeful, technical, and calm — built for engineers who value clarity over noise.

## Logo & Wordmark

- **Wordmark font:** [Lexend Giga](https://fonts.google.com/specimen/Lexend+Giga)
- **Casing:** All-caps — `NEXUS`
- **Weight:** Regular (400)
- **Usage:** The wordmark text is always rendered in the Wordmark Navy color (`#06255F`) on light backgrounds, or white (`#F0F4FF`) on dark backgrounds. Never stretch, skew, or recolor the wordmark outside of the approved palette.

<img src="examples/ex-wordmark-light.png" width="300" />
<img src="examples/ex-wordmark-dark.png" width="300" />

## Typography

| Role | Font | Weights |
|---|---|---|
| Wordmark | Lexend Giga | 400 |
| Headings | [Inter](https://fonts.google.com/specimen/Inter) | 600, 700 |
| Body | [Inter](https://fonts.google.com/specimen/Inter) | 400, 500 |
| Code / Monospace | [JetBrains Mono](https://fonts.google.com/specimen/JetBrains+Mono) | 400, 500 |

**Rationale:** Inter is a natural engineering-adjacent workhorse — highly legible at small sizes, widely available, and a familiar face in developer tooling. JetBrains Mono is purpose-built for code display and pairs well with the technical context of the app.

## Color Palette

### Brand Colors

| Name | Hex | Usage |
|---|---|---|
| **Primary** | ![](https://placehold.co/16x16/3A6EF0/3A6EF0.png) `#3A6EF0` | Primary actions, links, active states, wordmark |
| **Primary Dark** | ![](https://placehold.co/16x16/2853A4/2853A4.png) `#2853A4` | Primary hover/pressed states |
| **Primary Light** | ![](https://placehold.co/16x16/3290FE/3290FE.png) `#3290FE` | Subtle highlights, focus rings |
| **Wordmark Navy** | ![](https://placehold.co/16x16/06255F/06255F.png) `#06255F` | Wordmak text, other special use
| **Secondary** | ![](https://placehold.co/16x16/1ABFA3/1ABFA3.png) `#1ABFA3` | Secondary actions, badges, status indicators |
| **Secondary Dark** | ![](https://placehold.co/16x16/128F7A/128F7A.png) `#128F7A` | Secondary hover states |
| **Accent** | ![](https://placehold.co/16x16/F0883E/F0883E.png) `#F0883E` | Warnings, attention-grabbing callouts, CI/CD status |

**Rationale:**
- The **teal secondary** (`#1ABFA3`) complements the blue primary without competing — it reads as "confirmed / success" and pairs cleanly with the cool-leaning primary.
- The **amber accent** (`#F0883E`) provides high-visibility contrast for warnings and pipeline states, and adds warmth to an otherwise cool palette.

### Semantic Colors

| Name | Light | Dark |
|---|---|---|
| Success | ![](https://placehold.co/16x16/16A34A/16A34A.png) `#16A34A` | ![](https://placehold.co/16x16/4ADE80/4ADE80.png) `#4ADE80` |
| Warning | ![](https://placehold.co/16x16/D97706/D97706.png) `#D97706` | ![](https://placehold.co/16x16/FBB346/FBB346.png) `#FBB346` |
| Error | ![](https://placehold.co/16x16/DC2626/DC2626.png) `#DC2626` | ![](https://placehold.co/16x16/F87171/F87171.png) `#F87171` |
| Info | ![](https://placehold.co/16x16/3A6EF0/3A6EF0.png) `#3A6EF0` | ![](https://placehold.co/16x16/6B96F5/6B96F5.png) `#6B96F5` |

### Light Mode

| Token | Hex | Usage |
|---|---|---|
| `bg-base` | ![](https://placehold.co/16x16/F8F9FC/F8F9FC.png) `#F8F9FC` | App background |
| `bg-surface` | ![](https://placehold.co/16x16/FFFFFF/FFFFFF.png) `#FFFFFF` | Cards, panels, modals |
| `bg-subtle` | ![](https://placehold.co/16x16/EEF1F8/EEF1F8.png) `#EEF1F8` | Sidebar, section backgrounds |
| `bg-muted` | ![](https://placehold.co/16x16/E0E5F0/E0E5F0.png) `#E0E5F0` | Dividers, skeleton loaders |
| `text-primary` | ![](https://placehold.co/16x16/0F1623/0F1623.png) `#0F1623` | Headings, primary content |
| `text-secondary` | ![](https://placehold.co/16x16/4A5568/4A5568.png) `#4A5568` | Labels, metadata, secondary copy |
| `text-disabled` | ![](https://placehold.co/16x16/9AABBF/9AABBF.png) `#9AABBF` | Placeholder text, disabled states |
| `text-on-primary` | ![](https://placehold.co/16x16/FFFFFF/FFFFFF.png) `#FFFFFF` | Text on primary-colored elements |
| `border-default` | ![](https://placehold.co/16x16/D1D9E6/D1D9E6.png) `#D1D9E6` | Default borders |
| `border-strong` | ![](https://placehold.co/16x16/A0AEBE/A0AEBE.png) `#A0AEBE` | Emphasized borders |

### Dark Mode

| Token | Hex | Usage |
|---|---|---|
| `bg-base` | ![](https://placehold.co/16x16/0D1117/0D1117.png) `#0D1117` | App background |
| `bg-surface` | ![](https://placehold.co/16x16/161B27/161B27.png) `#161B27` | Cards, panels, modals |
| `bg-subtle` | ![](https://placehold.co/16x16/1C2333/1C2333.png) `#1C2333` | Sidebar, section backgrounds |
| `bg-muted` | ![](https://placehold.co/16x16/242D3E/242D3E.png) `#242D3E` | Dividers, skeleton loaders |
| `text-primary` | ![](https://placehold.co/16x16/E8EEFA/E8EEFA.png) `#E8EEFA` | Headings, primary content |
| `text-secondary` | ![](https://placehold.co/16x16/8B9EC4/8B9EC4.png) `#8B9EC4` | Labels, metadata, secondary copy |
| `text-disabled` | ![](https://placehold.co/16x16/4A5A78/4A5A78.png) `#4A5A78` | Placeholder text, disabled states |
| `text-on-primary` | ![](https://placehold.co/16x16/FFFFFF/FFFFFF.png) `#FFFFFF` | Text on primary-colored elements |
| `border-default` | ![](https://placehold.co/16x16/2A3550/2A3550.png) `#2A3550` | Default borders |
| `border-strong` | ![](https://placehold.co/16x16/3D4F6E/3D4F6E.png) `#3D4F6E` | Emphasized borders |

## Color Usage Principles

- **Primary blue** is reserved for actionable elements — buttons, links, active navigation states, and the wordmark. Avoid using it purely decoratively.
- **Secondary teal** is used for positive status signals: linked accounts, successful syncs, resolved items.
- **Accent amber** surfaces urgency without alarm — pipeline warnings, stale items, attention banners.
- **Backgrounds are intentionally low-contrast** between levels to keep the hierarchy subtle and reduce cognitive load during extended dashboard use.
- Ensure all text/background combinations meet **WCAG 2.1 AA** contrast minimums (4.5:1 for body text, 3:1 for large text and UI components).

## Voice & Tone

- **Direct.** No fluff. Developers read the smallest type first.
- **Calm.** The app exists to reduce chaos; the brand should feel like it already has things under control.
- **Precise.** Use exact language. Prefer "3 unassigned pull requests" over "some PRs need attention."
