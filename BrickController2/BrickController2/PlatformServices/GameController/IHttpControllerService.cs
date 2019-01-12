using System.Threading;
using System.Threading.Tasks;

namespace BrickController2.PlatformServices.GameController
{
    public interface IHttpControllerService : IControllerService
    {
        void Start();
        void Stop();
    }
}
