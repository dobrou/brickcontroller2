using System;
using System.Linq;
using BrickController2.Helpers;
using BrickController2.UI.Converters;
using System.Windows.Input;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Device = BrickController2.DeviceManagement.Device;

namespace BrickController2.UI.Controls
{
	[XamlCompilation(XamlCompilationOptions.Compile)]
	public partial class DeviceOutputTesterControl : ContentView
	{
		public DeviceOutputTesterControl ()
		{
			InitializeComponent ();
		}

        public static BindableProperty DeviceProperty = BindableProperty.Create(nameof(Device), typeof(Device), typeof(DeviceOutputTesterControl), propertyChanged: OnDeviceChanged);

        public Device Device
        {
            get => (Device)GetValue(DeviceProperty);
            set => SetValue(DeviceProperty, value);
        }

        private static void OnDeviceChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is DeviceOutputTesterControl dotc && newValue is Device device)
            {
                dotc.Setup(device);
            }
        }

        private void Setup(Device device)
        {
            StackLayout.Children.Clear();

            var channelColors = new[]
            {
                Color.Blue,
                Color.Red,
                Color.Green,
                Color.Orange,
                Color.Yellow,
                Color.LightGray,
            };

            for (int channel = 0; channel < device.NumberOfChannels; channel++)
            {
                var deviceOutputViewModel = new DeviceOutputViewModel(device, channel);
                var channelColor = channelColors[Math.Min(channel, channelColors.Length - 1)];

                var slider = new ExtendedSlider
                {
                    BindingContext = deviceOutputViewModel,
                    ScaleY = 2,                    
                    HeightRequest = 50,
                    MinimumTrackColor = channelColor,
                    MaximumTrackColor = channelColor,
                };

                slider.SetBinding<DeviceOutputViewModel>(ExtendedSlider.ValueProperty, vm => vm.Output, BindingMode.TwoWay);
                slider.SetBinding<DeviceOutputViewModel>(ExtendedSlider.TouchUpCommandProperty, vm => vm.TouchUpCommand);
                slider.SetBinding<DeviceOutputViewModel>(ExtendedSlider.IsEnabledProperty, vm => vm.Device.DeviceState, BindingMode.Default, new DeviceConnectedToBoolConverter());
                slider.SetBinding<DeviceOutputViewModel>(ExtendedSlider.MinimumProperty, vm => vm.MinValue);
                slider.SetBinding<DeviceOutputViewModel>(ExtendedSlider.MaximumProperty, vm => vm.MaxValue);

                StackLayout.Children.Add(slider);

                var invertSwitch = new Switch()
                {
                    BindingContext = deviceOutputViewModel,
                    BackgroundColor = channelColor,
                };
                invertSwitch.SetBinding(Switch.IsToggledProperty, nameof(DeviceOutputViewModel.IsInverted), BindingMode.TwoWay);
                var invertLabel = new Label { Text = "Invert" };

                var resetTouchUpSwitch = new Switch()
                {
                    BindingContext = deviceOutputViewModel,
                    BackgroundColor = channelColor,
                };
                resetTouchUpSwitch.SetBinding(Switch.IsToggledProperty, nameof(DeviceOutputViewModel.IsResetOnTouchUp), BindingMode.TwoWay);

                StackLayout.Children.Add(resetTouchUpSwitch);
                var resetLabel = new Label { Text = "Auto reset" };

                StackLayout.Children.Add(new StackLayout()
                {
                    Children =
                    {
                        invertSwitch,
                        invertLabel,
                        resetTouchUpSwitch,
                        resetLabel,
                    },
                    Orientation = StackOrientation.Horizontal,
                    HorizontalOptions = LayoutOptions.FillAndExpand,
                });
            }
        }

        private class DeviceOutputViewModel : NotifyPropertyChangedSource
        {
            private int _output;
            private bool _isInverted;
            private bool _isResetOnTouchUp;

            public DeviceOutputViewModel(Device device, int channel)
            {
                Device = device;
                Channel = channel;
                Output = 0;
                IsInverted = false;
                IsResetOnTouchUp = true;

                TouchUpCommand = new Command(ResetOutput);
            }

            public Device Device { get; }
            public int Channel { get; }

            public int MinValue => -MaxValue;
            public int MaxValue => 100;

            public bool IsInverted
            {
                get => _isInverted;
                set
                {
                    _isInverted = value;                    
                    UpdateDevice();
                }

            }

            public bool IsResetOnTouchUp
            {
                get => _isResetOnTouchUp;
                set
                {
                    _isResetOnTouchUp = value;
                    ResetOutput();
                }
            }

            public int Output
            {
                get { return _output; }
                set
                {
                    _output = value;
                    UpdateDevice();
                    RaisePropertyChanged();
                }
            }

            private void ResetOutput()
            {
                if (IsResetOnTouchUp)
                    Output = 0;
            }

            private void UpdateDevice()
            {
                Device.SetOutput(Channel, (IsInverted ? -1 : 1) * (float)Output / MaxValue);
            }

            public ICommand TouchUpCommand { get; }
        }
    }
}