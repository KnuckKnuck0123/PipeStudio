using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

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
                        var stations = settings.AllStations();
                        bool tapered = stations.Any(s => Math.Abs(s.Scale - stations[0].Scale) > 1e-9);
                        if (rail.IsClosed && Math.Abs(settings.StartScale - settings.EndScale) > 1e-9)
                            throw new InvalidOperationException("Closed rails need equal start and end scales.");
                        Brep[] bodies;
                        if (settings.Kind == ProfileKind.Custom || stations.Count > 2)
                        {
                            using (var custom = settings.Kind == ProfileKind.Custom ? CustomProfile.Load(doc, settings) : null)
                                bodies = SweepStations(doc, rail, settings, stations, custom);
                        }
                        else if (settings.Kind == ProfileKind.Circle)
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

        private static Brep[] SweepStations(RhinoDoc doc, Curve rail, ProfileSettings settings,
            IList<TaperStation> stations, CustomProfile custom)
        {
            var parameters = new double[stations.Count];
            for (int i = 0; i < stations.Count; ++i)
            {
                if (i == 0) parameters[i] = rail.Domain.T0;
                else if (i == stations.Count - 1) parameters[i] = rail.Domain.T1;
                else if (!rail.NormalizedLengthParameter(stations[i].Position, out parameters[i]))
                    throw new InvalidOperationException("Cannot place a taper station on this rail.");
            }
            var frames = rail.GetPerpendicularFrames(parameters);
            if (frames == null || frames.Length != stations.Count)
                throw new InvalidOperationException("Cannot find section frames for this rail.");
            var outer = new Curve[stations.Count];
            var inner = custom?.Inner == null ? null : new Curve[stations.Count];
            try
            {
                for (int i = 0; i < stations.Count; ++i)
                {
                    var frame = frames[i];
                    frame.Rotate(settings.RotationDegrees * Math.PI / 180.0, frame.ZAxis);
                    outer[i] = SectionAt(settings, custom?.Outer, custom?.Plane, frame, stations[i].Scale);
                    if (inner != null) inner[i] = SectionAt(settings, custom.Inner, custom.Plane, frame, stations[i].Scale);
                }
                var sweep = new SweepOneRail
                {
                    SweepTolerance = doc.ModelAbsoluteTolerance,
                    AngleToleranceRadians = doc.ModelAngleToleranceRadians,
                    ClosedSweep = rail.IsClosed,
                    MiterType = 1
                };
                var outside = sweep.PerformSweep(rail, outer, parameters);
                if (inner == null) return outside;
                var inside = sweep.PerformSweep(rail, inner, parameters);
                if (outside == null || outside.Length == 0 || inside == null || inside.Length == 0)
                {
                    if (outside != null) foreach (var body in outside) body.Dispose();
                    if (inside != null) foreach (var body in inside) body.Dispose();
                    throw new InvalidOperationException("Hollow profile sweep failed.");
                }
                if (!settings.CapEnds) return outside.Concat(inside).ToArray();
                Brep[] startCaps = null;
                Brep[] endCaps = null;
                try
                {
                    startCaps = Brep.CreatePlanarBreps(new Curve[] { outer[0], inner[0] }, doc.ModelAbsoluteTolerance);
                    endCaps = Brep.CreatePlanarBreps(new Curve[] { outer[outer.Length - 1], inner[inner.Length - 1] }, doc.ModelAbsoluteTolerance);
                    if (startCaps == null || startCaps.Length == 0 || endCaps == null || endCaps.Length == 0)
                        throw new InvalidOperationException("Could not form hollow end rings.");
                    var pieces = outside.Concat(inside).Concat(startCaps).Concat(endCaps).ToArray();
                    var result = Brep.JoinBreps(pieces, doc.ModelAbsoluteTolerance);
                    if (result == null || result.Length == 0 || result.Any(body => !body.IsSolid))
                    {
                        if (result != null) foreach (var body in result) body.Dispose();
                        throw new InvalidOperationException("Could not join the hollow wall. Try a simpler rail or wider wall.");
                    }
                    return result;
                }
                finally
                {
                    foreach (var body in outside) body.Dispose();
                    foreach (var body in inside) body.Dispose();
                    if (startCaps != null) foreach (var body in startCaps) body.Dispose();
                    if (endCaps != null) foreach (var body in endCaps) body.Dispose();
                }
            }
            finally
            {
                foreach (var section in outer) section?.Dispose();
                if (inner != null) foreach (var section in inner) section?.Dispose();
            }
        }

        private static Curve SectionAt(ProfileSettings settings, Curve source, Plane? sourcePlane, Plane frame, double scale)
        {
            if (source != null)
            {
                var section = source.DuplicateCurve();
                section.Transform(Transform.PlaneToPlane(sourcePlane.Value, frame));
                section.Transform(Transform.Scale(frame, scale, scale, scale));
                return section;
            }
            if (settings.Kind == ProfileKind.Circle)
                return new Circle(frame, settings.Width * scale / 2.0).ToNurbsCurve();
            var points = new List<Point3d>();
            foreach (var point in ProfileCatalog.GetOutline(settings))
                points.Add(frame.PointAt(point[0] * scale, point[1] * scale));
            return new PolylineCurve(points);
        }

        private sealed class CustomProfile : IDisposable
        {
            internal Curve Outer;
            internal Curve Inner;
            internal Plane Plane;

            internal static CustomProfile Load(RhinoDoc doc, ProfileSettings settings)
            {
                var profile = new CustomProfile();
                try
                {
                    profile.Outer = (doc.Objects.FindId(settings.CustomOuterId)?.Geometry as Curve)?.DuplicateCurve();
                    if (settings.CustomInnerId != Guid.Empty)
                        profile.Inner = (doc.Objects.FindId(settings.CustomInnerId)?.Geometry as Curve)?.DuplicateCurve();
                    if (profile.Outer == null || (settings.CustomInnerId != Guid.Empty && profile.Inner == null))
                        throw new InvalidOperationException("A custom profile curve was deleted; select it again.");
                    if (!profile.Outer.IsValid || !profile.Outer.IsClosed ||
                        !profile.Outer.TryGetPlane(out profile.Plane, doc.ModelAbsoluteTolerance))
                        throw new InvalidOperationException("Outer profile must be a valid closed planar curve.");
                    using (var self = Intersection.CurveSelf(profile.Outer, doc.ModelAbsoluteTolerance))
                        if (self != null && self.Count > 0)
                            throw new InvalidOperationException("Outer profile must not cross itself.");
                    using (var area = AreaMassProperties.Compute(profile.Outer))
                    {
                        if (area == null || area.Area <= doc.ModelAbsoluteTolerance * doc.ModelAbsoluteTolerance)
                            throw new InvalidOperationException("Outer profile has no usable area.");
                        profile.Plane.Origin = area.Centroid;
                    }
                    if (profile.Inner != null)
                    {
                        Plane innerPlane;
                        if (!profile.Inner.IsValid || !profile.Inner.IsClosed ||
                            !profile.Inner.TryGetPlane(out innerPlane, doc.ModelAbsoluteTolerance))
                            throw new InvalidOperationException("Inner profile must be a valid closed planar curve.");
                        using (var self = Intersection.CurveSelf(profile.Inner, doc.ModelAbsoluteTolerance))
                            if (self != null && self.Count > 0)
                                throw new InvalidOperationException("Inner profile must not cross itself.");
                        if (Math.Abs(profile.Plane.DistanceTo(innerPlane.Origin)) > doc.ModelAbsoluteTolerance ||
                            Math.Abs(Math.Abs(profile.Plane.Normal * innerPlane.Normal) - 1) > 1e-6)
                            throw new InvalidOperationException("Profile curves must lie in the same plane.");
                        if (Curve.PlanarClosedCurveRelationship(profile.Outer, profile.Inner, profile.Plane,
                            doc.ModelAbsoluteTolerance) != RegionContainment.BInsideA)
                            throw new InvalidOperationException("The inner curve must be strictly inside the outer curve.");
                    }
                    return profile;
                }
                catch { profile.Dispose(); throw; }
            }

            public void Dispose() { Outer?.Dispose(); Inner?.Dispose(); }
        }
    }
}
