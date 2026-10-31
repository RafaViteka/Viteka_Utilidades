using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace Viteka.Core;

public sealed record AuditActor(string Id, string Name);
public sealed record AuditEntry(long Id, string OperationId, string At, string UserId, string UserName, string Utility, string Action, string Result, string Note);
public sealed record AuditFilter(string? From = null, string? To = null, string? User = null, string? Utility = null, string? Action = null, string? Result = null, long? Before = null);
public sealed class PharmacyDatabase
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string connectionString;
    public PharmacyDatabase(string directory)
    {
        Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "pharmacy.db"), DefaultTimeout = 10, Pooling=false }.ToString();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText="PRAGMA user_version";if(Convert.ToInt32(cmd.ExecuteScalar())>1)throw new InvalidDataException("La base de Viteka requiere una aplicación posterior. No se ha modificado.");
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS documents(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS audit(id INTEGER PRIMARY KEY AUTOINCREMENT, operation_id TEXT NOT NULL UNIQUE,
              at TEXT NOT NULL,user_id TEXT NOT NULL,user_name TEXT NOT NULL,utility TEXT NOT NULL,action TEXT NOT NULL,result TEXT NOT NULL,note TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS audit_at ON audit(at,id);
            CREATE INDEX IF NOT EXISTS audit_user ON audit(user_id,id);
            CREATE TABLE IF NOT EXISTS catalog_history(id INTEGER PRIMARY KEY AUTOINCREMENT,province TEXT NOT NULL,at TEXT NOT NULL,value TEXT NOT NULL);
            PRAGMA user_version=1;
            """;
        cmd.ExecuteNonQuery();
    }
    public SqliteConnection Open() { var c = new SqliteConnection(connectionString); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;"; cmd.ExecuteNonQuery(); return c; }
    public T Transaction<T>(Func<SqliteConnection,SqliteTransaction,T> action)
    {
        using var c = Open(); using var tx = c.BeginTransaction(); var result = action(c,tx); tx.Commit(); return result;
    }
    public static string? Get(SqliteConnection c, SqliteTransaction tx, string key)
    { using var cmd = Command(c,tx,"SELECT value FROM documents WHERE key=$key", ("$key",key)); return cmd.ExecuteScalar() as string; }
    public static void Set(SqliteConnection c, SqliteTransaction tx, string key, object value)
    { using var cmd = Command(c,tx,"INSERT INTO documents VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value",("$key",key),("$value",JsonSerializer.Serialize(value,Json))); cmd.ExecuteNonQuery(); }
    public static SqliteCommand Command(SqliteConnection c, SqliteTransaction tx, string sql, params (string Key,object Value)[] parameters)
    { var cmd = c.CreateCommand(); cmd.Transaction=tx; cmd.CommandText=sql; foreach(var p in parameters)cmd.Parameters.AddWithValue(p.Key,p.Value); return cmd; }
    public static void Record(SqliteConnection c, SqliteTransaction tx, AuditActor actor, string action, string result, string note, string? operationId = null, string utility = "")
    {
        if (action.Length>80||note.Length>600||actor.Name.Length>64||utility.Length>100)throw new ArgumentException("Evento de auditoría no válido.");
        using var cmd=Command(c,tx,"INSERT OR IGNORE INTO audit(operation_id,at,user_id,user_name,utility,action,result,note) VALUES($op,$at,$uid,$name,$utility,$action,$result,$note)",
            ("$op",operationId??Guid.NewGuid().ToString("N")),("$at",DateTimeOffset.UtcNow.ToString("O")),("$uid",actor.Id),("$name",actor.Name),("$utility",utility),("$action",action),("$result",result),("$note",note));cmd.ExecuteNonQuery();
    }
    public void Record(AuditActor actor,string action,string result,string note,string? operationId=null,string utility="") => Transaction((c,tx)=>{Record(c,tx,actor,action,result,note,operationId,utility);return true;});
    public void Complete(string operationId,AuditActor actor,string result,string note)=>Transaction((c,tx)=>{
        if(note.Length>600)note=note[..600];
        using var cmd=Command(c,tx,"UPDATE audit SET user_id=$uid,user_name=$name,result=$result,note=$note WHERE operation_id=$op AND result='pending'",("$uid",actor.Id),("$name",actor.Name),("$result",result),("$note",note),("$op",operationId));cmd.ExecuteNonQuery();return true;
    });
    public AuditEntry[] Query(AuditFilter filter)
    {
        return Transaction((c,tx)=>{
            var clauses=new List<string>();var parameters=new List<(string,object)>();
            void Add(string column,string? value){if(string.IsNullOrEmpty(value))return;if(value.Length>100)throw new ArgumentException();clauses.Add(column+"=$"+column);parameters.Add(("$"+column,value));}
            Add("user_id",filter.User);Add("utility",filter.Utility);Add("action",filter.Action);Add("result",filter.Result);
            if(filter.Before is {} before){clauses.Add("id<$before");parameters.Add(("$before",before));}
            foreach(var (value,op,key) in new[]{(filter.From,">=","from"),(filter.To,"<=","to")})if(value is not null){if(!DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var date))throw new ArgumentException("Fecha no válida.");clauses.Add("at"+op+"$"+key);parameters.Add(("$"+key,date.ToUniversalTime().ToString("O")));}
            using var cmd=Command(c,tx,"SELECT id,operation_id,at,user_id,user_name,utility,action,result,note FROM audit"+(clauses.Count>0?" WHERE "+string.Join(" AND ",clauses):"")+" ORDER BY id DESC LIMIT 51",parameters.ToArray());
            using var r=cmd.ExecuteReader();var rows=new List<AuditEntry>();while(r.Read())rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8)));return rows.ToArray();
        });
    }
    public int RetentionMonths()=>Transaction((c,tx)=>{
        var value=Get(c,tx,"audit.retention");return value is null?12:JsonSerializer.Deserialize<int>(value);
    });
    public void SetRetention(int months,AuditActor actor)
    {
        if(months is <1 or >120)throw new ArgumentException("La conservación debe ser de 1 a 120 meses.");
        Transaction((c,tx)=>{Set(c,tx,"audit.retention",months);Record(c,tx,actor,"audit.retention","ok",months+" meses de conservación");return true;});
        Prune(months);
    }
    public void Prune(int? requested=null)
    { var months=requested??RetentionMonths(); if(months is <1 or >120)throw new ArgumentException();Transaction((c,tx)=>{using var cmd=Command(c,tx,"DELETE FROM audit WHERE at<$cutoff",("$cutoff",DateTimeOffset.UtcNow.AddMonths(-months).ToString("O")));cmd.ExecuteNonQuery();return true;}); }
}
