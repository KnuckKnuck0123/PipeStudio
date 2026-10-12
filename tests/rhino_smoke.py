# Compatible with Rhino 7 IronPython and Rhino 8 Python.
"""Run via RunPythonScript inside Rhino after building PipeStudio.

Uses a disposable headless document; the open model is never modified.
Checks actual Rhino geometry, end caps, taper volume, and rail preservation.
"""
import os
import math
import clr
import System
from System import Array, Object, Guid
from System.Collections.Generic import List
from System.Reflection import BindingFlags
import Rhino
import Rhino.Geometry as rg

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
framework = 'net7.0' if Rhino.RhinoApp.ExeVersion >= 8 else 'net48'
assembly_path = os.environ.get('PIPESTUDIO_ASSEMBLY', os.path.join(root, 'bin', 'Release', framework, 'PipeStudio.dll'))
assembly = System.Reflection.Assembly.LoadFrom(assembly_path)
clr.AddReference(assembly.GetName().Name)
kind_type = assembly.GetType('PipeStudio.ProfileKind')
settings_type = assembly.GetType('PipeStudio.ProfileSettings')
station_type = assembly.GetType('PipeStudio.TaperStation')


class Kinds:
    pass


ProfileKind = Kinds()
for name in ['Circle', 'Triangle', 'Square', 'Rectangle', 'Polygon', 'CStud', 'Lumber', 'IBeam', 'Angle', 'Custom']:
    setattr(ProfileKind, name, System.Enum.Parse(kind_type, name))


def ProfileSettings():
    return System.Activator.CreateInstance(settings_type)


def TaperStation(position, scale):
    station = System.Activator.CreateInstance(station_type)
    station.Position = position
    station.Scale = scale
    return station

build = assembly.GetType('PipeStudio.GeometryEngine').GetMethod('Build', BindingFlags.Static | BindingFlags.NonPublic)
checks = [0]


def check(condition, message):
    if not condition:
        raise AssertionError(message)
    checks[0] += 1


def generate(doc, rail, settings):
    rail_id = doc.Objects.AddCurve(rail)
    ids = List[Guid]()
    ids.Add(rail_id)
    batch = build.Invoke(None, Array[Object]([doc, ids, settings]))
    return rail_id, batch


