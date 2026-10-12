using System;
using System.Collections.Generic;
using System.Globalization;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using Rhino.Input.Custom;
using Rhino.UI;

namespace PipeStudio
{
    internal sealed class StudioDialog : Dialog
    {
        private readonly RhinoDoc document;
        private readonly StudioPreview preview;
        private readonly List<Guid> rails = new List<Guid>();
        private readonly DropDown kind = new DropDown { DataStore = ProfileCatalog.Names, SelectedIndex = 0 };
        private readonly DropDown lumber = new DropDown { DataStore = ProfileCatalog.LumberSizes, SelectedIndex = 9 };
        private readonly NumericStepper width = Number(2, 0.000001, 1000000);
        private readonly NumericStepper height = Number(4, 0.000001, 1000000);
        private readonly NumericStepper thickness = Number(0.125, 0.000001, 1000000);
        private readonly NumericStepper lip = Number(0.5, 0, 1000000);
        private readonly NumericStepper sides = Number(6, 3, 128, 0);
        private readonly NumericStepper rotation = Number(0, -360, 360, 1);
        private readonly NumericStepper startScale = Number(1, 0.01, 100);
        private readonly NumericStepper endScale = Number(1, 0.01, 100);
        private readonly TextBox taperStations = new TextBox { Width = 250 };
        private readonly Button selectProfile = new Button { Text = "Select profile curve(s)..." };
        private readonly Label profileLabel = new Label { Text = "No custom profile selected" };
        private readonly CheckBox cap = new CheckBox { Text = "Cap ends", Checked = true };
        private readonly CheckBox shaded = new CheckBox { Text = "Shaded preview", Checked = true };
        private readonly Label widthLabel = new Label { Text = "Diameter" };
        private readonly Label railLabel = new Label { Text = "No curves selected" };
        private readonly Label hint = new Label { Wrap = WrapMode.Word, Width = 375 };
        private readonly Label status = new Label { Wrap = WrapMode.Word, Width = 375 };
        private readonly Button create = new Button { Text = "Create members", Enabled = false };
        private readonly UITimer timer = new UITimer { Interval = 0.25 };
        private Guid customOuterId;
        private Guid customInnerId;
        private bool updating;
        private bool cleared;
        private bool closing;
        internal bool Created { get; private set; }

        private static NumericStepper Number(double value, double min, double max, int decimals = 3)
            => new NumericStepper { Value = value, MinValue = min, MaxValue = max, DecimalPlaces = decimals, Width = 125 };

        internal StudioDialog(RhinoDoc doc)
        {
            document = doc;
            preview = new StudioPreview(doc);
            Title = "Pipe Studio";
            Padding = new Padding(18);
            Resizable = false;
            var select = new Button { Text = "Select path curves..." };
            var cancel = new Button { Text = "Cancel" };
            DefaultButton = create;
            AbortButton = cancel;
            var layout = new DynamicLayout { Spacing = new Size(10, 9) };
            layout.AddRow(new Label { Text = "PIPE STUDIO", Font = new Eto.Drawing.Font(SystemFont.Bold, 16) });
            layout.AddRow(new Label { Text = "Profiles along curves  /  Rhino 7 + 8" });
            layout.AddRow(select);
            layout.AddRow(railLabel);
            layout.AddRow(new Label { Text = "Profile" }, kind);
            layout.AddRow(selectProfile);
            layout.AddRow(profileLabel);
            layout.AddRow(new Label { Text = "Lumber size (US)" }, lumber);
            layout.AddRow(widthLabel, width);
            layout.AddRow(new Label { Text = "Height / depth" }, height);
            layout.AddRow(new Label { Text = "Material thickness" }, thickness);
            layout.AddRow(new Label { Text = "Stud lip" }, lip);
            layout.AddRow(new Label { Text = "Polygon sides" }, sides);
            layout.AddRow(new Label { Text = "Rotation (degrees)" }, rotation);
            layout.AddRow(new Label { Text = "Start scale" }, startScale);
            layout.AddRow(new Label { Text = "End scale" }, endScale);
            layout.AddRow(new Label { Text = "Intermediate taper (position:scale)" }, taperStations);
            layout.AddRow(new Label { Text = "Dimensions: " + doc.ModelUnitSystem + ". Scale 1 = entered size." });
            layout.AddRow(cap, shaded);
            layout.AddRow(hint);
            layout.AddRow(status);
            layout.AddRow(create, cancel);
            Content = layout;

            select.Click += (s, e) => this.PushPickButton((sender, args) => SelectRails());
            selectProfile.Click += (s, e) => this.PushPickButton((sender, args) => SelectCustomProfile());
            create.Click += (s, e) => CreateMembers();
            cancel.Click += (s, e) => Close();
            kind.SelectedIndexChanged += (s, e) => { UpdateFields(); QueuePreview(); };
            lumber.SelectedIndexChanged += (s, e) => { ApplyLumber(); QueuePreview(); };
            foreach (var control in new[] { width, height, thickness, lip, sides, rotation, startScale, endScale })
                control.ValueChanged += (s, e) => QueuePreview();
            cap.CheckedChanged += (s, e) => QueuePreview();
            taperStations.TextChanged += (s, e) => QueuePreview();
            shaded.CheckedChanged += (s, e) => { preview.Shaded = shaded.Checked == true; document.Views.Redraw(); };
            timer.Elapsed += (s, e) => { timer.Stop(); RebuildPreview(); };
            Closing += (s, e) => { closing = true; ClearPreview(); };
            Closed += (s, e) => { RhinoDoc.CloseDocument -= OnCloseDocument; timer.Dispose(); };
            RhinoDoc.CloseDocument += OnCloseDocument;
            UpdateFields();
        }

