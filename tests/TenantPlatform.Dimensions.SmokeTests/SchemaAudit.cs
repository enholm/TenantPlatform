using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

// Offline structural replay, not a SQL engine. Data INSERT/UPDATE statements are classified but never run.
sealed class SchemaAudit
{
    sealed class Table
    {
        public HashSet<string> Columns = [];
        public Dictionary<string, string[]> Keys = [];
        public Dictionary<string, string[]> Indexes = [];
        public Dictionary<string, Relation> ForeignKeys = [];
        public HashSet<string> Checks = [];
    }
    sealed record Relation(string[] Columns, string Principal, string[] PrincipalColumns);
    readonly Dictionary<string, Table> tables = [];
    public int DataSqlCount { get; private set; }
    string context = "";
    void Need(bool condition, string detail) { if (!condition) throw new Exception("SCHEMA AUDIT " + context + ": " + detail); }
    Table Get(string table) { Need(tables.ContainsKey(table), "missing table " + table); return tables[table]; }
    void Columns(string table, IEnumerable<string> columns) { foreach (var column in columns) Need(Get(table).Columns.Contains(column), table + " missing column " + column); }
    void Key(string table, string name, string[] columns) { Columns(table, columns); Need(Get(table).Keys.TryAdd(name, columns), "duplicate key " + name); }
    void Index(string table, string name, string[] columns) { Columns(table, columns); Need(Get(table).Indexes.TryAdd(name, columns), "duplicate index " + name); }
    void ForeignKey(string table, string name, string[] columns, string principal, string[] principalColumns)
    {
        Columns(table, columns); Columns(principal, principalColumns);
        Need(Get(principal).Keys.Values.Any(x => x.SequenceEqual(principalColumns)), "principal unique key missing for " + name);
        Need(Get(table).ForeignKeys.TryAdd(name, new(columns, principal, principalColumns)), "duplicate FK " + name);
    }
    public bool HasTable(string table) => tables.ContainsKey(table);
    public void Apply(IReadOnlyList<MigrationOperation> operations, string migration)
    {
        foreach (var operation in operations)
        {
            context = migration + " / " + operation.GetType().Name;
            switch (operation)
            {
                case CreateTableOperation x:
                    Need(tables.TryAdd(x.Name, new()), "table already exists " + x.Name);
                    foreach (var column in x.Columns) Need(Get(x.Name).Columns.Add(column.Name), "duplicate column " + column.Name);
                    if (x.PrimaryKey is not null) Key(x.Name, x.PrimaryKey.Name, x.PrimaryKey.Columns);
                    foreach (var key in x.UniqueConstraints) Key(x.Name, key.Name, key.Columns);
                    foreach (var fk in x.ForeignKeys) ForeignKey(x.Name, fk.Name, fk.Columns, fk.PrincipalTable, fk.PrincipalColumns!);
                    foreach (var check in x.CheckConstraints) Get(x.Name).Checks.Add(check.Name);
                    break;
                case DropTableOperation x:
                    Get(x.Name);
                    Need(!tables.Where(t => t.Key != x.Name).Any(t => t.Value.ForeignKeys.Values.Any(f => f.Principal == x.Name)), "incoming FK remains before dropping " + x.Name);
                    tables.Remove(x.Name); break;
                case RenameTableOperation x:
                    var renamed = Get(x.Name); var target = x.NewName ?? x.Name;
                    tables.Remove(x.Name); Need(tables.TryAdd(target, renamed), "rename target exists " + target);
                    foreach (var table in tables.Values)
                        foreach (var (name, fk) in table.ForeignKeys.ToList())
                            if (fk.Principal == x.Name) table.ForeignKeys[name] = fk with { Principal = target };
                    break;
                case AddColumnOperation x: Need(Get(x.Table).Columns.Add(x.Name), "column already exists " + x.Table + "." + x.Name); break;
                case AlterColumnOperation x: Columns(x.Table, [x.Name]); break;
                case DropColumnOperation x:
                    Columns(x.Table, [x.Name]);
                    Need(!Get(x.Table).Indexes.Values.Concat(Get(x.Table).Keys.Values).Any(c => c.Contains(x.Name)), "index/key remains before dropping " + x.Table + "." + x.Name);
                    Need(!Get(x.Table).ForeignKeys.Values.Any(f => f.Columns.Contains(x.Name)) && !tables.Values.Any(t => t.ForeignKeys.Values.Any(f => f.Principal == x.Table && f.PrincipalColumns.Contains(x.Name))), "FK remains before column drop " + x.Name);
                    Get(x.Table).Columns.Remove(x.Name); break;
                case RenameColumnOperation x:
                    Columns(x.Table, [x.Name]); Get(x.Table).Columns.Remove(x.Name); Need(Get(x.Table).Columns.Add(x.NewName), "rename column target exists");
                    string[] Rename(string[] names) => names.Select(n => n == x.Name ? x.NewName : n).ToArray();
                    foreach (var (name, cols) in Get(x.Table).Indexes.ToList()) Get(x.Table).Indexes[name] = Rename(cols);
                    foreach (var (name, cols) in Get(x.Table).Keys.ToList()) Get(x.Table).Keys[name] = Rename(cols);
                    foreach (var (name, fk) in Get(x.Table).ForeignKeys.ToList()) Get(x.Table).ForeignKeys[name] = fk with { Columns = Rename(fk.Columns) };
                    foreach (var table in tables.Values)
                        foreach (var (name, fk) in table.ForeignKeys.ToList())
                            if (fk.Principal == x.Table) table.ForeignKeys[name] = fk with { PrincipalColumns = Rename(fk.PrincipalColumns) };
                    break;
                case AddPrimaryKeyOperation x: Key(x.Table, x.Name, x.Columns); break;
                case AddUniqueConstraintOperation x: Key(x.Table, x.Name, x.Columns); break;
                case DropPrimaryKeyOperation x: Need(Get(x.Table).Keys.Remove(x.Name), "missing primary key " + x.Name); break;
                case DropUniqueConstraintOperation x: Need(Get(x.Table).Keys.Remove(x.Name), "missing unique key " + x.Name); break;
                case CreateIndexOperation x: Index(x.Table, x.Name, x.Columns); break;
                case DropIndexOperation x: Need(Get(x.Table!).Indexes.Remove(x.Name), "missing index " + x.Name); break;
                case RenameIndexOperation x:
                    Need(Get(x.Table!).Indexes.Remove(x.Name, out var renamedColumns), "missing renamed index " + x.Name);
                    Need(Get(x.Table!).Indexes.TryAdd(x.NewName, renamedColumns!), "index rename target exists"); break;
                case AddForeignKeyOperation x: ForeignKey(x.Table, x.Name, x.Columns, x.PrincipalTable, x.PrincipalColumns!); break;
                case DropForeignKeyOperation x: Need(Get(x.Table).ForeignKeys.Remove(x.Name), "missing FK " + x.Name); break;
                case AddCheckConstraintOperation x: Need(Get(x.Table).Checks.Add(x.Name), "duplicate check " + x.Name); break;
                case DropCheckConstraintOperation x: Need(Get(x.Table).Checks.Remove(x.Name), "missing check " + x.Name); break;
                case SqlOperation x:
                    Need((x.Sql.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) || x.Sql.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)) && !System.Text.RegularExpressions.Regex.IsMatch(x.Sql, @"\b(CREATE|ALTER|DROP|TRUNCATE)\s+(TABLE|INDEX)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase), "unclassified SQL requires manual structural review");
                    DataSqlCount++; break;
                default: throw new Exception("SCHEMA AUDIT unsupported operation " + operation.GetType().Name + " in " + migration);
            }
        }
    }
    public void Compare(IRelationalModel model)
    {
        context = "final model comparison";
        Need(tables.Keys.Order().SequenceEqual(model.Tables.Select(t => t.Name).Order()), "table set differs");
        foreach (var table in model.Tables)
        {
            var actual = Get(table.Name);
            Need(actual.Columns.Order().SequenceEqual(table.Columns.Select(c => c.Name).Order()), "column set differs: " + table.Name);
            Need(actual.Keys.Keys.Order().SequenceEqual(table.UniqueConstraints.Select(k => k.Name).Order()), "key names differ: " + table.Name);
            Need(actual.Indexes.Keys.Order().SequenceEqual(table.Indexes.Select(i => i.Name).Order()), "index names differ: " + table.Name);
            Need(actual.ForeignKeys.Keys.Order().SequenceEqual(table.ForeignKeyConstraints.Select(f => f.Name).Order()), "FK names differ: " + table.Name);
            Need(actual.Checks.Order().SequenceEqual(table.CheckConstraints.Select(c => c.Name).Order()), "check names differ: " + table.Name);
            foreach (var key in table.UniqueConstraints) Need(actual.Keys[key.Name].SequenceEqual(key.Columns.Select(c => c.Name)), "key columns differ: " + key.Name);
            foreach (var index in table.Indexes) Need(actual.Indexes[index.Name].SequenceEqual(index.Columns.Select(c => c.Name)), "index columns differ: " + index.Name);
            foreach (var fk in table.ForeignKeyConstraints)
            {
                var replayed = actual.ForeignKeys[fk.Name];
                Need(replayed.Principal == fk.PrincipalTable.Name && replayed.Columns.SequenceEqual(fk.Columns.Select(c => c.Name)) && replayed.PrincipalColumns.SequenceEqual(fk.PrincipalColumns.Select(c => c.Name)), "FK columns differ: " + fk.Name);
            }
        }
        Verify.That(true, "all migration operations replay from empty schema to final table/column/key/index/FK metadata (offline structural audit)");
    }
}
