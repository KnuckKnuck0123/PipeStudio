# Changelog

## 0.10.0-alpha.3 — 2026-10-12

- Added custom solid profiles from one closed planar curve and hollow profiles from two nested closed curves.
- Added intermediate taper stations along rail length for grow/shrink patterns.
- Added native Rhino geometry checks for custom loops, hollow walls, and station dimensions.
- Kept dialog actions visible on smaller screens and enabled picking already selected custom profile curves.
- Raised batch selection to 2,000 curves; large batches preview the first 200 and process all curves on creation.
- Kept the existing flat cap control. Automatic trimming and joining of different members remain future work.

## 0.9.0 — 2026-10-12

- Added architectural profile sweeps, tapered sections, lumber presets, viewport preview, and batch creation.
- Packaged separate Rhino 7 and multi-runtime Rhino 8 Yak distributions.
- Added a package icon and corrected tapered circular sweeps and solid orientation.
- Verified 169 profile assertions and native geometry in Rhino 8 on macOS Apple Silicon. Other host and UI checks are listed in [docs/TESTING.md](docs/TESTING.md).
