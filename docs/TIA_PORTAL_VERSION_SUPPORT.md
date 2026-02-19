# TIA Portal Version Compatibility

## Overview

OpenTia Toolkit is designed for **TIA Portal V21 and later** as the primary development target. This document outlines the version requirements, assembly structure, and supported features.

**Primary Target: TIA Portal V21** - The toolkit requires V21 or later for full functionality.

## Minimum Version: V21

### TIA Portal V21+ (Primary Target) ✅

**Primary development target with full block creation support.**

The TIA Portal V21+ API provides a generic `Create()` method that supports all block types:

| Block Type | Abbreviation | Supported | Method Used |
|------------|--------------|-----------|-------------|
| Function Block | FB | ✅ Yes | `Create()` |
| System Function Block | SFB | ✅ Yes | `Create()` |
| Function | FC | ✅ Yes | `Create()` |
| System Function | SFC | ✅ Yes | `Create()` |
| Data Block (Global) | DB | ✅ Yes | `Create()` |
| Instance Data Block | - | ✅ Yes | `Create()` |
| Organization Block | OB | ✅ Yes | `Create()` |
| User Defined Type | UDT | ✅ Yes | `Create()` |

**API Methods Used:**
```csharp
PlcBlockComposition.Create(string name, BlockType type)
PlcBlockComposition.Create(string name, BlockType type, string programmingLanguage)
```

## Why V21+?

The TIA Portal Openness API evolved significantly between V19 and V21:

### V19 vs V21+ Architecture Differences

