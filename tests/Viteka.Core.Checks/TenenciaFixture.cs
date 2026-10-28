using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

sealed class TenenciaFixture : DbConnection
{
 public List<string> Steps{get;}=[];public List<string> ParametersSeen{get;}=[];public bool FailReadOnly{get;init;}public bool DuplicateStock{get;init;}public bool AmbiguousColumns{get;init;}public bool ReadOnlySet{get;set;}public string StockColumn{get;init;}="STOCK_ACTUAL";public string? AtcColumn{get;init;}public bool TwoAtcCandidates{get;init;}
 [AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"Fixture";public override string DataSource=>"Fixture";public override string ServerVersion=>"1";public override ConnectionState State=>ConnectionState.Open;
 public override void Open()=>Steps.Add("open");public override void Close(){}public override void ChangeDatabase(string databaseName)=>throw new NotSupportedException();protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)=>new Transaction(this);protected override DbCommand CreateDbCommand()=>new Command(this);
 sealed class Transaction(TenenciaFixture owner):DbTransaction{public override IsolationLevel IsolationLevel=>IsolationLevel.ReadCommitted;protected override DbConnection DbConnection=>owner;public override void Commit()=>throw new Exception("Must not commit");public override void Rollback()=>owner.Steps.Add("rollback");}
 sealed class Command(TenenciaFixture owner):DbCommand{
  [AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}protected override DbConnection? DbConnection{get=>owner;set{}}protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection{get;}=new Parameters();
  public override void Cancel(){}public override void Prepare(){}protected override DbParameter CreateDbParameter()=>new Parameter();public override object ExecuteScalar()=>throw new NotSupportedException();
  void Record(){if(DbTransaction is null||CommandTimeout!=15)throw new Exception("Missing boundary");owner.Steps.Add(CommandText);}
  public override int ExecuteNonQuery(){Record();if(CommandText!="SET TRANSACTION READ ONLY"||owner.FailReadOnly)throw new ProbeFixtureException();owner.ReadOnlySet=true;return -1;}
  protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior){
   Record();if(!owner.ReadOnlySet)throw new Exception("Not read-only");
   if(CommandText.StartsWith("SELECT DISTINCT",StringComparison.Ordinal)){var sample=new DataTable();sample.Columns.Add("ATC",typeof(string));sample.Rows.Add("A01AB01");sample.Rows.Add("A01AB02");return sample.CreateDataReader();}
   if(CommandText.Contains("LIKE ?",StringComparison.Ordinal)){
    var found=new DataTable();found.Columns.Add("ARTICULO",typeof(string));found.Columns.Add("DESCRIPCION",typeof(string));found.Columns.Add(owner.AtcColumn??"ATC",typeof(string));
    var prefixes=Parameters.Cast<DbParameter>().Select(p=>((string)p.Value!).TrimEnd('%')).ToArray();owner.ParametersSeen.AddRange(Parameters.Cast<DbParameter>().Select(p=>(string)p.Value!));
    foreach(var item in new[]{("650001","A01AB01"),("650002","A01AB02"),("650003","A02BC01")})if(prefixes.Any(p=>item.Item2.StartsWith(p,StringComparison.Ordinal)))found.Rows.Add(item.Item1,"Medicamento ficticio "+item.Item1,item.Item2);
    return found.CreateDataReader();
   }
var article=CommandText.Contains("FROM ARTICULOS",StringComparison.Ordinal);var table=new DataTable();table.Columns.Add("ARTICULO",typeof(string));table.Columns.Add(article?"DESCRIPCION":owner.StockColumn,article?typeof(string):typeof(decimal));if(!article&&owner.AmbiguousColumns)table.Columns.Add("ACTUAL",typeof(decimal));
   if(CommandText.EndsWith("WHERE 1 = 0",StringComparison.Ordinal)){if(article&&owner.AtcColumn is not null)table.Columns.Add(owner.AtcColumn,typeof(string));if(article&&owner.TwoAtcCandidates)table.Columns.Add("GRUPO_ANTERIOR",typeof(string));return table.CreateDataReader();}
   var values=Parameters.Cast<DbParameter>().Select(p=>(string)p.Value!).ToArray();owner.ParametersSeen.AddRange(values);if(values.Any(CommandText.Contains))throw new Exception("Literal CN in query");
   foreach(var code in values){if(article)table.Rows.Add(code,"Artículo ficticio "+code);else{table.Rows.Add(code,code=="650001"?2m:1m);if(owner.DuplicateStock)table.Rows.Add(code,5m);}}return table.CreateDataReader();
  }
 }
 sealed class Parameter:DbParameter{public override DbType DbType{get;set;}public override ParameterDirection Direction{get;set;}public override bool IsNullable{get;set;}[AllowNull]public override string ParameterName{get;set;}="";[AllowNull]public override string SourceColumn{get;set;}="";public override object? Value{get;set;}public override bool SourceColumnNullMapping{get;set;}public override int Size{get;set;}public override void ResetDbType(){}}
 sealed class Parameters:DbParameterCollection{
  readonly List<DbParameter> items=[];public override int Count=>items.Count;public override object SyncRoot=>this;public override int Add(object value){items.Add((DbParameter)value);return items.Count-1;}public override void AddRange(Array values){foreach(var v in values)Add(v!);}public override void Clear()=>items.Clear();public override bool Contains(object value)=>items.Contains((DbParameter)value);public override bool Contains(string value)=>items.Any(p=>p.ParameterName==value);public override void CopyTo(Array array,int index)=>((ICollection)items).CopyTo(array,index);public override IEnumerator GetEnumerator()=>items.GetEnumerator();public override int IndexOf(object value)=>items.IndexOf((DbParameter)value);public override int IndexOf(string name)=>items.FindIndex(p=>p.ParameterName==name);public override void Insert(int index,object value)=>items.Insert(index,(DbParameter)value);public override void Remove(object value)=>items.Remove((DbParameter)value);public override void RemoveAt(int index)=>items.RemoveAt(index);public override void RemoveAt(string name)=>RemoveAt(IndexOf(name));protected override DbParameter GetParameter(int index)=>items[index];protected override DbParameter GetParameter(string name)=>items[IndexOf(name)];protected override void SetParameter(int index,DbParameter value)=>items[index]=value;protected override void SetParameter(string name,DbParameter value)=>SetParameter(IndexOf(name),value);
 }
}
