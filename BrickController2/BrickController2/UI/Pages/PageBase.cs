using BrickController2.UI.Services.Background;
using BrickController2.UI.ViewModels;
using System;
using Xamarin.Forms;

namespace BrickController2.UI.Pages
{
    public abstract class PageBase : ContentPage
    {
        private readonly IBackgroundService _backgroundService;

        private bool _appeared;

        public PageBase(IBackgroundService backgroundService)
        {
            _backgroundService = backgroundService;
        }

        protected override void OnBindingContextChanged()
        {
            base.OnBindingContextChanged();
        }

        protected override void OnAppearing()
        {
            OnAppearingInternal();

            base.OnAppearing();
        }

        protected override void OnDisappearing()
        {
            OnDisappearingInternal();

            base.OnDisappearing();
        }

        protected override bool OnBackButtonPressed()
        {
            var result = ((BindingContext as PageViewModelBase)?.OnBackButtonPressed()) ?? true;
            return result && base.OnBackButtonPressed();
        }

        private void OnAppearingInternal()
        {
            if (!_appeared)
            {
                (BindingContext as PageViewModelBase)?.OnAppearing();
            }

            _appeared = true;
        }

        private void OnDisappearingInternal()
        {
            if (_appeared)
            {
                (BindingContext as PageViewModelBase)?.OnDisappearing();
            }

            _appeared = false;
        }
    }
}
