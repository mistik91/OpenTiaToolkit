using OpenTiaToolkit.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services
{
    internal sealed class HmiScreenJsonPatchOperationContext
    {
        public HmiScreenJsonPatchOperationContext(HmiJsonScreen screen, HmiScreenJsonPatchOperation operation, int index,
            string op, List<HmiScreenJsonPatchOperationResult> results, HmiScreenJsonPatchOperationReader reader,
            HmiScreenJsonPatchTargetResolver targets)
        {
            Screen = screen; Operation = operation; Index = index; Op = op; Results = results; Reader = reader; Targets = targets;
        }

        public HmiJsonScreen Screen { get; }
        public HmiScreenJsonPatchOperation Operation { get; }
        public int Index { get; }
        public string Op { get; }
        public List<HmiScreenJsonPatchOperationResult> Results { get; }
        public HmiScreenJsonPatchOperationReader Reader { get; }
        public HmiScreenJsonPatchTargetResolver Targets { get; }
        public void Success(string scope, string? itemName, string? property, string? previousValue, string? newValue) =>
            Results.Add(HmiScreenJsonPatchResultFactory.Success(Index, Op, scope, itemName, property, previousValue, newValue));
        public void Failure(string message, string? itemName = null, string? property = null, List<string>? suggestions = null) =>
            Results.Add(HmiScreenJsonPatchResultFactory.Failure(Index, Op, message, itemName, property, suggestions));
    }

    internal interface IHmiScreenJsonPatchOperationHandler
    {
        IReadOnlyList<string> OperationNames { get; }
        void Apply(HmiScreenJsonPatchOperationContext context);
    }

    internal sealed class HmiScreenJsonPatchDispatcher
    {
        private readonly Dictionary<string, IHmiScreenJsonPatchOperationHandler> _handlers;

        public HmiScreenJsonPatchDispatcher(IEnumerable<IHmiScreenJsonPatchOperationHandler> handlers)
        {
            _handlers = new Dictionary<string, IHmiScreenJsonPatchOperationHandler>(StringComparer.Ordinal);
            foreach (var handler in handlers)
            {
                foreach (var operationName in handler.OperationNames)
                {
                    var normalized = HmiScreenJsonPatchOperationCatalog.Normalize(operationName);
                    if (string.IsNullOrEmpty(normalized)) throw new ArgumentException("Handler operation names must not be empty.", nameof(handlers));
                    if (_handlers.ContainsKey(normalized)) throw new InvalidOperationException($"Duplicate patch operation handler registration for '{operationName}'.");
                    _handlers.Add(normalized, handler);
                }
            }
        }

        public IReadOnlyCollection<string> RegisteredOperationNames => _handlers.Keys;

        public void ValidateCatalogCoverage(IReadOnlyList<string> canonicalOperations)
        {
            var expected = new HashSet<string>(canonicalOperations.Select(HmiScreenJsonPatchOperationCatalog.Normalize), StringComparer.Ordinal);
            var missing = expected.Where(operation => !_handlers.ContainsKey(operation)).ToList();
            var unexpected = _handlers.Keys.Where(operation => !expected.Contains(operation)).ToList();
            if (missing.Count > 0 || unexpected.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Patch handler registry does not match the operation catalog. Missing: {string.Join(", ", missing)}. Unexpected: {string.Join(", ", unexpected)}.");
            }
        }

        public bool TryDispatch(string operationName, HmiScreenJsonPatchOperationContext context)
        {
            if (!_handlers.TryGetValue(HmiScreenJsonPatchOperationCatalog.Normalize(operationName), out var handler)) return false;
            handler.Apply(context); return true;
        }
    }

    internal sealed class HmiScreenJsonPatchEngine
    {
        private readonly HmiScreenJsonPatchDispatcher _dispatcher;
        private readonly HmiScreenJsonPatchOperationReader _reader;
        private readonly HmiScreenJsonPatchTargetResolver _targets;

        internal HmiScreenJsonPatchEngine(HmiScreenJsonPatchDispatcher dispatcher, HmiScreenJsonPatchOperationReader reader)
        {
            _dispatcher = dispatcher; _reader = reader; _targets = new HmiScreenJsonPatchTargetResolver(reader);
        }

        public static HmiScreenJsonPatchEngine CreateDefault()
        {
            var reader = new HmiScreenJsonPatchOperationReader();
            var handlers = new IHmiScreenJsonPatchOperationHandler[]
            {
                new HmiScreenJsonPropertyAndLayoutHandler(), new HmiScreenJsonItemLifecycleHandler(),
                new HmiScreenJsonEventHandlerOperations(), new HmiScreenJsonDynamizationHandler(),
                new HmiScreenJsonInterfaceHandler()
            };
            var dispatcher = new HmiScreenJsonPatchDispatcher(handlers);
            dispatcher.ValidateCatalogCoverage(HmiScreenJsonPatchOperationCatalog.SupportedOperations);
            return new HmiScreenJsonPatchEngine(dispatcher, reader);
        }

        internal HmiScreenJsonPatchDispatcher Dispatcher => _dispatcher;

        public HmiScreenJsonPatchApplyResult Apply(HmiJsonScreen screen, IReadOnlyList<HmiScreenJsonPatchOperation> operations)
        {
            var editedScreen = HmiScreenJsonPatchCloner.Screen(screen);
            var results = new List<HmiScreenJsonPatchOperationResult>();
            for (var index = 0; index < operations.Count; index++)
            {
                var operation = operations[index];
                var op = _reader.OperationName(operation);
                if (string.IsNullOrWhiteSpace(op))
                {
                    results.Add(HmiScreenJsonPatchResultFactory.Failure(index, string.Empty, "The patch operation is missing the op field."));
                    continue;
                }
                var context = new HmiScreenJsonPatchOperationContext(editedScreen, operation, index, op, results, _reader, _targets);
                if (_dispatcher.TryDispatch(op, context)) continue;
                results.Add(HmiScreenJsonPatchResultFactory.Failure(index, op,
                    "Unsupported patch operation. Supported operations are set-property, set-properties, move-item, resize-item, set-layout, rename-item, create-item, delete-item, duplicate-item, set-event-handler, remove-event-handler, set-property-event-handler, remove-property-event-handler, set-dynamization, remove-dynamization, and set-interface.",
                    suggestions: new List<string> { "Use one of: " + string.Join(", ", HmiScreenJsonPatchOperationCatalog.SupportedOperations) }));
            }
            return new HmiScreenJsonPatchApplyResult(editedScreen, results);
        }
    }
}
