using System.Threading.Tasks;
using Archer.Core.Prototypes;
using Spider.Archer.ResponseHandlers;
using Spider.ArcheType;
using Spider.Extensions.Logging.Abstraction;

namespace Archer.Core.RequestHandlers
{
    public class SimpleResponseHandler : IRequest
    {
        private MSSqlProvider mssqlProvider;
        private object obj;
        private ILogger logger;
        public SimpleResponseHandler(object obj)
        {
            this.obj = obj;
        }

        public void Suspend()
        {

        }

        public Task<IResponse> HandleRequest(IContext context)
        {
            var response = new Success(ContentTypes.JSON, System.Text.Json.JsonSerializer.Serialize(obj), new ResponseFormatter {
                IsCamelCase = true,
                IsWrapped = true
            });
            return Task.FromResult<IResponse>(response);
        }
    }
}