using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Mzying2001.MonkeySharp.Demo.Views
{
    public partial class ScriptIcon : UserControl
    {
        public static readonly DependencyProperty IconUrlProperty = DependencyProperty.Register(
            nameof(IconUrl), typeof(string), typeof(ScriptIcon), new PropertyMetadata(null, OnIconChanged));

        public static readonly DependencyProperty FallbackTextProperty = DependencyProperty.Register(
            nameof(FallbackText), typeof(string), typeof(ScriptIcon), new PropertyMetadata("?", OnFallbackChanged));

        public ScriptIcon()
        {
            InitializeComponent();
            Loaded += (sender, args) => ApplyIcon();
        }

        public string IconUrl
        {
            get => (string)GetValue(IconUrlProperty);
            set => SetValue(IconUrlProperty, value);
        }

        public string FallbackText
        {
            get => (string)GetValue(FallbackTextProperty);
            set => SetValue(FallbackTextProperty, value);
        }

        private static void OnIconChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            ((ScriptIcon)sender).ApplyIcon();
        }

        private static void OnFallbackChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            var control = (ScriptIcon)sender;
            if (control.FallbackTextBlock != null) control.FallbackTextBlock.Text = control.FallbackText ?? "?";
        }

        private void ApplyIcon()
        {
            if (Icon == null) return;
            FallbackTextBlock.Text = FallbackText ?? "?";
            Icon.Source = null;
            Icon.Visibility = Visibility.Collapsed;
            Fallback.Visibility = Visibility.Visible;
            if (!Uri.TryCreate(IconUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;
            try
            {
                Icon.Source = new BitmapImage(uri);
                Icon.Visibility = Visibility.Visible;
                Fallback.Visibility = Visibility.Collapsed;
            }
            catch (Exception)
            {
                ShowFallback();
            }
        }

        private void IconFailed(object sender, ExceptionRoutedEventArgs args) => ShowFallback();

        private void ShowFallback()
        {
            Icon.Source = null;
            Icon.Visibility = Visibility.Collapsed;
            Fallback.Visibility = Visibility.Visible;
        }
    }
}