        private ProfileKind Kind => (ProfileKind)Math.Max(0, kind.SelectedIndex);
        private ProfileSettings Settings(out string inputError)
        {
            var stations = ParseStations(taperStations.Text, out inputError);
            return new ProfileSettings
            {
                Kind = Kind, Width = width.Value, Height = height.Value,
                Thickness = thickness.Value, Lip = lip.Value, Sides = (int)sides.Value,
                RotationDegrees = rotation.Value, CapEnds = cap.Checked == true,
                StartScale = startScale.Value, EndScale = endScale.Value,
                IntermediateStations = stations,
                CustomOuterId = customOuterId, CustomInnerId = customInnerId,
                LumberSize = lumber.SelectedValue as string
            };
        }

        private static List<TaperStation> ParseStations(string input, out string error)
        {
            error = null;
            var stations = new List<TaperStation>();
            if (string.IsNullOrWhiteSpace(input)) return stations;
            foreach (var item in input.Split(','))
            {
                var pair = item.Trim().Split(':');
                if (pair.Length != 2 || !double.TryParse(pair[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double position)
                    || !double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double scale))
                {
                    error = "Enter taper stations as position:scale, separated by commas (example: 0.25:0.5, 0.5:1).";
                    return stations;
                }
                stations.Add(new TaperStation(position, scale));
            }
            return stations;
        }

        private void UpdateFields()
        {
            bool steel = Kind == ProfileKind.CStud || Kind == ProfileKind.IBeam || Kind == ProfileKind.Angle;
            bool round = Kind == ProfileKind.Circle || Kind == ProfileKind.Triangle || Kind == ProfileKind.Polygon;
            widthLabel.Text = round ? "Diameter / corner diameter" : "Width";
            height.Enabled = Kind == ProfileKind.Rectangle || steel;
            width.Enabled = Kind != ProfileKind.Lumber && Kind != ProfileKind.Custom;
            thickness.Enabled = steel;
            lip.Enabled = Kind == ProfileKind.CStud;
            sides.Enabled = Kind == ProfileKind.Polygon;
            rotation.Enabled = Kind != ProfileKind.Circle;
            lumber.Enabled = Kind == ProfileKind.Lumber;
            selectProfile.Enabled = Kind == ProfileKind.Custom;
            profileLabel.Visible = Kind == ProfileKind.Custom;
            if (Kind == ProfileKind.Lumber) ApplyLumber();
            hint.Text = Kind == ProfileKind.Custom
                ? "Select one closed planar curve for a solid section, or an outer and inner curve for a hollow section. Created members keep a fixed copy of the shape."
                : steel
                ? "Idealized sharp-corner section. Taper scales the entire section, including thickness."
                : "Profiles are centered on each rail. Taper scales the whole section. Closed rails need matching end scales.";
        }

        private void ApplyLumber()
        {
            if (Kind != ProfileKind.Lumber) return;
            double factor = Rhino.RhinoMath.UnitScale(UnitSystem.Inches, document.ModelUnitSystem);
            if (document.ModelUnitSystem == UnitSystem.None || document.ModelUnitSystem == UnitSystem.CustomUnits
                || double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0)
            {
                status.Text = "Set standard model units before choosing dimensional lumber.";
                return;
            }
            var dimensions = ProfileCatalog.GetLumberDimensions(lumber.SelectedValue as string ?? ProfileCatalog.LumberSizes[0]);
            updating = true;
            try { width.Value = dimensions[0] * factor; height.Value = dimensions[1] * factor; }
            finally { updating = false; }
        }

        private void QueuePreview()
        {
            if (updating || closing) return;
            create.Enabled = false;
            timer.Stop(); timer.Start();
        }

        private void SelectRails()
        {
            if (closing) return;
            using (var getter = new GetObject())
            {
                getter.SetCommandPrompt("Select Pipe Studio path curves (maximum 200)");
                getter.GeometryFilter = ObjectType.Curve;
                getter.SubObjectSelect = false;
                getter.EnablePreSelect(true, true);
                getter.GetMultiple(1, 0);
                if (getter.CommandResult() != Rhino.Commands.Result.Success) return;
                if (getter.ObjectCount > 200)
                {
                    status.Text = "Select no more than 200 curves per batch.";
                    return;
                }
                rails.Clear();
                for (int i = 0; i < getter.ObjectCount; ++i) rails.Add(getter.Object(i).ObjectId);
            }
            railLabel.Text = rails.Count + " path curve(s) selected";
            RebuildPreview();
        }

