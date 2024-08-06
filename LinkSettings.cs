using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using System.ComponentModel;
using System.Linq;


namespace Flow.Launcher.Plugin.LinkOpener
{
    public partial class LinkSettings : UserControl
    {
        public PluginInitContext Context { get; private set; }

        string SettingsPath => Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(Context.CurrentPluginMetadata.PluginDirectory)),
            "Settings",
            "Plugins",
            "Flow.Launcher.Plugin.LinkOpener",
            "Settings.json"
        );

        public ObservableCollection<SettingItem> SettingsItems { get; set; }

        public ICommand SelectIconCommand { get; private set; }

        public LinkSettings(ObservableCollection<SettingItem> settingsItems, PluginInitContext context)
        {
            InitializeComponent();
            Context = context;
            SettingsItems = settingsItems;

            SettingsItems.ToList().ForEach(x => x.PropertyChanged += (s, e) => SaveSettings());
            SettingsItems.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (SettingItem newItem in e.NewItems)
                    {
                        newItem.PropertyChanged += Item_PropertyChanged;
                    }
                }
                if (e.OldItems != null)
                {
                    foreach (SettingItem oldItem in e.OldItems)
                    {
                        oldItem.PropertyChanged -= Item_PropertyChanged;
                    }
                }
            };

            SelectIconCommand = new RelayCommand(OnSelectIcon);
            this.DataContext = this;
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e) => SaveSettings();

        private void SaveSettings()
        {
            var nonNullSettingsItems = SettingsItems
                .Where(item => !string.IsNullOrWhiteSpace(item.Keyword) ||
                               !string.IsNullOrWhiteSpace(item.Title) ||
                               !string.IsNullOrWhiteSpace(item.Url) ||
                               !string.IsNullOrWhiteSpace(item.IconPath))
                .ToList();

            string jsonData = JsonSerializer.Serialize(nonNullSettingsItems, new JsonSerializerOptions { WriteIndented = true });
            Dispatcher.InvokeAsync(() =>
            {
                File.WriteAllText(SettingsPath, jsonData);
            });
        }

        private void OnSelectIcon(object parameter)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files|*.png;*.jpg|All Files|*.*",
                Title = "Select an Icon"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var selectedItem = parameter as SettingItem;
                if (selectedItem != null)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        selectedItem.IconPath = openFileDialog.FileName;

                        int index = SettingsItems.IndexOf(selectedItem);
                        if (index >= 0)
                        {
                            SettingsItems[index] = selectedItem;
                        }
                    });
                }
            }
        }

        public class RelayCommand : ICommand
        {
            private readonly Action<object> _execute;
            private readonly Func<object, bool> _canExecute;

            public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;

            public void Execute(object parameter) => _execute(parameter);

            public event EventHandler CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }
        }
    }
}
