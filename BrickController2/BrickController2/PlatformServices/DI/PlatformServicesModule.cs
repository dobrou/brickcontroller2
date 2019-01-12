using System;
using System.Collections.Generic;
using System.Text;
using Autofac;
using BrickController2.PlatformServices.BluetoothLE;
using BrickController2.PlatformServices.GameController;
using BrickController2.PlatformServices.Infrared;
using BrickController2.PlatformServices.Localization;
using BrickController2.PlatformServices.Versioning;

namespace BrickController2.PlatformServices.DI
{
    public class PlatformServicesModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<ControllerServiceWrapper>().As<IControllerService>().SingleInstance();
            builder.RegisterType<HttpControllerService>().As<IHttpControllerService>().SingleInstance();
        }
    }
}
