using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTiaToolkit.Core.Abstractions;
using OpenTiaToolkit.Core.Commands.Portal;

namespace OpenTiaToolkit.Core.Commands.Multiuser;

public abstract class MultiuserCommandBase : ICommand
{
    protected readonly IMultiuserService Service;
    protected MultiuserCommandBase(IMultiuserService service) => Service = service;
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract CommandSchema Schema { get; }
    protected abstract Task<object> ExecuteCoreAsync(CommandArgs args);
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, ILogger logger)
    {
        try { return CommandResult.CreateSuccess(await ExecuteCoreAsync(args).ConfigureAwait(false)); }
        catch (Exception ex) { logger.LogError(ex, "Multiuser command {CommandName} failed", Name); return CommandResult.CreateFailure(ex.Message, ex); }
    }
}

public sealed class ListMultiuserServersCommand : MultiuserCommandBase
{
    public ListMultiuserServersCommand(IMultiuserService service) : base(service) { }
    public override string Name => "list-multiuser-servers";
    public override string Description => "Lists configured TIA Project Server connections.";
    public override CommandSchema Schema => new CommandSchema(Description);
    protected override async Task<object> ExecuteCoreAsync(CommandArgs args) => await Service.ListServersAsync();
}

public sealed class EnsureMultiuserServerCommand : MultiuserCommandBase
{
    public EnsureMultiuserServerCommand(IMultiuserService service) : base(service) { }
    public override string Name => "ensure-multiuser-server";
    public override string Description => "Creates or updates a TIA Project Server connection.";
    public override CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("server-alias", CommandParameterType.String, "Connection alias", true), new CommandParameter("protocol", CommandParameterType.String, "Http or Https", false, "Https"), new CommandParameter("host", CommandParameterType.String, "Project Server host", true), new CommandParameter("port", CommandParameterType.Integer, "Project Server port", true) });
    protected override async Task<object> ExecuteCoreAsync(CommandArgs args) => await Service.EnsureServerAsync(Required(args, "server-alias"), args.GetString("protocol", "Https"), Required(args, "host"), args.GetInt("port"));
    private static string Required(CommandArgs a, string n) => a.GetString(n) ?? throw new ArgumentException(n + " is required.");
}

public sealed class ListMultiuserProjectsCommand : MultiuserCommandBase
{
    public ListMultiuserProjectsCommand(IMultiuserService service) : base(service) { }
    public override string Name => "list-multiuser-projects";
    public override string Description => "Lists projects available on a configured TIA Project Server.";
    public override CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("server-alias", CommandParameterType.String, "Connection alias", true) });
    protected override async Task<object> ExecuteCoreAsync(CommandArgs args) => await Service.ListServerProjectsAsync(args.GetString("server-alias")!);
}

public sealed class ListMultiuserSessionsCommand : MultiuserCommandBase
{
    public ListMultiuserSessionsCommand(IMultiuserService service) : base(service) { }
    public override string Name => "list-multiuser-sessions";
    public override string Description => "Lists local sessions registered for a server project.";
    public override CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("server-alias", CommandParameterType.String, "Connection alias", true), new CommandParameter("project-name", CommandParameterType.String, "Server project name", true) });
    protected override async Task<object> ExecuteCoreAsync(CommandArgs args) => await Service.ListLocalSessionsAsync(args.GetString("server-alias")!, args.GetString("project-name")!);
}

public sealed class AddProjectToMultiuserServerCommand : MultiuserCommandBase
{
    public AddProjectToMultiuserServerCommand(IMultiuserService service) : base(service) { }
    public override string Name => "add-project-to-multiuser-server";
    public override string Description => "Adds a closed single-user TIA project to a configured Project Server.";
    public override CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("server-alias", CommandParameterType.String, "Connection alias", true), new CommandParameter("project-path", CommandParameterType.Path, "Closed .ap project file", true) });
    protected override async Task<object> ExecuteCoreAsync(CommandArgs args) => await Service.AddProjectToServerAsync(args.GetString("server-alias")!, args.GetString("project-path")!);
}

public sealed class CreateMultiuserSessionCommand : MultiuserCommandBase
{
    public CreateMultiuserSessionCommand(IMultiuserService service) : base(service) { }
    public override string Name => "create-multiuser-session";
    public override string Description => "Creates an agent-owned Multiuser or Exclusive local session.";
    public override CommandSchema Schema => new CommandSchema(Description, Combine(new[] { new CommandParameter("server-alias", CommandParameterType.String, "Connection alias", true), new CommandParameter("project-name", CommandParameterType.String, "Server project name", true), new CommandParameter("session-name", CommandParameterType.String, "Unique local session name", true), new CommandParameter("session-directory", CommandParameterType.Path, "Directory for the local session", true), new CommandParameter("mode", CommandParameterType.String, "Multiuser or Exclusive", false, "Multiuser") }, ProjectCommandSupport.UmacParameters()));
    protected override async Task<object> ExecuteCoreAsync(CommandArgs args) => await Service.CreateLocalSessionAsync(args.GetString("server-alias")!, args.GetString("project-name")!, args.GetString("session-name")!, args.GetString("session-directory")!, args.GetString("mode", "Multiuser"), ProjectCommandSupport.GetAuthenticationOptions(args));
    private static CommandParameter[] Combine(CommandParameter[] a, CommandParameter[] b) { var r = new CommandParameter[a.Length + b.Length]; a.CopyTo(r, 0); b.CopyTo(r, a.Length); return r; }
}

public sealed class GetMultiuserSessionStatusCommand : MultiuserCommandBase
{
    public GetMultiuserSessionStatusCommand(IMultiuserService service) : base(service) { }
    public override string Name => "get-multiuser-session-status";
    public override string Description => "Reports up-to-date state, markings, and conflicts for the selected local session.";
    public override CommandSchema Schema => new CommandSchema(Description);
    protected override async Task<object> ExecuteCoreAsync(CommandArgs args) => await Service.GetStatusAsync();
}

public sealed class SaveMultiuserSessionCommand : MultiuserCommandBase
{
    public SaveMultiuserSessionCommand(IMultiuserService service) : base(service) { }
    public override string Name => "save-multiuser-session";
    public override string Description => "Saves pending edits in the selected local session without checking in.";
    public override CommandSchema Schema => new CommandSchema(Description);
    protected override async Task<object> ExecuteCoreAsync(CommandArgs args) => await Service.SaveSessionAsync();
}

public sealed class CommitMultiuserSessionCommand : MultiuserCommandBase
{
    public CommitMultiuserSessionCommand(IMultiuserService service) : base(service) { }
    public override string Name => "commit-multiuser-session";
    public override string Description => "Blocks unsupported automated local-session check-in and directs the caller to TIA Portal.";
    public override CommandSchema Schema => new CommandSchema(Description, new[] { new CommandParameter("comment", CommandParameterType.String, "Server revision comment", true), new CommandParameter("require-up-to-date", CommandParameterType.Boolean, "Block commit when the session is stale", false, true), new CommandParameter("require-no-conflicts", CommandParameterType.Boolean, "Block commit when conflicted markings exist", false, true) });
    protected override async Task<object> ExecuteCoreAsync(CommandArgs args) => await Service.CommitSessionAsync(args.GetString("comment")!, args.GetBool("require-up-to-date", true), args.GetBool("require-no-conflicts", true));
}
