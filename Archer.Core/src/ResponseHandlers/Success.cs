using System;
using System.Net;
using System.Text.Json;
using Archer.Core.Prototypes;
using Archer.Core.ResponseHandlers;

namespace Spider.Archer.ResponseHandlers
{
    public class Success : Response
    {
        public Success(ContentTypes contentType, string response, ResponseFormatter responseFormatter) : base(contentType, responseFormatter.IsCamelCase ? JsonNamingPolicy.CamelCase : null)
        {
            base.HttpStatusCode = HttpStatusCode.OK;
            if (responseFormatter.IsWrapped)
            {
                Content = Serializer(new {
                    Status = 200,
                    Timestamp = DateTime.Now,
                    Response = "@"
                }).Replace("\"@\"", response);
            }
            else
            {
                Content = response;
            }
        }
    }
    public enum ContentTypes
    {
        JSON,
        XML,
        CSV
    }
}