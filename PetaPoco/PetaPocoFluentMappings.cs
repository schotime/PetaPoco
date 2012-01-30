using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace PetaPoco
{
    public class FluentMappingsPocoData : Database.PocoData
    {
        public FluentMappingsPocoData(Type t, FluentMappings.PetaPocoTypeDefinition typeConfig)
        {
            type = t;
            TableInfo = new TableInfo();

            // Get the table name
            var a = typeConfig.TableName ?? "";
            TableInfo.TableName = a.Length == 0 ? t.Name : a;

            // Get the primary key
            a = typeConfig.PrimaryKey ?? "";
            TableInfo.PrimaryKey = a.Length == 0 ? "ID" : a;

            a = typeConfig.SequenceName ?? "";
            TableInfo.SequenceName = a.Length == 0 ? null : a;

            TableInfo.AutoIncrement = typeConfig.AutoIncrement;

            // Set autoincrement false if primary key has multiple columns
            TableInfo.AutoIncrement = TableInfo.AutoIncrement ? !TableInfo.PrimaryKey.Contains(',') : TableInfo.AutoIncrement;

            // Call column mapper
            if (Database.Mapper != null)
                Database.Mapper.GetTableInfo(t, TableInfo);

            // Work out bound properties
            bool explicitColumns = typeConfig.ExplicitColumns;
            Columns = new Dictionary<string, Database.PocoColumn>(StringComparer.OrdinalIgnoreCase);
            foreach (var pi in t.GetProperties())
            {
                // Work out if properties is to be included
                var isColumnDefined = typeConfig.ColumnConfiguration.ContainsKey(pi.Name);
                if (explicitColumns)
                {
                    if (!isColumnDefined)
                        continue;
                }
                else
                {
                    if (isColumnDefined && typeConfig.ColumnConfiguration[pi.Name].IgnoreColumn)
                        continue;
                }

                var pc = new Database.PocoColumn();
                pc.PropertyInfo = pi;

                // Work out the DB column name
                if (isColumnDefined)
                {
                    var colattr = typeConfig.ColumnConfiguration[pi.Name];
                    pc.ColumnName = colattr.DbColumnName;
                    if (colattr.ResultColumn)
                        pc.ResultColumn = true;
                    else if (colattr.VersionColumn)
                        pc.VersionColumn = true;

                    // Support for composite keys needed
                    if (pi.Name == TableInfo.PrimaryKey)
                        TableInfo.PrimaryKey = pc.ColumnName;

                }
                if (pc.ColumnName == null)
                {
                    pc.ColumnName = pi.Name;
                    if (Database.Mapper != null && !Database.Mapper.MapPropertyToColumn(pi, ref pc.ColumnName, ref pc.ResultColumn))
                        continue;
                }

                // Store it
                Columns.Add(pc.ColumnName, pc);
            }

            // Build column list for automatic select
            QueryColumns = (from c in Columns where !c.Value.ResultColumn select c.Key).ToArray();

        }
    }

}

namespace PetaPoco.FluentMappings
{
    public class Configuration
    {
        public static void Configure(params IPetaPocoMap[] petaPocoMaps)
        {
            var mappings = PetaPocoMappings.BuildMappingsFromMaps(petaPocoMaps);
            SetFactory(mappings);
        }

        public static void Configure(PetaPocoMappings mappings)
        {
            SetFactory(mappings);
        }

        private static void SetFactory(PetaPocoMappings mappings)
        {
            Database.PocoDataFactory = t => (mappings != null && mappings.Config.ContainsKey(t))
                    ? new FluentMappingsPocoData(t, mappings.Config[t])
                    : new Database.PocoData(t);
        }
    }

    public class PetaPocoMappings
    {
        public Dictionary<Type, PetaPocoTypeDefinition> Config = new Dictionary<Type, PetaPocoTypeDefinition>();

        public PetaPocoMap<T> For<T>()
        {
            var definition = new PetaPocoTypeDefinition();
            var petaPocoMap = new PetaPocoMap<T>(definition);
            Config.Add(typeof(T), definition);
            return petaPocoMap;
        }

        public static PetaPocoMappings BuildMappingsFromMaps(params IPetaPocoMap[] petaPocoMaps)
        {
            var petaPocoConfig = new PetaPocoMappings();
            foreach (var petaPocoMap in petaPocoMaps)
            {
                var type = petaPocoMap.TypeDefinition.Type;
                petaPocoConfig.Config[type] = petaPocoMap.TypeDefinition;
            }
            return petaPocoConfig;
        }
    }
    
    public class PetaPocoColumnConfiguration<T>
    {
        private readonly Dictionary<string, PetaPocoColumnDefinition> _columnDefinitions;

        public PetaPocoColumnConfiguration(Dictionary<string, PetaPocoColumnDefinition> columnDefinitions)
        {
            _columnDefinitions = columnDefinitions;
        }

        public void Column(Expression<Func<T, object>> property)
        {
            Column(property, null);
        }

        public void Column(Expression<Func<T, object>> property, string dbColumnName)
        {
            SetColumnDefinition(property, dbColumnName, false, false, false);
        }

        public void Result(Expression<Func<T, object>> property)
        {
            Result(property, null);
        }

        public void Result(Expression<Func<T, object>> property, string dbColumnName)
        {
            SetColumnDefinition(property, dbColumnName, false, true, false);
        }

        public void Ignore(Expression<Func<T, object>> property)
        {
            SetColumnDefinition(property, null, true, false, false);
        }

