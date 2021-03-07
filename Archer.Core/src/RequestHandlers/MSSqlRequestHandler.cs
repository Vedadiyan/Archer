using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System.Net;
using Spider.ArcheType;
using Spider.Extensions.Logging.Abstraction;
using Archer.Core.Prototypes;
using Archer.Core.Prototypes.Logs;
using Archer.Core.ResponseHandlers;
using Spider.Archer.ResponseHandlers;

namespace Archer.Core.RequestHandlers
{
    public class MSSqlRequestHandler : IRequest
    {
        private MSSqlProvider mssqlProvider;
        private Definition definition;
        private ILogger logger;
        public MSSqlRequestHandler(Definition definition)
        {
            this.definition = definition;
            this.mssqlProvider = (MSSqlProvider)definition.DataServiceProvider;
            if (definition.Logs != "no-log")
            {
                logger = Storage.GetLogger(definition.Logs);
            }
        }

        public void Suspend()
        {

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
                            logger?.Warning(new Warning<MSSqlRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                            {
                                Parameters = context.Headers["Authorization"],
                                Url = definition.RouteTemplate,
                                TrackingCode = trackingCode,
                                Status = "Forbidden",
                                Cause = isAuthenticated.Message
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
                        logger?.Error(ex, new Error<MSSqlRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
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
            DataTable dataTable = new DataTable();
            var concatenatedValues = context.Query.Concat(context.RouteValues);
            if (mssqlProvider.UseBody)
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
                            logger?.Error(ex, new Error<MSSqlRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                            {
                                Url = definition.RouteTemplate,
                                Status = "Bad Request",
                            })));
                        }
                        return new Error(ContentTypes.JSON, HttpStatusCode.BadRequest, String.Empty, new ResponseFormatter
                        {
                            IsWrapped = definition.IsWrapped,
                            IsCamelCase = definition.IsCamelCase
                        });
                    }
                }
            }
            var concatenatedDictionary = concatenatedValues.ToDictionary(k => k.Key.ToLower(), v => v.Value);
            try
            {
                using (SqlConnection connection = new SqlConnection(Storage.GetConnection(mssqlProvider.ConnectionString).ConnectionString))
                {
                    SqlCommand command = connection.CreateCommand();
                    command.CommandText = mssqlProvider.Command;
                    foreach (var parameter in mssqlProvider.Parameters)
                    {
                        if (concatenatedDictionary.TryGetValue(parameter.Value.ToLower(), out Object value))
                        {
                            command.Parameters.AddWithValue(parameter.Key, value);
                        }
                    }
                    command.CommandType = (CommandType)Enum.Parse(typeof(CommandType), mssqlProvider.CommandType);
                    await connection.OpenAsync();
                    dataTable.Load(await command.ExecuteReaderAsync());
                }
            }
            catch (Exception ex)
            {
                string trackingCode = null;
                if (definition.LogLevel >= LogLevel.Exception)
                {
                    trackingCode = Guid.NewGuid().ToString();
                    logger?.Error(ex, new Error<MSSqlRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                    {
                        Parameters = concatenatedDictionary,
                        Url = definition.RouteTemplate,
                        TrackingCode = trackingCode,
                        Status = "Database Connection Failure",
                    })));
                }
                return new Error(ContentTypes.JSON, HttpStatusCode.InternalServerError, trackingCode, new ResponseFormatter
                {
                    IsWrapped = definition.IsWrapped,
                    IsCamelCase = definition.IsCamelCase
                });
            }

            if (dataTable.Rows.Count > 0)
            {
                List<Dictionary<String, Object>> values = new List<Dictionary<String, Object>>();
                for (int r = 0; r < dataTable.Rows.Count; r++)
                {
                    Dictionary<String, Object> obj = new Dictionary<String, Object>();
                    for (int c = 0; c < dataTable.Columns.Count; c++)
                    {
                        String columnName = dataTable.Columns[c].ColumnName;
                        obj.Add(columnName, dataTable.Rows[r][columnName]);
                    }
                    values.Add(obj);
                }
                try
                {
                    FormatObject formatObject = new FormatObject(definition.Map, definition.Exclude);
                    if (definition.Groups?.Count > 0)
                    {
                        GroupBy groupBy = new GroupBy(definition.Groups.ElementAt(0).Value, values);
                        groupBy.Group();
                        if (definition.Groups.Count > 1)
                        {
                            for (int i = 1; i < definition.Groups.Count; i++)
                            {
                                groupBy.ThenBy(definition.Groups.ElementAt(i).Value);
                            }
                        }
                        formatObject.Format(groupBy);
                    }
                    else
                    {
                        formatObject.Format(values);
                    }
                    if (definition.LogLevel >= LogLevel.Verbose)
                    {
                        logger?.Verbose(new Verbose<MSSqlRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                        {
                            Parameters = concatenatedDictionary,
                            Url = definition.RouteTemplate,
                            Status = "OK"
                        })));
                    }
                    return new Success(ContentTypes.JSON, formatObject.GetJSON(), new ResponseFormatter
                    {
                        IsWrapped = definition.IsWrapped,
                        IsCamelCase = definition.IsCamelCase
                    });
                }
                catch (Exception ex)
                {
                    if (definition.LogLevel >= LogLevel.Exception)
                    {
                        logger?.Error(ex, new Error<MSSqlRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                        {
                            Parameters = concatenatedDictionary,
                            Url = definition.RouteTemplate,
                            Status = "Object Could not be Formatted",
                        })));
                    }
                    return new Error(ContentTypes.JSON, HttpStatusCode.InternalServerError, String.Empty, new ResponseFormatter
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
                    logger?.Verbose(new Verbose<MSSqlRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
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
    }
}