using System.Threading.Tasks;
using Spider.ArcheType;

namespace Archer.Archetype {
    public interface IAuthentication {
        Task<(bool Result, string Message)> Authenticate(IContext context);
    }
}