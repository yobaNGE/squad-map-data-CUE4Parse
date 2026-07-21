using System.Collections;
using System.Globalization;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;

namespace Squad_pipeline_map_data_CUE4Parse.Infrastructure;

public sealed class UnrealPropertyReader(IGameAssetProvider assets)
{
    public object? Raw(IPropertyHolder? holder, params string[] names)
    {
        if (holder is null) return null;
        foreach (var name in names)
        {
            var property = holder.Properties.FirstOrDefault(candidate =>
                candidate.Name.Text.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (property?.Tag is not null) return Unwrap(property.Tag.GenericValue);
        }
        return null;
    }

    public object? RawStartingWith(IPropertyHolder? holder, string prefix)
    {
        var property = holder?.Properties.FirstOrDefault(candidate =>
            candidate.Name.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return property?.Tag is null ? null : Unwrap(property.Tag.GenericValue);
    }

    public string String(IPropertyHolder? holder, string defaultValue, params string[] names) =>
        ToStringValue(Raw(holder, names)) ?? defaultValue;

    public object? RawInherited(UObject? holder, params string[] names)
    {
        foreach (var source in InheritanceChain(holder))
        {
            var value = Raw(source, names);
            if (value is not null) return value;
        }
        return null;
    }

    public string StringInherited(UObject? holder, string defaultValue, params string[] names) =>
        ToStringValue(RawInherited(holder, names)) ?? defaultValue;

    public int IntInherited(UObject? holder, int defaultValue, params string[] names) =>
        ToInt(RawInherited(holder, names)) ?? defaultValue;

    public int Int(IPropertyHolder? holder, int defaultValue, params string[] names) =>
        ToInt(Raw(holder, names)) ?? defaultValue;

    public double Double(IPropertyHolder? holder, double defaultValue, params string[] names) =>
        ToDouble(Raw(holder, names)) ?? defaultValue;

    public double DoubleInherited(UObject? holder, double defaultValue, params string[] names) =>
        ToDouble(RawInherited(holder, names)) ?? defaultValue;

    public bool Bool(IPropertyHolder? holder, bool defaultValue, params string[] names) =>
        ToBool(Raw(holder, names)) ?? defaultValue;

    public bool BoolInherited(UObject? holder, bool defaultValue, params string[] names) =>
        ToBool(RawInherited(holder, names)) ?? defaultValue;

    public IReadOnlyList<object?> Array(IPropertyHolder? holder, params string[] names)
    {
        var value = Raw(holder, names);
        if (value is IReadOnlyList<object?> list) return list;
        if (value is IEnumerable sequence and not string)
            return sequence.Cast<object?>().Select(Unwrap).ToArray();
        return [];
    }

    public IReadOnlyList<object?> ArrayStartingWith(IPropertyHolder? holder, string prefix)
    {
        var value = RawStartingWith(holder, prefix);
        if (value is IReadOnlyList<object?> list) return list;
        if (value is IEnumerable sequence and not string)
            return sequence.Cast<object?>().Select(Unwrap).ToArray();
        return [];
    }

    public IReadOnlyList<string> StringArray(IPropertyHolder? holder, params string[] names) =>
        Array(holder, names).Select(ToStringValue).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray();

    public IReadOnlyList<object?> ArrayInherited(UObject? holder, params string[] names)
    {
        var value = RawInherited(holder, names);
        if (value is IReadOnlyList<object?> list) return list;
        if (value is IEnumerable sequence and not string)
            return sequence.Cast<object?>().Select(Unwrap).ToArray();
        return [];
    }

    public IReadOnlyList<KeyValuePair<object?, object?>> Map(IPropertyHolder? holder, params string[] names) =>
        ToMap(Raw(holder, names));

    public IReadOnlyList<KeyValuePair<object?, object?>> MapInherited(UObject? holder, params string[] names) =>
        ToMap(RawInherited(holder, names));

    public UObject? Object(IPropertyHolder? holder, params string[] names) => ResolveObject(Raw(holder, names));

    public UObject? ObjectInherited(UObject? holder, params string[] names) => ResolveObject(RawInherited(holder, names));

    public UObject? ResolveObject(object? value)
    {
        value = Unwrap(value);
        switch (value)
        {
            case UObject export:
                return export;
            case FPackageIndex index when index.TryLoad<UObject>(out var export):
                return export;
            case FSoftObjectPath path:
                return path.TryLoad<UObject>(out var softExport)
                    ? softExport
                    : assets.LoadObject(path.AssetPathName.Text);
            case string text when !string.IsNullOrWhiteSpace(text):
                return assets.LoadObject(text);
            default:
                return null;
        }
    }

    public Vec3 Vector(IPropertyHolder? holder, string name, Vec3? defaultValue = null) =>
        ToVector(Raw(holder, name)) ?? defaultValue ?? Vec3.Zero;

    public Vec3 VectorInherited(UObject? holder, string name, Vec3? defaultValue = null) =>
        ToVector(RawInherited(holder, name)) ?? defaultValue ?? Vec3.Zero;

    public Rotator Rotation(IPropertyHolder? holder, string name, Rotator? defaultValue = null) =>
        ToRotation(Raw(holder, name)) ?? defaultValue ?? Rotator.Zero;

