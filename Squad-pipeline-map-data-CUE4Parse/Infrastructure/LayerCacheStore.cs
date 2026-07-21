using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Squad_pipeline_map_data_CUE4Parse.Application;
using Squad_pipeline_map_data_CUE4Parse.Configuration;
using Squad_pipeline_map_data_CUE4Parse.Domain;

namespace Squad_pipeline_map_data_CUE4Parse.Infrastructure;

public enum SourceCacheStatus
{
    Missing,
    Ready,
    Stale,
    Disabled
}

public sealed record SourceCacheState(
    SourceCacheStatus Status,
    string CachedVersion,
    long Size,
    int LayerCount,
    int MaterializedLayerCount);

public sealed class LayerCacheStore
{
    public const int SchemaVersion = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new LayerObjectiveCacheConverter() }
    };

    private static readonly JsonSerializerOptions ArtifactJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _root;
    private readonly object _sync = new();

    public LayerCacheStore()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SquadPipeline",
            "cache");
        }

    public string BuildSourceKey(
        InstalledContentSource source,
        InstalledContentSource vanilla,
        string mappingsSignature) => Hash(
        $"{SchemaVersion}|{source.Id}|{source.Revision}|{vanilla.Revision}|{mappingsSignature}");

    public string BuildEnvironmentKey(
        IEnumerable<InstalledContentSource> enabledSources,
        string mappingsSignature) => Hash(
        $"{SchemaVersion}|{mappingsSignature}|" + string.Join('|', enabledSources
            .Where(source => source.Enabled)
            .OrderBy(source => source.Id, StringComparer.OrdinalIgnoreCase)
            .Select(source => $"{source.Id}:{source.Revision}")));

    public SourceCacheState GetState(
        InstalledContentSource source,
        string sourceKey)
    {
        var path = DatabasePath(source.Id);
        if (!File.Exists(path))
            return new SourceCacheState(
                source.Enabled ? SourceCacheStatus.Missing : SourceCacheStatus.Disabled,
                string.Empty,
                0,
                0,
                0);

        lock (_sync)
        {
            try
            {
                using var connection = Open(path, SqliteOpenMode.ReadOnly);
                var cachedKey = ReadInfo(connection, "SourceKey") ?? string.Empty;
                var cachedVersion = ReadInfo(connection, "InstalledVersion") ?? string.Empty;
                var invalidated = ReadInfo(connection, "Invalidated") == "1";
                var (layers, materialized) = ReadCounts(connection);
                var status = !source.Enabled
                    ? SourceCacheStatus.Disabled
                    : !invalidated && cachedKey.Equals(sourceKey, StringComparison.Ordinal)
                        ? SourceCacheStatus.Ready
                        : SourceCacheStatus.Stale;
                return new SourceCacheState(status, cachedVersion, ReadSize(path, source.Id), layers, materialized);
            }
            catch (SqliteException)
            {
                return new SourceCacheState(
                    source.Enabled ? SourceCacheStatus.Stale : SourceCacheStatus.Disabled,
                    string.Empty,
                    ReadSize(path, source.Id),
                    0,
                    0);
            }
        }
    }

    public IReadOnlyList<LayerDescriptor> LoadCatalog(
        InstalledContentSource source,
        string sourceKey)
    {
        if (GetState(source, sourceKey).Status != SourceCacheStatus.Ready) return [];

        lock (_sync)
        {
            using var connection = Open(DatabasePath(source.Id), SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Name, GameplayPackagePath, GameplayObjectName, WorldObjectPath,
                       LayerVersion, MapId, GameMode, SourceId, SourceName, SourceIsVanilla
                FROM Layers
                ORDER BY Name COLLATE NOCASE
                """;
            using var reader = command.ExecuteReader();
            var result = new List<LayerDescriptor>();
            while (reader.Read())
            {
                result.Add(new LayerDescriptor(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    new ContentSource(reader.GetString(7), reader.GetString(8), reader.GetBoolean(9))));
            }
            return result;
        }
    }

    public void SaveCatalog(
        InstalledContentSource source,
        string sourceKey,
        IReadOnlyList<LayerDescriptor> layers)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(_root);
            using var connection = Open(DatabasePath(source.Id));
            EnsureSchema(connection);
            using var transaction = connection.BeginTransaction();
            var cachedKey = ReadInfo(connection, "SourceKey", transaction);
            if (!sourceKey.Equals(cachedKey, StringComparison.Ordinal))
            {
                Execute(connection, transaction, "DELETE FROM Layers");
                Execute(connection, transaction, "DELETE FROM CacheInfo");
                var artifacts = ArtifactSourceDirectory(source.Id);
                if (Directory.Exists(artifacts)) Directory.Delete(artifacts, true);
            }

            var currentIds = layers.Select(layer => layer.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ReadLayerIds(connection, transaction).Where(id => !currentIds.Contains(id)))
                Execute(connection, transaction, "DELETE FROM Layers WHERE Id = $id", ("$id", id));

            foreach (var layer in layers)
                UpsertLayer(connection, transaction, layer);

            WriteInfo(connection, transaction, "SchemaVersion", SchemaVersion.ToString());
            WriteInfo(connection, transaction, "SourceKey", sourceKey);
            WriteInfo(connection, transaction, "InstalledVersion", source.Version);
            WriteInfo(connection, transaction, "SourceName", source.Name);
            WriteInfo(connection, transaction, "Invalidated", "0");
            WriteInfo(connection, transaction, "UpdatedUtc", DateTime.UtcNow.ToString("O"));
            transaction.Commit();
        }
    }

    public bool TryReadMetadata(
        LayerDescriptor layer,
        string environmentKey,
        out LayerMetadata? metadata)
    {
        metadata = null;
        var path = DatabasePath(layer.Source.Id);
        if (!File.Exists(path)) return false;

        lock (_sync)
        {
            using var connection = Open(path, SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT MetadataArtifactPath, MetadataJson
                FROM Layers
                WHERE Id = $id AND MetadataEnvironmentKey = $environmentKey
                """;
            command.Parameters.AddWithValue("$id", layer.Id);
            command.Parameters.AddWithValue("$environmentKey", environmentKey);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return false;
            var artifactPath = reader.IsDBNull(0) ? null : reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(artifactPath) && File.Exists(artifactPath))
            {
                using var stream = File.OpenRead(artifactPath);
                metadata = JsonSerializer.Deserialize<LayerMetadata>(stream, ArtifactJsonOptions);
                return metadata is not null;
            }

            var json = reader.IsDBNull(1) ? null : reader.GetString(1);
            if (string.IsNullOrWhiteSpace(json)) return false;
            metadata = JsonSerializer.Deserialize<LayerMetadata>(json, JsonOptions);
            return metadata is not null;
        }
    }

    public bool TryGetMetadataArtifact(
        LayerDescriptor layer,
        string environmentKey,
        out string? artifactPath)
    {
        artifactPath = null;
        var databasePath = DatabasePath(layer.Source.Id);
        if (!File.Exists(databasePath)) return false;

        lock (_sync)
        {
            using var connection = Open(databasePath, SqliteOpenMode.ReadOnly);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT MetadataArtifactPath
                FROM Layers
                WHERE Id = $id AND MetadataEnvironmentKey = $environmentKey
                """;
            command.Parameters.AddWithValue("$id", layer.Id);
            command.Parameters.AddWithValue("$environmentKey", environmentKey);
            artifactPath = command.ExecuteScalar() as string;
            return !string.IsNullOrWhiteSpace(artifactPath) && File.Exists(artifactPath);
        }
    }

    public async Task WriteMetadataAsync(
        LayerDescriptor layer,
        string environmentKey,
        LayerMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var databasePath = DatabasePath(layer.Source.Id);
        if (!File.Exists(databasePath)) return;

        var artifactPath = ArtifactPath(layer, environmentKey);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        var temporaryPath = artifactPath + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
                await JsonSerializer.SerializeAsync(stream, metadata, ArtifactJsonOptions, cancellationToken);
            File.Move(temporaryPath, artifactPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }

        lock (_sync)
        {
            using var connection = Open(databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE Layers
                SET MetadataJson = NULL,
                    MetadataArtifactPath = $artifactPath,
                    MetadataEnvironmentKey = $environmentKey
                WHERE Id = $id
                """;
            command.Parameters.AddWithValue("$artifactPath", artifactPath);
            command.Parameters.AddWithValue("$environmentKey", environmentKey);
            command.Parameters.AddWithValue("$id", layer.Id);
            command.ExecuteNonQuery();
        }
    }

    public void Invalidate(string sourceId)
    {
        var path = DatabasePath(sourceId);
        if (!File.Exists(path)) return;
        lock (_sync)
        {
            using var connection = Open(path);
            EnsureSchema(connection);
            using var transaction = connection.BeginTransaction();
            WriteInfo(connection, transaction, "Invalidated", "1");
            transaction.Commit();
        }
    }

    public void Clear(string sourceId)
    {
        lock (_sync)
        {
            SqliteConnection.ClearAllPools();
            var path = DatabasePath(sourceId);
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
            var artifactDirectory = ArtifactSourceDirectory(sourceId);
            if (Directory.Exists(artifactDirectory)) Directory.Delete(artifactDirectory, true);
        }
    }

    private string DatabasePath(string sourceId)
    {
        var safeId = new string(sourceId.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
        return Path.Combine(_root, sourceId.Equals("vanilla", StringComparison.OrdinalIgnoreCase)
            ? "vanilla.db"
            : $"mod-{safeId}.db");
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode = SqliteOpenMode.ReadWriteCreate)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS CacheInfo (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Layers (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                GameplayPackagePath TEXT NOT NULL,
                GameplayObjectName TEXT NOT NULL,
                WorldObjectPath TEXT NOT NULL,
                LayerVersion TEXT NOT NULL,
                MapId TEXT NOT NULL,
                GameMode TEXT NOT NULL,
                SourceId TEXT NOT NULL,
                SourceName TEXT NOT NULL,
                SourceIsVanilla INTEGER NOT NULL,
                MetadataJson TEXT NULL,
                MetadataEnvironmentKey TEXT NULL,
                MetadataArtifactPath TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Layers_MapMode ON Layers(MapId, GameMode);
            """;
        command.ExecuteNonQuery();

        if (!HasColumn(connection, "Layers", "MetadataArtifactPath"))
        {
            using var migration = connection.CreateCommand();
            migration.CommandText = "ALTER TABLE Layers ADD COLUMN MetadataArtifactPath TEXT NULL";
            migration.ExecuteNonQuery();
        }
    }

    private static void UpsertLayer(SqliteConnection connection, SqliteTransaction transaction, LayerDescriptor layer)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Layers (
                Id, Name, GameplayPackagePath, GameplayObjectName, WorldObjectPath,
                LayerVersion, MapId, GameMode, SourceId, SourceName, SourceIsVanilla)
            VALUES (
                $id, $name, $package, $object, $world,
                $version, $map, $mode, $sourceId, $sourceName, $isVanilla)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                GameplayPackagePath = excluded.GameplayPackagePath,
                GameplayObjectName = excluded.GameplayObjectName,
                WorldObjectPath = excluded.WorldObjectPath,
                LayerVersion = excluded.LayerVersion,
                MapId = excluded.MapId,
                GameMode = excluded.GameMode,
                SourceId = excluded.SourceId,
                SourceName = excluded.SourceName,
                SourceIsVanilla = excluded.SourceIsVanilla
            """;
        command.Parameters.AddWithValue("$id", layer.Id);
        command.Parameters.AddWithValue("$name", layer.Name);
        command.Parameters.AddWithValue("$package", layer.GameplayPackagePath);
        command.Parameters.AddWithValue("$object", layer.GameplayObjectName);
        command.Parameters.AddWithValue("$world", layer.WorldObjectPath);
        command.Parameters.AddWithValue("$version", layer.Version);
        command.Parameters.AddWithValue("$map", layer.MapId);
        command.Parameters.AddWithValue("$mode", layer.GameMode);
        command.Parameters.AddWithValue("$sourceId", layer.Source.Id);
        command.Parameters.AddWithValue("$sourceName", layer.Source.DisplayName);
        command.Parameters.AddWithValue("$isVanilla", layer.Source.IsVanilla);
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<string> ReadLayerIds(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id FROM Layers";
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    private static (int Layers, int Materialized) ReadCounts(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), COUNT(COALESCE(MetadataArtifactPath, MetadataJson)) FROM Layers";
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt32(0), reader.GetInt32(1)) : (0, 0);
    }

    private static string? ReadInfo(
        SqliteConnection connection,
        string key,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Value FROM CacheInfo WHERE Key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private static void WriteInfo(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CacheInfo(Key, Value) VALUES($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        command.ExecuteNonQuery();
    }

    private long ReadSize(string path, string sourceId)
    {
        var databaseSize = new[] { path, path + "-wal", path + "-shm" }
        .Where(File.Exists)
        .Sum(candidate => new FileInfo(candidate).Length);
        var artifactDirectory = ArtifactSourceDirectory(sourceId);
        return databaseSize + (Directory.Exists(artifactDirectory)
            ? Directory.EnumerateFiles(artifactDirectory, "*.json", SearchOption.AllDirectories)
                .Sum(candidate => new FileInfo(candidate).Length)
            : 0);
    }

    private string ArtifactPath(LayerDescriptor layer, string environmentKey) => Path.Combine(
        ArtifactSourceDirectory(layer.Source.Id),
        environmentKey,
        Hash(layer.Id) + ".json");

    private string ArtifactSourceDirectory(string sourceId)
    {
        var safeId = new string(sourceId.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
        return Path.Combine(_root, "artifacts", safeId);
    }

    private static bool HasColumn(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class LayerObjectiveCacheConverter : JsonConverter<LayerObjective>
    {
        public override LayerObjective Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var payload = root.GetProperty("value").GetRawText();
            return root.GetProperty("type").GetString() switch
            {
                "actor" => JsonSerializer.Deserialize<ObjectiveActor>(payload, options)
                           ?? throw new JsonException("Cached objective actor is empty."),
                "cluster" => JsonSerializer.Deserialize<ObjectiveCluster>(payload, options)
                             ?? throw new JsonException("Cached objective cluster is empty."),
                var type => throw new JsonException($"Unknown cached objective type '{type}'.")
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            LayerObjective value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            switch (value)
            {
                case ObjectiveActor actor:
                    writer.WriteString("type", "actor");
                    writer.WritePropertyName("value");
                    JsonSerializer.Serialize(writer, actor, options);
                    break;
                case ObjectiveCluster cluster:
                    writer.WriteString("type", "cluster");
                    writer.WritePropertyName("value");
                    JsonSerializer.Serialize(writer, cluster, options);
                    break;
                default:
                    throw new JsonException($"Unsupported objective type '{value.GetType().Name}'.");
            }
            writer.WriteEndObject();
        }
    }
}
