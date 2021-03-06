namespace EventTraceKit.VsExtension.Views.PresetManager
{
    using System.Collections.ObjectModel;
    using System.Windows;
    using Windows;

    public class HeaderDropDownMenu : DependencyObject
    {
        public static readonly DependencyProperty IsOpenProperty =
            DependencyProperty.Register(
                nameof(IsOpen),
                typeof(bool),
                typeof(HeaderDropDownMenu),
                new PropertyMetadata(Boxed.False));

        private static readonly DependencyPropertyKey ItemsPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(Items),
                typeof(ObservableCollection<HeaderCommand>),
                typeof(HeaderDropDownMenu),
                PropertyMetadataUtils.DefaultNull);

        public static readonly DependencyProperty ItemsProperty = ItemsPropertyKey.DependencyProperty;

        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register(
                nameof(Header),
                typeof(object),
                typeof(HeaderDropDownMenu),
                new PropertyMetadata(
                    null, (d, e) => ((HeaderDropDownMenu)d).HeaderPropertyChanged(e)));

        public HeaderDropDownMenu()
        {
            Items = new ObservableCollection<HeaderCommand>();
        }

        private void HeaderPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            //base.CoerceValue(AutomationNameProperty);
        }

        public bool IsOpen
        {
            get => (bool)GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, Boxed.Bool(value));
        }

        public object Header
        {
            get => GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public ObservableCollection<HeaderCommand> Items
        {
            get => (ObservableCollection<HeaderCommand>)GetValue(ItemsProperty);
            private set => SetValue(ItemsPropertyKey, value);
        }
    }
}
