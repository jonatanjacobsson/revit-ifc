<#
.SYNOPSIS
  Fork Autodesk/revit-ifc to jonatanjacobsson and push the MEP colors feature branch.
  Requires: gh auth login as jonatanjacobsson
#>
$ErrorActionPreference = "Stop"
$env:Path = "C:\revit-worker\temp\gh-cli\bin;" + $env:Path
$RepoDir = "C:\revit-worker\temp\revit-ifc"

gh auth status
$login = gh api user --jq .login
if ($login -ne "jonatanjacobsson") {
  Write-Warning "Logged in as '$login' (expected jonatanjacobsson). Continuing anyway."
}

$exists = $false
try {
  gh repo view jonatanjacobsson/revit-ifc --json name | Out-Null
  $exists = $true
} catch { $exists = $false }

if (-not $exists) {
  Write-Host "Creating fork jonatanjacobsson/revit-ifc ..."
  gh repo fork Autodesk/revit-ifc --clone=false --default-branch-only=false
} else {
  Write-Host "Fork already exists."
}

Set-Location $RepoDir
git remote remove origin -ErrorAction SilentlyContinue
git remote add origin "https://github.com/jonatanjacobsson/revit-ifc.git"
git fetch origin
# Ensure we push feature branch; also push a Release_26 tracking branch tip if helpful
git push -u origin feature/mep-system-type-graphic-overrides

# Open PR against fork default branch (not Autodesk upstream)
$default = gh repo view jonatanjacobsson/revit-ifc --json defaultBranchRef --jq .defaultBranchRef.name
gh pr create --repo jonatanjacobsson/revit-ifc `
  --base $default `
  --head feature/mep-system-type-graphic-overrides `
  --title "Add UseMEPSystemTypeGraphicOverrides (MEP system type LineColor/FillColor)" `
  --body @"
## Summary
- Opt-in IFC export setting ``UseMEPSystemTypeGraphicOverrides`` (UI: Additional Content).
- When on, pipes/ducts/fittings get ``IfcStyledItem`` from ``MEPSystemType.LineColor`` / ``FillColor``.
- Does **not** replace ``IfcMaterial`` (keeps semantic materials).
- Extends ``TypeObjectKey`` so fittings in different systems do not share one representation map color.
- Adds GitHub Actions workflow + CI docs for Revit 2026 builds (needs ``REVIT_API_REFS_URL`` secret).

Base: Autodesk ``IFC_v26.4.1``.

## Test plan
- [ ] Build with Revit 2026 API refs
- [ ] Install DLLs over IFC 2026.bundle
- [ ] Export M1 with setting off (unchanged) and on (system colors visible in viewer)
"@

Write-Host "Done. Repo: https://github.com/jonatanjacobsson/revit-ifc"
