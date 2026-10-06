using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTiaToolkit.Core.Models;
using OpenTiaToolkit.Core.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;

namespace OpenTiaToolkit.Tests.Services
{
    [TestClass]
    public class V21UnifiedHmiAdapterTests
    {
        private readonly V21UnifiedHmiAdapter _adapter = new V21UnifiedHmiAdapter(null);

        [TestMethod]
        public void NormalizeScreenPath_TrimsSegmentsAndNormalizesSeparators()
        {
            Assert.AreEqual("Group/Nested/Screen", _adapter.NormalizeScreenPath(@" / Group\\Nested / Screen / "));
            Assert.AreEqual(string.Empty, _adapter.NormalizeScreenPath("  "));
        }

        [TestMethod]
        public void NormalizeTagPath_TrimsSegmentsAndUsesDots()
        {
            Assert.AreEqual("Folder.Nested.Tag", _adapter.NormalizeTagPath(@" Folder/Nested\\Tag "));
            Assert.AreEqual(string.Empty, _adapter.NormalizeTagPath(null));
        }

        [TestMethod]
        public void NormalizeItemTypeName_StripsNamespaceAndAliasPunctuation()
        {
            Assert.AreEqual("hmisymboliciofield", _adapter.NormalizeItemTypeName("Siemens.Engineering.HmiUnified.Hmi_Symbolic-IO Field"));
            Assert.AreEqual(string.Empty, _adapter.NormalizeItemTypeName(null));
        }

        [TestMethod]
        public void StaticItemTypeCatalog_ContainsEstablishedAliases()
        {
            var types = _adapter.ListSupportedItemTypes(null);

            CollectionAssert.Contains(System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(types, type => type.Type)), "HmiButton");
            Assert.IsTrue(System.Linq.Enumerable.Any(types, type => System.Linq.Enumerable.Contains(type.Aliases, "iofield")));
            Assert.IsTrue(System.Linq.Enumerable.Any(types, type => System.Linq.Enumerable.Contains(type.Aliases, "faceplatecontainer")));
        }

