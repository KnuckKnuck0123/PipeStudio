using System;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.Commands;
using Rhino.PlugIns;
using Rhino.UI;

namespace PipeStudio
{
    [Guid("74d33b47-ef41-4740-aab3-01a3e5f0cbd4")]
    public sealed class PipeStudioPlugin : PlugIn { }

    [Guid("a471f5f8-8382-4cee-8433-f045b2104df6")]
    public sealed class PipeStudioCommand : Command
    {
        public override string EnglishName => "PipeStudio";
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            if (mode == RunMode.Scripted)
            {
                RhinoApp.WriteLine("PipeStudio opens an interactive profile panel. Run it without a leading hyphen.");
                return Result.Cancel;
            }
            using (var panel = new StudioDialog(doc))
            {
                try
                {
                    panel.ShowSemiModal(doc, RhinoEtoApp.MainWindowForDocument(doc));
                    return panel.Created ? Result.Success : Result.Cancel;
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine("Pipe Studio: " + ex.Message);
                    return Result.Failure;
                }
                finally { panel.ClearPreview(); }
            }
        }
    }
}
