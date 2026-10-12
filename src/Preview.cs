using System.Drawing;
using Rhino;
using Rhino.Display;

namespace PipeStudio
{
    internal sealed class StudioPreview : DisplayConduit
    {
        private readonly RhinoDoc document;
        internal GeometryBatch Batch;
        internal bool Shaded = true;
        private readonly DisplayMaterial material = new DisplayMaterial(Color.FromArgb(43, 172, 163));
        internal StudioPreview(RhinoDoc doc) { document = doc; }
        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            if (e.RhinoDoc != document || Batch == null) return;
            foreach (var member in Batch.Members)
                foreach (var brep in member.Breps) e.IncludeBoundingBox(brep.GetBoundingBox(false));
        }
        protected override void PostDrawObjects(DrawEventArgs e)
        {
            if (e.RhinoDoc != document || Batch == null) return;
            foreach (var member in Batch.Members)
                foreach (var brep in member.Breps)
                {
                    if (Shaded) e.Display.DrawBrepShaded(brep, material);
                    e.Display.DrawBrepWires(brep, Color.FromArgb(22, 114, 110), 1);
                }
        }
        internal void Clear()
        {
            Enabled = false;
            var old = Batch; Batch = null; old?.Dispose();
            document.Views.Redraw();
        }
        internal void Release()
        {
            Clear();
            material.Dispose();
        }
    }
}
