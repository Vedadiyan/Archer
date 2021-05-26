using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Archer.Core.Prototypes;
using Archer.Core.Prototypes.Logs;
using Archer.Core.ResponseHandlers;
using Quest.Core.Grammar;
using Spider.Archer.ResponseHandlers;
using Spider.ArcheType;
using Spider.Extensions.Logging.Abstraction;

namespace Archer.Core.RequestHandlers
{
    public class RedisRequestHandler : IRequest
    {
        private Definition definition;
        private RedisProvider redisProvider;
        private Timer timer;
        private ILogger logger;
        public RedisRequestHandler(Definition definition)
        {
            this.definition = definition;
            if (definition.Logs != "no-log")
            {
                logger = Storage.GetLogger(definition.Logs);
            }
            redisProvider = (RedisProvider)definition.DataServiceProvider;
            timer = new Timer(taskHandler, null, 0, (int)redisProvider.TaskProvider.Interval.TotalMilliseconds);
        }
        private async void taskHandler(Object state)
        {
            if (redisProvider.TaskProvider.DataServiceProvider is MSSqlProvider)
            {
                try
                {
                    MSSqlProvider mssqlProvider = (MSSqlProvider)redisProvider.TaskProvider.DataServiceProvider;
                    DataTable dataTable = new DataTable();
                    using (SqlConnection connection = new SqlConnection(Storage.GetConnection(mssqlProvider.ConnectionString).ConnectionString))
                    {
                        SqlCommand command = connection.CreateCommand();
                        command.CommandText = mssqlProvider.Command;
                        command.CommandTimeout = (int)redisProvider.TaskProvider.Interval.TotalMilliseconds / 2;
                        if (mssqlProvider.Parameters != null)
                        {
                            foreach (var parameter in mssqlProvider.Parameters)
                            {
                                String[] values = parameter.Value.Split(new String[] { "::" }, StringSplitOptions.None);
                                command.Parameters.AddWithValue(parameter.Key, Convert.ChangeType(values[0], (TypeCode)Enum.Parse(typeof(TypeCode), values[1])));
                            }
                        }
                        command.CommandType = (CommandType)Enum.Parse(typeof(CommandType), mssqlProvider.CommandType);
                        await connection.OpenAsync();
                        dataTable.Load(await command.ExecuteReaderAsync());
                    }
                    StringBuilder sb = new StringBuilder();
                    for (int column = 0; column < dataTable.Columns.Count; column++)
                    {
                        sb.Append(dataTable.Columns[column].ColumnName);
                        if (column < dataTable.Columns.Count - 1)
                        {
                            sb.Append(",");
                        }
                    }
                    sb.Append("\n");
                    for (int column = 0; column < dataTable.Columns.Count; column++)
                    {
                        sb.Append(Type.GetTypeCode(dataTable.Columns[column].DataType));
                        if (column < dataTable.Columns.Count - 1)
                        {
                            sb.Append(",");
                        }
                    }
                    sb.Append("\n");
                    foreach (DataRow row in dataTable.Rows)
                    {
                        for (int column = 0; column < dataTable.Columns.Count; column++)
                        {
                            object value = row[column];
                            if (value == DBNull.Value)
                            {
                                sb.Append("null");
                            }
                            else
                            {
                                var strValue = row[column].ToString();
                                if (strValue == "null")
                                {
                                    sb.Append(strValue).Append(" ");
                                }
                                else
                                {
                                    sb.Append(strValue.Replace(",", "\\,"));
                                }
                            }
                            if (column < dataTable.Columns.Count - 1)
                            {
                                sb.Append(",");
                            }

                        }
                        sb.Append("\n");
                    }
                    StackExchange.Redis.ConnectionMultiplexer connectionMultiplexer = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(Storage.GetConnection(redisProvider.ConnectionString).ConnectionString);
                    await connectionMultiplexer.GetDatabase(redisProvider.Database).StringSetAsync(redisProvider.Key, sb.ToString());
                }
                catch (Exception ex)
                {
                    logger?.Error(ex, new Error<RedisRequestHandler>(ex.Message));
                }
            }
        }
        public async Task<IResponse> HandleRequest(IContext context)
        {

            string requestId = Guid.NewGuid().ToString();
            if (definition.Authentication != "no-auth")
            {
                try
                {
                    (Boolean Result, String Message) isAuthenticated = await Storage.GetAuthentication(definition.Authentication).Authenticate(context);
                    if (!isAuthenticated.Result)
                    {
                        if (definition.LogLevel >= LogLevel.Warning)
                        {
                            logger?.Warning(new Warning<RedisRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                            {
                                Parameters = context.Headers["Authorization"],
                                Url = definition.RouteTemplate,
                                RequestId = requestId,
                                Status = "Forbidden",
                                Cause = isAuthenticated.Message
                            })));
                        }
                        return new Error(ContentTypes.JSON, HttpStatusCode.Forbidden, requestId, new ResponseFormatter
                        {
                            IsWrapped = definition.IsWrapped,
                            IsCamelCase = definition.IsCamelCase
                        });
                    }
                }
                catch (Exception ex)
                {
                    if (definition.LogLevel >= LogLevel.Exception)
                    {
                        logger?.Error(ex, new Error<RedisRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                        {
                            Url = definition.RouteTemplate,
                            RequestId = requestId,
                            Status = "Internal Server Error",
                        })));
                    }
                    return new Error(ContentTypes.JSON, HttpStatusCode.InternalServerError, requestId, new ResponseFormatter
                    {
                        IsWrapped = definition.IsWrapped,
                        IsCamelCase = definition.IsCamelCase
                    });
                }
            }
            Dictionary<string, object> concatenatedDictionary = new Dictionary<string, object>();
            foreach (var q in context.Query)
            {
                if (q.Value is string str)
                {
                    var value = str.TrimStart().TrimStart('\t').TrimStart().TrimEnd('\t');
                    if (definition.QuestParameters != null && definition.QuestParameters.Contains(q.Key) && value.StartsWith('{') && value.EndsWith('}'))
                    {
                        concatenatedDictionary.Add(q.Key.ToLower(), new QuestReader(value));
                    }
                    else if (definition.RestrictJsonInQueryString)
                    {
                        return new Error(ContentTypes.JSON, HttpStatusCode.BadRequest, requestId, new ResponseFormatter
                        {
                            IsWrapped = definition.IsWrapped,
                            IsCamelCase = definition.IsCamelCase
                        });
                    }
                }
                concatenatedDictionary.Add(q.Key.ToLower(), q.Value);
            }
            foreach (var r in context.RouteValues)
            {
                concatenatedDictionary.Add(r.Key.ToLower(), r.Value);
            }
            if (redisProvider.UseBody)
            {
                if (context.Headers["content-type"] == "application/json")
                {
                    try
                    {
                        using (StreamReader streamRreader = new StreamReader(context.Body))
                        {
                            streamRreader.BaseStream.Seek(0, SeekOrigin.Begin);
                            var body = await streamRreader.ReadToEndAsync();
                            foreach (var v in System.Text.Json.JsonSerializer.Deserialize<Dictionary<String, Object>>(body))
                            {
                                concatenatedDictionary.Add(v.Key.ToLower(), v.Value);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (definition.LogLevel >= LogLevel.Verbose)
                        {
                            logger?.Error(ex, new Error<RedisRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                            {
                                Url = definition.RouteTemplate,
                                Status = "Bad Request",
                            })));
                        }
                    }
                }
            }
            String query = redisProvider.Key;
            // Needs Revision
            foreach (var i in concatenatedDictionary)
            {
                if (i.Value is String || i.Value is DateTime)
                {
                    query = query.Replace($"@{i.Key}", $"\"{i.Value}\"");
                }
                else
                {
                    query = query.Replace($"@{i.Key}", i.Value.ToString());
                }
            }
            // End of Revision Block
            try
            {
                if (definition.LogLevel == LogLevel.Verbose)
                {
                    logger.Verbose(new Verbose<RedisRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                    {
                        Parameters = concatenatedDictionary,
                        Url = definition.RouteTemplate,
                        RequestId = requestId,
                        Status = "Processing"
                    })));
                }
                StackExchange.Redis.ConnectionMultiplexer connectionMultiplexer = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(Storage.GetConnection(redisProvider.ConnectionString).ConnectionString);
                String result = await connectionMultiplexer.GetDatabase(redisProvider.Database).StringGetAsync(redisProvider.Key);
                await connectionMultiplexer.CloseAsync();
                if (!String.IsNullOrEmpty(result))
                {
                    String[] lines = result.Split('\n');
                    String[] headers = lines[0].Split(',');
                    TypeCode[] types = lines[1].Split(',').Select(x => (TypeCode)Enum.Parse(typeof(TypeCode), x)).ToArray();
                    Dictionary<int, String> inputs = new Dictionary<int, String>();
                    ConcurrentBag<Dictionary<String, Object>> values = new ConcurrentBag<Dictionary<String, Object>>();
                    for (int iter = 0; iter < headers.Length; iter++)
                    {
                        foreach (var input in concatenatedDictionary)
                        {
                            if (input.Value is QuestReader qr)
                            {
                                if (qr.Eval(headers[iter]))
                                {
                                    inputs.Add(iter, input.Value.ToString());
                                    break;
                                }
                            }
                            else
                            {
                                if (headers[iter].ToLower() == input.Key)
                                {
                                    inputs.Add(iter, input.Value.ToString());
                                    break;
                                }
                            }
                        }
                    }
                    List<Task> tasks = new List<Task>();
                    for (int x = 2; x < lines.Length; x++)
                    {
                        int line = x;
                        tasks.Add(Task.Run(() =>
                        {
                            if (!String.IsNullOrEmpty(lines[line]))
                            {
                                var row = split(lines[line]).ToArray();
                                Boolean isMatch = true;
                                foreach (var input in inputs)
                                {
                                    if (row[input.Key] != input.Value)
                                    {
                                        isMatch = false;
                                        break;
                                    }
                                }
                                if (isMatch)
                                {
                                    Dictionary<String, Object> value = new Dictionary<String, Object>();
                                    for (int column = 0; column < row.Length; column++)
                                    {
                                        Object objectValue = null;
                                        if (!String.IsNullOrEmpty(row[column]))
                                        {
                                            if (row[column] != "null")
                                            {
                                                if (row[column] == "null ")
                                                {
                                                    objectValue = "null";
                                                }
                                                else
                                                {
                                                    objectValue = Convert.ChangeType(row[column], types[column]);
                                                }
                                            }
                                            else
                                            {
                                                objectValue = DBNull.Value;
                                            }
                                        }
                                        else
                                        {
                                            objectValue = "ERROR";
                                        }
                                        value.Add(headers[column], objectValue);
                                    }
                                    values.Add(value);
                                }
                            }
                        }));
                    }
                    await Task.WhenAll(tasks);
                    if (definition.LogLevel == LogLevel.Verbose)
                    {
                        logger.Verbose(new Verbose<RedisRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                        {
                            Parameters = concatenatedDictionary,
                            Url = definition.RouteTemplate,
                            RequestId = requestId,
                            Status = "Finished Processing"
                        })));
                    }
                    FormatObject formatObject = new FormatObject(definition.Map, definition.Exclude);
                    if (definition.Groups?.Count > 0)
                    {
                        if (definition.LogLevel == LogLevel.Verbose)
                        {
                            logger.Verbose(new Verbose<RedisRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                            {
                                Parameters = concatenatedDictionary,
                                Url = definition.RouteTemplate,
                                RequestId = requestId,
                                Status = "Post-Processing"
                            })));
                        }
                        GroupBy groupBy = new GroupBy(definition.Groups.ElementAt(0).Value, values.ToList());
                        groupBy.Group();
                        if (definition.Groups.Count > 1)
                        {
                            for (int i = 0; i < definition.Groups.Count; i++)
                            {
                                groupBy.ThenBy(definition.Groups.ElementAt(i).Value);
                            }
                        }
                        formatObject.Format(groupBy);
                        if (definition.LogLevel == LogLevel.Verbose)
                        {
                            logger.Verbose(new Verbose<RedisRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                            {
                                Parameters = concatenatedDictionary,
                                Url = definition.RouteTemplate,
                                RequestId = requestId,
                                Status = "Finished Post-Processing"
                            })));
                        }
                        return new Success(ContentTypes.JSON, formatObject.GetJSON(), new ResponseFormatter
                        {
                            IsWrapped = definition.IsWrapped,
                            IsCamelCase = definition.IsCamelCase
                        });
                    }
                    else
                    {
                        formatObject.Format(values.ToList());
                        return new Success(ContentTypes.JSON, formatObject.GetJSON(true), new ResponseFormatter
                        {
                            IsWrapped = definition.IsWrapped,
                            IsCamelCase = definition.IsCamelCase
                        });
                    }
                }
                else
                {
                    if (definition.LogLevel >= LogLevel.Verbose)
                    {
                        logger?.Verbose(new Verbose<RedisRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                        {
                            Parameters = concatenatedDictionary,
                            Url = definition.RouteTemplate,
                            RequestId = requestId,
                            Status = "No Content"
                        })));
                    }
                    return new Error(ContentTypes.JSON, HttpStatusCode.NoContent, requestId, new ResponseFormatter
                    {
                        IsWrapped = definition.IsWrapped,
                        IsCamelCase = definition.IsCamelCase
                    });
                }

            }
            catch (Exception ex)
            {
                if (definition.LogLevel >= LogLevel.Exception)
                {
                    logger?.Error(ex, new Error<RedisRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                    {
                        Parameters = concatenatedDictionary,
                        Url = definition.RouteTemplate,
                        RequestId = requestId,
                        Status = "Internal Server Error"
                    })));
                }
                return new Error(ContentTypes.JSON, HttpStatusCode.InternalServerError, requestId, new ResponseFormatter
                {
                    IsWrapped = definition.IsWrapped,
                    IsCamelCase = definition.IsCamelCase
                });
            }
        }

        public void Suspend()
        {
            timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        private IEnumerable<string> split(string str)
        {
            StringBuilder buffer = new StringBuilder();
            if (str.Length > 0)
            {
                if (str[0] == ',')
                {
                    yield return "";
                }
                else
                {
                    buffer.Append(str[0]);
                }
                for (int i = 1; i < str.Length; i++)
                {
                    if (str[i] == ',' && str[i - 1] != '\\')
                    {
                        yield return buffer.ToString();
                        buffer.Clear();
                    }
                    else
                    {
                        buffer.Append(str[i]);
                    }
                }
                if (buffer.Length > 0)
                {
                    yield return buffer.ToString();
                }
            }
        }
    }
}