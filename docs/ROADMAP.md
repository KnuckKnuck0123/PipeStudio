# PipeStudio ideas after 0.9

These are design notes, not promised release dates. The current command already supports multiple profile families, taper, rotation, caps, batch creation, and live preview.

## Next: make it easy to launch

**Rhino toolbar button and command icon.** Ship a small PipeStudio toolbar with a button that runs `_PipeStudio`. Give it a short tooltip and a 24–32 px mark that reads clearly in both light and dark UI. The existing 64 px Yak icon is for Package Manager; simplify its bent pipe and cyan path for the toolbar rather than shrinking the detailed artwork. Explore a right-click action only after there is a useful second command, such as “repeat last profile.”

Deliver the toolbar definition with the Yak package and test that it appears, runs the command, and survives restart/update in Rhino 7 and 8 on Mac and Windows. Rhino's RUI model stores toolbar buttons, images, and macros together; the older automatic loading guidance for a same-named `.rui` file is Windows-specific, so verify the Mac installation path instead of assuming it works there.

## Good candidates for 0.9.x

1. **Remember the last setup.** Reopen with the previous profile, dimensions, preview mode, and cap choice. Add a Reset button. Start with per-user settings; saved named presets can follow.
2. **Edit a generated member.** Store the rail link and profile settings on created objects so “Edit PipeStudio member” can rebuild one without manually recreating it. Decide how edits behave if the original rail has changed or disappeared.
3. **Anchor the section.** Choose center, top, bottom, left, or right alignment relative to the rail. This would make walls, studs, and beams easier to place against reference lines.
4. **Show orientation clearly.** Add a small start-frame and direction indicator in the preview, plus a Flip rail / Rotate 90° control. This addresses the current need to inspect taper direction and asymmetrical sections manually.
5. **Polish the panel.** Group dimensions by profile, hide inactive fields, add a compact profile diagram, and show units beside inputs. Keep the existing invalid-geometry feedback visible.

## Larger geometry ideas

- **Custom drawn profile:** select one closed planar Rhino curve as the section outline. Preserve arcs and NURBS segments rather than converting everything to a polygon. Let the user choose a profile origin and rotation so placement along the rail is predictable.
- **Hollow custom profile:** select two closed, coplanar curves, one nested inside the other. Sweep both loops to form a section with a void; the drawn inner curve defines the wall, including intentionally uneven thickness. Validate closure, planarity, containment, self-intersection, and end caps before creation. A numeric uniform wall offset from one curve could be a later convenience.
- **Multiple taper stations:** replace the current start/end scale pair with ordered stations along rail length, each with a positive scale. Support patterns such as big–small–big–small. Begin with straight interpolation between stations and visible handles or a simple station list; consider smooth interpolation after the basic geometry is reliable. For a closed rail, the first and last scales must match at the seam, even if intermediate stations vary.
- **Hollow round and rectangular tubes:** outer size and wall thickness, with open or capped ends. The current circular section is solid when capped.
- **Preset libraries:** common steel sections and custom saved profiles, with source and unit system visible. Keep manufacturer/rated properties separate from the modeled outline.
- **Joint and end treatments:** mitered ends, angle cuts, and basic trim to neighboring members. Start with predictable pairwise cases before automatic network cleanup.
- **Curved-rail frame control:** a user-picked reference plane or guide curve to reduce unwanted twist on spatial rails.
- **Batch organization:** optional layer naming, material assignment, and object names while preserving a single undo step.

## Suggested order

1. Finish the host/UI checks in [TESTING.md](TESTING.md), especially command launch, preview cleanup, and undo.
2. Add and test the toolbar button and small icon.
3. Add remembered settings and section alignment.
4. Design editable members before expanding the profile catalog; this avoids making every new feature a one-shot operation.
5. Prototype custom single-loop and nested-loop sweeps on a straight rail, then a curved rail. Add multi-station taper to the same section pipeline after those cases pass native Rhino geometry checks.

## Design decisions for custom profiles and taper

- Start with curves selected from the active Rhino document. Decide whether generated members keep a live link to those source curves or a frozen copy of their shape.
- Use distance along the rail, rather than raw curve parameter, for taper-station positions so a station at 50% lands halfway along the rail's length.
- Show each station's position and scale in the viewport preview; make add, move, edit, and remove actions undoable in the panel before creating geometry.
- Check profile loop correspondence and orientation during sweeps so the inner wall does not twist or flip when the rail curves.

## Rhino UI references

- [Rhino UI system and RUI structure](https://developer.rhino3d.com/guides/general/rhino-ui-system/)
- [Plug-in toolbar deployment guidance (Windows)](https://developer.rhino3d.com/guides/rhinocommon/create-deploy-plugin-toolbar/)
