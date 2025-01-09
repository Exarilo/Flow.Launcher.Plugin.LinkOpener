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
    public partial class LinkSettings : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public PluginInitContext Context { get; private set; }

        string SettingsPath => Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(Context.CurrentPluginMetadata.PluginDirectory)),
            "Settings",
            "Plugins",
            "Flow.Launcher.Plugin.LinkOpener",
            "Settings.json"
        );

        protected virtual void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool addToBulkOpenUrls;
        public bool AddToBulkOpenUrls
        {
            get => addToBulkOpenUrls;
            set
            {
                if (addToBulkOpenUrls != value)
                {
                    addToBulkOpenUrls = value;
                    OnPropertyChanged(nameof(AddToBulkOpenUrls));
                }
            }
        }
        public ObservableCollection<SettingItem> SettingsItems { get; set; }

        public ICommand SelectIconCommand { get; private set; }
        public ICommand RemoveIconCommand { get; private set; }

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
                        newItem.AddToBulkOpenUrls = AddToBulkOpenUrls;
                    }
                }
                if (e.OldItems != null)
                {
                    foreach (SettingItem oldItem in e.OldItems)
                    {
                        oldItem.PropertyChanged -= Item_PropertyChanged;
                    }
                }
                SaveSettings();
            };

            SelectIconCommand = new RelayCommand(OnSelectIcon);
            RemoveIconCommand = new RelayCommand(OnRemoveIcon);

            this.DataContext = this;
            AddToBulkOpenUrls = SettingsItems.Any(x => x.AddToBulkOpenUrls);
        }

        private void OnRemoveIcon(object parameter)
        {
            var selectedItem = parameter as SettingItem;
            if (selectedItem != null)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    selectedItem.IconPath = string.Empty;

                    int index = SettingsItems.IndexOf(selectedItem);
                    if (index >= 0)
                    {
                        SettingsItems[index] = selectedItem;
                    }
                });
            }
        }

        private void OnCheckBoxClicked(object sender, System.Windows.RoutedEventArgs e)
        {
            bool isChecked = (sender as CheckBox)?.IsChecked ?? false;

            foreach (var item in SettingsItems)
            {
                item.AddToBulkOpenUrls = isChecked;
            }

            SaveSettings();
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e) => SaveSettings();

        private void SaveSettings()
        {
            var filteredSettingsItems = SettingsItems
                .Where(item => !string.IsNullOrWhiteSpace(item.Keyword) ||
                               !string.IsNullOrWhiteSpace(item.Title) ||
                               !string.IsNullOrWhiteSpace(item.Url) ||
                               !string.IsNullOrWhiteSpace(item.IconPath))
                .ToList();

            string jsonData = JsonSerializer.Serialize(filteredSettingsItems, new JsonSerializerOptions { WriteIndented = true });
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