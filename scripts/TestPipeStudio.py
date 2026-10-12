# Runs with RunPythonScript in Rhino 7 or Rhino 8; no package installation.
"""Load the compiled library and open the panel without registering a plugin.

Restart Rhino after rebuilding: .NET keeps the loaded assembly in memory.
"""
import os
import System
from System import Array, Object
from System.Reflection import BindingFlags
import Rhino
import scriptcontext as sc
from Rhino.UI import EtoExtensions, RhinoEtoApp

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
framework = 'net7.0' if System.Environment.Version.Major >= 7 else 'net48'
path = os.path.join(root, 'bin', 'Release', framework, 'PipeStudio.dll')
if not os.path.isfile(path):
    raise IOError('Build Pipe Studio first. Missing: ' + path)

assembly = System.Reflection.Assembly.LoadFrom(path)
panel_type = assembly.GetType('PipeStudio.StudioDialog', True)
panel = System.Activator.CreateInstance(
    panel_type, BindingFlags.Instance | BindingFlags.NonPublic,
    None, Array[Object]([sc.doc]), None)
try:
    EtoExtensions.ShowSemiModal(panel, sc.doc, RhinoEtoApp.MainWindowForDocument(sc.doc))
finally:
    panel_type.GetMethod('ClearPreview', BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, None)
    panel.Dispose()
