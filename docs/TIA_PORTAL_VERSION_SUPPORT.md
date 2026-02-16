# TIA Portal Version Compatibility

## Overview

OpenTia Toolkit supports multiple versions of TIA Portal, with varying feature support across versions. This document outlines the differences between TIA Portal V19 and V21+ APIs, particularly for block creation functionality.

**Primary Target: TIA Portal V19** - The toolkit is designed to work primarily with V19, with forward compatibility support for V21+.

## Block Creation Support

### TIA Portal V19 (Primary Target) ⚠️

**Primary development target with limited block creation support.**

The TIA Portal V19 API only provides type-specific `Create` methods:

| Block Type | Abbreviation | Supported | Method Used |
|------------|--------------|-----------|-------------|
| Function Block | FB | ✅ Yes | `CreateFB()` |
| System Function Block | SFB | ✅ Yes | `CreateFB()` |
| Function | FC | ❌ No | Not supported in V19 |
| System Function | SFC | ❌ No | Not supported in V19 |
| Data Block (Global) | DB | ❌ No | Not supported in V19 |
| Instance Data Block | - | ⚠️ Limited | `CreateInstanceDB()` (requires FB name) |
| Organization Block | OB | ❌ No | Not supported in V19 |
| User Defined Type | UDT | ❌ No | Not supported in V19 |

**API Methods Used:**
```csharp
PlcBlockComposition.CreateFB(string name, bool isAutoNumbered, int number, ProgrammingLanguage language)
PlcBlockComposition.CreateInstanceDB(string name, bool isAutoNumbered, int number, string instanceOfName)
```

### TIA Portal V21+ (Forward Compatibility) ✅

**Forward compatibility support for complete block creation functionality.**

The TIA Portal V21+ API provides a generic `Create()` method that supports all block types:

| Block Type | Abbreviation | Supported | Notes |
|------------|--------------|-----------|-------|
| Function Block | FB | ✅ Yes | Full support |
| System Function Block | SFB | ✅ Yes | Full support |
| Function | FC | ✅ Yes | Full support |
| System Function | SFC | ✅ Yes | Full support |
| Data Block (Global) | DB | ✅ Yes | Full support |
| Instance Data Block | - | ✅ Yes | Full support |
| Organization Block | OB | ✅ Yes | Full support |
| User Defined Type | UDT | ✅ Yes | Full support |

**API Method Used:**
```csharp
PlcBlockComposition.Create(string name, BlockType type)
PlcBlockComposition.Create(string name, BlockType type, string programmingLanguage)
```

### Why the Difference?

The TIA Portal Openness API evolved significantly between V19 and V21:

- **V19 API**: Uses individual `Create*` methods for each block type
- **V21+ API**: Introduces a unified generic `Create()` method with BlockType enum

The `BlockType` enum in V19 only contains 6 values:
- `Undef` - Undefined block type
- `FB` - Function block
- `SFB` - System function block
- `UDT` - User defined datatype
- `FBT` - Function block template
- `SDT` - System data type

**Missing in V19 BlockType enum:** FC, DB, OB

## Behavior by Version

### When Using TIA Portal V19 (Primary Target)

The toolkit is designed for V19 and uses type-specific methods:

```
info: Attempting TIA Portal V21+ API (for forward compatibility): Generic Create method
info: Using TIA Portal V19 API (primary): Type-specific Create methods
info: Creating FB using V19 API: CreateFB
info: FB created successfully using V19 API
```

#### For Supported Block Types (FB, SFB):
Works as expected using V19's CreateFB() method.

#### For Unsupported Block Types (FC, DB, OB):

```
info: Using TIA Portal V19 API (primary): Type-specific Create methods
error: Block type 'FC' is not supported in TIA Portal V19.
       Supported types in V19: FB, SFB, InstanceDB.
       For FC, DB (global), OB support, upgrade to TIA Portal V21 or later.
```

### When Using TIA Portal V21+ (Forward Compatibility)

The toolkit automatically detects the V21+ API and uses the generic `Create()` method:

```
info: Attempting TIA Portal V21+ API (for forward compatibility): Generic Create method
info: Using TIA Portal V21+ API (forward compatibility): Create method found
info: Block created successfully: Test_Block_DB
```

