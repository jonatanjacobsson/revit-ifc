# CI notes (jonatanjacobsson fork)

GitHub-hosted runners do **not** include Autodesk Revit. To compile `Revit.IFC.sln` for Revit 2026:

1. On a machine with Revit 2026 installed, zip at least:
   - `RevitAPI.dll`
   - `RevitAPIUI.dll`
   - `RevitAPIIFC.dll`
   from `C:\Program Files\Autodesk\Revit 2026` (or the bundle that matches your add-in).
2. Host the zip privately (e.g. a private GitHub Release asset URL with a token, or an Azure blob SAS URL).
3. Add repository secret **`REVIT_API_REFS_URL`** pointing at that zip.
4. Do **not** commit Autodesk binaries to git.

`Directory.Build.props` remaps HintPaths when `REVIT_API_PATH` / `API\2026` is present.

Workflow: `.github/workflows/build-revit2026.yml`  
Tag `mep-colors-*` to publish a pre-release with the patched DLLs.
