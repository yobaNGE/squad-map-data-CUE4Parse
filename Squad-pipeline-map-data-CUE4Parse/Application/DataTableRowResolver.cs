using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using Squad_pipeline_map_data_CUE4Parse.Infrastructure;

namespace Squad_pipeline_map_data_CUE4Parse.Application;

internal sealed class DataTableRowResolver(UnrealPropertyReader properties)
{
    public ResolvedDataTableRow? Resolve(UObject? asset, string propertyName = "Data")
    {
        string? rowName = null;
        UDataTable? table = null;

        foreach (var source in properties.InheritanceChain(asset))
        {
            var handle = properties.Struct(source, propertyName);
            rowName ??= properties.String(handle, string.Empty, "RowName") is { Length: > 0 } name
                ? name
                : null;
            table ??= properties.Object(handle, "DataTable") as UDataTable;
        }

        return table is not null && rowName is not null &&
               table.TryGetDataTableRow(rowName, StringComparison.OrdinalIgnoreCase, out var row)
            ? new ResolvedDataTableRow(table, rowName, row)
            : null;
    }
}

internal sealed record ResolvedDataTableRow(
    UDataTable Table,
    string RowName,
    IPropertyHolder Row);
