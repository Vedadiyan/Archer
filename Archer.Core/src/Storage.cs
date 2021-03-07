using System;
using System.Collections.Generic;
using Archer.Archetype;
using Spider.Extensions.Logging.Abstraction;

namespace Archer.Core
{
    public static class Storage
    {
        private static Dictionary<String, IConnection> connectionStorage;
        private static Dictionary<String, IAuthentication> authenticationStorage;
        private static Dictionary<String, ILogger> loggerStorage;
        static Storage()
        {
            connectionStorage = new Dictionary<String, IConnection>();
            authenticationStorage = new Dictionary<String, IAuthentication>();
            loggerStorage = new Dictionary<String, ILogger>();
        }
        public static void RegisterConnection(String name, IConnection connection)
        {
            connectionStorage.Add(name, connection);
        }
        public static void RegisterAuthentication(String name, IAuthentication connection)
        {
            authenticationStorage.Add(name, connection);
        }
        public static void RegisterLogger(String name, ILogger connection)
        {
            loggerStorage.Add(name, connection);
        }
        public static IConnection GetConnection(String name)
        {
            return connectionStorage[name];
        }
        public static IAuthentication GetAuthentication(String name)
        {
            return authenticationStorage[name];
        }
        public static ILogger GetLogger(String name)
        {
            return loggerStorage[name];
        }
    }
}