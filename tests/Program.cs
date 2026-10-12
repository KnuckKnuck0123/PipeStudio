using PipeStudio;

int assertions = 0;
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new Exception(message);
}
void Near(double actual, double expected, string message)
{
    Check(Math.Abs(actual - expected) < 1e-9, message + $": {actual} != {expected}");
}
double Area(List<double[]> points)
{
    double sum = 0;
    for (int i = 1; i < points.Count; i++)
        sum += points[i - 1][0] * points[i][1] - points[i][0] * points[i - 1][1];
    return sum / 2;
}

var settings = new ProfileSettings { Width = 3, Height = 6, Thickness = .25, Lip = 1, CustomOuterId = Guid.NewGuid() };
Check(ProfileCatalog.Names.Length == Enum.GetValues<ProfileKind>().Length, "UI catalog matches enum");
foreach (ProfileKind kind in Enum.GetValues<ProfileKind>())
{
    settings.Kind = kind;
    Check(settings.Validate() == null, kind + " accepts valid settings");
    if (kind == ProfileKind.Custom) continue;
    var outline = ProfileCatalog.GetOutline(settings);
    if (kind == ProfileKind.Circle)
    {
        Check(outline.Count == 0, "Circle is delegated to true circular Rhino geometry");
        continue;
    }
    Check(outline.Count >= 4, kind + " has a polygon");
    Near(outline[0][0], outline[^1][0], kind + " closes X");
    Near(outline[0][1], outline[^1][1], kind + " closes Y");
    Check(Area(outline) > 0, kind + " is counterclockwise");
    Near(outline.Min(p => p[0]) + outline.Max(p => p[0]), 0, kind + " centers X bbox");
    Near(outline.Min(p => p[1]) + outline.Max(p => p[1]), 0, kind + " centers Y bbox");
    for (int i = 1; i < outline.Count; i++)
        Check(Math.Abs(outline[i][0] - outline[i - 1][0]) + Math.Abs(outline[i][1] - outline[i - 1][1]) > 1e-9,
            kind + " has no zero-length segments");
    double expected = kind switch
    {
        ProfileKind.Triangle => 3 * Math.Sqrt(3) / 4 * 1.5 * 1.5,
        ProfileKind.Polygon => 6 * Math.Sin(2 * Math.PI / 6) / 2 * 1.5 * 1.5,
        ProfileKind.Square => 9,
        ProfileKind.Rectangle or ProfileKind.Lumber => 18,
        ProfileKind.CStud => 6 * .25 + 2 * (3 + 1 - 2 * .25) * .25,
        ProfileKind.IBeam => 2 * 3 * .25 + (6 - 2 * .25) * .25,
        ProfileKind.Angle => (3 + 6 - .25) * .25,
        _ => throw new Exception("Unhandled kind")
    };
    Near(Area(outline), expected, kind + " material area");
    settings.RotationDegrees = 37;
    Near(Area(ProfileCatalog.GetOutline(settings)), expected, kind + " adapter owns rotation");
    settings.RotationDegrees = 0;
}

