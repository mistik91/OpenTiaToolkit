# Changelog

All notable changes to the OpenTiaToolkit project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### 2026-02-20 - PLC Tag Management Features ✅

#### Added
- **create-tag command** - Creates new PLC tags in specified tag tables
  - Support for all standard data types (Bool, Int, DInt, Real, String, etc.)
  - Optional logical address assignment (e.g., %M10.0, %MW12, %DB1.DBD0)
  - Optional tag table selection (defaults to first/default table)
  - Auto-address assignment when logical address not specified
  - Duplicate tag detection with error reporting
  - Tag table validation and error handling
- **CreateTagResult data model** - Comprehensive result reporting for tag creation
  - Success status with tag details (name, table, type, address)
  - Warning list for non-critical issues
  - Error information with detailed messages
- **IPlcService.CreateTagAsync()** - Service method for tag creation
- **TiaReflectionHelper usage** - Simplified FindAndReplaceTagsAsync to use reflection helpers
  - Replaced direct property access with `TiaReflectionHelper.GetName()`
  - Replaced direct property assignment with `TiaReflectionHelper.SetProperty()`
  - Improved error handling with try-catch
- **Substring matching for find-replace-tags** - New substring-match parameter
  - Replace substrings within tag names instead of exact matching
  - Supports case-insensitive substring matching
  - Useful for bulk renaming operations (e.g., replace "Speed" with "RPM" in all tags)
  - Can change separators (e.g., underscores to hyphens)

#### Changed
- **FindAndReplaceTagsAsync simplified** - Uses TiaReflectionHelper for consistency
  - Removed redundant property validation code
  - Streamlined name assignment logic
- **Command count** - Increased from 29 to 30 total commands

#### Removed
- **find-replace-db-members command** - Disabled (commented out) per requirements
  - Command file retained for potential future re-enabling
  - Service method (`FindAndReplaceDbMembersAsync`) remains available
- **find-replace-udt-members command** - Disabled (commented out) per requirements
  - Command file retained for potential future re-enabling
  - Service method (`FindAndReplaceUdtMembersAsync`) remains available

#### Technical Implementation Details
- Tag creation via `Tags.Create(tagName, dataType, logicalAddress)` method
- Reflection-based method invocation for TIA Portal API compatibility
- Tag table discovery with name-based lookup
- Collision detection for duplicate tag names
- Consistent error handling pattern with try-catch

#### Use Cases for create-tag
- Batch tag creation via scripting
- Automated PLC tag generation
- Tag table population from external sources
- Standardized tag creation workflows

### 2026-02-18 - V21 Migration (Primary Target) ✅

#### Changed
- **TIA Portal V21 is now the primary development target** (migrated from V19)
- ✅ All project files now reference V21 modular assemblies (Base.dll, Step7.dll)
- ✅ Updated TiaPortalAssemblyResolver to prefer V21 first, then V20-V19 for backward compatibility
- ✅ Updated test project paths to V21 location (D:\Siemens\V21\MCP_TEST_V21\)
- ✅ Updated PlcService to use V21+ generic Create method (removed V19 fallback)
- ✅ Updated test initialization to use V21 (tiaMajorVersion: 21)

#### Removed
- **~50 lines of V19 fallback code removed from PlcService**
  - Removed `CreateFBV19()` method (no longer needed)
  - Removed `CreateGlobalDBV19()` method (no longer needed)
  - Removed V19 fallback logic in `CreateBlockAsync()`
  - Simplified to single code path using V21's generic `Create()` method

#### Added
- ✅ Documentation updated to reflect V21 as primary development target
- ✅ Full block creation support: All types (FB, FC, DB, OB, SFB, SFC, UDT)
- ✅ Assembly resolution order: V21 (primary) → V20 → V19