| Aspect | V19 (Legacy) | V21+ (Current) |
|--------|--------------|----------------|
| **Assembly Structure** | Monolithic: 1 DLL (`Siemens.Engineering.dll`) | Modular: 4+ DLLs (`Base.dll`, `Step7.dll`) |
| **Assembly Path** | `PublicAPI\V19\` | `PublicAPI\V21\net48\` |
| **Block Creation** | Limited: Type-specific methods (`CreateFB()`) | Full: Generic `Create()` method |
| **Supported Blocks** | FB, SFB only | FB, FC, DB, OB, SFB, SFC, UDT (all) |

### V19 Limitations

The TIA Portal V19 API only provides type-specific `Create` methods:
- `CreateFB()` - Only creates FB/SFB blocks
- `CreateInstanceDB()` - Creates instance DBs (requires FB name)
- Missing: FC, SFC, DB (global), OB, UDT creation

**No Upgrade Path from V19**: V19's `BlockType` enum only contains 6 values (FB, SFB, UDT, FBT, SDT, Undef). FC, DB, and OB are not in the enum, making generic block creation impossible.

### V21+ Advantages

The TIA Portal V21+ API introduces a unified generic `Create()` method:
- Single method for all block types
- Full `BlockType` enum with all values (FB, FC, DB, OB, SFB, SFC, UDT)
- Programming language parameter support
- Cleaner, more maintainable code

## Assembly Loading Requirements

### Assembly Structure by Version

**V21+ (Primary Target):**
- Modular assembly structure (2+ DLLs):
  - `Siemens.Engineering.Base.dll` (core infrastructure)
  - `Siemens.Engineering.Step7.dll` (PLC operations)
  - Optional: `Siemens.Engineering.WinCC.dll` (HMI operations)
  - Optional: `Siemens.Engineering.WinCC.Extension.dll` (HMI extensions)
- Assembly path: `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48\`

**V19 (Backward Compatibility):**
- Single monolithic assembly: `Siemens.Engineering.dll`
- Assembly path: `C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19\`
- **Not recommended** - Limited block creation support

### Custom Assembly Resolver

**OpenTia Toolkit uses a custom assembly resolver that prioritizes V21+:**

- ✅ Uses `TiaPortalAssemblyResolver` (custom implementation)
- ✅ Supports V21+ modular assembly structure (2+ DLLs)
- ✅ Supports V19 monolithic assembly structure (1 DLL) for backward compatibility
- ✅ Prefers V21 assemblies first, then V20, V19 for backward compatibility
- ✅ Handles version detection automatically
- ✅ No external dependencies on official resolver packages

**Why a Custom Resolver?**

The custom resolver provides:
1. **V21 Primary Support**: Designed for V21 as the primary target
2. **Backward Compatibility**: Gracefully supports V20-V19 when V21 unavailable
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

1. **V21 PublicAPI** (primary target):
   - `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48\`

2. **V20 PublicAPI** (backward compatibility):
   - `C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20\net48\`

3. **V19 PublicAPI** (backward compatibility):
   - `C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19\`

4. **Infrastructure assemblies** (Bin folders):
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

## Example Usage

### Creating an FB Block

```bash
create-block --plc-name="MyPLC" --block-name="Motor_Control" --block-type="FB" --language="SCL"
```

**Result in V21+:** ✅ Created using generic `Create()`

### Creating an FC Block

```bash
create-block --plc-name="MyPLC" --block-name="Calculate" --block-type="FC" --language="SCL"
```

**Result in V21+:** ✅ Created using generic `Create()`

### Creating a DB Block

```bash
create-block --plc-name="MyPLC" --block-name="Process_Data" --block-type="DB" --language="SCL"
```

**Result in V21+:** ✅ Created using generic `Create()`

## Technical Implementation

The toolkit uses a **single code path** for V21+:

1. **Detect V21+ API**: Check for generic `Create()` method
2. **Use generic Create**: Invoke `Create(name, blockType)` or `Create(name, blockType, language)`
3. **All block types supported**: FB, FC, DB, OB, SFB, SFC, UDT

This ensures:
- ✅ Clean, maintainable code
- ✅ Full block creation support
- ✅ Consistent behavior across all V21+ versions
- ✅ Future-proof for newer TIA Portal versions

## Upgrade from V19

If you're currently using V19:

1. **Upgrade to TIA Portal V21+**
   - Full block creation support (all types)
   - Generic `Create()` method
   - Cleaner code architecture

2. **Update assembly references**
   - Replace `Siemens.Engineering.dll` with `Siemens.Engineering.Base.dll` and `Siemens.Engineering.Step7.dll`
   - Update assembly path to `V21\net48\`

3. **No code changes required**
   - The toolkit abstracts TIA Portal version differences
   - Same CLI commands and MCP tools work identically

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

### 2026-02-18 - V21 Migration (Primary Target) ✅
- 🎉 **Migrated to TIA Portal V21 as primary development target**
- ✅ All project files now reference V21 modular assemblies (Base.dll, Step7.dll)
- ✅ Updated TiaPortalAssemblyResolver to prefer V21 first, then V20-V19 for backward compatibility
- ✅ Updated test initialization to use V21
- ✅ Updated test project paths to V21 location (D:\Siemens\V21\MCP_TEST_V21\)
- ✅ Simplified PlcService: Removed V19 fallback code (~50 lines removed)
- ✅ Updated documentation to reflect V21 as primary development target
- ✅ V21 assembly structure: 2+ modular DLLs (Base.dll, Step7.dll)
- ✅ V19 backward compatibility maintained (tries V21 first, falls back to V20-V19)
- ✅ Custom TiaPortalAssemblyResolver retained (simpler than official resolver)
- ✅ Full block creation support: All types (FB, FC, DB, OB, SFB, SFC, UDT)
- ✅ Assembly resolution order: V21 (primary) → V20 → V19
- ✅ Single code path using V21's generic `Create()` method

### Previous Changes
- ✅ 2026-02-16: V19 Migration (Primary Target) - Reverted back to V19
- ✅ 2026-02-15: Official Resolver Migration (V21)
- ✅ 2026-02-15: Complete V21 Migration
- ✅ Added support for TIA Portal V21+ generic Create method
- ✅ Implemented V19 fallback with type-specific Create methods
- ✅ Fixed BlockType enum name (was "PlcBlockType", now "BlockType")
- ✅ Fixed enum value mappings to use abbreviations (FB, FC, DB, etc.)

## Support

For issues or questions:
1. Check this documentation first
2. Verify your TIA Portal version (V21+ required)
3. Review error logs for detailed version compatibility information
4. Report issues on GitHub with your TIA Portal version and error details
