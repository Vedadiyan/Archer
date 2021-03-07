using System;
using System.Net;
using System.Text.Json;
using Archer.Core.Prototypes;
using Spider.Archer.ResponseHandlers;

namespace Archer.Core.ResponseHandlers
{
    public class Error : Response
    {
        public Error(ContentTypes contentType, HttpStatusCode statusCode, String trackingCode, ResponseFormatter responseFormatter) : base(contentType, responseFormatter.IsCamelCase ? JsonNamingPolicy.CamelCase : null)
        {
            base.HttpStatusCode = statusCode;
            if (trackingCode == null)
            {
                trackingCode = "This Error Does NOT Support Tracking";
            }
            else if (trackingCode == String.Empty) {
                trackingCode = null;
            }
            if (responseFormatter.IsWrapped)
            {
                Content = Serializer(new
                {
                    Status = (int)statusCode,
                    Timestamp = DateTime.Now,
                    Response = new
                    {
                        Error = statusCode.ToString(),
                        TrackingCode = trackingCode
                    }
                });
            }
            else
            {
                Content = Serializer(new
                {
                    Error = statusCode.ToString(),
                    TrackingCode = trackingCode
                });
            }
        }
    }
}