#### Technical Details
- **V21 Assembly Structure**: 2+ modular DLLs (Base.dll, Step7.dll, WinCC.dll, WinCC.Extension.dll)
- **V19 Assembly Structure**: 1 monolithic DLL (Siemens.Engineering.dll) - backward compatibility only
- **Custom TiaPortalAssemblyResolver retained** - Simpler than official resolver, supports both V19 and V21+
- **Single code path** - Uses V21's generic `Create()` method for all block types
- **Full API capabilities** - V21+ supports complete block creation (previously limited in V19)

#### Migration Notes
- All projects migrated from V19 assembly (1 DLL) to V21 assemblies (2+ DLLs)
- Build targets updated to delete Siemens.Engineering.Base.dll and Siemens.Engineering.Step7.dll from output
- Test infrastructure updated for V21 test project
- Documentation (TIA_PORTAL_VERSION_SUPPORT.md, README.md) updated
- V19 backward compatibility maintained (resolver falls back to V20-V19 if V21 not found)

### 2026-02-16 - V19 Migration (Primary Target) ✅

#### Changed
- **TIA Portal V19 is now the primary development target** (migrated from V21)
- ✅ All project files now reference V19 monolithic assembly (Siemens.Engineering.dll)
- ✅ Updated TiaPortalAssemblyResolver to prefer V19 first, then V20-V21 for forward compatibility
- ✅ Updated test initialization to use V19 (tiaMajorVersion: 19)
- ✅ Updated test project paths to V19 location (D:\Siemens\V19\MCP_TEST\)
- ✅ Updated PlcService log messages to reflect V19 as primary target

#### Added
- **install-v19-gac.bat** - Optional GAC installation script for V19 assemblies
- ✅ Documentation updated to reflect V19 as primary development target
- ✅ Assembly resolution order: V19 (primary) → V20 → V21

#### Technical Details
- **V19 Assembly Structure**: 1 monolithic DLL (Siemens.Engineering.dll)
- **V21+ Assembly Structure**: 4 modular DLLs (Base, Step7, WinCC, WinCC.Extension)
- **Custom TiaPortalAssemblyResolver retained** - Simpler than official resolver, supports both V19 and V21+
- **GAC installation is optional for V19** - Custom resolver loads from installation directory
- **V19 API Limitations**: FB, SFB creation supported (FC, DB, OB limited/not supported)
- **Forward compatibility maintained**: Tries V21+ API first, falls back to V19 methods

#### Migration Notes
- All projects migrated from V21 assemblies (4 DLLs) to V19 assembly (1 DLL)
- Build targets updated to delete Siemens.Engineering.dll from output (was 4 DLLs)
- Test infrastructure updated for V19 test project
- Documentation (TIA_PORTAL_VERSION_SUPPORT.md, README.md) updated

### TIA Portal V19/V21 Compatibility - Block Creation

#### Added
- **Multi-version TIA Portal support** - Toolkit now supports both V19 and V21+ APIs
- **Automatic version detection** - Detects TIA Portal version and adapts behavior accordingly
- **Graceful degradation** - Falls back to V19-specific methods when V21+ API unavailable
- **V19 helper methods**:
  - `CreateFBV19()` - Creates FB blocks using V19's CreateFB() method
  - `CreateGlobalDBV19()` - Placeholder for future global DB support in V19
  - `GetProgrammingLanguageEnum()` - Converts language string to ProgrammingLanguage enum
- **Clear error messages** - Informs users about V19 limitations and recommends V21+ upgrade
- **Version compatibility documentation** - Comprehensive docs in `docs/TIA_PORTAL_VERSION_SUPPORT.md`

#### Changed
- **BlockType enum fix** - Corrected enum name from "PlcBlockType" to "BlockType"
- **Enum value mappings** - Updated to use abbreviations (FB, FC, DB, OB) instead of full names
- **CreateBlock implementation** - Now tries V21+ generic Create() first, falls back to V19 methods
- **README prerequisites** - Added note about V21+ recommendation for full block creation support

