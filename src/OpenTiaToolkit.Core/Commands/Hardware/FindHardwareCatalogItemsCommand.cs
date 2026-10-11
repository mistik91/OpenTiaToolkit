using OpenTiaToolkit.Core.Abstractions;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Commands.Hardware
{
    public sealed class FindHardwareCatalogItemsCommand : IContextualCommand
    {
        private readonly IHardwareQueryCapability _service;
        public FindHardwareCatalogItemsCommand(IHardwareQueryCapability service) => _service = service;
        public string Name => "find-hardware-catalog-items";
        public string Description => "Searches installed TIA Portal hardware catalog items by text";
        public CommandSchema Schema => new CommandSchema(Description, new[]
        {
            new CommandParameter("search-text", CommandParameterType.String, "Catalog search text; blank enumerates the available catalog", required: false, defaultValue: ""),
            new CommandParameter("type-identifier", CommandParameterType.String, "Optional TIA catalog type identifier filter; this is not a language culture", required: false),
            new CommandParameter("culture", CommandParameterType.String, "Unsupported legacy parameter; V21 catalog Find does not accept a language culture. Omit it or use type-identifier", required: false)
        });
        public Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
        {
            return ExecuteAsync(args, logger, CommandExecutionContext.None);
        }

        public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context)
        {
            try
            {
                context.StartCommand(Name, "Searching TIA Portal hardware catalog.");
                if (args.Has("culture"))
                    throw new ArgumentException("culture is unsupported for V21 hardware catalog searches. Omit it; use type-identifier only for a TIA type identifier filter.");
                var values = await _service.FindCatalogItemsAsync(args.GetString("search-text"), args.Has("type-identifier") ? args.GetString("type-identifier") : null);
                context.CompleteCommand(Name, $"Found {values.Count} hardware catalog item(s).");
                return CommandResult.CreateSuccess(values, $"Found {values.Count} hardware catalog item(s).");
            }
            catch (Exception ex) { return HardwareCommandSupport.Failure("Finding hardware catalog items", ex, logger); }
        }
    }
}
