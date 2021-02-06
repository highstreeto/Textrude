﻿using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace TextrudeInteractive
{
    /// <summary>
    ///     Interaction logic for FileBar.xaml
    /// </summary>
    public partial class FileBar : UserControl
    {
        private string _pathName = string.Empty;

        public Action<string, string> OnLoad = (_, _) => { };

        public Func<string> OnSave = () => string.Empty;

        public FileBar()
        {
            InitializeComponent();
        }

        /// <summary>
        ///     Path to file that the model is connected to
        /// </summary>
        public string PathName
        {
            get => _pathName;
            set
            {
                _pathName = value;
                FilePath.Content = _pathName;
            }
        }

        public void SetSaveOnly(bool onoff)
        {
            LoadButton.Visibility = onoff ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UnlinkFile(object sender, RoutedEventArgs e)
        {
            OnLoad(string.Empty, OnSave());
        }


        private void LoadFromFile()
        {
            var dlg = new OpenFileDialog();
            dlg.Filter =
                "csv files (*.csv)|*.csv|" +
                "yaml files (*.yaml)|*.yaml|" +
                "json files (*.json)|*.json|" +
                "txt files (*.txt)|*.txt|" +
                "All files (*.*)|*.*";
            dlg.FileName = PathName;
            if (dlg.ShowDialog() != true) return;
            //only change the format if loading from file the first time since
            //the user might override subsequently
            if (FileManager.TryLoadFile(dlg.FileName, out var text))

                OnLoad(dlg.FileName, text);
        }


        private void SaveToFile()
        {
            var dlg = new SaveFileDialog {FileName = PathName};
            if (dlg.ShowDialog() != true) return;
            if (FileManager.TrySave(dlg.FileName, OnSave()))
                OnLoad(dlg.FileName, OnSave());
        }

        private void FileBar_OnLoaded(object sender, RoutedEventArgs e)
        {
        }

        private void Load(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PathName))
                LoadFromFile();
            else if (FileManager.TryLoadFile(PathName, out var text))
            {
                OnLoad(PathName, text);
            }
        }

        private void Save(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PathName))
                SaveToFile();
            else

                FileManager.TrySave(PathName, OnSave());
        }

        public void SaveIfLinked() => FileManager.TrySave(PathName, OnSave());

        public void LoadIfLinked() => FileManager.TryLoadFile(PathName, out var t);
    }
}