#### Fixed
- **Wrong enum name search** - Was searching for "PlcBlockType", now correctly searches for "BlockType"
- **Wrong enum value format** - Was mapping to non-existent full names, now uses correct abbreviations
- **Missing enum values** - Documented that FC, DB, OB are not in V19's BlockType enum

#### V19 vs V21+ Support Matrix
- **V19**: FB, SFB only (FC, DB, OB not supported)
- **V21+**: All block types (FB, FC, DB, OB, SFB, SFC, UDT) fully supported

### Phase 5 - Find and Replace Operations

#### Added
- **3 Find and Replace commands** for large-scale PLC refactoring:
  - `find-replace-tags` - Rename PLC tags across all tag tables
  - `find-replace-db-members` - Rename GlobalDB members with nested struct support
  - `find-replace-udt-members` - Rename UDT members with nested struct support
- **RenameMapParser utility** - Handles multiple input formats (JSON, Dictionary, JsonElement)
  - Supports escaped quotes and outer quote removal
  - Case-insensitive dictionary for key comparisons
  - Automatic parsing of CLI-friendly JSON formats
- **TiaReflectionHelper extensions** - Reflection methods for TIA Portal API access
  - TagTableGroup, TagTables, Tags access
  - Structure, Composition, Members access for DB/UDT traversal
  - BlockGroup, TypeGroup, Groups enumeration
- **FindReplaceResult data model** - Comprehensive result reporting
  - Summary with TotalProcessed, MatchesFound, ChangesApplied, Skipped, Warnings, Errors
  - Detailed ChangeEntry list with ItemType, Path, Before, After
  - SkippedEntry list with ItemType, Path, Name, Reason
  - WarningEntry list with Code, Message, Path
  - ErrorEntry list with ItemType, Path, Name, Error, Details
- **Nested structure support** - Recursive traversal of complex data types
  - Handles UDT instances within DBs
  - Handles nested structs and arrays
  - Full hierarchical path reporting
- **Safety features**:
  - Default `dry-run=true` for preview before applying changes
  - Collision detection with warnings
  - Read-only protection checks
  - Case-insensitive matching option
- **Targeted operations**:
  - `target-blocks` parameter for specific GlobalDBs
  - `target-types` parameter for specific UDTs
- **IPlcService extensions** - 3 new async methods for find/replace operations
- **Comprehensive documentation** - CLI_USAGE.md updated with examples
- **4 new workflow examples** - Find/Replace usage patterns in documentation

#### Changed
- **Command count** - Increased from 26 to 29 total commands
- **PlcService implementation** - Added reflection-based member access and renaming
- **CLI Program.cs** - Registered 3 new Phase 5 commands
- **MCP Program.cs** - Registered 3 new Phase 5 commands

#### Technical Implementation Details
- Tag renaming via PlcSoftware.TagTableGroup.TagTables collection
- DB member renaming via PlcSoftware.BlockGroup.Blocks → Structure.Composition.Members
- UDT member renaming via PlcSoftware.TypeGroup.Types → Structure.Members
- Recursive composition traversal for nested structures
- Name property modification via reflection (setvalue)
- Collision detection before applying changes
- Dry-run mode for safe preview

#### Use Cases
- Standardizing naming conventions across large projects
- Renaming variables during project migration or refactoring
- Fixing typos in variable names that appear multiple times
- Converting between naming schemes (e.g., "Motor_Speed" to "MotorSpeed")

### Phase 4 - Advanced Block Operations

#### Added
- **Real TIA API integration** - Replaced all placeholder implementations with actual TIA Portal V19 API calls
- **IPortalService.Project property** - Exposes underlying ProjectBase for service access
- **Enhanced BlockInfo model**:
  - `IsReadOnly` property - Indicates if block is protected
  - `Comment` property - Block description/comment text
- **BlockInfoDetail model** - Extended block information with author, memory size, timestamps
- **4 new PLC commands**:
  - `delete-blocks` - Deletes blocks matching regex pattern (with confirmation safety)
  - `get-block-info` - Gets detailed information about a specific block
  - `export-block` - Exports a single block to XML
  - `search-blocks` - Searches blocks by name/comment with type filter
