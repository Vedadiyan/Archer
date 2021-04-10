using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Archer.Core.Prototypes;
using Archer.Core.Prototypes.Logs;
using Archer.Core.RequestHandlers;
using Spider.ArcheType;
using Spider.Core.Routing;
using Spider.Extensions.Logging.Abstraction;

namespace Archer.Core
{
    public class Interpreter
    {
        private readonly String apiDirectory;
        private Dictionary<String, Route> routes;
        private Dictionary<String, IRequest> requestPool;
        private ILogger logger;
        public Interpreter(String apiDirectory, String loggerName = null)
        {
            this.apiDirectory = apiDirectory;
            routes = new Dictionary<String, Route>();
            requestPool = new Dictionary<String, IRequest>();
            if (!String.IsNullOrEmpty(loggerName))
            {
                logger = Storage.GetLogger(loggerName);
            }
        }
        public void Load()
        {
            try
            {
                String[] files = Directory.GetFiles(apiDirectory, "*.api", new EnumerationOptions { RecurseSubdirectories = true });
                foreach (var file in files)
                {
                    try
                    {
                        String[] content = File.ReadAllLines(file);
                        Definition definition = GetDefinition(content);
                        Bind(file, definition);
                    }
                    catch (Exception ex)
                    {
                        logger?.Error(ex, new Error<Interpreter>(ex.Message));
                    }

                }
                Router.RegisterRoutes(routes.Select(x => x.Value).ToArray());
                Router.RegisterRoute(new Route("help", "Get", new SimpleResponseHandler(Router.Routes.SelectMany(x => x.Value).Select(x => x.Value))));
            }
            catch (Exception ex)
            {
                logger?.Error(ex, new Error<Interpreter>(ex.Message));
            }
            FileSystemWatcher fileSystemWatcher = new FileSystemWatcher(apiDirectory);
            fileSystemWatcher.Changed += async (sender, e) =>
            {
                if (e.Name.EndsWith(".api"))
                {
                    String[] content = await GetFileContent(e.FullPath);
                    if (content != null)
                    {
                        Definition definition = GetDefinition(content);
                        IRequest request = GetRequest(definition);
                        if (requestPool.ContainsKey(e.FullPath))
                        {
                            if (request != null)
                            {
                                requestPool[e.FullPath].Suspend();
                                requestPool.Remove(e.FullPath);
                                Router.UnregisterRoute(routes[e.FullPath]);
                                Router.RegisterRoute(routes[e.FullPath] = new Route(definition.RouteTemplate, definition.Method, request));
                                requestPool.Add(e.FullPath, request);
                                Console.WriteLine("Re-Registering {0}", e.FullPath);
                            }
                            else
                            {
                                routes.Remove(e.FullPath);
                                Console.WriteLine("Removing {0}", e.FullPath);
                            }
                        }
                        else
                        {
                            routes.Add(e.FullPath, new Route(definition.RouteTemplate, definition.Method, request));
                            Router.RegisterRoute(routes[e.FullPath]);
                            requestPool.Add(e.FullPath, request);
                            Console.WriteLine("Registering {0}", e.FullPath);
                        }
                    }
                }
            };
            fileSystemWatcher.Deleted += (sender, e) =>
            {
                if (e.Name.EndsWith(".api"))
                {
                    requestPool[e.FullPath].Suspend();
                    requestPool.Remove(e.FullPath);
                    Router.UnregisterRoute(routes[e.FullPath]);
                    routes.Remove(e.FullPath);
                }
            };
            fileSystemWatcher.EnableRaisingEvents = true;
        }
        public async Task<String[]> GetFileContent(String path)
        {
            int reTries = 0;
            while (true)
            {
                try
                {
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        using (StreamReader sr = new StreamReader(fs))
                        {
                            List<String> lines = new List<String>();
                            while (!sr.EndOfStream)
                            {
                                lines.Add(sr.ReadLine());
                            }
                            return lines.ToArray();
                        }
                    }
                }
                catch
                {
                    if (reTries < 10)
                    {
                        await Task.Delay(1000);
                        reTries++;
                    }
                    else
                    {
                        return null;
                    }
                }
            }
        }
        public Definition GetDefinition(String[] text)
        {
            try
            {
                Definition definition = new Definition();
                for (int i = 0; i < text.Length; i++)
                {
                    String[] values = text[i].Split(' ', 2);
                    switch (values[0].TrimStart().TrimStart('\t').TrimStart().ToLower())
                    {
                        case "create":
                            definition.RouteTemplate = trim(values[1]);
                            break;
                        case "authentication":
                            definition.Authentication = trim(values[1]);
                            break;
                        case "wrapped":
                            var wrappedValue = trim(values[1]).ToLower().Split('|');
                            if (trim(wrappedValue[0]) == "true")
                            {
                                definition.IsWrapped = true;
                            }
                            if (wrappedValue.Length > 1 && trim(wrappedValue[1]) == "camelcase")
                            {
                                definition.IsCamelCase = true;
                            }
                            break;
                        case "logs":
                            {
                                var logValue = trim(values[1]).Split('|');
                                definition.Logs = trim(logValue[0]);
                                if (logValue.Length > 1)
                                {
                                    var logLevel = trim(logValue[1]);
                                    switch (logLevel.ToLower())
                                    {
                                        case "verbose":
                                            definition.LogLevel = LogLevel.Verbose;
                                            break;
                                        case "debug":
                                            definition.LogLevel = LogLevel.Debug;
                                            break;
                                        case "information":
                                            definition.LogLevel = LogLevel.Information;
                                            break;
                                        case "warning":
                                            definition.LogLevel = LogLevel.Warning;
                                            break;
                                        case "exception":
                                            definition.LogLevel = LogLevel.Exception;
                                            break;
                                    }
                                }
                                break;
                            }
                        case "method":
                            definition.Method = trim(values[1]);
                            break;
                        case "use":
                            string[] useValues = trim(values[1]).Split();
                            switch (useValues[0].ToLower())
                            {
                                case "quest":
                                    List<string> questParameters = new List<string>();
                                    for(int iter = 1; iter < useValues.Length; iter++) {
                                        questParameters.Add(useValues[iter]);
                                    }
                                    definition.QuestParameters = questParameters.ToArray();
                                    break;
                                case "restrict-json-in-query-string":
                                    definition.RestrictJsonInQueryString = true;
                                    break;
                            }
                            break;
                        case "data-source":
                            {
                                String value = trim(values[1]);
                                i++;
                                switch (value.ToLower())
                                {
                                    case "mssql":
                                        {
                                            Boolean @continue = true;
                                            MSSqlProvider mSSqlProvider = new MSSqlProvider();
                                            Dictionary<String, String> parameters = new Dictionary<String, String>();
                                            for (; i < text.Length && @continue; i++)
                                            {
                                                values = text[i].TrimStart().TrimStart('\t').TrimStart().Split(' ', 2);
                                                switch (values[0].TrimStart().TrimStart('\t').TrimStart().ToLower())
                                                {
                                                    case "connection":
                                                        mSSqlProvider.ConnectionString = trim(values[1]);
                                                        break;
                                                    case "command":
                                                        mSSqlProvider.Command = trim(values[1]);
                                                        break;
                                                    case "command-type":
                                                        mSSqlProvider.CommandType = trim(values[1]);
                                                        break;
                                                    case "use":
                                                        switch (trim(values[1]))
                                                        {
                                                            case "body":
                                                                mSSqlProvider.UseBody = true;
                                                                break;
                                                        }
                                                        break;
                                                    case "param":
                                                        {
                                                            String[] parameter = trim(values[1]).Split("=");
                                                            parameters.Add(trim(parameter[0]), trim(parameter[1]));
                                                            break;
                                                        }
                                                    case "output":
                                                        {
                                                            Boolean continueL1 = true;
                                                            i++;
                                                            for (; i < text.Length && continueL1; i++)
                                                            {
                                                                values = trim(text[i]).Split(' ', 2);
                                                                switch (trim(values[0]))
                                                                {
                                                                    case "group":
                                                                        {
                                                                            Dictionary<int, String> groupByValues = new Dictionary<int, String>();
                                                                            i++;
                                                                            Boolean continueL2 = true;
                                                                            for (; i < text.Length && continueL2; i++)
                                                                            {
                                                                                values = trim(text[i]).Split(' ', 2);
                                                                                switch (trim(values[0]).ToLower())
                                                                                {
                                                                                    case "begin":
                                                                                        break;
                                                                                    case "end":
                                                                                        continueL2 = false;
                                                                                        i--;
                                                                                        break;
                                                                                    default:
                                                                                        {
                                                                                            groupByValues.Add(int.Parse(values[0]), values[1]);
                                                                                            break;
                                                                                        }
                                                                                }
                                                                            }
                                                                            definition.Groups = groupByValues;
                                                                            break;
                                                                        }
                                                                    case "map":
                                                                        {
                                                                            Boolean continueL2 = true;
                                                                            Dictionary<String, String> mapValues = new Dictionary<String, String>();
                                                                            i++;
                                                                            for (; i < text.Length && continueL2; i++)
                                                                            {
                                                                                values = trim(text[i]).Split(' ', 2);
                                                                                switch (trim(values[0]).ToLower())
                                                                                {
                                                                                    case "begin":
                                                                                        break;
                                                                                    case "end":
                                                                                        continueL2 = false;
                                                                                        i--;
                                                                                        break;
                                                                                    default:
                                                                                        {
                                                                                            mapValues.Add(values[0], values[1]);
                                                                                            break;
                                                                                        }
                                                                                }
                                                                            }
                                                                            definition.Map = mapValues;
                                                                            break;
                                                                        }
                                                                    case "exclude":
                                                                        {
                                                                            Boolean continueL2 = true;
                                                                            List<String> excludeValues = new List<String>();
                                                                            i++;
                                                                            for (; i < text.Length && continueL2; i++)
                                                                            {
                                                                                String _value = trim(text[i]);
                                                                                switch (_value.ToLower())
                                                                                {
                                                                                    case "begin":
                                                                                        break;
                                                                                    case "end":
                                                                                        continueL2 = false;
                                                                                        i--;
                                                                                        break;
                                                                                    default:
                                                                                        {
                                                                                            excludeValues.Add(_value);
                                                                                            break;
                                                                                        }
                                                                                }
                                                                            }
                                                                            definition.Exclude = excludeValues;
                                                                            break;
                                                                        }
                                                                    case "end":
                                                                        continueL1 = false;
                                                                        i--;
                                                                        break;
                                                                }
                                                            }
                                                            break;
                                                        }
                                                    case "end":
                                                        @continue = false;
                                                        i--;
                                                        break;
                                                }
                                            }
                                            mSSqlProvider.Parameters = parameters;
                                            definition.DataServiceProvider = mSSqlProvider;
                                            break;
                                        }
                                    case "mongodb":
                                        {
                                            Boolean @continue = true;
                                            MongoDbProvider mongoDbProvider = new MongoDbProvider();
                                            for (; i < text.Length && @continue; i++)
                                            {
                                                values = trim(text[i]).Split(' ', 2);
                                                switch (trim(values[0]).ToLower())
                                                {
                                                    case "connection":
                                                        mongoDbProvider.ConnectionString = trim(values[1]);
                                                        break;
                                                    case "database":
                                                        mongoDbProvider.Database = trim(values[1]);
                                                        break;
                                                    case "collection":
                                                        mongoDbProvider.Collection = trim(values[1]);
                                                        break;
                                                    case "query":
                                                        {
                                                            StringBuilder sb = new StringBuilder();
                                                            i++;
                                                            Boolean continueL2 = true;
                                                            for (; i < text.Length && continueL2; i++)
                                                            {
                                                                String tmp = trim(text[i]);
                                                                values = tmp.Split(' ', 2);
                                                                switch (trim(values[0]).ToLower())
                                                                {
                                                                    case "begin":
                                                                        break;
                                                                    case "end":
                                                                        continueL2 = false;
                                                                        i--;
                                                                        break;
                                                                    default:
                                                                        {
                                                                            sb.Append(tmp);
                                                                            break;
                                                                        }
                                                                }
                                                            }
                                                            mongoDbProvider.Query = sb.ToString();
                                                            break;
                                                        }
                                                    case "use":
                                                        switch (trim(values[1]).ToLower())
                                                        {
                                                            case "body":
                                                                mongoDbProvider.UseBody = true;
                                                                break;
                                                        }
                                                        break;
                                                    case "end":
                                                        @continue = false;
                                                        i--;
                                                        break;
                                                }
                                            }
                                            definition.DataServiceProvider = mongoDbProvider;
                                            break;
                                        }
                                    case "redis":
                                        {
                                            Boolean @continue = true;
                                            RedisProvider redisProviders = new RedisProvider();
                                            for (; i < text.Length && @continue; i++)
                                            {
                                                values = trim(text[i]).Split(' ', 2);
                                                switch (trim(values[0]).ToLower())
                                                {
                                                    case "connection":
                                                        redisProviders.ConnectionString = trim(values[1]);
                                                        break;
                                                    case "database":
                                                        redisProviders.Database = int.Parse(trim(values[1]));
                                                        break;
                                                    case "key":
                                                        {
                                                            redisProviders.Key = values[1];
                                                            break;
                                                        }
                                                    case "task":
                                                        {
                                                            Boolean continueL1 = true;
                                                            i++;
                                                            TaskProvider taskProvider = new TaskProvider();
                                                            for (; i < text.Length && continueL1; i++)
                                                            {
                                                                values = trim(text[i]).Split(' ', 2);
                                                                switch (trim(values[0]).ToLower())
                                                                {
                                                                    case "data-source":
                                                                        {
                                                                            Boolean continueL2 = true;
                                                                            String _values = trim(values[1]);
                                                                            i++;
                                                                            switch (_values.ToLower())
                                                                            {
                                                                                case "mssql":
                                                                                    {
                                                                                        MSSqlProvider mssqlProvider = new MSSqlProvider();
                                                                                        Dictionary<String, String> parameters = new Dictionary<String, String>();
                                                                                        for (; i < text.Length && continueL2; i++)
                                                                                        {
                                                                                            values = trim(text[i]).Split(' ', 2);
                                                                                            switch (trim(values[0]).ToLower())
                                                                                            {
                                                                                                case "connection":
                                                                                                    {
                                                                                                        mssqlProvider.ConnectionString = trim(values[1]);
                                                                                                        break;
                                                                                                    }
                                                                                                case "command":
                                                                                                    {
                                                                                                        mssqlProvider.Command = trim(values[1]);
                                                                                                        break;
                                                                                                    }
                                                                                                case "command-type":
                                                                                                    {
                                                                                                        mssqlProvider.CommandType = trim(values[1]);
                                                                                                        break;
                                                                                                    }
                                                                                                case "param":
                                                                                                    {
                                                                                                        String[] parameter = trim(values[1]).Split("=");
                                                                                                        parameters.Add(parameter[0], parameter[1]);
                                                                                                        break;
                                                                                                    }
                                                                                                case "end":
                                                                                                    {
                                                                                                        @continueL2 = false;
                                                                                                        i--;
                                                                                                        break;
                                                                                                    }
                                                                                            }
                                                                                        }
                                                                                        mssqlProvider.Parameters = parameters;
                                                                                        taskProvider.DataServiceProvider = mssqlProvider;
                                                                                        break;
                                                                                    }
                                                                            }
                                                                            break;
                                                                        }
                                                                    case "interval":
                                                                        {
                                                                            taskProvider.Interval = TimeSpan.Parse(trim(values[1]));
                                                                            break;
                                                                        }
                                                                    case "end":
                                                                        continueL1 = false;
                                                                        i--;
                                                                        break;
                                                                }
                                                            }
                                                            redisProviders.TaskProvider = taskProvider;
                                                            break;
                                                        }
                                                    case "output":
                                                        {
                                                            Boolean continueL1 = true;
                                                            i++;
                                                            for (; i < text.Length && continueL1; i++)
                                                            {
                                                                values = trim(text[i]).Split(' ', 2);
                                                                switch (trim(values[0]).ToLower())
                                                                {
                                                                    case "group":
                                                                        {
                                                                            Dictionary<int, String> groupByValues = new Dictionary<int, String>();
                                                                            i++;
                                                                            Boolean continueL2 = true;
                                                                            for (; i < text.Length && continueL2; i++)
                                                                            {
                                                                                values = trim(text[i]).Split(' ', 2);
                                                                                switch (trim(values[0]).ToLower())
                                                                                {
                                                                                    case "begin":
                                                                                        break;
                                                                                    case "end":
                                                                                        continueL2 = false;
                                                                                        i--;
                                                                                        break;
                                                                                    default:
                                                                                        {
                                                                                            groupByValues.Add(int.Parse(values[0]), values[1]);
                                                                                            break;
                                                                                        }
                                                                                }
                                                                            }
                                                                            definition.Groups = groupByValues;
                                                                            break;
                                                                        }
                                                                    case "map":
                                                                        {
                                                                            Boolean continueL2 = true;
                                                                            Dictionary<String, String> mapValues = new Dictionary<String, String>();
                                                                            i++;
                                                                            for (; i < text.Length && continueL2; i++)
                                                                            {
                                                                                values = trim(text[i]).Split(' ', 2);
                                                                                switch (trim(values[0]).ToLower())
                                                                                {
                                                                                    case "begin":
                                                                                        break;
                                                                                    case "end":
                                                                                        continueL2 = false;
                                                                                        i--;
                                                                                        break;
                                                                                    default:
                                                                                        {
                                                                                            mapValues.Add(values[0], values[1]);
                                                                                            break;
                                                                                        }
                                                                                }
                                                                            }
                                                                            definition.Map = mapValues;
                                                                            break;
                                                                        }
                                                                    case "exclude":
                                                                        {
                                                                            Boolean continueL2 = true;
                                                                            List<String> excludeValues = new List<String>();
                                                                            i++;
                                                                            for (; i < text.Length && continueL2; i++)
                                                                            {
                                                                                String _value = trim(text[i]);
                                                                                switch (_value.ToLower())
                                                                                {
                                                                                    case "begin":
                                                                                        break;
                                                                                    case "end":
                                                                                        continueL2 = false;
                                                                                        i--;
                                                                                        break;
                                                                                    default:
                                                                                        {
                                                                                            excludeValues.Add(_value);
                                                                                            break;
                                                                                        }
                                                                                }
                                                                            }
                                                                            definition.Exclude = excludeValues;
                                                                            break;
                                                                        }
                                                                    case "end":
                                                                        continueL1 = false;
                                                                        i--;
                                                                        break;
                                                                }
                                                            }
                                                            break;
                                                        }
                                                    case "use":
                                                        switch (trim(values[1]).ToLower())
                                                        {
                                                            case "body":
                                                                redisProviders.UseBody = true;
                                                                break;
                                                        }
                                                        break;
                                                    case "end":
                                                        @continue = false;
                                                        i--;
                                                        break;
                                                }
                                            }
                                            definition.DataServiceProvider = redisProviders;
                                            break;
                                        }
                                    case "route":
                                        {
                                            Boolean @continue = true;
                                            RouteProvider routeProvider = new RouteProvider();
                                            Dictionary<String, String> parameters = new Dictionary<String, String>();
                                            for (; i < text.Length && @continue; i++)
                                            {
                                                values = trim(text[i]).Split(' ', 2);
                                                switch (trim(values[0]).ToLower())
                                                {
                                                    case "url":
                                                        routeProvider.Url = trim(values[1]);
                                                        break;
                                                    case "headers":
                                                        {
                                                            Dictionary<String, String> headers = new Dictionary<String, String>();
                                                            i++;
                                                            Boolean continueL2 = true;
                                                            for (; i < text.Length && continueL2; i++)
                                                            {
                                                                String tmp = trim(text[i]);
                                                                values = tmp.Split(' ', 2);
                                                                switch (trim(values[0]).ToLower())
                                                                {
                                                                    case "begin":
                                                                        break;
                                                                    case "end":
                                                                        continueL2 = false;
                                                                        i--;
                                                                        break;
                                                                    default:
                                                                        {
                                                                            headers.Add(values[0], values[1]);
                                                                            break;
                                                                        }
                                                                }
                                                            }
                                                            routeProvider.Headers = headers;
                                                            break;
                                                        }
                                                    case "method":
                                                        {
                                                            routeProvider.Method = values[1];
                                                            break;
                                                        }
                                                    case "output":
                                                        {
                                                            Boolean continueL1 = true;
                                                            i++;
                                                            for (; i < text.Length && continueL1; i++)
                                                            {
                                                                values = trim(text[i]).Split(' ', 2);
                                                                switch (trim(values[0]).ToLower())
                                                                {
                                                                    case "map":
                                                                        {
                                                                            Boolean continueL2 = true;
                                                                            Dictionary<String, String> mapValues = new Dictionary<String, String>();
                                                                            i++;
                                                                            for (; i < text.Length && continueL2; i++)
                                                                            {
                                                                                values = trim(text[i]).Split(' ', 2);
                                                                                switch (trim(values[0]).ToLower())
                                                                                {
                                                                                    case "begin":
                                                                                        break;
                                                                                    case "end":
                                                                                        continueL2 = false;
                                                                                        i--;
                                                                                        break;
                                                                                    default:
                                                                                        {
                                                                                            mapValues.Add(values[0], values[1]);
                                                                                            break;
                                                                                        }
                                                                                }
                                                                            }
                                                                            definition.Map = mapValues;
                                                                            break;
                                                                        }
                                                                    case "exclude":
                                                                        {
                                                                            Boolean continueL2 = true;
                                                                            List<String> excludeValues = new List<String>();
                                                                            i++;
                                                                            for (; i < text.Length && continueL2; i++)
                                                                            {
                                                                                String _value = trim(text[i]);
                                                                                switch (_value.ToLower())
                                                                                {
                                                                                    case "begin":
                                                                                        break;
                                                                                    case "end":
                                                                                        continueL2 = false;
                                                                                        i--;
                                                                                        break;
                                                                                    default:
                                                                                        {
                                                                                            excludeValues.Add(_value);
                                                                                            break;
                                                                                        }
                                                                                }
                                                                            }
                                                                            definition.Exclude = excludeValues;
                                                                            break;
                                                                        }
                                                                    case "end":
                                                                        continueL1 = false;
                                                                        i--;
                                                                        break;
                                                                }
                                                            }
                                                            break;
                                                        }

                                                    case "use":
                                                        switch (trim(values[1]).ToLower())
                                                        {
                                                            case "body":
                                                                routeProvider.UseBody = true;
                                                                break;
                                                        }
                                                        break;
                                                    case "end":
                                                        @continue = false;
                                                        i--;
                                                        break;
                                                }
                                            }
                                            definition.DataServiceProvider = routeProvider;
                                            break;
                                        }
                                    case "broker":
                                        {
                                            Boolean @continue = true;
                                            PushProvider pushProvider = new PushProvider();
                                            Dictionary<String, String> parameters = new Dictionary<String, String>();
                                            for (; i < text.Length && @continue; i++)
                                            {
                                                values = trim(text[i]).Split(' ', 2);
                                                switch (trim(values[0]).ToLower())
                                                {
                                                    case "connection":
                                                        pushProvider.Connection = trim(values[1]);
                                                        break;
                                                    case "subjects":
                                                        {
                                                            pushProvider.Subjects = trim(values[1]).Split('|').Select(x => x.TrimStart().TrimStart('\t').TrimStart().TrimEnd().TrimEnd('\t').TrimEnd()).ToArray();
                                                            break;
                                                        }
                                                    case "protocol":
                                                        {
                                                            pushProvider.Protocol = values[1];
                                                            break;
                                                        }
                                                    case "end":
                                                        @continue = false;
                                                        i--;
                                                        break;
                                                }
                                            }
                                            definition.DataServiceProvider = pushProvider;
                                            break;
                                        }
                                }
                                break;
                            }
                    }
                }
                return definition;
            }
            catch (Exception ex)
            {
                logger?.Error(ex, new Error<Interpreter>(ex.Message));
                return null;
            }
        }
        public void Bind(String filePath, Definition definition)
        {
            IRequest request = GetRequest(definition);
            if (request != null)
            {
                try
                {
                    Route route = new Route(definition.RouteTemplate, definition.Method, request);
                    requestPool.Add(filePath, request);
                    routes.Add(filePath, route);
                }
                catch (Exception ex)
                {
                    logger?.Error(ex, new Error<Interpreter>(ex.Message));
                }
            }
        }
        public IRequest GetRequest(Definition definition)
        {
            if (definition != null)
            {
                IRequest request = null;
                if (definition.DataServiceProvider is MSSqlProvider)
                {
                    request = new MSSqlRequestHandler(definition);
                }
                else if (definition.DataServiceProvider is MongoDbProvider)
                {
                    request = new MongoDbRequestHandler(definition);
                }
                else if (definition.DataServiceProvider is RouteProvider)
                {
                    request = new RouteRequestHandler(definition);
                }
                else if (definition.DataServiceProvider is RedisProvider)
                {
                    request = new RedisRequestHandler(definition);
                }
                else if (definition.DataServiceProvider is PushProvider)
                {
                    //request = new PushRequestHandler(definition);
                }
                return request;
            }
            return null;
        }
        private string trim(string str)
        {
            return str.TrimStart().TrimStart('\t').TrimStart().TrimEnd().TrimEnd('\t').TrimEnd();
        }
    }
}