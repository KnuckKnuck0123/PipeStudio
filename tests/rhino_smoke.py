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
clr.AddReference('PipeStudio')
from PipeStudio import ProfileKind, ProfileSettings

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
    for kind in System.Enum.GetValues(ProfileKind):
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
    print('Pipe Studio Rhino smoke checks passed: {0}'.format(checks[0]))
finally:
    doc.Dispose()