    public Rotator RotationInherited(UObject? holder, string name, Rotator? defaultValue = null) =>
        ToRotation(RawInherited(holder, name)) ?? defaultValue ?? Rotator.Zero;

    public IPropertyHolder? Struct(IPropertyHolder? holder, params string[] names) => Raw(holder, names) as IPropertyHolder;

    public IReadOnlyList<UObject> InheritanceChain(UObject? source)
    {
        var result = new List<UObject>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(source);
        return result;

        void Add(UObject? current)
        {
            if (current is null || !visited.Add(current.GetPathName())) return;
            result.Add(current);

            if (current.Template?.TryLoad(out UObject? template) == true) Add(template);
            if (current is UClass directClass) AddClassDefaults(directClass);
            if (current.Class?.TryLoad(out UObject? loadedClass) == true && loadedClass is UClass unrealClass)
                AddClassDefaults(unrealClass);
        }

        void AddClassDefaults(UClass unrealClass)
        {
            if (!visitedClasses.Add(unrealClass.GetPathName())) return;
            if (unrealClass.ClassDefaultObject?.TryLoad(out UObject? classDefaultObject) == true)
                Add(classDefaultObject);
            else
                Add(assets.LoadObject(InferClassDefaultObjectPath(unrealClass)));
            if (unrealClass.SuperStruct?.TryLoad<UClass>(out var superClass) == true)
                AddClassDefaults(superClass);
        }
    }

    private static string InferClassDefaultObjectPath(UClass unrealClass)
    {
        var classPath = unrealClass.GetPathName();
        var separator = classPath.LastIndexOf('.');
        if (separator < 0) return string.Empty;
        var className = classPath[(separator + 1)..];
        return $"{classPath[..separator]}.Default__{className}";
    }

    public static object? Unwrap(object? value) => value switch
    {
        null => null,
        FPropertyTagType property => Unwrap(property.GenericValue),
        FScriptStruct script => Unwrap(script.StructType),
        UScriptArray array => array.Properties.Select(item => Unwrap(item.GenericValue)).ToArray(),
        UScriptSet set => set.Properties.Select(item => Unwrap(item.GenericValue)).ToArray(),
        _ => value
    };

    public static string? ToStringValue(object? value)
    {
        value = Unwrap(value);
        return value switch
        {
            null => null,
            string text => text,
            FText text => text.Text,
            FName name => name.IsNone ? null : name.Text,
            FSoftObjectPath path => path.AssetPathName.IsNone ? null : path.AssetPathName.Text,
            Enum enumeration => enumeration.ToString(),
            _ => value.ToString()
        };
    }

    public static int? ToInt(object? value)
    {
        value = Unwrap(value);
        if (value is null) return null;
        try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch (Exception) when (value is not string) { return null; }
    }

    public static double? ToDouble(object? value)
    {
        value = Unwrap(value);
        if (value is null) return null;
        try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
        catch (Exception) when (value is not string) { return null; }
    }

    public static bool? ToBool(object? value)
    {
        value = Unwrap(value);
        if (value is bool boolean) return boolean;
        if (value is byte number) return number != 0;
        return bool.TryParse(value?.ToString(), out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<KeyValuePair<object?, object?>> ToMap(object? value)
    {
        value = Unwrap(value);
        if (value is UScriptMap scriptMap)
            return scriptMap.Properties.Select(pair =>
                    new KeyValuePair<object?, object?>(
                        Unwrap(pair.Key.GenericValue),
                        Unwrap(pair.Value?.GenericValue)))
                .ToArray();
        if (value is IDictionary dictionary)
            return dictionary.Cast<DictionaryEntry>()
                .Select(pair => new KeyValuePair<object?, object?>(Unwrap(pair.Key), Unwrap(pair.Value)))
                .ToArray();
        return [];
    }

    public static Vec3? ToVector(object? value)
    {
        value = Unwrap(value);
        if (value is FVector vector) return new Vec3(vector.X, vector.Y, vector.Z);
        if (value is IPropertyHolder holder)
        {
            var reader = new HolderReader(holder);
            return new Vec3(reader.Double("X"), reader.Double("Y"), reader.Double("Z"));
        }
        return null;
    }

    public static Rotator? ToRotation(object? value)
    {
        value = Unwrap(value);
        if (value is FRotator rotator) return new Rotator(rotator.Pitch, rotator.Yaw, rotator.Roll);
        if (value is IPropertyHolder holder)
        {
            var reader = new HolderReader(holder);
            return new Rotator(reader.Double("Pitch"), reader.Double("Yaw"), reader.Double("Roll"));
        }
        return null;
    }

    private readonly struct HolderReader(IPropertyHolder holder)
    {
        public double Double(string name)
        {
            var value = holder.Properties.FirstOrDefault(property =>
                property.Name.Text.Equals(name, StringComparison.OrdinalIgnoreCase))?.Tag?.GenericValue;
            return ToDouble(value) ?? 0;
        }
    }
}

public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 Zero => new(0, 0, 0);
    public static Vec3 One => new(1, 1, 1);
    public static Vec3 operator +(Vec3 left, Vec3 right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    public static Vec3 operator *(Vec3 left, Vec3 right) => new(left.X * right.X, left.Y * right.Y, left.Z * right.Z);
}

public readonly record struct Rotator(double Pitch, double Yaw, double Roll)
{
    public static Rotator Zero => new(0, 0, 0);
}
