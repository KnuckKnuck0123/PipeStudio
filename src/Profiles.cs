using System;
using System.Collections.Generic;

namespace PipeStudio
{
    public enum ProfileKind { Circle, Triangle, Square, Rectangle, Polygon, CStud, Lumber, IBeam, Angle }

    /// <summary>Dimensions use document units. Width is the diameter for Circle and the
    /// circumcircle diameter for Triangle/Polygon. Height is web depth for CStud/IBeam.
    /// Thickness is shared by web and flanges. Lip is the outside return length of a
    /// CStud flange (zero makes an unlipped channel). Rotation is in degrees.</summary>
    public sealed class ProfileSettings
    {
        public ProfileKind Kind { get; set; } = ProfileKind.Circle;
        public double Width { get; set; } = 2.0;
        public double Height { get; set; } = 4.0;
        public double Thickness { get; set; } = 0.125;
        public double Lip { get; set; } = 0.5;
        public int Sides { get; set; } = 6;
        public double RotationDegrees { get; set; }
        public double StartScale { get; set; } = 1.0;
        public double EndScale { get; set; } = 1.0;
        public bool CapEnds { get; set; } = true;
        public string LumberSize { get; set; } = "2x4";

        public ProfileSettings Clone() { return (ProfileSettings)MemberwiseClone(); }

        public string Validate()
        {
            if (!Enum.IsDefined(typeof(ProfileKind), Kind)) return "Choose a valid profile.";
            if (!Finite(Width) || Width <= 0) return "Width must be a finite positive number.";
            if (!Finite(Height) || Height <= 0) return "Height must be a finite positive number.";
            if (!Finite(Thickness) || Thickness < 0) return "Thickness must be a finite nonnegative number.";
            if (!Finite(Lip) || Lip < 0) return "Lip must be a finite nonnegative number.";
            if (!Finite(RotationDegrees)) return "Rotation must be a finite number.";
            if (!Finite(StartScale) || StartScale <= 0) return "Start scale must be a finite positive number.";
            if (!Finite(EndScale) || EndScale <= 0) return "End scale must be a finite positive number.";
            if (Kind == ProfileKind.Polygon && (Sides < 3 || Sides > 128)) return "Polygon sides must be between 3 and 128.";
            if (Kind == ProfileKind.CStud || Kind == ProfileKind.IBeam || Kind == ProfileKind.Angle)
            {
                if (Thickness <= 0) return "Material thickness must be greater than zero.";
                if (Thickness >= Width || Thickness >= Height) return "Thickness must be smaller than width and height.";
                if ((Kind == ProfileKind.CStud || Kind == ProfileKind.IBeam) && Thickness >= Height / 2)
                    return "Thickness must be smaller than half the height.";
            }
            if (Kind == ProfileKind.CStud && Lip != 0)
            {
                if (Lip <= Thickness) return "Lip must exceed thickness, or be zero for an unlipped channel.";
                if (Lip >= Height / 2) return "Lip must be smaller than half the height.";
                if (Thickness >= Width / 2) return "A lipped channel needs a width greater than twice its thickness.";
            }
            return null;
        }

        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
    }

    public static class ProfileCatalog
    {
        public static readonly string[] Names = { "Circle", "Triangle", "Square", "Rectangle", "Polygon", "C Stud", "Lumber", "I Beam", "Angle / L" };
        public static readonly string[] LumberSizes = { "1x2", "1x3", "1x4", "1x6", "1x8", "1x10", "1x12", "2x2", "2x3", "2x4", "2x6", "2x8", "2x10", "2x12", "4x4", "4x6", "6x6" };

