using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Commands;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Abstractions
{
    /// <summary>
    /// Optional command interface for hosts that can pass cancellation and progress context.
    /// </summary>
    public interface IContextualCommand : ICommand
    {
        Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger, CommandExecutionContext context);
    }
}
