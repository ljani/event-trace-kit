﻿namespace EventTraceKit.VsExtension.Windows
{
    using System;
    using System.Windows;
    using System.Windows.Controls.Primitives;
    using System.Windows.Interop;
    using System.Windows.Markup;
    using EventTraceKit.VsExtension.Native;
    using Microsoft.VisualStudio.PlatformUI;

    public sealed class MenuPopupPositionerExtension : MarkupExtension
    {
        private const double BorderThickness = 1.0;

        public string ElementName { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var service = (IProvideValueTarget)serviceProvider.GetService(
                typeof(IProvideValueTarget));

            var border = service.TargetObject as FrameworkElement;
            if (border == null)
                return this;

            Popup popup = border.FindAncestor<Popup>();
            if (popup != null)
                popup.Opened += (sender, args) => OnOpened(border, popup);

            return new Thickness(BorderThickness);
        }

        private void OnOpened(FrameworkElement border, Popup popup)
        {
            var element = border.FindName(ElementName) as FrameworkElement;
            if (element == null)
                throw new InvalidOperationException(
                    $"Element with name \"{ElementName}\" not found.");

            var hwndSource = (HwndSource)PresentationSource.FromVisual(popup.Child);
            if (hwndSource == null)
                return;

            RECT bounds;
            NativeMethods.GetWindowRect(hwndSource.Handle, out bounds);

            Point point = element.PointToScreen(new Point());
            if (popup.Placement == PlacementMode.Left || popup.Placement == PlacementMode.Right) {
                border.Visibility = Visibility.Collapsed;
                if (popup.Placement == PlacementMode.Left && bounds.Left > point.X)
                    popup.HorizontalOffset = 2.0;
                else if (popup.Placement == PlacementMode.Right && bounds.Left < point.X)
                    popup.HorizontalOffset = -2.0;
            } else if (point.Y > bounds.Top) {
                border.Visibility = Visibility.Hidden;
            } else {
                double left = (point.X - bounds.Left) * DpiHelper.DeviceToLogicalUnitsScalingFactorX;
                double width = (bounds.Left + bounds.Width - point.X) * DpiHelper.DeviceToLogicalUnitsScalingFactorX;
                border.Margin = new Thickness(left + 1, 0, 0, 0);
                border.Width = width.Clamp(0, element.ActualWidth - 2);
                border.Visibility = Visibility.Visible;
            }
        }
    }
}