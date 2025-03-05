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
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.LinkOpener
{
    public partial class LinkSettings : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public PluginInitContext Context { get; private set; }
        private string defaultDelimiter = "-";
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
                    foreach (var item in SettingsItems)
                    {
                        item.AddToBulkOpenUrls = value;
                    }
                    
                    OnPropertyChanged(nameof(AddToBulkOpenUrls));
                    SaveSettings();
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
            SettingsItems = settingsItems ?? new ObservableCollection<SettingItem>();

            foreach (var item in SettingsItems)
            {
                item.PropertyChanged += Item_PropertyChanged;
            }
            if (SettingsItems.Any())
            {
                var delimiterGroups = SettingsItems
                    .GroupBy(item => item.Delimiter)
                    .OrderByDescending(g => g.Count());
                
                if (delimiterGroups.Any())
                {
                    defaultDelimiter = delimiterGroups.First().Key;
                }
            }
            
            SettingsItems.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (SettingItem newItem in e.NewItems)
                    {
                        newItem.PropertyChanged += Item_PropertyChanged;
                        newItem.Delimiter = defaultDelimiter;
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
            Loaded += (s, e) => InitializeAdvancedControls();
        }
        private void InitializeAdvancedControls()
        {
            try
            {
                if (DefaultDelimiterTextBox != null)
                {
                    DefaultDelimiterTextBox.Text = defaultDelimiter;
                }
            }
            catch {}
        }
        private void OnDefaultDelimiterChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (sender is TextBox textBox)
                {
                    string newDelimiter = textBox.Text;
                    
                    if (string.IsNullOrEmpty(newDelimiter))
                    {
                        newDelimiter = " ";
                    }
                    
                    defaultDelimiter = newDelimiter;
                    
                    foreach (var item in SettingsItems)
                    {
                        item.Delimiter = defaultDelimiter;
                    }
                    
                    SaveSettings();
                }
            }
            catch {}
        }

        private void OnRemoveIcon(object parameter)
        {
            var selectedItem = parameter as SettingItem;
            if (selectedItem != null)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        selectedItem.IconPath = string.Empty;

                        int index = SettingsItems.IndexOf(selectedItem);
                        if (index >= 0)
                        {
                            SettingsItems[index] = selectedItem;
                        }
                    }
                    catch {}
                });
            }
        }

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e) => SaveSettings();

        private void SaveSettings()
        {
            const int maxRetries = 3;
            const int delayMs = 100;
            
            try
            {
                string settingsFolder = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(settingsFolder))
                {
                    Directory.CreateDirectory(settingsFolder);
                }
                
                var filteredSettingsItems = SettingsItems
                    .Where(item => !string.IsNullOrWhiteSpace(item.Keyword) ||
                                   !string.IsNullOrWhiteSpace(item.Title) ||
                                   !string.IsNullOrWhiteSpace(item.Url) ||
                                   !string.IsNullOrWhiteSpace(item.IconPath))
                    .ToList();

                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonData = JsonSerializer.Serialize(filteredSettingsItems, options);
                
                Dispatcher.InvokeAsync(async () =>
                {
                    for (int attempt = 0; attempt < maxRetries; attempt++)
                    {
                        try
                        {
                            string tempPath = Path.GetTempFileName();
                            await File.WriteAllTextAsync(tempPath, jsonData);
                            
                            if (File.Exists(SettingsPath))
                            {
                                File.Delete(SettingsPath);
                            }
                            File.Move(tempPath, SettingsPath);
                            return;
                        }
                        catch (IOException) when (attempt < maxRetries - 1)
                        {
                            await Task.Delay(delayMs * (attempt + 1));
                        }
                        catch {}
                    }
                });
            }
            catch {}
        }
        private void OnSelectIcon(object parameter)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.ico|All Files|*.*",
                Title = "Select an Icon"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var selectedItem = parameter as SettingItem;
                if (selectedItem != null)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            selectedItem.IconPath = openFileDialog.FileName;

                            int index = SettingsItems.IndexOf(selectedItem);
                            if (index >= 0)
                            {
                                SettingsItems[index] = selectedItem;
                            }
                        }
                        catch {}
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