All block types (FB, FC, DB, OB, SFB, SFC, UDT) are supported when using V21+.

## Upgrade Recommendations

### For Users Currently on V19 (Primary Target)

**V19 is the primary development target.** For full block creation functionality:

1. **Continue using V19 with known limitations**
   - Only FB and SFB blocks can be created programmatically
   - FC, DB, and OB blocks must be created manually in TIA Portal
   - Instance DBs can be created if you specify the parent FB name
   - All other operations work correctly (read, export, delete, etc.)

2. **Upgrade to TIA Portal V21+ for full block creation** (optional)
   - V21 provides the complete Openness API with generic Create method
   - All block types (FB, FC, DB, OB, SFB, SFC, UDT) become available
   - Future API enhancements will be available

### For New Projects

**Start with TIA Portal V19** as the baseline, with the option to upgrade to V21+ for full block creation support.

## Example Usage

### Creating an FB Block (Works in V19 and V21+)

```bash
create-block --plc-name="MyPLC" --block-name="Motor_Control" --block-type="FB" --language="SCL"
```

**Result in V19:** ✅ Created using `CreateFB()`
**Result in V21+:** ✅ Created using generic `Create()`

### Creating an FC Block (V21+ Only)

```bash
create-block --plc-name="MyPLC" --block-name="Calculate" --block-type="FC" --language="SCL"
```

**Result in V19:** ❌ Error - not supported
**Result in V21+:** ✅ Created using generic `Create()`

### Creating a DB Block (V21+ Only)

```bash
create-block --plc-name="MyPLC" --block-name="Process_Data" --block-type="DB" --language="SCL"
```

**Result in V19:** ❌ Error - not supported
**Result in V21+:** ✅ Created using generic `Create()`

## Technical Implementation

The toolkit uses a **graceful degradation** approach:

1. **First:** Try to use V21+ generic `Create()` method
2. **Fallback:** If not found, use V19 type-specific methods
3. **Error:** If block type not supported in V19, provide clear error message

This ensures:
- ✅ Maximum compatibility across TIA Portal versions
- ✅ Clear user feedback about version limitations
- ✅ Future-proof for newer TIA Portal versions

## Assembly Loading Requirements ⚠️

### Assembly Structure by Version

