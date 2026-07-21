using System.IO;
using CUE4Parse.UE4.Assets.Exports;
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
        var function = _assets.LoadPackageExportsWithScriptData(FactionSetupPackage)
                           .OfType<UFunction>()
                           .FirstOrDefault(candidate => candidate.Name.Equals(
                               "GetUnitTypeIcon", StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidDataException(
                           $"Function GetUnitTypeIcon was not found in '{FactionSetupPackage}'.");

        var namesByValue = ReadTypeNames(function);
        var iconsByValue = ReadTypeIcons(function);
        var descriptors = new Dictionary<string, UnitTypeDescriptor>(StringComparer.OrdinalIgnoreCase);
        string? zeroValueName = null;

        foreach (var (name, value) in enumAsset.Names)
        {
            var memberName = EnumMemberName(name.Text)!;
            if (memberName.EndsWith("_MAX", StringComparison.OrdinalIgnoreCase)) continue;
            if (value == 0) zeroValueName = memberName;
            if (!namesByValue.TryGetValue((byte)value, out var displayName) ||
                !iconsByValue.TryGetValue((byte)value, out var iconPath))
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
