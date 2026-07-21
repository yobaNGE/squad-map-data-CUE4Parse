using System.Collections;
using System.Text.Json.Nodes;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.EdGraph;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.UObject;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AssetInspector;

internal sealed class AssetGraphInspector(int maxDepth, int maxItems)
{
    private readonly HashSet<string> _visited = new(StringComparer.OrdinalIgnoreCase);

    public JsonObject Inspect(UObject export) => InspectObject(export, 0);

    private JsonObject InspectObject(UObject export, int depth)
    {
        var path = export.GetPathName();
        var node = new JsonObject
        {
            ["path"] = path,
            ["name"] = export.Name,
            ["exportType"] = export.ExportType,
            ["runtimeType"] = export.GetType().FullName
        };

        if (!_visited.Add(path))
        {
            node["cycle"] = true;
            return node;
        }

        if (export.Class is not null)
            node["class"] = InspectResolvedReference(export.Class, depth);
        if (export.Template is not null)
            node["template"] = InspectResolvedReference(export.Template, depth);
        if (export.Super is not null)
            node["super"] = InspectResolvedReference(export.Super, depth);
        if (export is UStruct { SuperStruct.IsNull: false } unrealStruct)
            node["superStruct"] = InspectPackageIndex(unrealStruct.SuperStruct, depth);
        if (export is UClass unrealClass && unrealClass.ClassDefaultObject is { IsNull: false } defaultObject)
            node["classDefaultObject"] = InspectPackageIndex(defaultObject, depth);

        var properties = new JsonObject();
        foreach (var property in export.Properties.Take(maxItems))
            properties[property.Name.Text] = InspectValue(property.Tag?.GenericValue, depth);
        node["properties"] = properties;

        if (export is UDataTable dataTable)
        {
            var rows = new JsonObject();
            foreach (var row in dataTable.RowMap.Take(maxItems))
                rows[row.Key.Text] = InspectHolder(row.Value, depth);
            node["rows"] = rows;
            if (dataTable.RowMap.Count > maxItems)
                node["omittedRows"] = dataTable.RowMap.Count - maxItems;
        }

        if (export is UEnum unrealEnum)
        {
            var names = new JsonArray();
            foreach (var (name, value) in unrealEnum.Names.Take(maxItems))
            {
                names.Add(new JsonObject
                {
                    ["name"] = name.Text,
                    ["value"] = value
                });
            }
            node["enumNames"] = names;
        }

        if (export is UStruct { ScriptBytecode.Length: > 0 } script)
        {
            var serializer = Newtonsoft.Json.JsonSerializer.CreateDefault();
            var token = JToken.FromObject(script.ScriptBytecode, serializer);
            node["scriptBytecode"] = JsonNode.Parse(token.ToString(Formatting.None));
        }

        if (export is UEdGraphNode { Pins.Length: > 0 } graphNode)
        {
            var serializer = Newtonsoft.Json.JsonSerializer.CreateDefault();
            var token = JToken.FromObject(graphNode.Pins, serializer);
            node["pins"] = JsonNode.Parse(token.ToString(Formatting.None));
        }

        if (export.Properties.Count > maxItems)
            node["omittedProperties"] = export.Properties.Count - maxItems;
        return node;
    }

