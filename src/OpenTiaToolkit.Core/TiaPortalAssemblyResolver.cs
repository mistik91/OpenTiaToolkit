using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace OpenTiaToolkit.Core
{
    /// <summary>
    /// TIA Portal Openness assembly resolver.
    /// MUST be loaded before any TIA Portal Openness types are used.
    /// This class contains NO Openness type references to avoid circular dependencies.
    /// </summary>
    public static class TiaPortalAssemblyResolver
    {
        private static string? _opennessFolder;
        private static bool _isRegistered = false;

        /// <summary>
        /// Registers the TIA Portal assembly resolver with the current AppDomain.
        /// This should be called once at application startup, before any Openness types are loaded.
        /// </summary>
        public static void Register()
        {
            if (_isRegistered)
                return;

            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            _isRegistered = true;
        }

        /// <summary>
        /// Assembly resolver for TIA Portal Openness assemblies.
        /// Looks up assemblies from the TIA Portal installation directory.
        /// Searches both PublicAPI and Bin directories for infrastructure assemblies.
        /// </summary>
        private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            try
            {
                var requestedAssemblyName = new AssemblyName(args.Name);

                // Only respond to requests for TIA Portal Openness assemblies
                if (!requestedAssemblyName.Name.StartsWith("Siemens.Engineering.", StringComparison.OrdinalIgnoreCase) &&
                    !requestedAssemblyName.Name.Equals("Siemens.Engineering", StringComparison.OrdinalIgnoreCase))
                    return null;

                // Debug output
                System.Diagnostics.Debug.WriteLine($"[TiaPortalAssemblyResolver] Resolving: {args.Name}");

                // Get TIA Portal root directory (first time only)
                if (_opennessFolder == null)
                {
                    _opennessFolder = GetTiaPortalRoot();
                    if (_opennessFolder == null)
                    {
                        System.Diagnostics.Debug.WriteLine("[TiaPortalAssemblyResolver] GetTiaPortalRoot returned null!");
                        return null;
                    }
                    System.Diagnostics.Debug.WriteLine($"[TiaPortalAssemblyResolver] TIA Portal Root: {_opennessFolder}");
                }

                // Try multiple locations where assemblies might be
                string[] searchPaths = new string[]
                {
                    // PublicAPI assemblies (main Openness API) - V21 first (primary)
                    // Note: V19 doesn't have a net48 subfolder, V21+ does
                    Path.Combine(_opennessFolder, "PublicAPI", "V21", "net48"),
                    Path.Combine(_opennessFolder, "PublicAPI", "V20", "net48"),
                    // Fallback to V19 for backward compatibility
                    Path.Combine(_opennessFolder, "PublicAPI", "V19"),
                    // Infrastructure assemblies in Bin\PublicAPI
                    Path.Combine(_opennessFolder, "Bin", "PublicAPI"),
                    Path.Combine(_opennessFolder, "Bin", "PublicAPI", "Client"),
                    // Additional infrastructure assemblies in Bin
                    _opennessFolder + "\\Bin",
                };

                foreach (string searchPath in searchPaths)
                {
                    string filePath = Path.Combine(searchPath, requestedAssemblyName.Name + ".dll");
                    System.Diagnostics.Debug.WriteLine($"[TiaPortalAssemblyResolver] Trying: {filePath}");

                    if (File.Exists(filePath))
                    {
                        System.Diagnostics.Debug.WriteLine($"[TiaPortalAssemblyResolver] File exists, loading...");
                        Assembly loadedAssembly = Assembly.LoadFrom(filePath);

                        // Check if version matches (important!)
                        Version? requestedVersion = requestedAssemblyName.Version;
                        Version? loadedVersion = loadedAssembly.GetName().Version;

                        if (requestedVersion != null && loadedVersion != null && requestedVersion != loadedVersion)
                        {
                            // Version mismatch - try next location
                            System.Diagnostics.Debug.WriteLine($"[TiaPortalAssemblyResolver] Version mismatch: requested {requestedVersion}, loaded {loadedVersion}");
                            continue;
                        }

                        System.Diagnostics.Debug.WriteLine($"[TiaPortalAssemblyResolver] SUCCESS: Loaded {loadedAssembly.FullName}");
                        return loadedAssembly;
                    }
                }

                // Not found in any location
                System.Diagnostics.Debug.WriteLine("[TiaPortalAssemblyResolver] Assembly not found in any location!");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TiaPortalAssemblyResolver] Exception: {ex.Message}");
                // If resolution fails, return null to let .NET continue searching
                return null;
            }
        }

        /// <summary>
        /// Gets the TIA Portal root directory from multiple sources.
        /// Returns the path to the TIA Portal installation (e.g., "C:\Program Files\Siemens\Automation\Portal V21")
        /// V21 is tried first as the primary target version.
        /// </summary>
        private static string? GetTiaPortalRoot()
        {
            // DIRECT PATH TO V21 (primary target)
            // This ensures the resolver always works even if registry keys are missing
            string v21Direct = @"C:\Program Files\Siemens\Automation\Portal V21";
            if (Directory.Exists(v21Direct))
            {
                System.Diagnostics.Debug.WriteLine($"[TiaPortalAssemblyResolver] Found V21 at: {v21Direct}");
                return v21Direct;
            }

            // Try V21 via registry first (primary target version)
            string? path = TryGetTiaPortalRoot("V21");
            if (path != null)
            {
                System.Diagnostics.Debug.WriteLine($"[TiaPortalAssemblyResolver] Found V21 in registry: {path}");
                return path;
            }

            // Try V20-V19 for backward compatibility (descending order)
            for (int v = 20; v >= 19; v--)
            {
                path = TryGetTiaPortalRoot($"V{v}");
                if (path != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[TiaPortalAssemblyResolver] Found V{v} in registry: {path}");
                    return path;
                }
            }

            // Fallback: Try common installation paths for V21 (primary)
            string[] commonPaths = new[]
            {
                @"C:\Program Files\Siemens\Automation\Portal V21",
                @"C:\Program Files (x86)\Siemens\Automation\Portal V21",
                @"C:\Program Files\Siemens\Automation\Portal V19",
                @"C:\Program Files (x86)\Siemens\Automation\Portal V19",
            };

            foreach (string testPath in commonPaths)
            {
                if (Directory.Exists(testPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[TiaPortalAssemblyResolver] Found via common path: {testPath}");
                    return testPath;
                }
            }

            System.Diagnostics.Debug.WriteLine("[TiaPortalAssemblyResolver] No TIA Portal installation found!");
            return null;
        }

        /// <summary>
        /// Attempts to get the TIA Portal root path for a specific version.
        /// </summary>
        private static string? TryGetTiaPortalRoot(string version)
        {
            try
            {
                // Registry path: HKEY_LOCAL_MACHINE\SOFTWARE\Siemens\Automation\Shared Settings\General\(Version)
                using (RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Siemens\Automation\Shared Settings\General\{version}"))
                {
                    if (key != null)
                    {
                        object? pathValue = key.GetValue("Root");
                        if (pathValue != null)
                        {
                            string rootPath = pathValue.ToString()!;
                            if (Directory.Exists(rootPath))
                                return rootPath;
                        }
                    }
                }
            }
            catch
            {
                // Ignore registry access errors
            }

            return null;
        }

    }
}
