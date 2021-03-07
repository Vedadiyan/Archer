// using System;
// using System.Collections.Generic;
// using System.Threading;
// using System.Threading.Tasks;
// using Jedi.Server.Prototypes;
// using Jedi.Server.ResponseHandlers;
// using Spider.Core;
// using Spider.Core.Abstraction;

// namespace Jedi.Server.RequestHandlers
// {
//     public class PushRequestHandler : IRequest
//     {
//         private List<String> keys = new List<String>();
//         private Dictionary<String, IWebSocket> connections = new Dictionary<String, IWebSocket>();
//         private Definition definition;
//         private PushProvider pushProvider;
//         private Task connectTask;
//         public PushRequestHandler(Definition definition)
//         {
//             this.definition = definition;
//             pushProvider = (PushProvider)definition.DataServiceProvider;
//             connectTask = connect(new CancellationToken());
//         }
//         private Task connect(CancellationToken cancellationToken)
//         {
//             return Task.Run(() =>
//             {
//                 String[] connection = pushProvider.Connection.Split(':');
//                 foreach (var subject in pushProvider.Subjects)
//                 {
//                     Spider.Streams.Core.StreamClient client = new Spider.Streams.Core.StreamClient(connection[0], int.Parse(connection[1]));
//                     client.Connect();
//                     client.Send(new Spider.Streams.Core.Message(Spider.Streams.Core.MessageType.Subscribe, subject, String.Empty, "Default", 0, new Byte[] { }));
//                     client.Message += async (sender, e) =>
//                     {
//                         for(int iter = 0; iter < keys.Count; iter++) {
//                             if(connections.TryGetValue(keys[iter], out IWebSocket socket)) {
//                                 await socket.Push(e);
//                             }
//                         }
//                     };
//                 }

//             }, cancellationToken);
//         }
//         public async Task<IResponse> HandleRequest(IContext context)
//         {
//             if (context.IsWebSocket)
//             {
//                 await context.WebSocket.Accept();
//                 keys.Add(context.WebSocket.Id);
//                 connections.Add(context.WebSocket.Id, context.WebSocket);
//                 context.WebSocket.Closed += (sender, e) =>
//                 {
//                     keys.Remove(e);
//                     connections.Remove(e);
//                 };
//             }
//             return null;
//         }

//         public void Suspend()
//         {
//             throw new NotImplementedException();
//         }
//     }
// }