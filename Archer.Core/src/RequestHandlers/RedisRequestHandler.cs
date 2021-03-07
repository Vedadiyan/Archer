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
                            sb.Append(row[column].ToString());
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

            if (definition.Authentication != "no-auth")
            {
                try
                {
                    (Boolean Result, String Message) isAuthenticated = await Storage.GetAuthentication(definition.Authentication).Authenticate(context);
                    if (!isAuthenticated.Result)
                    {
                        string trackingCode = null;
                        if (definition.LogLevel >= LogLevel.Warning)
                        {
                            trackingCode = Guid.NewGuid().ToString();
                            logger?.Warning(new Warning<RedisRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                            {
                                Parameters = context.Headers["Authorization"],
                                Url = definition.RouteTemplate,
                                TrackingCode = trackingCode,
                                Status = "Forbidden",
                                Cause =  isAuthenticated.Message
                            })));
                        }
                        return new Error(ContentTypes.JSON, HttpStatusCode.Forbidden, trackingCode, new ResponseFormatter
                        {
                            IsWrapped = definition.IsWrapped,
                            IsCamelCase = definition.IsCamelCase
                        });
                    }
                }
                catch (Exception ex)
                {
                    string trackingCode = null;
                    if (definition.LogLevel >= LogLevel.Exception)
                    {
                        trackingCode = Guid.NewGuid().ToString();
                        logger?.Error(ex, new Error<RedisRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                        {
                            Url = definition.RouteTemplate,
                            TrackingCode = trackingCode,
                            Status = "Internal Server Error",
                        })));
                    }
                    return new Error(ContentTypes.JSON, HttpStatusCode.InternalServerError, trackingCode, new ResponseFormatter
                    {
                        IsWrapped = definition.IsWrapped,
                        IsCamelCase = definition.IsCamelCase
                    });
                }
            }
            var concatenatedValues = context.Query.Concat(context.RouteValues);
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
                            concatenatedValues = concatenatedValues.Concat(System.Text.Json.JsonSerializer.Deserialize<Dictionary<String, Object>>(body));
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
            var concatenatedDictionary = concatenatedValues.ToDictionary(k => k.Key.ToLower(), v => v.Value);
            String query = redisProvider.Key;
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
            try
            {
                StackExchange.Redis.ConnectionMultiplexer connectionMultiplexer = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(Storage.GetConnection(redisProvider.ConnectionString).ConnectionString);
                Console.WriteLine("Working...");
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
                            if (headers[iter].ToLower() == input.Key)
                            {
                                inputs.Add(iter, input.Value.ToString());
                                break;
                            }
                        }
                    }
                    List<Task> tasks = new List<Task>();
                    for (int x = 2; x < lines.Length - 2; x++)
                    {
                        int line = x;
                        tasks.Add(Task.Run(() =>
                        {
                            var row = lines[line].Split(',');
                            Boolean isMatch = inputs.Count == 0;
                            foreach (var input in inputs)
                            {
                                if (row[input.Key] == input.Value)
                                {
                                    isMatch = true;
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
                                        objectValue = Convert.ChangeType(row[column], types[column]);
                                    }
                                    else
                                    {
                                        objectValue = "ERROR";
                                    }
                                    value.Add(headers[column], objectValue);
                                }
                                values.Add(value);
                            }
                        }));
                    }
                    await Task.WhenAll(tasks);
                    Console.WriteLine("Done");
                    FormatObject formatObject = new FormatObject(definition.Map, definition.Exclude);
                    if (definition.Groups?.Count > 0)
                    {
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
                        return new Success(ContentTypes.JSON, formatObject.GetJSON() , new ResponseFormatter
                        {
                            IsWrapped = definition.IsWrapped,
                            IsCamelCase = definition.IsCamelCase
                        });
                    }
                    else
                    {
                        formatObject.Format(values.ToList());
                        Console.WriteLine("Done Again");
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
                            Status = "Not Found"
                        })));
                    }
                    return new Error(ContentTypes.JSON, HttpStatusCode.NotFound, String.Empty, new ResponseFormatter
                    {
                        IsWrapped = definition.IsWrapped,
                        IsCamelCase = definition.IsCamelCase
                    });
                }

            }
            catch (Exception ex)
            {
                string trackingCode = null;
                if (definition.LogLevel >= LogLevel.Exception)
                {
                    trackingCode = Guid.NewGuid().ToString();
                    logger?.Error(ex, new Error<RedisRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                    {
                        Parameters = concatenatedDictionary,
                        Url = definition.RouteTemplate,
                        TrackingCode = trackingCode,
                        Status = "Internal Server Error"
                    })));
                }
                return new Error(ContentTypes.JSON, HttpStatusCode.InternalServerError, trackingCode, new ResponseFormatter
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
    }
}