settings.Kind = ProfileKind.CStud;
settings.Lip = 0;
Near(Area(ProfileCatalog.GetOutline(settings)), 6 * .25 + 2 * (3 - .25) * .25, "Unlipped channel area");
settings.Lip = .25;
Check(settings.Validate() != null, "Reject lip that degenerates at thickness");
settings.Lip = 3;
Check(settings.Validate() != null, "Reject lips that meet");
settings.Lip = 1;
settings.Thickness = 1.5;
Check(settings.Validate() != null, "Reject overlapping lipped channel web and returns");
settings.Kind = ProfileKind.IBeam;
settings.Thickness = 3;
Check(settings.Validate() != null, "Reject vanishing I-beam opening");
settings.Kind = ProfileKind.Angle;
settings.Thickness = 0;
Check(settings.Validate() != null, "Reject zero material thickness");
settings = new ProfileSettings { Kind = ProfileKind.Polygon, Sides = 2 };
Check(settings.Validate() != null, "Reject two-sided polygon");
settings.Sides = 129;
Check(settings.Validate() != null, "Reject unbounded polygon complexity");
settings.Sides = 7;
Check(settings.Validate() == null, "Accept odd polygon");
var odd = ProfileCatalog.GetOutline(settings);
Near(odd.Min(p => p[0]) + odd.Max(p => p[0]), 0, "Odd polygon centered X");
Near(odd.Min(p => p[1]) + odd.Max(p => p[1]), 0, "Odd polygon centered Y");
settings.Width = double.NaN;
Check(settings.Validate() != null, "Reject NaN width");
settings.Width = double.PositiveInfinity;
Check(settings.Validate() != null, "Reject infinite width");
settings.Width = -1;
Check(settings.Validate() != null, "Reject negative width");
settings.Width = 2;
settings.RotationDegrees = double.NegativeInfinity;
Check(settings.Validate() != null, "Reject infinite rotation");
settings.RotationDegrees = 0;
settings.StartScale = 0;
Check(settings.Validate() != null, "Reject zero start taper scale");
settings.StartScale = double.PositiveInfinity;
Check(settings.Validate() != null, "Reject infinite start taper scale");
settings.StartScale = 1;
settings.EndScale = double.NaN;
Check(settings.Validate() != null, "Reject NaN end taper scale");
settings.EndScale = -1;
Check(settings.Validate() != null, "Reject negative end taper scale");
settings.EndScale = 2;
Check(settings.Validate() == null, "Accept finite positive taper");
settings.Kind = (ProfileKind)999;
Check(settings.Validate() != null, "Reject undefined profile enum");

foreach (string size in ProfileCatalog.LumberSizes)
{
    var dimensions = ProfileCatalog.GetLumberDimensions(size);
    Check(dimensions.Length == 2 && dimensions.All(d => d > 0), "Valid preset " + size);
}
var twoByFour = ProfileCatalog.GetLumberDimensions("2x4");
Near(twoByFour[0], 1.5, "2x4 actual width");
Near(twoByFour[1], 3.5, "2x4 actual height");
twoByFour[0] = 999;
Near(ProfileCatalog.GetLumberDimensions("2x4")[0], 1.5, "Preset returns independent values");
bool unknownRejected = false;
try { ProfileCatalog.GetLumberDimensions("bogus"); } catch (ArgumentException) { unknownRejected = true; }
Check(unknownRejected, "Unknown lumber size rejected");
var original = new ProfileSettings();
var clone = original.Clone();
clone.Width = 9;
Check(original.Width == 2, "Settings clone is independent");
original.IntermediateStations.Add(new TaperStation(.25, .5));
original.IntermediateStations.Add(new TaperStation(.75, 1.5));
Check(original.Validate() == null, "Ordered intermediate taper stations accepted");
Check(original.AllStations().Count == 4, "Endpoints and intermediate stations combined");
clone = original.Clone();
clone.IntermediateStations[0].Scale = 4;
Check(original.IntermediateStations[0].Scale == .5, "Clone owns its taper stations");
original.IntermediateStations[1].Position = .25;
Check(original.Validate() != null, "Duplicate taper positions rejected");
original.IntermediateStations[1].Position = .75;
original.IntermediateStations[0].Scale = 0;
Check(original.Validate() != null, "Zero intermediate taper rejected");
original.IntermediateStations[0].Scale = .5;
original.Kind = ProfileKind.Custom;
Check(original.Validate() != null, "Custom profile requires outer curve");
original.CustomOuterId = Guid.NewGuid();
Check(original.Validate() == null, "Custom profile accepts outer curve reference");
original.CustomInnerId = original.CustomOuterId;
Check(original.Validate() != null, "Custom inner curve must be distinct");
Console.WriteLine($"Passed {assertions} profile assertions.");