        [TestMethod]
        public void ItemTypeCatalog_FiltersAndResolvesAliases()
        {
            var filtered = _adapter.ListSupportedItemTypes("faceplate");

            Assert.IsTrue(filtered.Count > 0);
            Assert.IsTrue(System.Linq.Enumerable.All(filtered, type =>
                type.Type.IndexOf("faceplate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                System.Linq.Enumerable.Any(type.Aliases, alias => alias.IndexOf("faceplate", StringComparison.OrdinalIgnoreCase) >= 0)));
            Assert.IsTrue(_adapter.TryResolveItemClrType("button", out var resolved));
            Assert.AreEqual("HmiButton", resolved.Name);
        }

        [TestMethod]
        public void ResolvePublicInstanceProperty_PrefersMostDerivedMember()
        {
            var property = V21HmiEngineeringPrimitives.ResolvePublicInstanceProperty(typeof(DerivedPropertyOwner), "Value");

            Assert.IsNotNull(property);
            Assert.AreEqual(typeof(DerivedPropertyOwner), property!.DeclaringType);
        }

        [TestMethod]
        public void ResolveProperty_TraversesNestedClrPathAndRejectsNullIntermediate()
        {
            var child = new NestedChild { Value = "ok" };
            var resolved = V21HmiEngineeringPrimitives.ResolveProperty(new NestedOwner { Child = child }, "Child.Value");

            Assert.IsNotNull(resolved);
            Assert.AreSame(child, resolved!.Target);
            Assert.AreEqual("Value", resolved.PropertyInfo.Name);
            var exception = Assert.ThrowsException<InvalidOperationException>(() =>
                V21HmiEngineeringPrimitives.ResolveProperty(new NestedOwner(), "Child.Value"));
            StringAssert.Contains(exception.Message, "Child");
        }

        [TestMethod]
        public void InvariantConversionAndColorParsing_AreCultureIndependent()
        {
            Assert.AreEqual(12.5m, V21HmiEngineeringPrimitives.ConvertInvariantStringToType("12.5", typeof(decimal)));
            Assert.AreEqual(Color.FromArgb(255, 0x12, 0x34, 0x56), V21HmiEngineeringPrimitives.ParseColor("#123456"));
        }

        [TestMethod]
        public void Validation_RejectsReadOnlyClrProperty()
        {
            Assert.IsFalse(V21HmiEngineeringPrimitives.TryValidateObjectPropertyValue(
                typeof(ReadOnlyOwner), "Value", "changed", out var error));
            StringAssert.Contains(error, "read-only");
        }

        [TestMethod]
        public void ExceptionFormatting_PreservesInnerFailureChain()
        {
            var text = V21HmiEngineeringPrimitives.FormatExceptionMessage(
                new InvalidOperationException("outer", new ArgumentException("inner")));

            StringAssert.Contains(text, "outer");
            StringAssert.Contains(text, "inner");
        }

        [TestMethod]
        public void EventValidation_RejectsDuplicatesUnsupportedEnumsAndEmptyScripts()
        {
            Assert.IsFalse(V21HmiEventSynchronizer.TryValidateEventHandlerDefinitions(
                new[] { Event("Click", "a"), Event("click", "b") }, out var duplicateError));
            StringAssert.Contains(duplicateError, "duplicate");

            Assert.IsFalse(V21HmiEventSynchronizer.TryValidateEventHandler(Event("Missing", "a"), typeof(FakeEvent), out var enumError));
            Assert.IsFalse(string.IsNullOrWhiteSpace(enumError));

            Assert.IsFalse(V21HmiEventSynchronizer.TryValidateEventHandler(Event("Click", ""), typeof(FakeEvent), out var scriptError));
            StringAssert.Contains(scriptError, "ScriptCode");
        }

        [TestMethod]
        public void PropertyEventValidation_RejectsMissingDuplicateAndEmptyScriptDefinitions()
        {
            Assert.IsFalse(V21HmiEventSynchronizer.TryValidatePropertyEventHandlerDefinitions(
                new[] { new HmiJsonPropertyEventHandler { EventType = "Change" } }, out var missingError));
            StringAssert.Contains(missingError, "PropertyName");

            var duplicate = new[]
            {
                PropertyEvent("Text", "Change", "a"),
                PropertyEvent("text", "change", "b")
            };
            Assert.IsFalse(V21HmiEventSynchronizer.TryValidatePropertyEventHandlerDefinitions(duplicate, out var duplicateError));
            StringAssert.Contains(duplicateError, "duplicate");

            Assert.IsFalse(V21HmiEventSynchronizer.TryValidatePropertyEventHandler(
                PropertyEvent("Text", "Change", ""), typeof(FakeEvent), out var scriptError));
            StringAssert.Contains(scriptError, "ScriptCode");
        }

        [TestMethod]
        public void DynamizationValidation_RejectsDuplicateUnsupportedAndEmptyScriptDefinitions()
        {
            Assert.IsFalse(V21HmiDynamizationSynchronizer.TryValidateDynamizationDefinitions(
                new[] { Dynamization("Visible", "Tag"), Dynamization("visible", "Tag") }, out var duplicateError));
            StringAssert.Contains(duplicateError, "duplicate");

            Assert.IsFalse(V21HmiDynamizationSynchronizer.TryValidateDynamizationDefinitions(
                new[] { Dynamization("Visible", "UnknownKind") }, out var unsupportedError));
            StringAssert.Contains(unsupportedError, "Unsupported");

            var script = Dynamization("Visible", "Script");
            script.Script = new HmiJsonScriptDynamization { ScriptCode = "" };
            Assert.IsFalse(V21HmiDynamizationSynchronizer.TryValidateDynamizationDefinition(script, out var scriptError));
            StringAssert.Contains(scriptError, "ScriptCode");
        }

        [TestMethod]
        public void InterfaceValidation_RejectsMissingDuplicateAndInvalidNestedDynamizations()
        {
            Assert.IsFalse(V21HmiDynamizationSynchronizer.TryValidateInterfaceBindingDefinitions(
                new[] { new HmiJsonInterfaceBinding() }, out var missingError));
            StringAssert.Contains(missingError, "PropertyName");

            Assert.IsFalse(V21HmiDynamizationSynchronizer.TryValidateInterfaceBindingDefinitions(
                new[]
                {
                    new HmiJsonInterfaceBinding { PropertyName = "Speed" },
                    new HmiJsonInterfaceBinding { PropertyName = "speed" }
                }, out var duplicateError));
            StringAssert.Contains(duplicateError, "duplicate");

            Assert.IsFalse(V21HmiDynamizationSynchronizer.TryValidateInterfaceBindingDefinitions(
                new[]
                {
                    new HmiJsonInterfaceBinding
                    {
                        PropertyName = "Speed",
                        Dynamizations = new List<HmiJsonDynamization> { Dynamization("Speed", "UnknownKind") }
                    }
                }, out var nestedError));
            StringAssert.Contains(nestedError, "Interface.Speed.Dynamizations");
        }

        [TestMethod]
        public void InterfaceSynchronization_BoxesBooleanForObjectTypedValueProperty()
        {
            var entry = new FakeInterfaceEntry("NO", false, typeof(bool));
            var composition = new FakeInterfaceComposition(entry);

            var success = V21HmiDynamizationSynchronizer.TrySynchronizeInterfaceBindings(
                composition,
                new[]
                {
                    new HmiJsonInterfaceBinding
                    {
                        PropertyName = "NO",
                        ValueType = "String",
                        Value = "True"
                    }
                },
                out var error);

            Assert.IsTrue(success, error);
            Assert.AreEqual(true, entry.Value);
            Assert.AreEqual(typeof(bool), entry.LastAssignedType);
            Assert.AreEqual(1, entry.WriteCount);
        }

        [TestMethod]
        public void InterfaceSynchronization_UsesSupportedTypeWhenCurrentValueIsNull()
        {
            var entry = new FakeInterfaceEntry(
                "Enable",
                initialValue: null,
                requiredWriteType: typeof(bool),
                supportedTypes: new[] { typeof(bool) });
            var composition = new FakeInterfaceComposition(entry);

            var success = V21HmiDynamizationSynchronizer.TrySynchronizeInterfaceBindings(
                composition,
                new[]
                {
                    new HmiJsonInterfaceBinding
                    {
                        PropertyName = "Enable",
                        Value = "true"
                    }
                },
                out var error);

            Assert.IsTrue(success, error);
            Assert.AreEqual(true, entry.Value);
            Assert.AreEqual(typeof(bool), entry.LastAssignedType);
        }

        [TestMethod]
        public void InterfaceSynchronization_ConvertsInvariantNumericValue()
        {
            var entry = new FakeInterfaceEntry("TrendMax", 0L, typeof(long));
            var composition = new FakeInterfaceComposition(entry);

            var success = V21HmiDynamizationSynchronizer.TrySynchronizeInterfaceBindings(
                composition,
                new[]
                {
                    new HmiJsonInterfaceBinding
                    {
                        PropertyName = "TrendMax",
                        ValueType = "Int64",
                        Value = "150"
                    }
                },
                out var error);

            Assert.IsTrue(success, error);
            Assert.AreEqual(150L, entry.Value);
            Assert.AreEqual(typeof(long), entry.LastAssignedType);
        }

        [TestMethod]
        public void InterfaceValidation_DryRunRejectsInvalidBooleanWithoutWriting()
        {
            var entry = new FakeInterfaceEntry("NO", false, typeof(bool));
            var owner = new FakeInterfaceOwner(entry);
            var primitives = new V21HmiEngineeringPrimitives(null);
            var properties = new V21HmiItemPropertyAccessor(primitives);
            var composites = new V21HmiCompositeAccess(properties);

            var valid = composites.TryValidateOrApplyInterfaceBindings(
                owner,
                owner.GetType(),
                new[]
                {
                    new HmiJsonInterfaceBinding
                    {
                        PropertyName = "NO",
                        ValueType = "Boolean",
                        Value = "not-a-boolean"
                    }
                },
                apply: false,
                out var error);

            Assert.IsFalse(valid);
            StringAssert.Contains(error, "Boolean");
            Assert.AreEqual(false, entry.Value);
            Assert.AreEqual(0, entry.WriteCount);
        }

        [TestMethod]
        public void InterfaceValidation_DryRunAcceptsTypedBooleanWithoutWriting()
        {
            var entry = new FakeInterfaceEntry("NO", false, typeof(bool));
            var owner = new FakeInterfaceOwner(entry);
            var primitives = new V21HmiEngineeringPrimitives(null);
            var properties = new V21HmiItemPropertyAccessor(primitives);
            var composites = new V21HmiCompositeAccess(properties);

            var valid = composites.TryValidateOrApplyInterfaceBindings(
                owner,
                owner.GetType(),
                new[]
                {
                    new HmiJsonInterfaceBinding
                    {
                        PropertyName = "NO",
                        ValueType = "Boolean",
                        Value = "True"
                    }
                },
                apply: false,
                out var error);

            Assert.IsTrue(valid, error);
            Assert.AreEqual(false, entry.Value);
            Assert.AreEqual(0, entry.WriteCount);
        }

        [TestMethod]
        public void InterfaceValueAccessor_RestoresNullWithoutSubstitutingEmptyString()
        {
            var entry = new FakeInterfaceEntry("Optional", "temporary", requiredWriteType: null);

            var success = V21HmiInterfaceValueAccessor.TryWriteRawValue(entry, null, out var error);

            Assert.IsTrue(success, error);
            Assert.IsNull(entry.Value);
            Assert.IsNull(entry.LastAssignedType);
            Assert.AreEqual(1, entry.WriteCount);
        }

        [TestMethod]
        public void MappingTableValidation_RejectsUnsupportedEntryType()
        {
            var dynamization = Dynamization("Visible", "Tag");
            dynamization.ValueConverter = new HmiJsonValueConverter
            {
                MappingTable = new HmiJsonMappingTable
                {
                    Entries = new List<HmiJsonMappingTableEntry>
                    {
                        new HmiJsonMappingTableEntry { EntryType = "NotSupported" }
                    }
                }
            };

            Assert.IsFalse(V21HmiDynamizationSynchronizer.TryValidateDynamizationDefinition(dynamization, out var error));
            StringAssert.Contains(error, "Unsupported mapping table entry type");
        }

        [TestMethod]
        public void MappingTableValidation_RejectsInvalidConditionType()
        {
            var dynamization = Dynamization("Visible", "Tag");
            dynamization.ValueConverter = new HmiJsonValueConverter
            {
                MappingTable = new HmiJsonMappingTable { ConditionType = "NotAConditionType" }
            };

            Assert.IsFalse(V21HmiDynamizationSynchronizer.TryValidateDynamizationDefinition(dynamization, out var error));
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
        }

        [TestMethod]
        public void DynamizationTargetValidation_RejectsMissingTargetProperty()
        {
            var owner = new FakeDynamizationOwner();

            Assert.IsFalse(V21HmiDynamizationSynchronizer.TryValidateDynamizationTarget(
                owner,
                owner.GetType(),
                Dynamization("Missing", "Tag"),
                out var error));
            StringAssert.Contains(error, "was not found");
        }

        [TestMethod]
        public void CompositeValidation_WithApplyFalse_PerformsNoMutation()
        {
            var primitives = new V21HmiEngineeringPrimitives(null);
            var properties = new V21HmiItemPropertyAccessor(primitives);
            var composites = new V21HmiCompositeAccess(properties);
            FakeEventComposition.CreateCount = 0;

            var valid = composites.TryValidateOrApplyEventHandlers(
                owner: null,
                ownerType: typeof(FakeEventOwner),
                desiredEventHandlers: new[] { Event("Click", "return 1;") },
                apply: false,
                out var error);

            Assert.IsTrue(valid, error);
            Assert.AreEqual(0, FakeEventComposition.CreateCount);
        }

        [TestMethod]
        public void EventSynchronization_EquivalentStatePerformsNoMutation()
        {
            var composition = new FakeEventComposition();
            composition.Add(FakeEvent.Click, "return 1;");
            composition.ResetCounters();

            var success = V21HmiEventSynchronizer.TrySynchronizeEventHandlers(
                composition,
                new[] { Event("Click", "return 1;") },
                typeof(FakeEvent),
                out var error);

            Assert.IsTrue(success, error);
            Assert.AreEqual(0, composition.CreateCalls);
            Assert.AreEqual(0, composition.DeleteCalls);
            Assert.AreEqual(0, composition.WriteCalls);
        }

        [TestMethod]
        public void EventSynchronization_CreatesUpdatesAndDeletesExpectedEntries()
        {
            var composition = new FakeEventComposition();
            composition.Add(FakeEvent.Click, "old");
            composition.Add(FakeEvent.Loaded, "remove");
            composition.ResetCounters();

            var success = V21HmiEventSynchronizer.TrySynchronizeEventHandlers(
                composition,
                new[] { Event("Click", "updated"), Event("Change", "created") },
                typeof(FakeEvent),
                out var error);

            Assert.IsTrue(success, error);
            Assert.AreEqual(1, composition.CreateCalls);
            Assert.AreEqual(1, composition.DeleteCalls);
            Assert.AreEqual(2, composition.WriteCalls);
            CollectionAssert.AreEquivalent(
                new[] { FakeEvent.Click, FakeEvent.Change },
                System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(composition.Entries, entry => entry.EventType)));
        }

        [TestMethod]
        public void PropertyEventSynchronization_UpdatesStableKeyWithoutRecreate()
        {
            var composition = new FakePropertyEventComposition();
            composition.Add("Text", FakeEvent.Change, "old");
            composition.ResetCounters();

            var success = V21HmiEventSynchronizer.TrySynchronizePropertyEventHandlers(
                composition,
                new[] { PropertyEvent("Text", "Change", "updated") },
                typeof(FakeEvent),
                out var error);

            Assert.IsTrue(success, error);
            Assert.AreEqual(0, composition.CreateCalls);
            Assert.AreEqual(0, composition.DeleteCalls);
            Assert.AreEqual(1, composition.WriteCalls);
            Assert.AreEqual("updated", composition.Entries[0].Script.ScriptCode);
        }

        private static HmiJsonEventHandler Event(string eventType, string scriptCode) => new HmiJsonEventHandler
        {
            EventType = eventType,
            Script = new HmiJsonScriptPayload { ScriptCode = scriptCode }
        };

        private static HmiJsonPropertyEventHandler PropertyEvent(string property, string eventType, string scriptCode) =>
            new HmiJsonPropertyEventHandler
            {
                PropertyName = property,
                EventType = eventType,
                Script = new HmiJsonScriptPayload { ScriptCode = scriptCode }
            };

        private static HmiJsonDynamization Dynamization(string property, string type) =>
            new HmiJsonDynamization { Property = property, Type = type };

        private class BasePropertyOwner { public string? Value { get; set; } }
        private sealed class DerivedPropertyOwner : BasePropertyOwner { public new string? Value { get; set; } }
        private sealed class NestedOwner { public NestedChild? Child { get; set; } }
        private sealed class NestedChild { public string? Value { get; set; } }
        private sealed class ReadOnlyOwner { public string Value => "fixed"; }
        private sealed class FakeDynamizationOwner { public object Dynamizations { get; } = new object(); public string Visible { get; set; } = string.Empty; }
        private sealed class FakeInterfaceOwner
        {
            internal FakeInterfaceOwner(FakeInterfaceEntry entry)
            {
                Interface = new FakeInterfaceComposition(entry);
            }

            public FakeInterfaceComposition Interface { get; }
        }

        private sealed class FakeInterfaceComposition : IEnumerable
        {
            private readonly IReadOnlyList<FakeInterfaceEntry> _entries;

            internal FakeInterfaceComposition(params FakeInterfaceEntry[] entries)
            {
                _entries = entries;
            }

            public IEnumerator GetEnumerator() => _entries.GetEnumerator();
        }

        private sealed class FakeInterfaceEntry
        {
            private readonly Type? _requiredWriteType;
            private readonly IReadOnlyList<Type> _supportedTypes;
            private object? _value;

            internal FakeInterfaceEntry(
                string propertyName,
                object? initialValue,
                Type? requiredWriteType,
                IReadOnlyList<Type>? supportedTypes = null)
            {
                PropertyName = propertyName;
                _value = initialValue;
                _requiredWriteType = requiredWriteType;
                _supportedTypes = supportedTypes ?? Array.Empty<Type>();
            }

            public string PropertyName { get; }
            public Type? LastAssignedType { get; private set; }
            public int WriteCount { get; private set; }

            public object? Value
            {
                get => _value;
                set
                {
                    LastAssignedType = value?.GetType();
                    WriteCount++;
                    if (_requiredWriteType != null && LastAssignedType != _requiredWriteType)
                    {
                        throw new InvalidOperationException(
                            $"Expected {_requiredWriteType.Name}, received {LastAssignedType?.Name ?? "null"}.");
                    }

                    _value = value;
                }
            }

            public IEnumerable<FakeAttributeInfo> GetAttributeInfos()
            {
                yield return new FakeAttributeInfo
                {
                    Name = "Value",
                    SupportedTypes = _supportedTypes
                };
            }
        }

        private sealed class FakeAttributeInfo
        {
            public string Name { get; set; } = string.Empty;
            public IReadOnlyList<Type> SupportedTypes { get; set; } = Array.Empty<Type>();
        }

        private enum FakeEvent { Click, Change, Loaded }
        private sealed class FakeEventOwner { public FakeEventComposition EventHandlers { get; } = new FakeEventComposition(); }
        private sealed class FakeEventComposition : IEnumerable
        {
            internal static int CreateCount;
            internal List<FakeEventHandler> Entries { get; } = new List<FakeEventHandler>();
            internal int CreateCalls { get; private set; }
            internal int DeleteCalls { get; private set; }
            internal int WriteCalls => System.Linq.Enumerable.Sum(Entries, entry => entry.Script.WriteCount);

            internal void Add(FakeEvent eventType, string scriptCode) => Entries.Add(new FakeEventHandler(this, eventType, scriptCode));
            internal void ResetCounters()
            {
                CreateCalls = 0;
                DeleteCalls = 0;
                foreach (var entry in Entries) entry.Script.WriteCount = 0;
            }

            public object Create(FakeEvent eventType)
            {
                CreateCount++;
                CreateCalls++;
                var entry = new FakeEventHandler(this, eventType, string.Empty);
                Entries.Add(entry);
                return entry;
            }

            internal void Delete(FakeEventHandler entry) { DeleteCalls++; Entries.Remove(entry); }
            public IEnumerator GetEnumerator() => Entries.GetEnumerator();
        }

        private sealed class FakeEventHandler
        {
            private readonly FakeEventComposition _owner;
            internal FakeEventHandler(FakeEventComposition owner, FakeEvent eventType, string scriptCode)
            {
                _owner = owner;
                EventType = eventType;
                Script = new FakeScript(scriptCode);
            }
            public FakeEvent EventType { get; }
            public FakeScript Script { get; }
            public void Delete() => _owner.Delete(this);
        }

        private sealed class FakePropertyEventComposition : IEnumerable
        {
            internal List<FakePropertyEventHandler> Entries { get; } = new List<FakePropertyEventHandler>();
            internal int CreateCalls { get; private set; }
            internal int DeleteCalls { get; private set; }
            internal int WriteCalls => System.Linq.Enumerable.Sum(Entries, entry => entry.Script.WriteCount);

            internal void Add(string property, FakeEvent eventType, string scriptCode) => Entries.Add(new FakePropertyEventHandler(this, property, eventType, scriptCode));
            internal void ResetCounters()
            {
                CreateCalls = 0;
                DeleteCalls = 0;
                foreach (var entry in Entries) entry.Script.WriteCount = 0;
            }
            public object Create(string property, FakeEvent eventType)
            {
                CreateCalls++;
                var entry = new FakePropertyEventHandler(this, property, eventType, string.Empty);
                Entries.Add(entry);
                return entry;
            }
            internal void Delete(FakePropertyEventHandler entry) { DeleteCalls++; Entries.Remove(entry); }
            public IEnumerator GetEnumerator() => Entries.GetEnumerator();
        }

        private sealed class FakePropertyEventHandler
        {
            private readonly FakePropertyEventComposition _owner;
            internal FakePropertyEventHandler(FakePropertyEventComposition owner, string property, FakeEvent eventType, string scriptCode)
            {
                _owner = owner;
                PropertyName = property;
                EventType = eventType;
                Script = new FakeScript(scriptCode);
            }
            public string PropertyName { get; }
            public FakeEvent EventType { get; }
            public FakeScript Script { get; }
            public void Delete() => _owner.Delete(this);
        }

        private sealed class FakeScript
        {
            private string? _scriptCode;
            internal FakeScript(string? scriptCode) { _scriptCode = scriptCode; }
            internal int WriteCount { get; set; }
            public bool? Async { get; set; }
            public string? GlobalDefinitionAreaScriptCode { get; set; }
            public string? ScriptCode
            {
                get => _scriptCode;
                set { _scriptCode = value; WriteCount++; }
            }
        }
    }
}
