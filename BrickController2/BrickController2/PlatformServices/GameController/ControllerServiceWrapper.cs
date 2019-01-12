using System;
using System.Collections.Generic;
using System.Text;

namespace BrickController2.PlatformServices.GameController
{

    /// <summary>
    /// Wrap multiple game controllers and join events from them as one game controller.
    /// </summary>
    public class ControllerServiceWrapper : IControllerService
    {
        private IControllerService[] _gameControllers;

        public ControllerServiceWrapper(IGameControllerService _gameControllerService, IHttpControllerService _httpControllerService)
        {
            _gameControllers = new IControllerService[] { _gameControllerService , _httpControllerService};
        }

        public event EventHandler<GameControllerEventArgs> GameControllerEvent
        {
            add
            {
                foreach (var c in _gameControllers)
                {
                    c.GameControllerEvent += value;
                }
            }

            remove
            {
                foreach (var c in _gameControllers)
                {
                    c.GameControllerEvent -= value;
                }
            }
        }

    }
}
