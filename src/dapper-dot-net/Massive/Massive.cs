﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Collections;
using System.Text.RegularExpressions;

namespace Massive
{
    /// <summary>An object extensions.</summary>
    public static class ObjectExtensions
    {
        /// <summary>Extension method for adding in a bunch of parameters.</summary>
        /// <param name="cmd"> The cmd to act on.</param>
        /// <param name="args">A variable-length parameters list containing arguments.</param>
        public static void AddParams(this DbCommand cmd, params object[] args)
        {
            foreach (var item in args)
            {
                AddParam(cmd, item);
            }
        }

        /// <summary>Extension for adding single parameter.</summary>
        /// <param name="cmd"> The cmd to act on.</param>
        /// <param name="item">The item.</param>
        public static void AddParam(this DbCommand cmd, object item)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = string.Format("@{0}", cmd.Parameters.Count);
            if (item == null)
            {
                p.Value = DBNull.Value;
            }
            else
            {
                if (item.GetType() == typeof(Guid))
                {
                    p.Value = item.ToString();
                    p.DbType = DbType.String;
                    p.Size = 4000;
                }
                else if (item.GetType() == typeof(ExpandoObject))
                {
                    var d = (IDictionary<string, object>)item;
                    p.Value = d.Values.FirstOrDefault();
                }
                else
                {
                    p.Value = item;
                }
                //from DataChomp
                if (item.GetType() == typeof(string))
                    p.Size = 4000;
            }
            cmd.Parameters.Add(p);
        }

        /// <summary>Turns an IDataReader to a Dynamic list of things.</summary>
        /// <param name="rdr">The rdr to act on.</param>
        /// <returns>rdr as a List&lt;dynamic&gt;</returns>
        public static List<dynamic> ToExpandoList(this IDataReader rdr)
        {
            var result = new List<dynamic>();
            while (rdr.Read())
            {
                result.Add(rdr.RecordToExpando());
            }
            return result;
        }

        /// <summary>An IDataReader extension method that record to expando.</summary>
        /// <param name="rdr">The rdr to act on.</param>
        /// <returns>A dynamic.</returns>
        public static dynamic RecordToExpando(this IDataReader rdr)
        {
            dynamic e = new ExpandoObject();
            var d = e as IDictionary<string, object>;
            for (int i = 0; i < rdr.FieldCount; i++)
                d.Add(rdr.GetName(i), rdr[i]);
            return e;
        }

        /// <summary>Turns the object into an ExpandoObject.</summary>
        /// <param name="o">The o to act on.</param>
        /// <returns>o as a dynamic.</returns>
        public static dynamic ToExpando(this object o)
        {
            var result = new ExpandoObject();
            var d = result as IDictionary<string, object>; //work with the Expando as a Dictionary
            if (o.GetType() == typeof(ExpandoObject)) return o; //shouldn't have to... but just in case
            if (o.GetType() == typeof(NameValueCollection) || o.GetType().IsSubclassOf(typeof(NameValueCollection)))
            {
                var nv = (NameValueCollection)o;
                nv.Cast<string>().Select(key => new KeyValuePair<string, object>(key, nv[key])).ToList().ForEach(i => d.Add(i));
            }
            else
            {
                var props = o.GetType().GetProperties();
                foreach (var item in props)
                {
                    d.Add(item.Name, item.GetValue(o, null));
                }
            }
            return result;
        }