doc = Rhino.RhinoDoc.CreateHeadless(None)
try:
    doc.ModelUnitSystem = Rhino.UnitSystem.Inches
    doc.ModelAbsoluteTolerance = 0.001
    straight = rg.LineCurve(rg.Point3d(0, 0, 0), rg.Point3d(0, 0, 20))
    curved = rg.ArcCurve(rg.Arc(rg.Point3d(0, 0, 0), rg.Point3d(10, 4, 0), rg.Point3d(20, 0, 0)))
    for kind in System.Enum.GetValues(kind_type):
        if kind == ProfileKind.Custom:
            continue
        for tapered in [False, True]:
            for rail in [straight, curved]:
                settings = ProfileSettings()
                settings.Kind = kind
                settings.Width = 2
                settings.Height = 4
                settings.Thickness = 0.125
                settings.Lip = 0.5
                settings.StartScale = 1
                settings.EndScale = 0.5 if tapered else 1
                rail_id, batch = generate(doc, rail, settings)
                try:
                    label = str(kind) + (' taper' if tapered else ' constant') + (' arc' if rail is curved else ' line')
                    check(batch.Errors.Count == 0, label + ': ' + '; '.join([str(x) for x in batch.Errors]))
                    check(batch.Members.Count == 1, label + ' member count')
                    for brep in batch.Members[0].Breps:
                        check(brep.IsValid and brep.IsSolid, label + ' valid capped solid')
                    check(doc.Objects.FindId(rail_id) is not None, label + ' original rail preserved')
                    if tapered and rail is straight and kind in [ProfileKind.Circle, ProfileKind.Square]:
                        volume = sum(rg.VolumeMassProperties.Compute(b).Volume for b in batch.Members[0].Breps)
                        area = math.pi if kind == ProfileKind.Circle else 4
                        expected = 20 * area * (1 + 0.5 + 0.25) / 3
                        check(abs(volume - expected) < 0.02 * expected, label + ' frustum volume: {0} vs {1}'.format(volume, expected))
                finally:
                    batch.Dispose()
    # Closed rails cannot silently introduce a taper seam.
    settings = ProfileSettings()
    settings.EndScale = 0.5
    closed = rg.Circle(rg.Plane.WorldXY, 10).ToNurbsCurve()
    rail_id, batch = generate(doc, closed, settings)
    try:
        check(batch.Members.Count == 0 and batch.Errors.Count == 1, 'closed taper rejected')
    finally:
        batch.Dispose()
    # Drawn custom profiles are copied into swept geometry at creation time.
    outer = rg.Circle(rg.Plane.WorldXY, 2).ToNurbsCurve()
    inner = rg.Circle(rg.Plane.WorldXY, 1).ToNurbsCurve()
    outer_id = doc.Objects.AddCurve(outer)
    inner_id = doc.Objects.AddCurve(inner)
    rectangular = rg.PolylineCurve([rg.Point3d(-2, -1, 0), rg.Point3d(2, -1, 0),
                                    rg.Point3d(2, 1, 0), rg.Point3d(-2, 1, 0), rg.Point3d(-2, -1, 0)])
    rectangular_id = doc.Objects.AddCurve(rectangular)
    settings = ProfileSettings()
    settings.Kind = ProfileKind.Custom
    settings.CustomOuterId = rectangular_id
    rail_id, batch = generate(doc, straight, settings)
    try:
        check(batch.Errors.Count == 0, 'custom polyline: ' + '; '.join([str(x) for x in batch.Errors]))
        check(batch.Members.Count == 1, 'custom polyline member')
    finally:
        batch.Dispose()
    for hollow in [False, True]:
        settings = ProfileSettings()
        settings.Kind = ProfileKind.Custom
        settings.CustomOuterId = outer_id
        settings.CustomInnerId = inner_id if hollow else Guid.Empty
        rail_id, batch = generate(doc, straight, settings)
        try:
            label = 'hollow' if hollow else 'solid'
            check(batch.Errors.Count == 0, 'custom ' + label + ': ' + '; '.join([str(x) for x in batch.Errors]))
            check(batch.Members.Count == 1, 'custom ' + label + ' member')
            check(all(b.IsValid and b.IsSolid for b in batch.Members[0].Breps), 'custom ' + label + ' valid solid')
            volume = sum(rg.VolumeMassProperties.Compute(b).Volume for b in batch.Members[0].Breps)
            expected = 20 * math.pi * (3 if hollow else 4)
            check(abs(volume - expected) < .03 * expected, 'custom ' + label + ' volume: {0} vs {1}'.format(volume, expected))
            check(doc.Objects.FindId(outer_id) is not None, 'custom source curve preserved')
        finally:
            batch.Dispose()
    # Curved outer and inner loops preserve a deliberately varying wall on an arc rail.
    shaped_outer = rg.Ellipse(rg.Plane.WorldXY, 3, 2).ToNurbsCurve()
    shaped_inner = rg.Ellipse(rg.Plane.WorldXY, 2, .75).ToNurbsCurve()
    shaped_outer_id = doc.Objects.AddCurve(shaped_outer)
    shaped_inner_id = doc.Objects.AddCurve(shaped_inner)
    settings = ProfileSettings()
    settings.Kind = ProfileKind.Custom
    settings.CustomOuterId = shaped_outer_id
    settings.CustomInnerId = shaped_inner_id
    settings.IntermediateStations.Add(TaperStation(.25, .5))
    settings.IntermediateStations.Add(TaperStation(.5, 1.0))
    settings.IntermediateStations.Add(TaperStation(.75, .5))
    rail_id, batch = generate(doc, curved, settings)
    try:
        check(batch.Errors.Count == 0, 'curved hollow multi-station: ' + '; '.join([str(x) for x in batch.Errors]))
        check(batch.Members.Count == 1, 'curved hollow multi-station member')
        check(all(b.IsValid and b.IsSolid for b in batch.Members[0].Breps), 'curved hollow multi-station valid solid')
    finally:
        batch.Dispose()
    # An inner loop must be coplanar and strictly nested.
    offset_inner = rg.Circle(rg.Plane(rg.Point3d(0, 0, 1), rg.Vector3d.ZAxis), 1).ToNurbsCurve()
    settings.IntermediateStations.Clear()
    settings.CustomInnerId = doc.Objects.AddCurve(offset_inner)
    rail_id, batch = generate(doc, straight, settings)
    try:
        check(batch.Members.Count == 0 and batch.Errors.Count == 1, 'noncoplanar inner profile rejected')
    finally:
        batch.Dispose()
    settings.CustomInnerId = doc.Objects.AddCurve(rg.Circle(rg.Plane.WorldXY, 4).ToNurbsCurve())
    rail_id, batch = generate(doc, straight, settings)
    try:
        check(batch.Members.Count == 0 and batch.Errors.Count == 1, 'non-nested inner profile rejected')
    finally:
        batch.Dispose()
    # Several stations can grow and shrink a member along one rail.
    for rail in [straight, curved]:
        settings = ProfileSettings()
        settings.Kind = ProfileKind.Circle
        settings.Width = 2
        settings.IntermediateStations.Add(TaperStation(.25, .5))
        settings.IntermediateStations.Add(TaperStation(.5, 1.0))
        settings.IntermediateStations.Add(TaperStation(.75, .5))
        rail_id, batch = generate(doc, rail, settings)
        try:
            label = 'multi-station ' + ('arc' if rail is curved else 'line')
            check(batch.Errors.Count == 0, label + ': ' + '; '.join([str(x) for x in batch.Errors]))
            check(batch.Members.Count == 1, label + ' member')
            check(all(b.IsValid and b.IsSolid for b in batch.Members[0].Breps), label + ' valid solid')
            if rail is straight:
                for fraction, expected_diameter in [(.25, 1), (.5, 2), (.75, 1)]:
                    plane = rg.Plane(rg.Point3d(0, 0, 20 * fraction), rg.Vector3d.ZAxis)
                    sections = []
                    for brep in batch.Members[0].Breps:
                        sections.extend(rg.Brep.CreateContourCurves(brep, plane))
                    check(len(sections) > 0, label + ' contour at ' + str(fraction))
                    actual_diameter = max(c.GetBoundingBox(True).Max.X for c in sections) - min(c.GetBoundingBox(True).Min.X for c in sections)
                    check(abs(actual_diameter - expected_diameter) < .08 * expected_diameter,
                          label + ' station {0} diameter {1} vs {2}'.format(fraction, actual_diameter, expected_diameter))
        finally:
            batch.Dispose()
    print('Pipe Studio Rhino smoke checks passed: {0}'.format(checks[0]))
finally:
    doc.Dispose()
