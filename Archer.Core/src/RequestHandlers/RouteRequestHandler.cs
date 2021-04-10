using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Archer.Core.Prototypes;
using Archer.Core.Prototypes.Logs;
using Archer.Core.ResponseHandlers;
using Newtonsoft.Json.Linq;
using Spider.Archer.ResponseHandlers;
using Spider.ArcheType;
using Spider.Extensions.Logging.Abstraction;

namespace Archer.Core.RequestHandlers
{
    public class RouteRequestHandler : IRequest
    {
        private Definition definition;
        private RouteProvider routeProvider;
        private ILogger logger;
        public RouteRequestHandler(Definition definition)
        {
            this.definition = definition;
            routeProvider = (RouteProvider)definition.DataServiceProvider;
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
                            logger?.Warning(new Warning<RouteRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
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
                    string trackingCode = null;
                    if (definition.LogLevel >= LogLevel.Exception)
                    {
                        trackingCode = Guid.NewGuid().ToString();
                        logger?.Error(ex, new Error<RouteRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
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
            var concatenatedValues = context.Query.Concat(context.RouteValues);
            if (routeProvider.UseBody)
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
                            logger?.Error(ex, new Error<RouteRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                            {
                                Url = definition.RouteTemplate,
                                RequestId = requestId,
                                Status = "Bad Request"
                            })));
                        }
                        return new Error(ContentTypes.JSON, HttpStatusCode.BadRequest, requestId, new ResponseFormatter
                        {
                            IsWrapped = definition.IsWrapped,
                            IsCamelCase = definition.IsCamelCase
                        });

                    }
                }
            }
            var concatenatedDictionary = concatenatedValues.ToDictionary(k => k.Key.ToLower(), v => v.Value);
            String url = routeProvider.Url.ToLower();
            String bodyTemplate = routeProvider.BodyTemplate;
            String tmp = String.Empty;
            foreach (var i in concatenatedDictionary)
            {
                tmp = url.Replace($"@{i.Key}", i.Value.ToString());
                if (tmp != url)
                {
                    url = tmp;
                }
                else if (bodyTemplate != null)
                {
                    bodyTemplate = bodyTemplate.Replace($"@{i.Key}", i.Value.ToString());
                }
            }
            try
            {
                HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(url);
                webRequest.Method = routeProvider.Method;
                foreach (var i in context.Headers.AllKeys.Except(new string[] { "Host", "Accept-Encoding" }))
                {
                    if (webRequest.Headers[i] == null)
                    {
                        webRequest.Headers.Add(i, context.Headers[i]);
                    }
                    else
                    {
                        webRequest.Headers[i] = context.Headers[i];
                    }
                }
                if (routeProvider.Headers != null)
                {
                    foreach (var i in routeProvider.Headers)
                    {
                        if (webRequest.Headers[i.Key] == null)
                        {
                            webRequest.Headers.Add(i.Key, i.Value);
                        }
                        else
                        {
                            webRequest.Headers[i.Key] = i.Value;
                        }
                    }
                }
                if (!String.IsNullOrEmpty(bodyTemplate))
                {
                    using (StreamWriter sw = new StreamWriter(await webRequest.GetRequestStreamAsync()))
                    {
                        await sw.WriteAsync(bodyTemplate);
                    }
                }
                HttpWebResponse webResponse = (HttpWebResponse)await webRequest.GetResponseAsync();
                String responseString = String.Empty;
                using (StreamReader sr = new StreamReader(webResponse.GetResponseStream()))
                {
                    responseString = await sr.ReadToEndAsync();
                }
                if (context.Headers["accept"] == "application/json")
                {
                    FormatObject formatObject = new FormatObject(definition.Map, definition.Exclude);
                    return new Success(ContentTypes.JSON, formatObject.GetJSON(JToken.Parse(responseString)), new ResponseFormatter
                    {
                        IsWrapped = definition.IsWrapped,
                        IsCamelCase = definition.IsCamelCase
                    });
                }
                else
                {
                    return new Success(ContentTypes.JSON, responseString, new ResponseFormatter
                    {
                        IsWrapped = definition.IsWrapped,
                        IsCamelCase = definition.IsCamelCase
                    });
                }
            }
            catch (WebException WebException)
            {
                HttpWebResponse webResponse = (HttpWebResponse)WebException.Response;
                String responseString = String.Empty;
                try
                {
                    using (StreamReader sr = new StreamReader(webResponse.GetResponseStream()))
                    {
                        responseString = await sr.ReadToEndAsync();
                    }
                }
                catch
                {
                    responseString = WebException.Message;
                }
                if (definition.LogLevel >= LogLevel.Exception)
                {
                    logger?.Error(WebException, new Error<RouteRequestHandler>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                    {
                        Parameters = concatenatedDictionary,
                        Url = definition.RouteTemplate,
                        Status = "Server Error",
                        RequestId = requestId,
                        Response = responseString
                    })));
                }
                return new Error(ContentTypes.JSON, webResponse.StatusCode, requestId, new ResponseFormatter
                {
                    IsWrapped = definition.IsWrapped,
                    IsCamelCase = definition.IsCamelCase
                });
            }
        }
    }
}