        private void SelectCustomProfile()
        {
            if (closing) return;
            using (var getter = new GetObject())
            {
                getter.SetCommandPrompt("Select one closed profile curve, or outer then inner closed curves");
                getter.GeometryFilter = ObjectType.Curve;
                getter.SubObjectSelect = false;
                getter.EnablePreSelect(false, true);
                getter.GetMultiple(1, 2);
                if (getter.CommandResult() != Rhino.Commands.Result.Success) return;
                customOuterId = getter.Object(0).ObjectId;
                customInnerId = getter.ObjectCount == 2 ? getter.Object(1).ObjectId : Guid.Empty;
            }
            profileLabel.Text = customInnerId == Guid.Empty ? "One profile curve selected" : "Outer and inner profile curves selected";
            RebuildPreview();
        }

        private void RebuildPreview()
        {
            if (closing || cleared) return;
            timer.Stop();
            preview.Clear();
            create.Enabled = false;
            if (Kind == ProfileKind.Lumber)
            {
                if (document.ModelUnitSystem == UnitSystem.None || document.ModelUnitSystem == UnitSystem.CustomUnits)
                {
                    status.Text = "Set standard model units before choosing dimensional lumber.";
                    return;
                }
                ApplyLumber();
            }
            var settings = Settings(out string inputError);
            if (inputError != null) { status.Text = inputError; return; }
            var error = settings.Validate();
            if (error != null) { status.Text = error; return; }
            if (rails.Count == 0) { status.Text = "Select curves to preview your members."; return; }
            try
            {
                var batch = GeometryEngine.Build(document, rails, settings);
                preview.Batch = batch;
                preview.Enabled = batch.Members.Count > 0;
                create.Enabled = batch.Members.Count > 0;
                create.Text = batch.Errors.Count == 0 ? "Create members" : "Create valid members (" + batch.Members.Count + ")";
                status.Text = batch.Members.Count + " member(s) ready."
                    + (batch.Errors.Count == 0 ? "" : " " + batch.Errors.Count + " skipped. " + batch.Errors[0]);
                document.Views.Redraw();
            }
            catch (Exception ex) { preview.Clear(); status.Text = "Preview failed: " + ex.Message; }
        }

        private void CreateMembers()
        {
            RebuildPreview(); // Re-read the rails so creation never uses stale document geometry.
            var batch = preview.Batch;
            if (batch == null || batch.Members.Count == 0) return;
            var settings = Settings(out string inputError);
            if (inputError != null) { status.Text = inputError; return; }
            uint undo = document.BeginUndoRecord("Pipe Studio");
            var ids = new List<Guid>();
            try
            {
                foreach (var member in batch.Members)
                {
                    var attributes = new ObjectAttributes { Name = "Pipe Studio - " + ProfileCatalog.Names[(int)Kind], LayerIndex = document.Layers.CurrentLayerIndex };
                    attributes.SetUserString("PipeStudio.Profile", Kind.ToString());
                    attributes.SetUserString("PipeStudio.Rail", member.RailId.ToString());
                    attributes.SetUserString("PipeStudio.StartScale", settings.StartScale.ToString(CultureInfo.InvariantCulture));
                    attributes.SetUserString("PipeStudio.EndScale", settings.EndScale.ToString(CultureInfo.InvariantCulture));
                    attributes.SetUserString("PipeStudio.TaperStations", taperStations.Text ?? "");
                    if (Kind == ProfileKind.Custom)
                    {
                        attributes.SetUserString("PipeStudio.CustomOuter", customOuterId.ToString());
                        if (customInnerId != Guid.Empty) attributes.SetUserString("PipeStudio.CustomInner", customInnerId.ToString());
                    }
                    foreach (var brep in member.Breps)
                    {
                        Guid id = document.Objects.AddBrep(brep, attributes);
                        if (id == Guid.Empty) throw new InvalidOperationException("Rhino could not add a member to the document.");
                        ids.Add(id);
                    }
                }
                if (batch.Errors.Count > 0)
                    foreach (string error in batch.Errors) RhinoApp.WriteLine("Pipe Studio skipped " + error);
                document.Objects.UnselectAll();
                foreach (var id in ids) document.Objects.Select(id);
                Created = true;
                RhinoApp.WriteLine("Pipe Studio created {0} member(s), {1} object(s); {2} rail(s) skipped.", batch.Members.Count, ids.Count, batch.Errors.Count);
            }
            catch (Exception ex)
            {
                foreach (Guid id in ids) document.Objects.Delete(id, true);
                status.Text = "Creation failed: " + ex.Message;
            }
            finally
            {
                if (undo != 0) document.EndUndoRecord(undo);
                document.Views.Redraw();
            }
            if (Created) Close();
        }

        private void OnCloseDocument(object sender, DocumentEventArgs e)
        {
            if (e.Document == document && !closing) Close();
        }

        internal void ClearPreview()
        {
            if (cleared) return;
            cleared = true;
            timer.Stop();
            RhinoDoc.CloseDocument -= OnCloseDocument;
            preview.Release();
        }
    }
}
