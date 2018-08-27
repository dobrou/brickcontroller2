﻿using Autofac;
using BrickController2.UI.Services;

namespace BrickController2.iOS.UI.Services.DI
{
    public class UIServicesModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<DialogService>().As<IDialogService>().SingleInstance();
        }
    }
}