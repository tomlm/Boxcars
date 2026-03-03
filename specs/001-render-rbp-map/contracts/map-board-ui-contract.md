# Contract: Map Board UI Loading and View State

## Purpose
Defines the user-facing contract for loading a map file and rendering board state in the existing Blazor Server + Fluent UI application.

## Surface
- **Consumer**: Browser client using game board page
- **Provider**: Boxcars server-side UI/component layer and map parsing service

## Input Contract

### Map Load Request
- **Action**: User selects local map file through UI file picker.
- **Accepted extensions**: `.rbp`, `.rb3`
- **Required payload**:
  - File name
  - File bytes/text content

### Zoom Input
- **Mouse wheel**:
  - `delta` (positive/negative zoom direction)
  - `cursorX`, `cursorY` in viewport coordinates
- **In-app zoom scrollbar/slider control**:
  - target zoom value or step increment/decrement

## Output Contract

### Successful Map Load
- `status`: `success`
- `board`:
  - `bounds`: left/top/width/height
  - `background`: resolved source path/key
  - `cities`: array of `{name, x, y, regionCode}`
  - `trainDots`: array of `{id, x, y}`
  - `lines`: optional line segments
- `viewport`:
  - `zoomPercent`: initial fit-to-board value, clamped in [25, 300]

### Failed Map Load
- `status`: `error`
- `errors`: non-empty list of user-facing messages
- `board`: absent

### Zoom Update
- `status`: `success`
- `viewport`:
  - `zoomPercent` in [25, 300]
  - `anchorMode`: `Cursor` (wheel) or `ViewportCenter` (in-app zoom scrollbar/slider)
  - `centerX`, `centerY`

## Behavioral Guarantees
- Unknown optional map sections do not fail load if required render sections are valid.
- Missing required sections return `error` and no partial board render.
- Zoom clamped to [25, 300].
- Wheel zoom anchor: cursor-centered.
- In-app zoom scrollbar/slider zoom anchor: viewport-centered.
- Map layer alignment remains consistent across zoom changes.

## Error Contract
Errors should be actionable and indicate failing condition category:
- Unsupported file type
- Parse failure with section context
- Missing required section(s)
- Missing or unreadable background asset
- Invalid coordinate data
