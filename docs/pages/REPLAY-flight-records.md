# Flight Records Page

## Purpose

The Flight Records page is the historical library of all drone flights.

Each row represents one immutable **Flight Record**.

Selecting a row opens that flight's **Flight Replay** page.

---

## Core Flow

**Flight Records → Flight Replay → Create Mission from Flight → Mission Template → New Flight Run → New Flight Record**

A recorded flight is never overwritten.

---

## Header

**Title:** Flight Records

Optional subtitle:
> Review, search, export, compare, and reuse historical drone flights.

### Summary Metrics

- Total Flights
- Total Flight Time
- Total Distance
- Active / Total Drones

Example:

- 248 Flights
- 63h 42m Flight Time
- 1,284 km Flown
- 12 Drones

---

## Search

Global search field:

`Search flights, drones, missions, locations...`

Searchable fields should include:

- Flight ID
- Drone ID
- Mission name
- City
- Country
- Operator

---

## Filters

Compact filters above the table:

- Date Range
- Drone
- Location
- Mission
- Flight Type
- Status
- Warnings

Quick filters:

- All Flights
- Autonomous
- Manual
- Re-flights
- With Warnings

---

## Main Table

Recommended columns:

| Column | Purpose |
|---|---|
| Select | Enables comparison and bulk export |
| Flight ID | Unique historical flight identifier |
| Date & Time | Flight start timestamp |
| Location | City, Country |
| Drone | Drone ID |
| Mission / Run | Mission name and run number |
| Type | Autonomous / Manual / Re-flight |
| Duration | Total flight duration |
| Distance | Distance flown |
| Max Altitude | Maximum recorded altitude |
| Battery | Start → End battery |
| Events | Warning / event count |
| Status | Completed / Aborted / Failed |
| More | Row actions |

Example row:

`FL-00382 | 15 Sep 2026 · 14:32 | Bengaluru, India | DRN-004 | Solar Inspection Route A · Run 3 | Autonomous | 16m 22s | 4.8 km | 87 m | 94% → 41% | ⚠ 1 | Completed`

---

## Location Data

Show **City, Country** in the table.

Do not show full latitude and longitude as permanent columns.

Coordinates should be available inside:

- Expanded row
- Flight Replay page
- Exported telemetry
- Optional hover/detail panel

Example expanded information:

- Start Coordinates: 12.9121, 77.6446
- End Coordinates: 12.9148, 77.6492
- Takeoff Time
- Landing Time

---

## Row Interaction

Clicking anywhere on a row opens:

**Flight Replay**

The three-dot menu should include:

- View Replay
- Export Flight Data
- Create Mission from Flight
- Compare Flight
- Copy Flight ID

For a re-flight, also include:

- View Source Flight

---

## Re-flight Relationships

Flights created from reusable missions should retain their relationship to the mission and source flight.

Example:

**Solar Inspection Route A**
- Run 1 → FL-00301
- Run 2 → FL-00341
- Run 3 → FL-00382

For re-flights, optionally display:

`Source: FL-00291`

This should appear in expanded details rather than as a permanent main table column.

---

## Compare Flights

Allow row selection using checkboxes.

When two flights are selected, show a contextual action bar:

`2 flights selected`

Actions:

- Compare Flights
- Export
- Clear Selection

Compare should open the existing flight comparison experience.

---

## Expandable Row

Optional expanded row content:

- Start Coordinates
- End Coordinates
- Takeoff Time
- Landing Time
- Operator
- Mission Version
- Source Flight
- Warning Summary
- Weather Summary

Use this for detailed metadata that would otherwise make the main table too wide.

---

## Statuses

Recommended status pills:

- Completed
- Aborted
- Failed

Recommended flight type labels:

- Autonomous
- Manual
- Re-flight

Keep colours subtle and operational.

---

## Sorting

Allow sorting by:

- Date & Time
- Duration
- Distance
- Max Altitude
- Battery Used
- Warning Count

Default sort:

**Newest flight first**

---

## Pagination

Example:

`1–25 of 248 flights`

Include:

- Previous
- Next
- Rows per page

---

## Export

Support exporting selected or filtered flights.

Possible formats:

- Flight Summary CSV
- Telemetry CSV
- KML
- GeoJSON
- Original Flight Log

---

## Design Direction

Use the same visual system as the existing SkyFleet Flight Replay page:

- Dark enterprise drone-operations UI
- Blue accents
- Compact spacing
- Thin borders
- High information density
- Professional aviation / fleet-management feel
- Table is the dominant element
- Avoid oversized cards
- Avoid sci-fi / neon styling
- Avoid generic sales-dashboard visuals

---

## Key Product Principle

**Flight Records are immutable history.**

The user may replay, export, compare, or create a Mission Template from a Flight Record, but the original flight data should never be edited.
