# CI notes (jonatanjacobsson fork)

GitHub-hosted runners do **not** include Autodesk Revit. To compile `Revit.IFC.sln` for Revit 2026:

1. On a machine with Revit 2026 installed, zip at least:
   - `RevitAPI.dll`
   - `RevitAPIUI.dll`
   - `RevitAPIIFC.dll`
   from `C:\Program Files\Autodesk\Revit 2026` (or the bundle that matches your add-in).
   For the UI project, also include `Autodesk.UI.Windows.dll`, `Autodesk.Weave.Wpf.dll`, and `UserInterfaceUtility.dll`.
2. Host the zip **privately** (this fork uses private repo
   [`jonatanjacobsson/revit-2026-api-refs`](https://github.com/jonatanjacobsson/revit-2026-api-refs)
   release `v2026.1`).
3. Add repository secrets on **`jonatanjacobsson/revit-ifc`**:
   - **`REVIT_API_REFS_URL`** — GitHub API asset URL, e.g.
     `https://api.github.com/repos/jonatanjacobsson/revit-2026-api-refs/releases/assets/<id>`
   - **`REVIT_API_REFS_TOKEN`** — PAT (or `gh` token) with `repo` scope so Actions can download the private asset
4. Do **not** commit Autodesk binaries to git.

`Directory.Build.props` remaps HintPaths when `REVIT_API_PATH` / `API\2026` is present.

Workflow: `.github/workflows/build-revit2026.yml`  
Tag `mep-colors-*` to publish a pre-release with the patched DLLs.