**V19 (Primary Target):**
- Single monolithic assembly: `Siemens.Engineering.dll`
- Assembly path: `C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19\net48\`
- Custom `TiaPortalAssemblyResolver` handles V19 assembly loading

**V21+ (Forward Compatibility):**
- Modular assembly structure (4 DLLs):
  - Siemens.Engineering.Base.dll
  - Siemens.Engineering.Step7.dll
  - Siemens.Engineering.WinCC.dll
  - Siemens.Engineering.WinCC.Extension.dll
- Assembly path: `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48\`

### Custom Assembly Resolver

**OpenTia Toolkit uses a custom assembly resolver that supports both V19 and V21+:**

- ✅ Uses `TiaPortalAssemblyResolver` (custom implementation)
- ✅ Supports V19 monolithic assembly structure (1 DLL)
- ✅ Supports V21+ modular assembly structure (4 DLLs) for forward compatibility
- ✅ Prefers V19 assemblies first, then tries V20-V21 for forward compatibility
- ✅ Handles version detection automatically
- ✅ No external dependencies on official resolver packages

**Why a Custom Resolver?**

The custom resolver provides:
1. **V19 Primary Support**: Designed for V19 as the primary target
2. **Forward Compatibility**: Gracefully supports V20-V21+ when available
3. **Simplicity**: No dependency on official resolver NuGet packages
4. **Flexibility**: Can be customized for specific project needs

### Initialization

The custom resolver is initialized at application startup:

**CLI (Program.cs:18-21):**
```csharp
static Program()
{
    // Register custom TIA Portal assembly resolver
    TiaPortalAssemblyResolver.Register();
}
```

**MCP (Program.cs:17-20):**
```csharp
static Program()
{
    // Register custom TIA Portal assembly resolver
    TiaPortalAssemblyResolver.Register();
}
```

**Tests (TestHelpers.cs:18-20):**
```csharp
static TestHelpers()
{
    // Register TIA Portal assembly resolver BEFORE any Openness types are loaded
    TiaPortalAssemblyResolver.Register();
}
```

### Assembly Resolution Order

The resolver searches for assemblies in this order:

1. **V19 PublicAPI** (primary target):
   - `C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19\net48\`

2. **V21+ PublicAPI** (forward compatibility):
   - `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48\`
   - `C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\net48\`

3. **Infrastructure assemblies** (Bin folders):
   - `Bin\PublicAPI`
   - `Bin\PublicAPI\Client`
   - `Bin`

### Important Notes

- The custom `TiaPortalAssemblyResolver.cs` is used instead of the official resolver
- All projects reference the TIA Portal assemblies directly (not via NuGet packages)
- The custom resolver is initialized **before any Openness types are loaded**
- Assemblies are loaded from the TIA Portal installation directory
- All project files have `<Private>False</Private>` set (Copy Local = false)
- Build targets delete Siemens DLLs from output to comply with Openness API requirements

### GAC Installation

GAC installation is **optional** for V19. The custom resolver can load assemblies directly from the TIA Portal installation directory.

If you prefer GAC installation for V19:

1. **Open Developer Command Prompt for VS 2019 as Administrator**
   - Press Windows key
   - Search for "Developer Command Prompt for VS 2019"
   - Right-click and select "Run as administrator"

2. **Install V19 assembly:**
   ```cmd
   gacutil /i "C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19\net48\Siemens.Engineering.dll"
   ```

**Note:** GAC installation is not required for V19. The custom resolver handles assembly loading from the installation directory.

### Troubleshooting

If you encounter `FileLoadException` for Siemens assemblies:

1. Ensure TIA Portal V19 is installed at the standard path
2. Ensure no Siemens DLLs are in your output directory (bin/Debug/net48/)
3. Check that the custom resolver is registered before any Openness types are loaded
4. Verify the assembly path in TiaPortalAssemblyResolver.GetTiaPortalRoot()
5. Restart your application

## Additional Resources

- [TIA Portal Openness API Documentation (V21)](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/)
- [Siemens Support Portal](https://support.industry.siemens.com/)
- [TIA Portal Applications on GitHub](https://github.com/tia-portal-applications)

## Version Detection

The toolkit automatically detects which TIA Portal version is installed and adapts its behavior accordingly. No manual configuration is required.

To check your TIA Portal version:
```bash
open-tia-portal
# Check the "version" field in the output
```

## Changelog

### 2026-02-16 - V19 Migration (Primary Target) ✅
- 🎉 **Migrated back to TIA Portal V19 as primary development target**
- ✅ All project files now reference V19 monolithic assembly (Siemens.Engineering.dll)
- ✅ Updated TiaPortalAssemblyResolver to prefer V19 first, then V20-V21 for forward compatibility
- ✅ Updated test initialization to use V19 (tiaMajorVersion: 19)
- ✅ Updated test project paths to V19 location (D:\Siemens\V19\MCP_TEST\)
- ✅ Updated PlcService log messages to reflect V19 as primary target
- ✅ Updated documentation to reflect V19 as primary development target
- ✅ V19 assembly structure: 1 monolithic DLL (Siemens.Engineering.dll)
- ✅ V21+ forward compatibility maintained (tries V21+ API first, falls back to V19)
- ✅ Custom TiaPortalAssemblyResolver retained (simpler than official resolver)
- ✅ GAC installation is optional for V19 (not required)
- ✅ Supports V19 APIs: FB, SFB creation (FC, DB, OB limited/not supported)
- ✅ Forward compatibility: V21+ APIs supported when available
- ✅ Assembly resolution order: V19 (primary) → V20 → V21

### Previous Changes
- ✅ 2026-02-15: Official Resolver Migration (V21)
- ✅ 2026-02-15: Complete V21 Migration
- ✅ 2026-02-14: V21 Migration
- ✅ Added support for TIA Portal V21+ generic Create method
- ✅ Implemented V19 fallback with type-specific Create methods
- ✅ Added clear error messages for unsupported block types in V19
- ✅ Fixed BlockType enum name (was "PlcBlockType", now "BlockType")
- ✅ Fixed enum value mappings to use abbreviations (FB, FC, DB, etc.)

## Support

For issues or questions:
1. Check this documentation first
2. Verify your TIA Portal version
3. Review error logs for detailed version compatibility information
4. Report issues on GitHub with your TIA Portal version and error details
