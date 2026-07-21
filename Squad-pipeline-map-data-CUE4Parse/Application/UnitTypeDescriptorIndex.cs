using System.IO;
using System.Text;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.EdGraph;
using CUE4Parse.UE4.Kismet;
using CUE4Parse.UE4.Objects.UObject;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class UnitTypeDescriptorIndex
{
    private const string EnumPath =
        "/Game/Settings/FactionSetups/ESQFactionSetupType.ESQFactionSetupType";
    private const string FactionSetupPackage =
        "SquadGame/Content/Settings/FactionSetups/BP_SQFactionSetup.uasset";

    private readonly IGameAssetProvider _assets;
    private readonly Index _index;

    public UnitTypeDescriptorIndex(IGameAssetProvider assets)
    {
        _assets = assets;
        _index = Build();
    }

    public UnitTypeDescriptor Resolve(string? serializedType)
    {
        var index = _index;
        var key = EnumMemberName(serializedType) ?? index.ZeroValueName;
        return index.Descriptors.TryGetValue(key, out var descriptor)
            ? descriptor
            : throw new InvalidDataException($"Unknown ESQFactionSetupType value '{serializedType}'.");
    }

    private Index Build()
    {
        var enumAsset = _assets.LoadObject(EnumPath) as UEnum
                        ?? throw new InvalidDataException($"Unable to load '{EnumPath}'.");
        var exports = _assets.LoadPackageExportsWithScriptData(FactionSetupPackage);
        var function = exports.OfType<UFunction>()
                           .FirstOrDefault(candidate => candidate.Name.Equals(
                               "GetUnitTypeIcon", StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidDataException(
                           $"Function GetUnitTypeIcon was not found in '{FactionSetupPackage}'.");

        IReadOnlyDictionary<string, string> namesByMember;
        IReadOnlyDictionary<string, string> iconsByMember;
        if (function.ScriptBytecode is { Length: > 0 })
        {
            namesByMember = ByEnumMember(enumAsset, ReadTypeNames(function));
            iconsByMember = ByEnumMember(enumAsset, ReadTypeIcons(function));
        }
        else
        {
            var select = exports.OfType<UK2Node_Select>()
                             .SingleOrDefault(candidate => IsGetUnitTypeIconNode(candidate.GetPathName()))
                         ?? throw new InvalidDataException("GetUnitTypeIcon editor graph has no select node.");
            namesByMember = ReadEditorTypeNames(exports);
            iconsByMember = ReadEditorTypeIcons(select);
        }
        var descriptors = new Dictionary<string, UnitTypeDescriptor>(StringComparer.OrdinalIgnoreCase);
        string? zeroValueName = null;

        foreach (var (name, value) in enumAsset.Names)
        {
            var memberName = EnumMemberName(name.Text)!;
            if (memberName.EndsWith("_MAX", StringComparison.OrdinalIgnoreCase)) continue;
            if (value == 0) zeroValueName = memberName;
            if (!namesByMember.TryGetValue(memberName, out var displayName) ||
                !iconsByMember.TryGetValue(memberName, out var iconPath))
                throw new InvalidDataException(
                    $"GetUnitTypeIcon does not define ESQFactionSetupType value {value} ({memberName}).");

            descriptors[memberName] = new UnitTypeDescriptor(displayName, iconPath);
        }

        return new Index(
            zeroValueName ?? throw new InvalidDataException("ESQFactionSetupType has no zero value."),
            descriptors);
    }

    private static IReadOnlyDictionary<byte, string> ReadTypeNames(UFunction function)
    {
        var map = function.ScriptBytecode.OfType<EX_SetMap>().FirstOrDefault(candidate =>
            candidate.Elements.Length > 0 && candidate.Elements.Length % 2 == 0 &&
            candidate.Elements[0] is EX_ByteConst && candidate.Elements[1] is EX_TextConst)
                  ?? throw new InvalidDataException("GetUnitTypeIcon does not contain its type-name map.");
        var result = new Dictionary<byte, string>();

        for (var index = 0; index < map.Elements.Length; index += 2)
        {
            var key = ((EX_ByteConst)map.Elements[index]).Value;
            var text = (EX_TextConst)map.Elements[index + 1];
            if (text.Value.SourceString is EX_StringConst source)
                result[key] = source.Value;
        }

        return result;
    }

    private IReadOnlyDictionary<string, string> ReadEditorTypeNames(IReadOnlyList<UObject> exports)
    {
        var entry = exports.OfType<UK2Node_FunctionEntry>()
                        .SingleOrDefault(candidate => IsGetUnitTypeIconNode(candidate.GetPathName()))
                    ?? throw new InvalidDataException("GetUnitTypeIcon editor graph has no function entry.");
        var properties = new UnrealPropertyReader(_assets);
        var serializedMap = properties.Array(entry, "LocalVariables")
            .Select(variable => new
            {
                Name = properties.String(variable as IPropertyHolder, string.Empty, "VarName"),
                Value = properties.String(variable as IPropertyHolder, string.Empty, "DefaultValue")
            })
            .SingleOrDefault(variable => variable.Name.Equals("TypeNames", StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(serializedMap))
            throw new InvalidDataException("GetUnitTypeIcon editor graph has no TypeNames default map.");

        return ParseTypeNamesDefault(serializedMap);
    }

    private static IReadOnlyDictionary<string, string> ParseTypeNamesDefault(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        Skip('(');
        while (offset < value.Length && value[offset] != ')')
        {
            Skip('(');
            var member = ReadIdentifier();
            Skip(',');
            if (!ReadIdentifier().Equals("NSLOCTEXT", StringComparison.Ordinal))
                throw new InvalidDataException("TypeNames contains a non-text value.");
            Skip('(');
            ReadQuoted();
            Skip(',');
            ReadQuoted();
            Skip(',');
            result[member] = ReadQuoted();
            Skip(')');
            Skip(')');
            if (offset < value.Length && value[offset] == ',') offset++;
        }
        return result;

        void Skip(char expected)
        {
            while (offset < value.Length && char.IsWhiteSpace(value[offset])) offset++;
            if (offset >= value.Length || value[offset] != expected)
                throw new InvalidDataException($"Invalid TypeNames default value at offset {offset}.");
            offset++;
            while (offset < value.Length && char.IsWhiteSpace(value[offset])) offset++;
        }

        string ReadIdentifier()
        {
            while (offset < value.Length && char.IsWhiteSpace(value[offset])) offset++;
            var start = offset;
            while (offset < value.Length && (char.IsLetterOrDigit(value[offset]) || value[offset] == '_')) offset++;
            return value[start..offset];
        }

        string ReadQuoted()
        {
            Skip('"');
            var text = new StringBuilder();
            while (offset < value.Length && value[offset] != '"')
            {
                var character = value[offset++];
                if (character == '\\' && offset < value.Length)
                {
                    character = value[offset++] switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        var escaped => escaped
                    };
                }
                text.Append(character);
            }
            Skip('"');
            return text.ToString();
        }
    }

    private static IReadOnlyDictionary<string, string> ReadEditorTypeIcons(UK2Node_Select select) =>
        select.Pins.OfType<UEdGraphPin>()
            .Where(pin => pin.PinName.Text.StartsWith("NewEnumerator", StringComparison.OrdinalIgnoreCase)
                          && !string.IsNullOrWhiteSpace(pin.DefaultValue))
            .ToDictionary(pin => pin.PinName.Text, pin => pin.DefaultValue, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> ByEnumMember(
        UEnum enumAsset,
        IReadOnlyDictionary<byte, string> byValue) => enumAsset.Names
        .Where(entry => byValue.ContainsKey((byte)entry.Item2))
        .ToDictionary(
            entry => EnumMemberName(entry.Item1.Text)!,
            entry => byValue[(byte)entry.Item2],
            StringComparer.OrdinalIgnoreCase);

    private static bool IsGetUnitTypeIconNode(string path) =>
        path.Contains(":GetUnitTypeIcon.", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<byte, string> ReadTypeIcons(UFunction function)
    {
        var constants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        EX_SwitchValue? iconSwitch = null;

        foreach (var assignment in function.ScriptBytecode.OfType<EX_Let>())
        {
            var target = VariableName(assignment.Variable);
            if (target is null) continue;

            if (assignment.Assignment is EX_SoftObjectConst { Value: EX_StringConst path })
                constants[target] = path.Value;
            else if (target.Equals("Icon", StringComparison.OrdinalIgnoreCase) &&
                     assignment.Assignment is EX_SwitchValue switchValue)
                iconSwitch = switchValue;
        }

        if (iconSwitch is null)
            throw new InvalidDataException("GetUnitTypeIcon does not contain its icon switch.");

        var result = new Dictionary<byte, string>();
        foreach (var item in iconSwitch.Cases)
        {
            if (item.CaseIndexValueTerm is not EX_ByteConst key) continue;
            var constantName = VariableName(item.CaseTerm);
            if (constantName is not null && constants.TryGetValue(constantName, out var path))
                result[key.Value] = path;
        }
        return result;
    }

    private static string? VariableName(KismetExpression expression) =>
        expression is EX_VariableBase variable ? variable.Variable.ToString() : null;

    private static string? EnumMemberName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var separator = value.LastIndexOf("::", StringComparison.Ordinal);
        return separator >= 0 ? value[(separator + 2)..] : value;
    }

    private sealed record Index(
        string ZeroValueName,
        IReadOnlyDictionary<string, UnitTypeDescriptor> Descriptors);
}

internal sealed record UnitTypeDescriptor(string DisplayName, string IconObjectPath);