    private JsonNode? InspectValue(object? value, int depth)
    {
        if (value is FPropertyTagType property) return InspectValue(property.GenericValue, depth);
        if (value is FScriptStruct scriptStruct) return InspectValue(scriptStruct.StructType, depth);

        return value switch
        {
            null => null,
            UObject export => InspectReference(export.GetPathName(), export, depth),
            FPackageIndex index => InspectPackageIndex(index, depth),
            FSoftObjectPath path => InspectSoftPath(path, depth),
            IPropertyHolder holder => InspectHolder(holder, depth),
            UScriptArray array => InspectSequence(array.Properties.Select(item => item.GenericValue), depth),
            UScriptSet set => InspectSequence(set.Properties.Select(item => item.GenericValue), depth),
            UScriptMap map => InspectMap(map, depth),
            IDictionary dictionary => InspectDictionary(dictionary, depth),
            IEnumerable sequence when value is not string => InspectSequence(sequence.Cast<object?>(), depth),
            string text => JsonValue.Create(text),
            FName name => JsonValue.Create(name.Text),
            FText text => JsonValue.Create(text.Text),
            bool boolean => JsonValue.Create(boolean),
            byte number => JsonValue.Create(number),
            sbyte number => JsonValue.Create(number),
            short number => JsonValue.Create(number),
            ushort number => JsonValue.Create(number),
            int number => JsonValue.Create(number),
            uint number => JsonValue.Create(number),
            long number => JsonValue.Create(number),
            ulong number => JsonValue.Create(number),
            float number => JsonValue.Create(number),
            double number => JsonValue.Create(number),
            decimal number => JsonValue.Create(number),
            Enum enumeration => JsonValue.Create(enumeration.ToString()),
            _ => new JsonObject
            {
                ["value"] = value.ToString(),
                ["runtimeType"] = value.GetType().FullName
            }
        };
    }

    private JsonObject InspectPackageIndex(FPackageIndex index, int depth)
    {
        UObject? resolved = null;
        if (depth < maxDepth) index.TryLoad(out resolved);
        return InspectReference(index.ResolvedObject?.GetFullName() ?? index.ToString(), resolved, depth);
    }

    private JsonObject InspectResolvedReference(CUE4Parse.UE4.Assets.ResolvedObject reference, int depth)
    {
        UObject? resolved = null;
        if (depth < maxDepth) reference.TryLoad(out resolved);
        return InspectReference(reference.GetFullName(), resolved, depth);
    }

    private JsonObject InspectSoftPath(FSoftObjectPath path, int depth)
    {
        UObject? resolved = null;
        if (depth < maxDepth) path.TryLoad(out resolved);
        return InspectReference(path.AssetPathName.Text, resolved, depth);
    }

    private JsonObject InspectReference(string path, UObject? resolved, int depth)
    {
        var node = new JsonObject { ["reference"] = path };
        if (resolved is not null && depth < maxDepth)
            node["resolved"] = InspectObject(resolved, depth + 1);
        return node;
    }

    private JsonObject InspectHolder(IPropertyHolder holder, int depth)
    {
        var properties = new JsonObject();
        foreach (var property in holder.Properties.Take(maxItems))
            properties[property.Name.Text] = InspectValue(property.Tag?.GenericValue, depth);
        return new JsonObject
        {
            ["runtimeType"] = holder.GetType().FullName,
            ["properties"] = properties
        };
    }

    private JsonArray InspectSequence(IEnumerable<object?> values, int depth)
    {
        var result = new JsonArray();
        foreach (var value in values.Take(maxItems))
            result.Add(InspectValue(value, depth));
        return result;
    }

    private JsonArray InspectMap(UScriptMap map, int depth)
    {
        var result = new JsonArray();
        foreach (var pair in map.Properties.Take(maxItems))
        {
            result.Add(new JsonObject
            {
                ["key"] = InspectValue(pair.Key.GenericValue, depth),
                ["keyRuntimeType"] = pair.Key.GenericValue?.GetType().FullName,
                ["value"] = InspectValue(pair.Value?.GenericValue, depth)
            });
        }
        return result;
    }

    private JsonArray InspectDictionary(IDictionary dictionary, int depth)
    {
        var result = new JsonArray();
        foreach (DictionaryEntry pair in dictionary.Cast<DictionaryEntry>().Take(maxItems))
        {
            result.Add(new JsonObject
            {
                ["key"] = InspectValue(pair.Key, depth),
                ["value"] = InspectValue(pair.Value, depth)
            });
        }
        return result;
    }
}
