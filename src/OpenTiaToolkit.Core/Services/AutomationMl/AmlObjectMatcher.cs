using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTiaToolkit.Core.Services.AutomationMl;

internal sealed class AmlObjectMatcher
{
    private readonly ILookup<string, AmlObject> _sourceChildren;
    private readonly ILookup<string, AmlObject> _projectChildren;
    private readonly HashSet<string> _connectedSourceObjects;
    private readonly Dictionary<string, AmlObject> _matches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AmlObject> _claims = new(StringComparer.Ordinal);

    public AmlObjectMatcher(AmlModel source, AmlModel project)
    {
        _sourceChildren = source.Objects.ToLookup(item => Parent(item.Key), StringComparer.Ordinal);
        _projectChildren = project.Objects.ToLookup(item => Parent(item.Key), StringComparer.Ordinal);
        _connectedSourceObjects = new HashSet<string>(source.Connections
            .SelectMany(link => new[] { EndpointOwner(link.First), EndpointOwner(link.Second) }), StringComparer.Ordinal);
        LegacyReadback = source.ApcVersion == "1.0.0" && project.ApcVersion != null && project.ApcVersion != "1.0.0";
    }

    public bool LegacyReadback { get; }

    public bool IsEmptyDefaultTableDirective(AmlObject item) =>
        item.Kind == "TagTable" && Property(item, "AssignToDefault") == "true" &&
        !_sourceChildren.Contains(item.Key) && !_connectedSourceObjects.Contains(item.Key);

    public bool HasTypedChild(AmlObject item) =>
        _sourceChildren[item.Key].Any(child => child.Kind == "DeviceItem" && Property(child, "TypeIdentifier") != null);

    public AmlObject? Match(AmlObject source, out bool ambiguous)
    {
        ambiguous = false;
        var parent = Parent(source.Key);
        var projectParent = string.Empty;
        if (parent.Length != 0)
        {
            if (!_matches.TryGetValue(parent, out var matchedParent)) return null;
            projectParent = matchedParent.Key;
        }

        var siblings = _projectChildren[projectParent].Where(item => item.Kind == source.Kind).ToArray();
        var exact = siblings.FirstOrDefault(item => Segment(item.Key) == Segment(source.Key));
        if (exact != null && !(source.Kind == "TagTable" && Property(source, "AssignToDefault") == "true"))
            return Remember(source, exact, ref ambiguous);

        var candidates = Candidates(source, siblings, parent, out var sourceAmbiguous).Take(2).ToArray();
        ambiguous = sourceAmbiguous || candidates.Length > 1;
        return !ambiguous && candidates.Length == 1 ? Remember(source, candidates[0], ref ambiguous) : null;
    }

    public string? TranslateEndpoint(string endpoint)
    {
        var owner = EndpointOwner(endpoint);
        return _matches.TryGetValue(owner, out var actual) ? actual.Key + endpoint.Substring(owner.Length) : null;
    }

    public IEnumerable<AmlObject> BuiltInRepresentations(AmlObject project) =>
        _projectChildren[project.Key].Where(child =>
            child.Kind == "DeviceItem" && child.Name == project.Name &&
            IsBuiltIn(child) && Property(child, "TypeIdentifier") == null);

    internal static string? Property(AmlObject item, string key) =>
        item.Properties.TryGetValue(key, out var value) ? value : null;

    internal static bool IsBuiltIn(AmlObject item) =>
        string.Equals(Property(item, "BuiltIn"), "true", StringComparison.OrdinalIgnoreCase);

    internal static string? PortLabel(AmlObject item) => Property(item, "Label")?.Replace(" ", string.Empty);

    private IEnumerable<AmlObject> Candidates(AmlObject item, AmlObject[] siblings, string parent, out bool ambiguous)
    {
        ambiguous = false;
        switch (item.Kind)
        {
            case "Channel" when _matches.TryGetValue(parent, out var module) && module.Kind == "DeviceItem":
                // Current CAx exports can attach a module's channels to its integrated submodule.
                if (Property(module, "TypeIdentifier") == null) break;
                return BuiltInRepresentations(module).SelectMany(child => _projectChildren[child.Key])
                    .Where(child => child.Kind == "Channel" && Segment(child.Key) == Segment(item.Key));
            case "Node" when Property(item, "Type") is string type:
                if (_sourceChildren[parent].Count(peer => peer.Kind == "Node" && Property(peer, "Type") == type) != 1)
                {
                    ambiguous = true;
                    break;
                }
                return siblings.Where(candidate => Property(candidate, "Type") == type);
            case "CommunicationInterface" when Property(item, "Label") is string label:
                if (_sourceChildren[parent].Count(peer => peer.Kind == item.Kind && Property(peer, "Label") == label) != 1)
                {
                    ambiguous = true;
                    break;
                }
                return siblings.Where(candidate => Property(candidate, "Label") == label);
            case "CommunicationPort" when IsBuiltIn(item) && PortLabel(item) is string portLabel:
                var peers = _sourceChildren[parent].Where(peer => peer.Kind == item.Kind && PortLabel(peer) == portLabel).ToArray();
                if (peers.Length != 1 &&
                    (!LegacyReadback || peers.Any(peer => !IsBuiltIn(peer)) ||
                     peers.Count(peer => _connectedSourceObjects.Contains(peer.Key)) > 1))
                {
                    ambiguous = true;
                    break;
                }
                return siblings.Where(candidate => IsBuiltIn(candidate) && PortLabel(candidate) == portLabel);
            case "TagTable" when Property(item, "AssignToDefault") == "true":
                if (_sourceChildren[parent].Count(peer => peer.Kind == item.Kind &&
                    Property(peer, "AssignToDefault") == "true" && !IsEmptyDefaultTableDirective(peer)) != 1)
                {
                    ambiguous = true;
                    break;
                }
                return siblings.Where(candidate => Property(candidate, "AssignToDefault") == "true" ||
                    string.Equals(candidate.Name, "Default tag table", StringComparison.OrdinalIgnoreCase));
        }

        return Array.Empty<AmlObject>();
    }

    private AmlObject? Remember(AmlObject source, AmlObject project, ref bool ambiguous)
    {
        if (_claims.TryGetValue(project.Key, out var previous))
        {
            if (!LegacyReadback || source.Kind != "CommunicationPort" || previous.Kind != source.Kind ||
                !IsBuiltIn(source) || !IsBuiltIn(previous) || Parent(source.Key) != Parent(previous.Key) ||
                PortLabel(source) == null || PortLabel(source) != PortLabel(previous) ||
                (_connectedSourceObjects.Contains(source.Key) && _connectedSourceObjects.Contains(previous.Key)))
            {
                ambiguous = true;
                return null;
            }

            if (_connectedSourceObjects.Contains(source.Key)) _claims[project.Key] = source;
        }
        else
        {
            _claims.Add(project.Key, source);
        }

        _matches.Add(source.Key, project);
        return project;
    }

    private static string EndpointOwner(string key)
    {
        var tail = Segment(key);
        return tail == "LogicalEndPoint:" || tail == "CommunicationPortInterface:" ? Parent(key) : key;
    }

    private static string Parent(string key)
    {
        var index = key.LastIndexOf('/');
        return index < 0 ? string.Empty : key.Substring(0, index);
    }

    private static string Segment(string key) => key.Substring(key.LastIndexOf('/') + 1);
}