        /// <summary>Turns the object into a Dictionary.</summary>
        /// <param name="thingy">The thingy to act on.</param>
        /// <returns>thingy as an IDictionary&lt;string,object&gt;</returns>
        public static IDictionary<string, object> ToDictionary(this object thingy)
        {
            return (IDictionary<string, object>)thingy.ToExpando();
        }
    }

    /// <summary>A class that wraps your database table in Dynamic Funtime.</summary>
    public class DynamicModel
    {
        /// <summary>The factory.</summary>
        DbProviderFactory _factory;

        /// <summary>The connection string.</summary>
        string _connectionString;

        /// <summary>Initializes a new instance of the Massive.DynamicModel class.</summary>
        /// <param name="connectionStringName">Name of the connection string.</param>
        /// <param name="tableName">           The name of the table.</param>
        /// <param name="primaryKeyField">     The primary key field.</param>
        public DynamicModel(string connectionStringName = "", string tableName = "", string primaryKeyField = "")
        {
            _factory = DbProviderFactories.GetFactory("System.Data.SqlClient");
            /*
            TableName = tableName == "" ? this.GetType().Name : tableName;
            PrimaryKeyField = string.IsNullOrEmpty(primaryKeyField) ? "ID" : primaryKeyField;
            if (connectionStringName == "")
                connectionStringName = ConfigurationManager.ConnectionStrings[0].Name;
            var _providerName = "System.Data.SqlClient";
            if (ConfigurationManager.ConnectionStrings[connectionStringName] != null)
            {
                if (!string.IsNullOrEmpty(ConfigurationManager.ConnectionStrings[connectionStringName].ProviderName))
                    _providerName = ConfigurationManager.ConnectionStrings[connectionStringName].ProviderName;
            }
            else
            {
                throw new InvalidOperationException("Can't find a connection string with the name '" + connectionStringName + "'");
            }
            _factory = DbProviderFactories.GetFactory(_providerName);
            _connectionString = ConfigurationManager.ConnectionStrings[connectionStringName].ConnectionString;
             */
        }

        /// <summary>Enumerates the reader yielding the result - thanks to Jeroen Haegebaert.</summary>
        /// <param name="sql"> The SQL.</param>
        /// <param name="args">A variable-length parameters list containing arguments.</param>
        /// <returns>
        /// An enumerator that allows foreach to be used to process query in this collection.
        /// </returns>
        public virtual IEnumerable<dynamic> Query(string sql, params object[] args)
        {
            using (var conn = OpenConnection())
            {
                var rdr = CreateCommand(sql, conn, args).ExecuteReader();
                while (rdr.Read())
                {
                    yield return rdr.RecordToExpando(); ;
                }
            }
        }

        /// <summary>Enumerates the reader yielding the result - thanks to Jeroen Haegebaert.</summary>
        /// <param name="sql">       The SQL.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="args">      A variable-length parameters list containing arguments.</param>
        /// <returns>
        /// An enumerator that allows foreach to be used to process query in this collection.
        /// </returns>
        public virtual IEnumerable<dynamic> Query(string sql, DbConnection connection, params object[] args)
        {
            using (var rdr = CreateCommand(sql, connection, args).ExecuteReader())
            {
                while (rdr.Read())
                {
                    yield return rdr.RecordToExpando(); ;
                }
            }

        }

        /// <summary>Returns a single result.</summary>
        /// <param name="sql"> The SQL.</param>
        /// <param name="args">A variable-length parameters list containing arguments.</param>
        /// <returns>An object.</returns>
        public virtual object Scalar(string sql, params object[] args)
        {
            object result = null;
            using (var conn = OpenConnection())
            {
                result = CreateCommand(sql, conn, args).ExecuteScalar();
            }
            return result;
        }

        /// <summary>Creates a DBCommand that you can use for loving your database.</summary>
        /// <param name="sql"> The SQL.</param>
        /// <param name="conn">The connection.</param>
        /// <param name="args">A variable-length parameters list containing arguments.</param>
        /// <returns>The new command.</returns>
        DbCommand CreateCommand(string sql, DbConnection conn, params object[] args)
        {
            var result = _factory.CreateCommand();
            result.Connection = conn;
            result.CommandText = sql;
            if (args.Length > 0)
                result.AddParams(args);
            return result;
        }

        /// <summary>Returns and OpenConnection.</summary>
        /// <returns>A DbConnection.</returns>
        public virtual DbConnection OpenConnection()
        {
            var result = _factory.CreateConnection();
            result.ConnectionString = _connectionString;
            result.Open();
            return result;
        }

        /// <summary>
        /// Builds a set of Insert and Update commands based on the passed-on objects. These objects can
        /// be POCOs, Anonymous, NameValueCollections, or Expandos. Objects With a PK property (whatever
        /// PrimaryKeyField is set to) will be created at UPDATEs.
        /// </summary>
        /// <param name="things">The things to save.</param>
        /// <returns>A List&lt;DbCommand&gt;</returns>
        public virtual List<DbCommand> BuildCommands(params object[] things)
        {
            var commands = new List<DbCommand>();
            foreach (var item in things)
            {
                if (HasPrimaryKey(item))
                {
                    commands.Add(CreateUpdateCommand(item, GetPrimaryKey(item)));
                }
                else
                {
                    commands.Add(CreateInsertCommand(item));
                }
            }

            return commands;
        }

        /// <summary>
        /// Executes a set of objects as Insert or Update commands based on their property settings,
        /// within a transaction. These objects can be POCOs, Anonymous, NameValueCollections, or
        /// Expandos. Objects With a PK property (whatever PrimaryKeyField is set to) will be created at
        /// UPDATEs.
        /// </summary>
        /// <param name="things">The things to save.</param>
        /// <returns>An int.</returns>
        public virtual int Save(params object[] things)
        {
            var commands = BuildCommands(things);
            return Execute(commands);
        }

        /// <summary>Executes a series of DBCommands in a transaction.</summary>
        /// <param name="command">The command.</param>
        /// <returns>An int.</returns>
        public virtual int Execute(DbCommand command)
        {
            return Execute(new DbCommand[] { command });
        }

        /// <summary>Executes a series of DBCommands in a transaction.</summary>
        /// <param name="commands">The commands.</param>
        /// <returns>An int.</returns>
        public virtual int Execute(IEnumerable<DbCommand> commands)
        {
            var result = 0;
            using (var conn = OpenConnection())
            {
                using (var tx = conn.BeginTransaction())
                {
                    foreach (var cmd in commands)
                    {
                        cmd.Connection = conn;
                        cmd.Transaction = tx;
                        result += cmd.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
            }
            return result;
        }

        /// <summary>Gets or sets the primary key field.</summary>
        /// <value>The primary key field.</value>
        public virtual string PrimaryKeyField { get; set; }

        /// <summary>
        /// Conventionally introspects the object passed in for a field that looks like a PK. If you've
        /// named your PrimaryKeyField, this becomes easy.
        /// </summary>
        /// <param name="o">The object to process.</param>
        /// <returns>true if primary key, false if not.</returns>
        public virtual bool HasPrimaryKey(object o)
        {
            return o.ToDictionary().ContainsKey(PrimaryKeyField);
        }

        /// <summary>
        /// If the object passed in has a property with the same name as your PrimaryKeyField it is
        /// returned here.
        /// </summary>
        /// <param name="o">The object to process.</param>
        /// <returns>The primary key.</returns>
        public virtual object GetPrimaryKey(object o)
        {
            object result = null;
            o.ToDictionary().TryGetValue(PrimaryKeyField, out result);
            return result;
        }

        /// <summary>Gets or sets the name of the table.</summary>
        /// <value>The name of the table.</value>
        public virtual string TableName { get; set; }

        /// <summary>
        /// Creates a command for use with transactions - internal stuff mostly, but here for you to play
        /// with.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the requested operation is invalid.</exception>
        /// <param name="o">The object to process.</param>
        /// <returns>The new insert command.</returns>
        public virtual DbCommand CreateInsertCommand(object o)
        {
            DbCommand result = null;
            var expando = o.ToExpando();
            var settings = (IDictionary<string, object>)expando;
            var sbKeys = new StringBuilder();
            var sbVals = new StringBuilder();
            var stub = "INSERT INTO {0} ({1}) \r\n VALUES ({2})";
            result = CreateCommand(stub, null);
            int counter = 0;
            foreach (var item in settings)
            {
                sbKeys.AppendFormat("{0},", item.Key);
                sbVals.AppendFormat("@{0},", counter.ToString());
                result.AddParam(item.Value);
                counter++;
            }
            if (counter > 0)
            {
                var keys = sbKeys.ToString().Substring(0, sbKeys.Length - 1);
                var vals = sbVals.ToString().Substring(0, sbVals.Length - 1);
                var sql = string.Format(stub, TableName, keys, vals);
                result.CommandText = sql;
            }
            else throw new InvalidOperationException("Can't parse this object to the database - there are no properties set");
            return result;
        }

        /// <summary>
        /// Creates a command for use with transactions - internal stuff mostly, but here for you to play
        /// with.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the requested operation is invalid.</exception>
        /// <param name="o">  The object to process.</param>
        /// <param name="key">The key.</param>
        /// <returns>The new update command.</returns>
        public virtual DbCommand CreateUpdateCommand(object o, object key)
        {
            var expando = o.ToExpando();
            var settings = (IDictionary<string, object>)expando;
            var sbKeys = new StringBuilder();
            var stub = "UPDATE {0} SET {1} WHERE {2} = @{3}";
            var args = new List<object>();
            var result = CreateCommand(stub, null);
            int counter = 0;
            foreach (var item in settings)
            {
                var val = item.Value;
                if (!item.Key.Equals(PrimaryKeyField, StringComparison.CurrentCultureIgnoreCase) && item.Value != null)
                {
                    result.AddParam(val);
                    sbKeys.AppendFormat("{0} = @{1}, \r\n", item.Key, counter.ToString());
                    counter++;
                }
            }
            if (counter > 0)
            {
                //add the key
                result.AddParam(key);
                //strip the last commas
                var keys = sbKeys.ToString().Substring(0, sbKeys.Length - 4);
                result.CommandText = string.Format(stub, TableName, keys, PrimaryKeyField, counter);
            }
            else throw new InvalidOperationException("No parsable object was sent in - could not divine any name/value pairs");
            return result;
        }

        /// <summary>Removes one or more records from the DB according to the passed-in WHERE.</summary>
        /// <param name="where">The where.</param>
        /// <param name="key">  The key.</param>
        /// <param name="args"> A variable-length parameters list containing arguments.</param>
        /// <returns>The new delete command.</returns>
        public virtual DbCommand CreateDeleteCommand(string where = "", object key = null, params object[] args)
        {
            var sql = string.Format("DELETE FROM {0} ", TableName);
            if (key != null)
            {
                sql += string.Format("WHERE {0}=@0", PrimaryKeyField);
                args = new object[] { key };
            }
            else if (!string.IsNullOrEmpty(where))
            {
                sql += where.Trim().StartsWith("where", StringComparison.CurrentCultureIgnoreCase) ? where : "WHERE " + where;
            }
            return CreateCommand(sql, null, args);
        }

        /// <summary>
        /// Adds a record to the database. You can pass in an Anonymous object, an ExpandoObject, A
        /// regular old POCO, or a NameValueColletion from a Request.Form or Request.QueryString.
        /// </summary>
        /// <param name="o">The object to process.</param>
        /// <returns>An object.</returns>
        public virtual object Insert(object o)
        {
            dynamic result = 0;
            using (var conn = OpenConnection())
            {
                var cmd = CreateInsertCommand(o);
                cmd.Connection = conn;
                cmd.ExecuteNonQuery();
                cmd.CommandText = "SELECT @@IDENTITY as newID";
                result = cmd.ExecuteScalar();
            }
            return result;
        }

        /// <summary>
        /// Updates a record in the database. You can pass in an Anonymous object, an ExpandoObject, A
        /// regular old POCO, or a NameValueCollection from a Request.Form or Request.QueryString.
        /// </summary>
        /// <param name="o">  The object to process.</param>
        /// <param name="key">The key.</param>
        /// <returns>An int.</returns>
        public virtual int Update(object o, object key)
        {
            return Execute(CreateUpdateCommand(o, key));
        }

        /// <summary>Removes one or more records from the DB according to the passed-in WHERE.</summary>
        /// <param name="key">  The key.</param>
        /// <param name="where">The where.</param>
        /// <param name="args"> A variable-length parameters list containing arguments.</param>
        /// <returns>An int.</returns>
        public int Delete(object key = null, string where = "", params object[] args)
        {
            return Execute(CreateDeleteCommand(where: where, key: key, args: args));
        }

        /// <summary>
        /// Returns all records complying with the passed-in WHERE clause and arguments, ordered as
        /// specified, limited (TOP) by limit.
        /// </summary>
        /// <param name="where">  The where.</param>
        /// <param name="orderBy">Describes who order this object.</param>
        /// <param name="limit">  The limit.</param>
        /// <param name="columns">The columns.</param>
        /// <param name="args">   A variable-length parameters list containing arguments.</param>
        /// <returns>
        /// An enumerator that allows foreach to be used to process all in this collection.
        /// </returns>
        public virtual IEnumerable<dynamic> All(string where = "", string orderBy = "", int limit = 0, string columns = "*", params object[] args)
        {
            string sql = limit > 0 ? "SELECT TOP " + limit + " {0} FROM {1} " : "SELECT {0} FROM {1} ";
            if (!string.IsNullOrEmpty(where))
                sql += where.Trim().StartsWith("where", StringComparison.CurrentCultureIgnoreCase) ? where : "WHERE " + where;
            if (!String.IsNullOrEmpty(orderBy))
                sql += orderBy.Trim().StartsWith("order by", StringComparison.CurrentCultureIgnoreCase) ? orderBy : " ORDER BY " + orderBy;
            return Query(string.Format(sql, columns, TableName), args);
        }

        /// <summary>
        /// Returns a dynamic PagedResult. Result properties are Items, TotalPages, and TotalRecords.
        /// </summary>
        /// <param name="where">      The where.</param>
        /// <param name="orderBy">    Describes who order this object.</param>
        /// <param name="columns">    The columns.</param>
        /// <param name="pageSize">   Size of the page.</param>
        /// <param name="currentPage">The current page.</param>
        /// <param name="args">       A variable-length parameters list containing arguments.</param>
        /// <returns>A dynamic.</returns>
        public virtual dynamic Paged(string where = "", string orderBy = "", string columns = "*", int pageSize = 20, int currentPage = 1, params object[] args)
        {
            dynamic result = new ExpandoObject();
            var countSQL = string.Format("SELECT COUNT({0}) FROM {1}", PrimaryKeyField, TableName);
            if (String.IsNullOrEmpty(orderBy))
                orderBy = PrimaryKeyField;

            if (!string.IsNullOrEmpty(where))
            {
                if (!where.Trim().StartsWith("where", StringComparison.CurrentCultureIgnoreCase))
                {
                    where = "WHERE " + where;
                }
            }
            var sql = string.Format("SELECT {0} FROM (SELECT ROW_NUMBER() OVER (ORDER BY {2}) AS Row, {0} FROM {3} {4}) AS Paged ", columns, pageSize, orderBy, TableName, where);
            var pageStart = (currentPage - 1) * pageSize;
            sql += string.Format(" WHERE Row >={0} AND Row <={1}", pageStart, (pageStart + pageSize));
            countSQL += where;
            result.TotalRecords = Scalar(countSQL, args);
            result.TotalPages = result.TotalRecords / pageSize;
            if (result.TotalRecords % pageSize > 0)
                result.TotalPages += 1;
            result.Items = Query(string.Format(sql, columns, TableName), args);
            return result;
        }

        /// <summary>Returns a single row from the database.</summary>
        /// <param name="key">    The key.</param>
        /// <param name="columns">The columns.</param>
        /// <returns>A dynamic.</returns>
        public virtual dynamic Single(object key, string columns = "*")
        {
            var sql = string.Format("SELECT {0} FROM {1} WHERE {2} = @0", columns, TableName, PrimaryKeyField);
            var items = Query(sql, key).ToList();
            return items.FirstOrDefault();
        }
    }
}