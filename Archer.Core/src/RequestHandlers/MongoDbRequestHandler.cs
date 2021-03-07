using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Archer.Core.Prototypes;
using Archer.Core.Prototypes.Logs;
using Archer.Core.ResponseHandlers;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json.Linq;
using Spider.Archer.ResponseHandlers;
using Spider.ArcheType;
using Spider.Extensions.Logging.Abstraction;

namespace Archer.Core.RequestHandlers
{
    public class MongoDbRequestHandler : IRequest
    {
        private MongoDbProvider mongoDbProvider;
        private Definition definition;
        private ILogger logger;
        public MongoDbRequestHandler(Definition definition)
        {
            this.definition = definition;
            mongoDbProvider = (MongoDbProvider)definition.DataServiceProvider;
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
                            logger?.Warning(new Warning<MongoDbRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
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
                        logger?.Error(ex, new Error<MongoDbRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
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
            if (mongoDbProvider.UseBody)
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
                        if (definition.LogLevel >= LogLevel.Exception)
                        {
                            logger?.Error(ex, new Error<MongoDbRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                            {
                                Url = definition.RouteTemplate,
                                Status = "Bad Request"
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
            String query = mongoDbProvider.Query;
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
                IMongoClient mongoClient = MongoDbPool.GetOrCreate(Storage.GetConnection(mongoDbProvider.ConnectionString).ConnectionString);
                BsonDocument ignoreId = new BsonDocument {
                    {
                        "$project",
                        new BsonDocument {
                            {
                                "_id",
                                0
                            }
                        }
                    }
                };
                var result = (await mongoClient.GetDatabase(mongoDbProvider.Database).GetCollection<BsonDocument>(mongoDbProvider.Collection).AggregateAsync<BsonDocument>(new BsonDocument[] { ignoreId }.Concat(JArray.Parse(mongoDbProvider.Query).Select(x => BsonDocument.Parse(x.ToString()))).ToArray())).ToList();
                if (result.Count > 0)
                {
                    if (definition.LogLevel >= LogLevel.Verbose)
                    {
                        logger?.Verbose(new Verbose<MongoDbRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                        {
                            Parameters = concatenatedDictionary,
                            Url = definition.RouteTemplate,
                            Status = "OK"
                        })));
                    }
                    return new Success(ContentTypes.JSON, result.ToJson(), new ResponseFormatter
                    {
                        IsWrapped = definition.IsWrapped,
                        IsCamelCase = definition.IsCamelCase
                    });
                }
                else
                {
                    if (definition.LogLevel >= LogLevel.Verbose)
                    {
                        logger?.Verbose(new Verbose<MongoDbRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
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
                    logger?.Error(ex, new Error<MongoDbRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
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
    }
    public class MongoDbPool
    {
        private static Dictionary<String, IMongoClient> connections;
        static MongoDbPool()
        {
            connections = new Dictionary<String, IMongoClient>();
        }
        private static Object lck = new Object();
        public static IMongoClient GetOrCreate(String connectionString)
        {
            if (connections.TryGetValue(connectionString, out IMongoClient connection))
            {
                return connection;
            }
            else
            {
                lock (lck)
                {
                    if (connections.TryGetValue(connectionString, out connection))
                    {
                        return connection;
                    }
                    else
                    {
                        IMongoClient mongoClient = new MongoClient(connectionString);
                        connections.Add(connectionString, mongoClient);
                        return mongoClient;
                    }
                }
            }
        }
    }
}