- **Real PlcSoftware object storage** - PlcService now stores actual PlcSoftware objects instead of strings
- **BoemaCLITool pattern implementation** - Uses SoftwareContainer.GetPlcSoftware() to access PLC software
- **Block iteration and export** - Uses plcSoftware.BlockGroup.Blocks collection
- **Export functionality** - Uses plcBlock.Export() with ExportOptions
- **Delete functionality** - Uses plcBlock.Delete() with read-only protection
- **8 new integration tests** - Test9BlockOperations.cs with comprehensive block operation tests

#### Changed
- **OpenPlcAsync** - Now uses reflection to find PlcSoftware via SoftwareContainer
- **GetBlocksAsync** - Iterates actual blocks from TIA API with property extraction
- **ExportPlcAsync** - Exports each block using TIA Export method with error handling
- **Command count** - Increased from 22 to 26 total commands
- **Test count** - Increased from 45 to 53 total integration tests

#### Technical Implementation Details
- PlcSoftware access via DeviceItem.GetService<SoftwareContainer>()
- Block enumeration via PlcSoftware.BlockGroup.Blocks
- Property extraction via reflection (Name, Type, Number, ProgrammingLanguage, IsReadOnly, Comment)
- Export via PlcBlock.Export(FileInfo, ExportOptions.WithDefaults)
- Delete via PlcBlock.Delete() with confirmation check
- Manual regex filtering in C# (V19 lacks advanced filter APIs)

