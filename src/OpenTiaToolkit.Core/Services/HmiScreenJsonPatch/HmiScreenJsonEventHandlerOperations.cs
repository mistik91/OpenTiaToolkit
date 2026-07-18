using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiScreenJsonEventHandlerOperations : IHmiScreenJsonPatchOperationHandler
    {
        public IReadOnlyList<string> OperationNames { get; } = new[]
        { "set-event-handler", "remove-event-handler", "set-property-event-handler", "remove-property-event-handler" };

        public void Apply(HmiScreenJsonPatchOperationContext context)
        {
            if (!context.Targets.TryResolveCompositeTarget(context, out var target)) return;
            switch (HmiScreenJsonPatchOperationCatalog.Normalize(context.Op))
            {
                case "seteventhandler": SetEvent(context, target); break;
                case "removeeventhandler": RemoveEvent(context, target); break;
                case "setpropertyeventhandler": SetPropertyEvent(context, target); break;
                case "removepropertyeventhandler": RemovePropertyEvent(context, target); break;
            }
        }

        private static void SetEvent(HmiScreenJsonPatchOperationContext context, HmiScreenJsonCompositeTarget target)
        {
            var handler = BuildEvent(context);
            if (handler == null || string.IsNullOrWhiteSpace(handler.EventType))
            { context.Failure("The set-event-handler operation requires eventHandler or eventType plus scriptCode.", context.Reader.ItemName(context.Operation), "EventHandlers"); return; }
            target.EventHandlers ??= new List<HmiJsonEventHandler>();
            target.EventHandlers.RemoveAll(existing => existing.EventType.Equals(handler.EventType, StringComparison.OrdinalIgnoreCase));
            target.EventHandlers.Add(handler);
            target.EventHandlers = target.EventHandlers.OrderBy(existing => existing.EventType, StringComparer.OrdinalIgnoreCase).ToList();
            context.Success(target.Scope, target.ItemName, "EventHandlers." + handler.EventType, null, handler.EventType);
        }

        private static void RemoveEvent(HmiScreenJsonPatchOperationContext context, HmiScreenJsonCompositeTarget target)
        {
            var eventType = context.Reader.EventType(context.Operation);
            if (string.IsNullOrWhiteSpace(eventType)) { context.Failure("The remove-event-handler operation requires eventType.", context.Reader.ItemName(context.Operation), "EventHandlers"); return; }
            var handlers = target.EventHandlers ?? new List<HmiJsonEventHandler>();
            if (handlers.RemoveAll(handler => handler.EventType.Equals(eventType!, StringComparison.OrdinalIgnoreCase)) == 0)
            { context.Failure($"Event handler '{eventType}' was not found.", context.Reader.ItemName(context.Operation), "EventHandlers." + eventType); return; }
            target.EventHandlers = handlers;
            context.Success(target.Scope, target.ItemName, "EventHandlers." + eventType, eventType, null);
        }

        private static void SetPropertyEvent(HmiScreenJsonPatchOperationContext context, HmiScreenJsonCompositeTarget target)
        {
            var handler = BuildPropertyEvent(context);
            if (handler == null || string.IsNullOrWhiteSpace(handler.PropertyName) || string.IsNullOrWhiteSpace(handler.EventType))
            { context.Failure("The set-property-event-handler operation requires propertyEventHandler or property plus eventType plus scriptCode.", context.Reader.ItemName(context.Operation), "PropertyEventHandlers"); return; }
            target.PropertyEventHandlers ??= new List<HmiJsonPropertyEventHandler>();
            target.PropertyEventHandlers.RemoveAll(existing => existing.PropertyName.Equals(handler.PropertyName, StringComparison.OrdinalIgnoreCase) && existing.EventType.Equals(handler.EventType, StringComparison.OrdinalIgnoreCase));
            target.PropertyEventHandlers.Add(handler);
            target.PropertyEventHandlers = target.PropertyEventHandlers.OrderBy(existing => existing.PropertyName, StringComparer.OrdinalIgnoreCase).ThenBy(existing => existing.EventType, StringComparer.OrdinalIgnoreCase).ToList();
            context.Success(target.Scope, target.ItemName, $"PropertyEventHandlers.{handler.PropertyName}.{handler.EventType}", null, handler.EventType);
        }

        private static void RemovePropertyEvent(HmiScreenJsonPatchOperationContext context, HmiScreenJsonCompositeTarget target)
        {
            var property = context.Reader.PropertyName(context.Operation); var eventType = context.Reader.EventType(context.Operation);
            if (string.IsNullOrWhiteSpace(property) || string.IsNullOrWhiteSpace(eventType))
            { context.Failure("The remove-property-event-handler operation requires property and eventType.", context.Reader.ItemName(context.Operation), "PropertyEventHandlers"); return; }
            var handlers = target.PropertyEventHandlers ?? new List<HmiJsonPropertyEventHandler>();
            if (handlers.RemoveAll(handler => handler.PropertyName.Equals(property!, StringComparison.OrdinalIgnoreCase) && handler.EventType.Equals(eventType!, StringComparison.OrdinalIgnoreCase)) == 0)
            { context.Failure($"Property event handler '{property}.{eventType}' was not found.", context.Reader.ItemName(context.Operation), "PropertyEventHandlers." + property + "." + eventType); return; }
            target.PropertyEventHandlers = handlers;
            context.Success(target.Scope, target.ItemName, "PropertyEventHandlers." + property + "." + eventType, eventType, null);
        }

        private static HmiJsonEventHandler? BuildEvent(HmiScreenJsonPatchOperationContext context)
        {
            if (context.Operation.EventHandler != null) return HmiScreenJsonPatchCloner.Payload(context.Operation.EventHandler);
            var eventType = context.Reader.EventType(context.Operation);
            if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(context.Operation.ScriptCode)) return null;
            return new HmiJsonEventHandler { EventType = eventType!, Script = Script(context.Operation) };
        }

        private static HmiJsonPropertyEventHandler? BuildPropertyEvent(HmiScreenJsonPatchOperationContext context)
        {
            if (context.Operation.PropertyEventHandler != null) return HmiScreenJsonPatchCloner.Payload(context.Operation.PropertyEventHandler);
            var property = context.Reader.PropertyName(context.Operation); var eventType = context.Reader.EventType(context.Operation);
            if (string.IsNullOrWhiteSpace(property) || string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(context.Operation.ScriptCode)) return null;
            return new HmiJsonPropertyEventHandler { PropertyName = property!, EventType = eventType!, Script = Script(context.Operation) };
        }

        private static HmiJsonScriptPayload Script(HmiScreenJsonPatchOperation operation) => new HmiJsonScriptPayload
        { Async = operation.Async, GlobalDefinitionAreaScriptCode = operation.GlobalDefinitionAreaScriptCode, ScriptCode = operation.ScriptCode };
    }
}