        public void Version(Expression<Func<T, object>> property)
        {
            Version(property, null);
        }

        public void Version(Expression<Func<T, object>> property, string dbColumnName)
        {
            SetColumnDefinition(property, dbColumnName, false, false, true);
        }

        private void SetColumnDefinition(Expression<Func<T, object>> property, string dbColumnName, bool ignoreColumn, bool resultColumn, bool versionColumn) 
        {
            var propertyInfo = PropertyHelper<T>.GetProperty(property);
            _columnDefinitions[propertyInfo.Name] = new PetaPocoColumnDefinition
            {
                PropertyInfo = propertyInfo, 
                DbColumnName = dbColumnName,
                ResultColumn = resultColumn,
                IgnoreColumn = ignoreColumn,
                VersionColumn = versionColumn
            };
        }
    }

    public interface IPetaPocoMap
    {
        PetaPocoTypeDefinition TypeDefinition { get; }
    }

    public class PetaPocoMap<T> : IPetaPocoMap
    {
        private readonly PetaPocoTypeDefinition _petaPocoTypeDefinition;

        public PetaPocoMap() : this(new PetaPocoTypeDefinition())
        {
        }

        public PetaPocoMap(PetaPocoTypeDefinition petaPocoTypeDefinition)
        {
            _petaPocoTypeDefinition = petaPocoTypeDefinition;
            _petaPocoTypeDefinition.Type = typeof (T);
            _petaPocoTypeDefinition.AutoIncrement = true;
            _petaPocoTypeDefinition.ColumnConfiguration = new Dictionary<string, PetaPocoColumnDefinition>();
        }

        public PetaPocoMap<T> TableName(string tableName)
        {
            _petaPocoTypeDefinition.TableName = tableName;
            return this;
        }

        public PetaPocoMap<T> Columns(Action<PetaPocoColumnConfiguration<T>> columnConfiguration)
        {
            return Columns(columnConfiguration, false);
        }

        public PetaPocoMap<T> Columns(Action<PetaPocoColumnConfiguration<T>> columnConfiguration, bool explicitColumns)
        {
            _petaPocoTypeDefinition.ExplicitColumns = explicitColumns;
            columnConfiguration(new PetaPocoColumnConfiguration<T>(_petaPocoTypeDefinition.ColumnConfiguration));
            return this;
        }

        public PetaPocoMap<T> PrimaryKey(Expression<Func<T, object>> column, string sequenceName)
        {
            var propertyInfo = PropertyHelper<T>.GetProperty(column);
            return PrimaryKey(propertyInfo.Name, sequenceName);
        }

        public PetaPocoMap<T> PrimaryKey(Expression<Func<T, object>> column)
        {
            return PrimaryKey(column, null);
        }

        public PetaPocoMap<T> PrimaryKey(Expression<Func<T, object>> column, bool autoIncrement)
        {
            var propertyInfo = PropertyHelper<T>.GetProperty(column);
            return PrimaryKey(propertyInfo.Name, autoIncrement);
        }

        public PetaPocoMap<T> CompositePrimaryKey(params Expression<Func<T, object>>[] columns)
        {
            var columnNames = new string[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                columnNames[i] = PropertyHelper<T>.GetProperty(columns[i]).Name;
            }

            _petaPocoTypeDefinition.PrimaryKey = string.Join(",", columnNames);
            return this;
        }

        public PetaPocoMap<T> PrimaryKey(string primaryKeyColumn, bool autoIncrement)
        {
            _petaPocoTypeDefinition.PrimaryKey = primaryKeyColumn;
            _petaPocoTypeDefinition.AutoIncrement = autoIncrement;
            return this;
        }

        public PetaPocoMap<T> PrimaryKey(string primaryKeyColumn, string sequenceName)
        {
            _petaPocoTypeDefinition.PrimaryKey = primaryKeyColumn;
            _petaPocoTypeDefinition.SequenceName = sequenceName;
            return this;
        }

        public PetaPocoMap<T> PrimaryKey(string primaryKeyColumn)
        {
            return PrimaryKey(primaryKeyColumn, null);
        }

        PetaPocoTypeDefinition IPetaPocoMap.TypeDefinition
        {
            get { return _petaPocoTypeDefinition; }
        }
    }

    public static class PropertyHelper<T>
    {
        public static PropertyInfo GetProperty<TValue>(Expression<Func<T, TValue>> selector)
        {
            Expression body = selector;
            if (body is LambdaExpression)
            {
                body = ((LambdaExpression)body).Body;
            }
            if (body is UnaryExpression)
            {
                body = ((UnaryExpression)body).Operand;
            }
            switch (body.NodeType)
            {
                case ExpressionType.MemberAccess:
                    return (PropertyInfo)((MemberExpression)body).Member;
                default:
                    throw new InvalidOperationException();
            }
        }
    }

    public class PetaPocoTypeDefinition
    {
        public Type Type { get; set; }
        public string TableName { get; set; }
        public string PrimaryKey { get; set; }
        public string SequenceName { get; set; }
        public bool AutoIncrement { get; set; }
        public bool ExplicitColumns { get; set; }
        public Dictionary<string, PetaPocoColumnDefinition> ColumnConfiguration { get; set; }
    }

    public class PetaPocoColumnDefinition
    {
        public PropertyInfo PropertyInfo { get; set; }
        public string DbColumnName { get; set; }
        public bool ResultColumn { get; set; }
        public bool IgnoreColumn { get; set; }
        public bool VersionColumn { get; set; }
    }

}