        /// <summary>Common North American surfaced lumber actual sizes, in inches;
        /// these are convenient nominal presets, not a structural specification.</summary>
        public static double[] GetLumberDimensions(string size)
        {
            switch (size)
            {
                case "1x2": return new[] { 0.75, 1.5 };
                case "1x3": return new[] { 0.75, 2.5 };
                case "1x4": return new[] { 0.75, 3.5 };
                case "1x6": return new[] { 0.75, 5.5 };
                case "1x8": return new[] { 0.75, 7.25 };
                case "1x10": return new[] { 0.75, 9.25 };
                case "1x12": return new[] { 0.75, 11.25 };
                case "2x2": return new[] { 1.5, 1.5 };
                case "2x3": return new[] { 1.5, 2.5 };
                case "2x4": return new[] { 1.5, 3.5 };
                case "2x6": return new[] { 1.5, 5.5 };
                case "2x8": return new[] { 1.5, 7.25 };
                case "2x10": return new[] { 1.5, 9.25 };
                case "2x12": return new[] { 1.5, 11.25 };
                case "4x4": return new[] { 3.5, 3.5 };
                case "4x6": return new[] { 3.5, 5.5 };
                case "6x6": return new[] { 5.5, 5.5 };
                default: throw new ArgumentException("Unknown lumber preset: " + size, nameof(size));
            }
        }

        /// <summary>Returns a closed counterclockwise outline in the XY plane,
        /// centered on its bounding box, with rotation and taper scales left to the geometry adapter.
        /// Circle returns an empty list so the Rhino adapter can use a true circle.</summary>
        public static List<double[]> GetOutline(ProfileSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            string error = settings.Validate();
            if (error != null) throw new ArgumentException(error, nameof(settings));
            var points = new List<double[]>();
            double w = settings.Width, h = settings.Height, t = settings.Thickness, lip = settings.Lip;
            double left = -w / 2, right = w / 2, bottom = -h / 2, top = h / 2;
            switch (settings.Kind)
            {
                case ProfileKind.Circle: return points;
                case ProfileKind.Triangle:
                case ProfileKind.Polygon:
                    int n = settings.Kind == ProfileKind.Triangle ? 3 : settings.Sides;
                    for (int i = 0; i < n; i++)
                    {
                        double a = Math.PI / 2 + i * 2 * Math.PI / n;
                        Add(points, w / 2 * Math.Cos(a), w / 2 * Math.Sin(a));
                    }
                    Center(points);
                    break;
                case ProfileKind.Square:
                    bottom = -w / 2; top = w / 2;
                    goto case ProfileKind.Rectangle;
                case ProfileKind.Rectangle:
                case ProfileKind.Lumber:
                    Add(points, left, bottom); Add(points, right, bottom);
                    Add(points, right, top); Add(points, left, top);
                    break;
                case ProfileKind.CStud:
                    Add(points, left, bottom); Add(points, right, bottom);
                    if (lip > 0)
                    {
                        Add(points, right, bottom + lip); Add(points, right - t, bottom + lip);
                        Add(points, right - t, bottom + t);
                    }
                    else Add(points, right, bottom + t);
                    Add(points, left + t, bottom + t); Add(points, left + t, top - t);
                    if (lip > 0)
                    {
                        Add(points, right - t, top - t); Add(points, right - t, top - lip);
                        Add(points, right, top - lip);
                    }
                    else Add(points, right, top - t);
                    Add(points, right, top); Add(points, left, top);
                    break;
                case ProfileKind.IBeam:
                    Add(points, left, bottom); Add(points, right, bottom); Add(points, right, bottom + t);
                    Add(points, t / 2, bottom + t); Add(points, t / 2, top - t);
                    Add(points, right, top - t); Add(points, right, top); Add(points, left, top);
                    Add(points, left, top - t); Add(points, -t / 2, top - t);
                    Add(points, -t / 2, bottom + t); Add(points, left, bottom + t);
                    break;
                case ProfileKind.Angle:
                    Add(points, left, bottom); Add(points, right, bottom); Add(points, right, bottom + t);
                    Add(points, left + t, bottom + t); Add(points, left + t, top); Add(points, left, top);
                    break;
            }
            Add(points, points[0][0], points[0][1]);
            return points;
        }

        private static void Add(List<double[]> points, double x, double y) { points.Add(new[] { x, y }); }
        private static void Center(List<double[]> points)
        {
            double minX = double.PositiveInfinity, minY = minX, maxX = double.NegativeInfinity, maxY = maxX;
            foreach (double[] p in points)
            { minX = Math.Min(minX, p[0]); minY = Math.Min(minY, p[1]); maxX = Math.Max(maxX, p[0]); maxY = Math.Max(maxY, p[1]); }
            foreach (double[] p in points) { p[0] -= (minX + maxX) / 2; p[1] -= (minY + maxY) / 2; }
        }
    }
}
