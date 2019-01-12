using System;

namespace BrickController2.PlatformServices.GameController
{
    public interface IControllerService
    {
        event EventHandler<GameControllerEventArgs> GameControllerEvent;
    }
}