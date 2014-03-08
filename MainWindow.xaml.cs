using Microsoft.Practices.Prism;
using Microsoft.Practices.Prism.Commands;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AdaptiveKeyboardConfig
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public MainWindow()
        {
            Apps = new ObservableCollection<AppEntry>();

            InitializeComponent();
            this.DataContext = this;

            AddApp = new DelegateCommand<string>(showAvailableAppsContextMenu);
            RemoveApp = new DelegateCommand<string>(removeApps, p => appList.SelectedIndex >= 0);

            Loaded += (sender, args) =>
            {
                Apps.AddRange(AppEntry.LoadAppsFromRegistry(this, 32));
            };

            appList.SelectionChanged += (s, e) => RemoveApp.RaiseCanExecuteChanged();
        }

        async void removeApps(string p)
        {
            // FIXME: Dirty code to realize removal animation

            var idx = appList.SelectedIndex;
            var appsToRemove = appList.SelectedItems.Cast<AppEntry>().ToArray();
            foreach (var item in appsToRemove)
                item.MarkForDeletion();

            await Task.Delay(200);

            foreach (var item in appsToRemove)
            {
                item.RemoveRegistryEntry();
                Apps.Remove(item);
            }

            if (idx >= appList.Items.Count)
                idx = appList.Items.Count - 1;
            if (idx >= 0)
            {
                appList.SelectedIndex = idx;
                var item = (ListBoxItem)(appList.ItemContainerGenerator.ContainerFromItem(appList.SelectedItem));
                if (item != null)
                    item.Focus();
            }
        }

        void showAvailableAppsContextMenu(string p)
        {
            addAppContextMenu.Items.Clear();

            foreach (var app in AppEntry.EnumAppsExcluding(Apps.Concat(new[] {AppEntry.FromVisual(this)})))
            {
                app.LoadIconFromModule(this, 32);
                addAppContextMenu.Items.Add(app);
            }

            addAppContextMenu.IsOpen = true;
        }

        void addAppMenuItemClick(object sender, EventArgs e)
        {
            var app = ((MenuItem)sender).DataContext as AppEntry;
            if (Apps.FirstOrDefault(a => a.Path == app.Path) != null)
                return;
            Apps.Insert(0, app);
            appList.ScrollIntoView(app);
            app.UpdateRegistryEntry();
        }

        private void modeSwitchButtonClick(object sender, RoutedEventArgs e)
        {
            var app = ((Button)sender).DataContext as AppEntry;
            if (app == null)
                return;

            app.Mode = (Mode)(((int)app.Mode + 1) % Enum.GetNames(typeof(Mode)).Length);
        }

        public ObservableCollection<AppEntry> Apps { get; set; }
        public DelegateCommand<string> AddApp { get; set; }
        public DelegateCommand<string> RemoveApp { get; set; }
    }
}
