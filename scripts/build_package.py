#!/usr/bin/env python3
"""Build two local, cross-platform Yak distributions. Does not install or publish."""
from pathlib import Path
import argparse
import re
import subprocess
import tempfile
import zipfile
import xml.etree.ElementTree as ET


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--no-build", action="store_true", help="Package existing Release outputs")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    manifest = root / "package" / "manifest.yml"
    manifest_text = manifest.read_text()
    version_match = re.search(r"^version:\s*(\S+)\s*$", manifest_text, re.MULTILINE)
    icon_match = re.search(r"^icon:\s*(\S+)\s*$", manifest_text, re.MULTILINE)
    if not version_match or not icon_match:
        raise SystemExit("Package manifest must define version and icon")
    version, icon_name = version_match.group(1), icon_match.group(1)
    project_version = ET.parse(root / "PipeStudio.csproj").findtext(".//Version")
    if version != project_version:
        raise SystemExit(f"Manifest version {version} does not match project version {project_version}")
    icon_path = root / "package" / icon_name
    if not icon_path.is_file():
        raise SystemExit(f"Missing package icon: {icon_path}")
    if not args.no_build:
        subprocess.run(["dotnet", "build", str(root / "PipeStudio.csproj"), "-c", "Release",
                        "--disable-build-servers", "--maxcpucount:1", "/p:UseSharedCompilation=false"], check=True)
    outputs = {tfm: root / "bin" / "Release" / tfm / "PipeStudio.rhp" for tfm in ("net48", "net7.0")}
    for output in outputs.values():
        if not output.is_file():
            raise SystemExit(f"Missing compiled plugin: {output}")
    destination = root / "build" / "packages"
    destination.mkdir(parents=True, exist_ok=True)
    # Yak packages are ZIP archives. The distribution tags encode minimum Rhino
    # SDK versions and platform, matching the pinned references in the project.
    # Only our assembly is shipped; RhinoCommon, Rhino.UI and Eto come from Rhino.
    for tag, frameworks in (("rh7_0-any", ("net48",)), ("rh8_0-any", ("net48", "net7.0"))):
        artifact = destination / f"PipeStudio-{version}-{tag}.yak"
        with tempfile.NamedTemporaryFile(dir=destination, suffix=".yak", delete=False) as temporary:
            temporary_path = Path(temporary.name)
        try:
            with zipfile.ZipFile(temporary_path, "w", zipfile.ZIP_DEFLATED) as package:
                package.write(manifest, "manifest.yml")
                package.write(icon_path, icon_name)
                package.write(root / "README.md", "README.md")
                package.write(root / "LICENSE", "LICENSE")
                for framework in frameworks:
                    name = "PipeStudio.rhp" if tag.startswith("rh7") else f"{framework}/PipeStudio.rhp"
                    package.write(outputs[framework], name)
            temporary_path.replace(artifact)
        finally:
            if temporary_path.exists():
                temporary_path.unlink()
        print(artifact)


if __name__ == "__main__":
    main()
