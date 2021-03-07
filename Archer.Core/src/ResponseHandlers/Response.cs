using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spider.ArcheType;
using Spider.Archer.ResponseHandlers;

namespace Archer.Core.ResponseHandlers
{
    public abstract class Response : IResponse
    {
        public virtual HttpStatusCode HttpStatusCode { get; protected set; }
        public virtual WebHeaderCollection WebHeaderCollection { get; set; }
        public virtual string ContentType { get; protected set; }
        protected string Content { get; set; }
        protected Func<Object, String> Serializer { get; }
        protected Response(ContentTypes contentType, JsonNamingPolicy jsonNamingPolicy)
        {
            switch (contentType)
            {
                case ContentTypes.JSON:
                    {
                        ContentType = "application/json";
                        Serializer = (x) => JsonSerializer.Serialize(x, options: new JsonSerializerOptions {
                            PropertyNameCaseInsensitive = true,
                            PropertyNamingPolicy = jsonNamingPolicy,
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                        });
                        break;
                    }
            }
        }
        public virtual Stream RenderResponse()
        {
            MemoryStream MemoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Content));
            MemoryStream.Seek(0, SeekOrigin.Begin);
            return MemoryStream;
        }

        public virtual Task<Stream> RenderResponseAsync()
        {
            return Task.Run<Stream>(() =>
            {
                return RenderResponse();
            });
        }
    }
}