#### V19 API Limitations
- No bulk/batch operations (must iterate blocks one-by-one)
- No advanced search APIs (manual C# filtering required)
- No direct source code access (must export to XML to read content)
- No template-based block creation (V20+ feature)
- No automatic dependency checking when deleting blocks

### REPL Mode - Interactive Shell Experience

#### Added
- **Interactive REPL mode** - Persistent connections across commands with shell-like interface
- **REPL-specific commands**:
  - `exit` / `quit` - Exit REPL mode
  - `clear` / `cls` - Clear screen
  - `help` / `?` - Show command list
  - `Ctrl+C` - Graceful shutdown
- **DI container persistence** - Services maintain state across REPL session
- **Quote-aware input parser** - Supports double-quoted arguments in REPL
- **Backward compatibility** - Single-shot mode behavior unchanged
- **Documentation** - Added REPL mode usage examples to README

### Phase 3 - Device & PLC Operations

#### Added
- **IPlcService interface** - Abstraction for PLC operations
- **PlcService implementation** - Service layer for PLC operations
- **10 PLC commands**:
  - `list-plcs` - Lists all PLC devices with automatic discovery (name-based, software-based, type-based detection)
  - `open-plc` - Opens PLC device software
  - `get-software-tree` - Gets PLC software structure (blocks, types, sources)
  - `compile-software` - Compiles PLC software with optional password
  - `export-plc` - Exports all PLC blocks to XML files
  - `get-blocks` - Lists blocks from PLC software with optional regex filter
  - `delete-blocks` - Deletes blocks matching regex pattern (with confirmation safety)
  - `get-block-info` - Gets detailed information about a specific block
  - `export-block` - Exports a single block to XML
  - `search-blocks` - Searches blocks by name/comment with optional type filter
- **2 Device detail commands**:
  - `get-device-info` - Gets detailed device information
  - `get-device-item-info` - Gets device item details
- **New data models**:
  - `PlcInfo` - PLC device information with confirmation status
  - `SoftwareTreeNode` - Hierarchical PLC software structure
  - `CompilationResult` - Compilation result with error/warning counts
  - `BlockInfo` - Block information (name, type, language, number)
- **24 integration tests** - Test7PlcSoftware.cs (6 tests), Test8PlcOperations.cs (10 tests), Test9BlockOperations.cs (8 tests)
- **All tests passing** - 53/53 active tests (100% success rate)
- **Automatic PLC discovery** - Finds PLCs even with non-standard naming

### Phase 2 - Portal Instance Management & Project Operations

#### Added
- **7 Portal instance management commands**:
  - `open-tia-portal` - Launches new TIA Portal instance (optional UI)
  - `close-tia-portal` - Closes TIA Portal instance
  - `list-instances` - Lists running TIA Portal instances
  - `connect-instance` - Connects to specific TIA Portal instance by process ID
- **3 Project save/tree commands**:
  - `save-project` - Saves current project
  - `save-as-project` - Saves project to new location
  - `get-project-tree` - Gets hierarchical project structure with depth limit
- **New data models**:
  - `TiaPortalInstance` - Running instance information
  - `ProjectTreeNode` - Hierarchical project structure
  - `DeviceItemInfo` - Detailed device information
- **IPortalService extensions** - Added 9 new methods for instance and save operations
- **18 integration tests** - Test4PortalInstances.cs (6 tests), Test5ProjectSave.cs (6 tests), Test6ProjectTree.cs (6 tests)

### Fixed
- Test cleanup now properly handles TIA Portal state between tests
- Added directory creation for TestOutputPath
- Updated TestOutputPath to avoid TIA Portal access issues

### Phase 1 - Foundation

#### Added
- Initial project structure
- Core abstractions (ICommand, CommandResult, CommandArgs, IPortalService)
- Portal service wrapping TIA Openness API
- 8 Phase 1 commands:
  - connect-portal
  - disconnect-portal
  - portal-status
  - open-project
  - close-project
  - list-projects
  - get-project-info
  - list-devices
- CLI application with table output formatting
- MCP server with automatic tool registration
- 13 integration tests - Test1Portal.cs (4 tests), Test2Project.cs (6 tests), Test3Devices.cs (3 tests)
- Comprehensive documentation

### Dependencies
- Microsoft.Extensions.Logging 8.0.0
- Microsoft.Extensions.DependencyInjection 8.0.0
- Spectre.Console 0.49.1
- ModelContextProtocol 0.3.0-preview.4
- Moq 4.20.70
- MSTest 3.9.2
- System.Text.Json 8.0.5
- Siemens.Collaboration.Net.* packages for TIA resolution

## [1.0.0] - TBD

### Complete Release (Phases 1-5)

#### Features
- Dual CLI and MCP interface sharing same commands
- Interactive REPL mode with persistent connections
- TIA Portal V19+ compatibility
- 29 commands across 5 categories (Portal, Project, Device, PLC, Find/Replace)
- Automatic PLC discovery with multiple detection strategies
- Advanced block operations (delete, search, detailed info, single export)
- Find and Replace operations (tags, DB members, UDT members)
- Session-only state management
- Table-formatted output for CLI
- JSON output for MCP
- Help system with command documentation
- Command argument parsing (multiple formats)
- Error handling with user-friendly messages
- 100% test coverage (53 passing integration tests)

#### Commands by Category
- Portal Instance Management (7 commands)
- Project Operations (7 commands)
- Device Operations (3 commands)
- PLC Operations (10 commands)
- Find and Replace Operations (3 commands)

#### Architecture
- Layered architecture with clear separation of concerns
- Shared core library with abstractions
- IPortalService for portal and project operations
- IPlcService for PLC operations
- CLI application with dependency injection
- MCP server with STDIO protocol support

#### Testing
- 53 integration tests covering all functionality
- Real TIA Portal V19 integration
- 9 test files organized by category
- 100% success rate

#### Documentation
- Comprehensive README
- CLI usage guide
- Developer guide
- Architecture documentation
- Complete CHANGELOG

---

## Format Summary

- `Added`: New features
- `Changed`: Changes to existing functionality
- `Deprecated`: Soon-to-be removed features
- `Removed`: Removed features
- `Fixed`: Bug fixes
- `Security`: Security vulnerability fixes
