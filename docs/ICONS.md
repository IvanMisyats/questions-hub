# Icons

This document describes how icons are rendered in the QuestionsHub application.

## Overview

The application uses a centralized SVG sprite file (`wwwroot/icons.svg`) with icons referenced via `<use>` elements. This approach provides:

- **Reduced file size**: Icons are defined once and reused
- **Browser caching**: The sprite file is cached, reducing load times
- **Consistent styling**: Icons inherit text color via `fill: currentColor`
- **No external dependencies**: No icon frameworks or CDNs required

## Available Icons

The following icons are available in the sprite file:

| ID | Name Aliases | Description |
|----|--------------| ------------|
| `i-check` | check, copied | Checkmark for confirmations |
| `i-link` | link | Link/chain icon |
| `i-drag` | drag, handle, grip | Grip dots for drag handles |
| `i-search` | search | Magnifying glass |
| `i-close` | close, x | X/close button |
| `i-group` | group, users, people | Multiple people |
| `i-shield-exclamation` | shield-exclamation, shield | Shield with warning |
| `i-person-plus` | person-plus, user-plus | Person with plus sign |
| `i-person-minus` | person-minus, user-minus | Person with minus sign |
| `i-chevron-left` | chevron-left, arrow-left | Left arrow for pagination |
| `i-chevron-right` | chevron-right, arrow-right | Right arrow for pagination |
| `i-chevron-double-left` | chevron-double-left, chevrons-left | Double left arrow for first page |
| `i-chevron-double-right` | chevron-double-right, chevrons-right | Double right arrow for last page |
| `i-sun` | sun, light | Sun icon for light theme indicator |
| `i-moon` | moon, dark | Moon icon for dark theme indicator |
| `i-download` | download, export | Download / export file |
| `i-eye` | eye, show, visible | Eye icon for show/visible state |
| `i-eye-slash` | eye-slash, hide, hidden | Eye with slash for hide/hidden state |

## Usage

### Using the Icon Component (Recommended)

The preferred way to use icons is through the `Icon.razor` component:

```razor
@* Basic usage *@
<Icon Name="search" />

@* With Bootstrap classes *@
<Icon Name="group" Class="me-2 text-muted" />

@* Custom size (default is 16) *@
<Icon Name="shield-exclamation" Size="64" Class="text-danger" />

@* Accessible icon with title *@
<Icon Name="close" Title="Закрити" />
```

#### Component Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `Name` | string | Yes | - | Icon name (case-insensitive) |
| `Class` | string | No | null | Additional CSS classes (e.g., Bootstrap utilities) |
| `Size` | int | No | 16 | Width and height in pixels |
| `Title` | string | No | null | If set, adds accessible title and role="img" |

### Direct SVG Usage

Alternatively, you can use direct SVG markup:

```html
<svg class="icon text-danger me-2" width="16" height="16" aria-hidden="true" focusable="false">
  <use href="/icons.svg#i-trash"></use>
</svg>
```

## Styling

Icons use the `.icon` CSS class defined in `app.css`:

```css
.icon {
    display: inline-block;
    vertical-align: -0.125em;
    fill: currentColor;
}
```

This ensures icons:
- Align properly with text (vertical alignment)
- Inherit color from parent text (`fill: currentColor`)
- Work seamlessly with Bootstrap text utilities like `text-danger`, `text-muted`, etc.

## Adding New Icons

To add a new icon:

1. Open `wwwroot/icons.svg`
2. Add a new `<symbol>` element with a unique ID (prefix with `i-`):
   ```xml
   <symbol id="i-new-icon" viewBox="0 0 16 16">
     <path d="..."/>
   </symbol>
   ```
3. Optionally, add name aliases in `Components/Icon.razor` in the `GetSymbolId` method

## Icon Sources

The icons are based on Bootstrap Icons path data but do not require the Bootstrap Icons package. Only the raw SVG path data is used.
