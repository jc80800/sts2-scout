using Microsoft.Data.Sqlite;
using Scout.Core;

namespace Scout.Storage;

public sealed class ScoutDatabase : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly string session = Guid.NewGuid().ToString("N");
    public ScoutDatabase(ScoutPaths paths)
    {
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.FilePath("scout.db"), ForeignKeys = true, Pooling = false }.ToString());
        paths.FilePath("scout.db-journal");
        paths.FilePath("scout.db-wal");
        paths.FilePath("scout.db-shm");
        try
        {
            connection.Open();
            using var version = connection.CreateCommand(); version.CommandText = "PRAGMA user_version";
            var current = Convert.ToInt32(version.ExecuteScalar());
            if (current > 1) throw new InvalidDataException("Database is newer than Scout; use the newer app");
            if (current == 0)
            {
                using var tx = connection.BeginTransaction();
                using var stream = typeof(ScoutDatabase).Assembly.GetManifestResourceStream("Scout.Storage.Migrations.001_initial.sql")!;
                using var reader = new StreamReader(stream);
                using var command = connection.CreateCommand(); command.Transaction = tx; command.CommandText = reader.ReadToEnd(); command.ExecuteNonQuery(); tx.Commit();
            }
        }
        catch { connection.Dispose(); throw; }
    }
    public long Save(Observation observation, StrategyPack pack, RunContext context, Selection[] selections)
    {
        PackValidation.Validate(pack);
        using var tx = connection.BeginTransaction();
        void Execute(string sql, params (string, object?)[] values)
        {
            using var cmd = connection.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
            foreach (var (key, value) in values) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        Execute("INSERT OR IGNORE INTO provenance VALUES($v,$j)", ("$v", pack.PackVersion), ("$j", Json.Write(pack)));
        using (var check = connection.CreateCommand())
        {
            check.Transaction = tx; check.CommandText = "SELECT pack_json FROM provenance WHERE pack_version=$v"; check.Parameters.AddWithValue("$v", pack.PackVersion);
            if ((string?)check.ExecuteScalar() != Json.Write(pack)) throw new InvalidDataException("Pack version collision");
        }
        var known = double.IsFinite(observation.Confidence) && observation.Confidence is >= .9 and <= 1 && Enum.IsDefined(observation.Screen) && observation.Screen != Screen.Unknown;
        Execute("INSERT INTO observations(at,session,screen,confidence,profile_version,frame_hash,pack_version,context_json) VALUES($at,$session,$s,$c,$p,$h,$v,$ctx)", ("$at", observation.At.ToString("O")), ("$session", session), ("$s", known ? observation.Screen.ToString() : "Unknown"), ("$c", known ? observation.Confidence : 0), ("$p", observation.ProfileVersion), ("$h", observation.FrameHash), ("$v", pack.PackVersion), ("$ctx", Json.Write(context)));
        using var idCommand = connection.CreateCommand(); idCommand.Transaction = tx; idCommand.CommandText = "SELECT last_insert_rowid()";
        var id = (long)idCommand.ExecuteScalar()!;
        foreach (var choice in observation.Choices)
        {
            var entityKnown = known && double.IsFinite(choice.Confidence) && choice.Confidence is >= .9 and <= 1 && pack.Entities.Any(e => e.Id == choice.EntityId);
            Execute("INSERT INTO choices VALUES($o,$s,$e,$c,$p,$pc,$u)", ("$o", id), ("$s", choice.Slot), ("$e", entityKnown ? choice.EntityId : null), ("$c", entityKnown ? choice.Confidence : 0), ("$p", entityKnown && choice.PriceConfidence is >= .9 and <= 1 && choice.Price >= 0 ? choice.Price : null), ("$pc", entityKnown && choice.PriceConfidence is >= .9 and <= 1 && choice.Price >= 0 ? choice.PriceConfidence : 0), ("$u", entityKnown && choice.Upgraded ? 1 : 0));
        }
        foreach (var s in selections.Where(s => !s.Confirmed && known && s.Confidence is >= 0 and <= 1 && pack.Entities.Any(e => e.Id == s.EntityId)))
            Execute("INSERT INTO selections(observation_id,slot,entity_id,confidence,evidence,confirmed) VALUES($o,$s,$e,$c,$x,0)", ("$o", id), ("$s", s.Slot), ("$e", s.EntityId), ("$c", s.Confidence), ("$x", s.Evidence));
        tx.Commit(); return id;
    }
    public void Dispose() => connection.Dispose();
}
