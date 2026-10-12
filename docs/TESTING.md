# Host validation

Only the Rhino 8 macOS Apple Silicon geometry smoke and local Yak installation have been run in a licensed Rhino installation. Command UI, preview, undo, and the other host rows remain pending.

## 0.10 alpha feature checks (2026-10-12)

- Both Rhino targets build; the standalone profile suite passes 178 assertions.
- In Rhino 8.35 on macOS Apple Silicon, a disposable headless document passes geometry smoke checks for one-loop custom circles and polylines, two nested circular loops with caps on and off, a curved hollow ellipse with varying wall and multiple taper stations, and the existing built-in profiles.
- Cross sections measured at 25%, 50%, and 75% of a straight rail match requested grow/shrink scales. Noncoplanar and non-nested inner loops are rejected.
- The 0.10 alpha Yak archives pass ZIP integrity checks. Rhino 8 accepted the local alpha package, but command loading and interaction after restart remain unverified; the installed-package geometry result below applies to 0.9.
- Review feedback found that the expanded dialog can exceed the screen height, hiding Create members. The settings now scroll and the action buttons remain visible. Rhino 8 has the local 0.10 alpha package installed; restart Rhino to load it. Interactive layout, preview, creation, and undo still need a user-facing check.

## 0.9 verification (2026-10-12)

- The standalone profile suite passed 169 assertions.
- Both Release targets compiled with zero errors. NU1701 warns that the pinned Rhino 8 reference uses .NET Framework assets for the .NET 7 target; NU1900 arose because NuGet vulnerability data was unavailable in this environment.
- Rhino 8.35.26251.13002 on macOS Apple Silicon passed the native geometry smoke test after correcting tapered circle sweeps and solid orientation. The same test passed against the `net7.0` assembly from a locally installed 0.9 Yak package.
- Both 0.9 Yak archives passed ZIP integrity checks and include the manifest icon.

## Initial build verification (2026-09-27)

- Both Release targets (`net48` and `net7.0`) compiled with zero errors. Two NU1701 warnings came from the baseline Rhino 8 NuGet reference assets described in the readme.
- The standalone profile suite passed 169 assertions.
- Both local Yak archives passed ZIP integrity checks. McNeel's Yak CLI also rebuilt extracted copies successfully and inferred the expected `rh7_0-any` and `rh8_0-any` distribution tags.
- No package was installed or published. Native geometry smoke checks and the host/UI matrix below remain pending.

| Host | Runtime/build | Load and command | UI and preview | Geometry / undo |
| --- | --- | --- | --- | --- |
| Rhino 7 Windows | .NET Framework / net48 | Pending | Pending | Pending |
| Rhino 7 macOS | Mono / net48 | Pending | Pending | Pending |
| Rhino 8 Windows | Default .NET / net7.0 | Pending | Pending | Pending |
| Rhino 8 Windows | Legacy .NET Framework / net48 | Pending | Pending | Pending |
| Rhino 8 macOS Intel | .NET / net7.0 | Pending | Pending | Pending |
| Rhino 8 macOS Apple Silicon | .NET / net7.0 | Local Yak installed; command pending | Pending | Installed-package geometry smoke passed; undo pending |

Record Rhino's exact version, operating system, CPU architecture, document units, tolerance, and runtime for each pass. Include early Rhino 8 and the current service release when validating the .NET 7 baseline across Rhino's later runtime updates.

## Manual checks

1. Install the matching local Yak package and restart Rhino. Confirm `PipeStudio` is recognized. Confirm the multi-target Rhino 8 package selects the runtime's correct assembly and does not ship host DLLs.
2. On one straight horizontal curve, create every profile with and without caps. Inspect the cross-section dimensions, rotation, validity, and solid/open status. For circle, width is diameter; for triangle and polygon it is the corner/circumcircle diameter.
3. Repeat on a vertical line, diagonal line, arc, smooth spatial curve, and kinked polyline. Confirm the preview matches final geometry and orientation is inspectable. Tight bends or sections larger than bend clearance may fail; errors must be shown without adding invalid members.
4. Create a closed circular rail with equal start/end scale. Try unequal scales and confirm rejection. Inspect seam continuity, twist, and geometry validity for each section family.
5. Taper a line and an arc from 1 to 0.5 and then from 0.5 to 1. Check circle diameters and polygon/steel dimensions at both ends. Confirm material thickness scales with the whole section. Change both scales to 2 and confirm uniform doubling. Confirm zero/negative scale cannot be entered.
6. Rotate asymmetrical C and angle profiles by 0, 90, and 180 degrees on horizontal and vertical rails; inspect start and end alignment. Reverse a rail and inspect the effect on taper and orientation.
7. In inches, millimeters, and feet documents, choose 2x4 lumber and confirm actual dimensions correspond to 1.5 × 3.5 inches. Repeat 1x12, 4x4, and 6x6. In unitless/custom-unit documents, confirm lumber creation is blocked with a useful message.
8. Try impossible thickness and lip values; confirm a useful validation message and disabled creation. Try a deleted rail or a rail shorter than model tolerance; confirm errors identify skipped rails. A mixed batch must report failures and add only valid members when requested.
9. Change dimensions rapidly and toggle preview shading/caps. Confirm the preview updates, no temporary objects enter the document, and cancel removes the preview. Reopen the command and confirm no stale preview remains.
10. Create several rails as a batch. Confirm original curves remain, members go to the current layer, and one Undo removes the generated batch. Confirm Redo restores it.
11. While the dialog is open, close its document and switch between documents. Confirm cleanup and preview confinement. Test window sizing, input focus, Enter, Escape, and the curve-selection button on both macOS and Windows.

## Automated checks

The profile tests exercise dimension validation, outline geometry, and lumber presets independently of Rhino. They do not test Rhino's native geometry kernel, Eto host behavior, caps, or package loading.

After loading the plugin, run `tests/rhino_smoke.py` with Rhino's `RunPythonScript` command. This separate in-host smoke check exercises native geometry for straight and curved rails across the profile families, including taper and a volume check. Record its result in the host matrix; it does not cover UI interaction or package selection.

```sh
dotnet run --project tests/PipeStudio.ProfileTests.csproj
dotnet build PipeStudio.csproj -c Release
python3 scripts/build_package.py --no-build
```
