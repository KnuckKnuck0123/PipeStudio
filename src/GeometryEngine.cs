using System;
using System.Collections.Generic;
using Rhino;
using Rhino.Geometry;

namespace PipeStudio
{
    internal sealed class GeneratedMember : IDisposable
    {
        public Guid RailId;
        public readonly List<Brep> Breps = new List<Brep>();
        public void Dispose() { foreach (var b in Breps) b.Dispose(); Breps.Clear(); }
    }

    internal sealed class GeometryBatch : IDisposable
    {
        public readonly List<GeneratedMember> Members = new List<GeneratedMember>();
        public readonly List<string> Errors = new List<string>();
        public void Dispose() { foreach (var m in Members) m.Dispose(); Members.Clear(); }
    }

    internal static class GeometryEngine
    {
        internal static GeometryBatch Build(RhinoDoc doc, IList<Guid> rails, ProfileSettings settings)
        {
            var batch = new GeometryBatch();
            var error = settings.Validate();
            if (error != null) { batch.Errors.Add(error); return batch; }
            for (int i = 0; i < rails.Count; ++i)
            {
                var member = new GeneratedMember { RailId = rails[i] };
                try
                {
                    var obj = doc.Objects.FindId(rails[i]);
                    var live = obj == null ? null : obj.Geometry as Curve;
                    if (live == null) throw new InvalidOperationException("Rail no longer exists; select curves again.");
                    using (var rail = live.DuplicateCurve())
                    {
                        if (!rail.IsValid || rail.GetLength() <= doc.ModelAbsoluteTolerance)
                            throw new InvalidOperationException("Rail is invalid or shorter than model tolerance.");
                        bool tapered = Math.Abs(settings.StartScale - settings.EndScale) > 1e-9;
                        if (rail.IsClosed && tapered)
                            throw new InvalidOperationException("Closed rails need equal start and end scales.");
                        Brep[] bodies;
                        if (settings.Kind == ProfileKind.Circle)
                        {
                            if (!tapered)
                                bodies = Brep.CreatePipe(rail, settings.Width * settings.StartScale / 2.0, false,
                                    settings.CapEnds ? PipeCapMode.Flat : PipeCapMode.None, false,
                                    doc.ModelAbsoluteTolerance, doc.ModelAngleToleranceRadians);
                            else
                            {
                                var parameters = rail.DivideByCount(32, true);
                                var frames = parameters == null ? null : rail.GetPerpendicularFrames(parameters);
                                if (frames == null || frames.Length < 2)
                                    throw new InvalidOperationException("Cannot find taper frames for this rail.");
                                using (var start = new Circle(frames[0], settings.Width * settings.StartScale / 2.0).ToNurbsCurve())
                                using (var end = new Circle(frames[frames.Length - 1], settings.Width * settings.EndScale / 2.0).ToNurbsCurve())
                                {
                                    var sweep = new SweepOneRail
                                    {
                                        SweepTolerance = doc.ModelAbsoluteTolerance,
                                        AngleToleranceRadians = doc.ModelAngleToleranceRadians,
                                        MiterType = 1
                                    };
                                    bodies = sweep.PerformSweep(rail, new Curve[] { start, end });
                                }
                            }
                        }
                        else
                        {
                            Plane plane;
                            if (!rail.PerpendicularFrameAt(rail.Domain.T0, out plane))
                                throw new InvalidOperationException("Cannot find a start frame for this rail.");
                            plane.Rotate(settings.RotationDegrees * Math.PI / 180.0, plane.ZAxis);
                            var points = new List<Point3d>();
                            foreach (var p in ProfileCatalog.GetOutline(settings)) points.Add(plane.PointAt(p[0] * settings.StartScale, p[1] * settings.StartScale));
                            using (var section = new PolylineCurve(points))
                            {
                                if (rail.IsLinear(doc.ModelAbsoluteTolerance) && !tapered)
                                {
                                    using (var surface = Surface.CreateExtrusion(section, rail.PointAtEnd - rail.PointAtStart))
                                        bodies = surface == null ? null : new[] { surface.ToBrep() };
                                }
                                else
                                {
                                    var sweep = new SweepOneRail
                                    {
                                        SweepTolerance = doc.ModelAbsoluteTolerance,
                                        AngleToleranceRadians = doc.ModelAngleToleranceRadians,
                                        ClosedSweep = rail.IsClosed,
                                        MiterType = 1
                                    };
                                    if (!tapered) bodies = sweep.PerformSweep(rail, section);
                                    else
                                    {
                                        // Use transported frames to keep the two sections consistently oriented.
                                        var parameters = rail.DivideByCount(32, true);
                                        var frames = parameters == null ? null : rail.GetPerpendicularFrames(parameters);
                                        if (frames == null || frames.Length < 2)
                                            throw new InvalidOperationException("Cannot find taper frames for this rail.");
                                        var startFrame = frames[0];
                                        var endFrame = frames[frames.Length - 1];
                                        startFrame.Rotate(settings.RotationDegrees * Math.PI / 180.0, startFrame.ZAxis);
                                        endFrame.Rotate(settings.RotationDegrees * Math.PI / 180.0, endFrame.ZAxis);
                                        var startPoints = new List<Point3d>();
                                        var endPoints = new List<Point3d>();
                                        foreach (var p in ProfileCatalog.GetOutline(settings))
                                        {
                                            startPoints.Add(startFrame.PointAt(p[0] * settings.StartScale, p[1] * settings.StartScale));
                                            endPoints.Add(endFrame.PointAt(p[0] * settings.EndScale, p[1] * settings.EndScale));
                                        }
                                        using (var start = new PolylineCurve(startPoints))
                                        using (var end = new PolylineCurve(endPoints))
                                            bodies = sweep.PerformSweep(rail, new Curve[] { start, end });
                                    }
                                }
                            }
                        }
                        if (bodies == null || bodies.Length == 0)
                            throw new InvalidOperationException("Sweep failed. Try a smaller section or a simpler rail.");
                        // Take ownership of every result before validating, so failures release them all.
                        foreach (var body in bodies) if (body != null) member.Breps.Add(body);
                        for (int j = 0; j < member.Breps.Count; ++j)
                        {
                            var body = member.Breps[j];
                            if (settings.CapEnds && !rail.IsClosed && !body.IsSolid)
                            {
                                var capped = body.CapPlanarHoles(doc.ModelAbsoluteTolerance);
                                if (capped != null) { body.Dispose(); body = capped; member.Breps[j] = body; }
                                if (!body.IsSolid) throw new InvalidOperationException("End caps failed; uncheck Cap ends or simplify the rail.");
                            }
                            if (body.IsSolid && body.SolidOrientation == BrepSolidOrientation.Inward)
                                body.Flip();
                            if (!body.IsValid) throw new InvalidOperationException("Sweep produced invalid geometry.");
                        }
                        if (member.Breps.Count == 0) throw new InvalidOperationException("No geometry was produced.");
                    }
                    batch.Members.Add(member);
                }
                catch (Exception ex)
                {
                    member.Dispose();
                    batch.Errors.Add("Rail " + (i + 1) + ": " + ex.Message);
                }
            }
            return batch;
        }
    }
}
