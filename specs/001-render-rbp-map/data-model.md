# Data Model: RBP Map Board Rendering

## Entity: MapFile
- **Purpose**: Represents a user-selected map source file.
- **Fields**:
  - `FileName` (string, required)
  - `FullPath` (string, required)
  - `Extension` (string, required, allowed: `.rbp`, `.rb3`)
  - `RawContent` (string, required)
- **Validation**:
  - Extension must be supported.
  - Content must be non-empty.

## Entity: MapDefinition
- **Purpose**: Parsed map domain object used for board rendering.
- **Fields**:
  - `Name` (string, optional)
  - `Version` (string/int, optional)
  - `ScaleLeft` (number, required)
  - `ScaleTop` (number, required)
  - `ScaleWidth` (number, required)
  - `ScaleHeight` (number, required)
  - `BackgroundKey` (string, optional)
  - `Regions` (collection of Region)
  - `Cities` (collection of City)
  - `Railroads` (collection of Railroad, optional for current visual scope)
  - `TrainDots` (collection of TrainDot)
  - `MapLines` (collection of LineSegment)
  - `Separators` (collection of LineSegment)
- **Validation**:
  - Required scale bounds must exist and be positive.
  - Must contain sufficient data to draw a board (background reference or drawable geometry plus city and dot collections).

## Entity: Region
- **Purpose**: Groups cities and metadata for payout/identity.
- **Fields**:
  - `Code` (string, required)
  - `Name` (string, required)
  - `Probability` (decimal, optional)
- **Relationships**:
  - One Region has many Cities.

## Entity: City
- **Purpose**: Board city marker and label anchor.
- **Fields**:
  - `Name` (string, required)
  - `RegionCode` (string, required)
  - `Probability` (decimal, optional)
  - `PayoutIndex` (int, optional)
  - `MapDotIndex` (int, optional)
  - `X` (number, required)
  - `Y` (number, required)
- **Validation**:
  - Name required and preserved exactly for display.
  - Coordinates must be within or near board bounds (out-of-bounds flagged).

## Entity: TrainDot
- **Purpose**: Valid train piece position marker.
- **Fields**:
  - `Id` (string/int, required)
  - `X` (number, required)
  - `Y` (number, required)
  - `ColorIndex` (int, optional)
- **Validation**:
  - Coordinates required.

## Entity: LineSegment
- **Purpose**: Drawable line for map overlays/borders/separators.
- **Fields**:
  - `X1` (number, required)
  - `Y1` (number, required)
  - `X2` (number, required)
  - `Y2` (number, required)
  - `StyleIndex` (int, optional)
- **Validation**:
  - Coordinate pairs required.

## Entity: BoardViewport
- **Purpose**: Current board viewing state.
- **Fields**:
  - `ZoomPercent` (number, required; range 25–300)
  - `DefaultMode` (enum, required: `FitToBoard`)
  - `CenterX` (number, required)
  - `CenterY` (number, required)
  - `ZoomAnchor` (enum, required: `Cursor` for wheel, `ViewportCenter` for scroll bar)
- **State transitions**:
  - `Initialized(FitToBoard)` → `ZoomChanged(Wheel)`
  - `Initialized(FitToBoard)` → `ZoomChanged(ScrollBar)`
  - Any zoom change clamps value to [25, 300].

## Entity: MapLoadResult
- **Purpose**: Outcome of map load/parse request.
- **Fields**:
  - `Succeeded` (bool, required)
  - `Definition` (MapDefinition, nullable)
  - `Errors` (collection of string)
  - `Warnings` (collection of string)
- **Validation**:
  - If `Succeeded = true`, `Definition` is required.
  - If `Succeeded = false`, at least one error is required.
