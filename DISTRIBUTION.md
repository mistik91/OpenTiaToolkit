# OpenTiaToolkit Distribution Plan

## Objective
Enable easy installation and global PATH access for OpenTiaToolkit on Windows through **Chocolatey** and **Winget** package managers, with **GitHub Releases** as the distribution source.

## Target Users
Mixed technical background - some comfortable with command-line, others need automated installation.

---

## Solution Overview

Two parallel distribution channels:

1. **Chocolatey Package** (`choco install opentiatoolkit`)
2. **Winget Package** (`winget install OpenTiaToolkit`)

Both will:
- Download executables from GitHub Releases
- Add to Windows PATH automatically
- Include GAC installation script for Siemens dependencies
- Support easy updates and uninstallation

---

## Implementation Plan

### Phase 1: GitHub Release Infrastructure

**Files to Create:**

1. **`build-release.ps1`** - PowerShell script to:
   - Build Release configuration
   - Create release folder with all executables and dependencies
   - Package into a ZIP file
   - Generate checksums file
   - Output to `dist/` folder

2. **`dist/` folder structure:**
   ```
   dist/
   ├── OpenTiaToolkit-v1.0.0.zip
   ├── OpenTiaToolkit-v1.0.0.zip.sha256
   └── release/
       ├── OpenTiaToolkit.CLI.exe
       ├── OpenTiaToolkit.MCP.exe
       ├── OpenTiaToolkit.Core.dll
       ├── install-dependencies.bat  (for Siemens GAC)
       └── README.txt
   ```

3. **Update `OpenTiaToolkit.CLI.csproj` and `OpenTiaToolkit.MCP.csproj`:**
   - Add `<Version>` property if not present
   - Ensure all dependencies are copied to output

---

### Phase 2: Chocolatey Package

**Files to Create:**

1. **`chocolatey/opentiatoolkit.nuspec`** - Package metadata
2. **`chocolatey/tools/chocolateyinstall.ps1`** - Installation script
3. **`chocolatey/tools/chocolateyuninstall.ps1`** - Uninstallation script

**Workflow:**
1. Update version in `nuspec` file
2. Run `choco pack` to create `.nupkg` file
3. Push to Chocolatey community feed: `choco push opentiatoolkit.1.0.0.nupkg`
4. Or host privately on GitHub Packages

---

### Phase 3: Winget Package

**Files to Create:**

Winget uses YAML manifests. You'll need to submit to the [Winget-pkgs repository](https://github.com/microsoft/winget-pkgs).

1. **`winget/manifests/YOURNAME/OpenTiaToolkit/OpenTiaToolkit.locale.en-US.yaml`**
2. **`winget/manifests/YOURNAME/OpenTiaToolkit/OpenTiaToolkit.yaml`** (installer)
3. **`winget/manifests/YOURNAME/OpenTiaToolkit/OpenTiaToolkit.version.yaml`**

**Workflow:**
1. Create PR to microsoft/winget-pkgs repository
2. Once merged, package is available to all Windows users
3. Or use private Winget source (enterprise scenario)

---

### Phase 4: Update Documentation

Add installation instructions to `README.md`:

```markdown
## Installation

### via Chocolatey (Recommended for Windows users)
```bash
choco install opentiatoolkit
```

### via Winget
```bash
winget install YOURNAME.OpenTiaToolkit
```

### Manual Download
Download from [GitHub Releases](https://github.com/your-org/OpenTiaToolkit/releases)

## Prerequisites
- Windows 10+
- .NET Framework 4.8 (usually pre-installed)
- TIA Portal V19+ installed

## Usage
After installation, the tools are available globally:
```bash
opentiatoolkit
opentiatoolkit connect-portal
opentiatoolkit open-project --project-path="C:\Project.ap19"
```
```

---

## Critical Files to Create/Modify

### New Files to Create

1. **`build-release.ps1`** - Release build script
2. **`chocolatey/opentiatoolkit.nuspec`** - Chocolatey package metadata
3. **`chocolatey/tools/chocolateyinstall.ps1`** - Chocolatey install script
4. **`chocolatey/tools/chocolateyuninstall.ps1`** - Chocolatey uninstall script
5. **`winget/manifests/YOURNAME/OpenTiaToolkit/OpenTiaToolkit.locale.en-US.yaml`**
6. **`winget/manifests/YOURNAME/OpenTiaToolkit/OpenTiaToolkit.yaml`**
7. **`winget/manifests/YOURNAME/OpenTiaToolkit/OpenTiaToolkit.version.yaml`**

### Files to Modify

1. **`src/OpenTiaToolkit.CLI/OpenTiaToolkit.CLI.csproj`** - Add version info
2. **`src/OpenTiaToolkit.MCP/OpenTiaToolkit.MCP.csproj`** - Add version info
3. **`README.md`** - Add installation instructions

---

## Verification Steps

### Test build script:
```powershell
.\build-release.ps1
# Verify dist/ folder contains all files
```

### Test Chocolatey package locally:
```powershell
choco pack chocolatey\opentiatoolkit.nuspec
choco install opentiatoolkit -source .\opentiatoolkit.1.0.0.nupkg
opentiatoolkit  # Should work from any directory
```

### Test Winget manifest:
```powershell
winget validate winget\manifests\*
winget install --manifest PATH_TO_MANIFEST
opentiatoolkit  # Should work globally
```

### Test uninstallation:
```powershell
choco uninstall opentiatoolkit
# or
winget uninstall YOURNAME.OpenTiaToolkit
```

---

## Release Workflow

For each new version:

1. Update version numbers in all files
2. Run `.\build-release.ps1`
3. Create GitHub Release with `dist/*.zip` asset
4. Update checksums in install scripts
5. Push Chocolatey package: `choco push`
6. Submit Winget manifest PR

---

## Notes

- **Siemens DLLs:** Cannot be redistributed (proprietary). Users must have TIA Portal installed.
- **GAC Installation:** Provide optional script to install Siemens assemblies to GAC.
- **Updates:** Both Chocolatey and Winget support automatic updates.
- **Private option:** Both can be hosted on private GitHub Packages for enterprise use.

---

## Alternative: Simpler Options

If Chocolatey/Winget seem too complex, consider these simpler alternatives:

### Option A: Portable ZIP with Install Script
- Create a ZIP with executables
- Include `install.bat` that:
  - Copies files to `C:\Program Files\OpenTiaToolkit\`
  - Adds to PATH via registry or setx
  - Prompts for GAC installation

### Option B: Scoop (Simpler than Chocolatey)
- Scoop is developer-focused, simpler manifest format
- No GAC installation (just extracts to user profile)
- Less overhead than Chocolatey/Winget

---

## Resources

- [Chocolatey Documentation](https://docs.chocolatey.org/)
- [Winget Documentation](https://docs.microsoft.com/en-us/windows/package-manager/)
- [Winget Repository](https://github.com/microsoft/winget-pkgs)
- [.NET Versioning](https://docs.microsoft.com/en-us/dotnet/standard/library-guidance/versioning)
