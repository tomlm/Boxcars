# Quickstart: RBP Map Board Rendering

## Prerequisites
- .NET 8 SDK installed
- Valid app settings for local run (existing project setup)
- Sample map files available (for example `USAMAP.RB3`)
- Referenced background image available (for example `USABGND2.JPG`)

## Run
1. Start the app from repository root:
   - `dotnet run --project src/Boxcars/Boxcars.csproj`
2. Open the app in browser.
3. Navigate to the game board page where map loading is exposed.

## Validate P1 (Load and render complete board)
1. Load a valid `.RB3`/`.RBP` file.
2. Confirm board background appears.
3. Confirm city rectangles and labels render.
4. Confirm train-position dots render.
5. Confirm map elements remain aligned to expected positions.

## Validate P2 (Readability + zoom)
1. Use mouse wheel to zoom in/out.
2. Verify wheel zoom anchors to cursor location.
3. Use in-app zoom scrollbar/slider control.
4. Verify in-app zoom scrollbar/slider anchors to viewport center.
5. Verify zoom is clamped between 25% and 300%.
6. Reload map and confirm initial zoom resets to fit-to-board.

## Validate P3 (Error handling)
1. Load malformed file.
2. Load file with missing required render sections.
3. Confirm clear user-facing error appears.
4. Confirm no misleading partial board is displayed.

## Regression checks
- Reload a different valid map in same session and verify prior board is fully replaced.
- Repeat load on same file and verify deterministic visual output.

## Timed Validation Procedure (SC-003 and SC-006)

### Baseline validation profile
- OS: Windows 11
- CPU: 4 logical cores
- Memory: 16 GB RAM
- Browser: Chromium-based current stable release

### Measurement method
1. For SC-003, measure elapsed time from map-file selection confirmation to first visible board render completion.
2. For SC-006, measure elapsed time from zoom input event (wheel or in-app zoom scrollbar/slider change) to visible board zoom update.
3. Record timing in milliseconds and mark pass/fail per threshold.

### Sample size and thresholds
- SC-003: Run at least 20 valid map-load attempts; pass if at least 95% complete within 2000 ms.
- SC-006: Run at least 30 zoom input attempts per input type; pass if 100% complete within 200 ms.

### Positional tolerance measurement (SC-002, SC-010, SC-011)
- Use CSS pixel measurements.
- SC-002 tolerance: ±2 CSS pixels for marker placement checks.
- SC-010 and SC-011 tolerance: ±10 CSS pixels for anchor-preservation checks.
