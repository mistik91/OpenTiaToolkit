using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTiaToolkit.MCP.Server
{
    /// <summary>
    /// Registry that manages available MCP tools backed by ICommand implementations.
    /// </summary>
    public class ToolRegistry
    {
        private readonly Dictionary<string, ToolAdapter> _tools;
        private readonly ILogger<ToolRegistry> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolRegistry"/> class.
        /// </summary>
        /// <param name="commands">The commands to register as tools.</param>
        /// <param name="logger">Logger instance.</param>
        public ToolRegistry(IEnumerable<ICommand> commands, ILogger<ToolRegistry> logger)
        {
            _logger = logger;
            _tools = new Dictionary<string, ToolAdapter>(StringComparer.OrdinalIgnoreCase);

            foreach (var command in commands)
            {
                var adapter = new ToolAdapter(command, logger);
                _tools[command.Name] = adapter;
                _logger.LogInformation("Registered MCP tool: {ToolName} - {Description}", command.Name, command.Description);
            }
        }

        /// <summary>
        /// Gets all registered tool adapters.
        /// </summary>
        public IReadOnlyDictionary<string, ToolAdapter> Tools => _tools;

        /// <summary>
        /// Gets a tool adapter by name.
        /// </summary>
        /// <param name="toolName">The tool name.</param>
        /// <returns>The tool adapter, or null if not found.</returns>
        public ToolAdapter? GetTool(string toolName)
        {
            return _tools.TryGetValue(toolName, out var adapter) ? adapter : null;
        }

        /// <summary>
        /// Lists all available tools.
        /// </summary>
        /// <returns>List of tool information.</returns>
        public List<ToolInfo> ListTools()
        {
            var tools = new List<ToolInfo>();

            foreach (var kvp in _tools)
            {
                tools.Add(new ToolInfo
                {
                    Name = kvp.Value.Name,
                    Description = kvp.Value.Description,
                    InputSchema = kvp.Value.GetInputSchema()
                });
            }

            return tools;
        }
    }

    /// <summary>
    /// Information about a registered tool.
    /// </summary>
    public class ToolInfo
    {
        /// <summary>Gets or sets the tool name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the tool description.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Gets or sets the JSON schema for input arguments.</summary>
        public string InputSchema { get; set; } = string.Empty;
    }
}
