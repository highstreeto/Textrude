﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Engine.Application;
using MaterialDesignExtensions.Controls;
using TextrudeInteractive.Annotations;
using TextrudeInteractive.AutoCompletion;

namespace TextrudeInteractive
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MaterialWindow, INotifyPropertyChanged
    {
        private const string HomePage = @"https://github.com/NeilMacMullen/Textrude";

        private readonly ISubject<EngineInputSet> _inputStream =
            new BehaviorSubject<EngineInputSet>(EngineInputSet.EmptyYaml);

        private readonly AvalonEditCompletionHelper _mainEditWindow;

        private readonly TabControlManager<InputMonacoPane> _modelManager;
        private readonly TabControlManager<OutputMonacoPane> _outputManager;
        private readonly ProjectManager _projectManager;
        private readonly bool _uiIsReady;

        private UpgradeManager.VersionInfo _latestVersion = UpgradeManager.VersionInfo.Default;

        private bool _lineNumbersOn = true;
        private int _responseTimeMs = 50;

        private double _textSize = 14;

        private bool _wordWrapOn;

        public MainWindow()
        {
            InitializeComponent();

            if (!MonacoBinding.IsWebView2RuntimeAvailable())
            {
                MessageBox.Show(
                    "The WebView2 runtime or Edge (non-stable channel) must be installed for the editor to work!\n" +
                    "Please install one of the two.\n" +
                    "Textrude will now exit.",
                    "Textrude: WebView2 runtime must be installed!",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                Application.Current.Shutdown();
            }

            templateFileBar.OnSave = () => TemplateTextBox.Text;
            templateFileBar.OnLoad = (text, _) => TemplateTextBox.Text = text;
            ;
            SetTitle(string.Empty);
            _modelManager = new("model", InputModels, p => p.OnUserInput = OnModelChanged);
            _outputManager = new("output", OutputTab, _ => { });

            _mainEditWindow = new AvalonEditCompletionHelper(TemplateTextBox);

            _projectManager = new ProjectManager(this);


            SetUi(EngineInputSet.EmptyYaml, false);
            SetOutputPanes(EngineOutputSet.Empty, false);

            //do this before setting up the input stream so we can change the responsiveness
            var settings = LoadSettings();


            //check to see if the application was invoked with arguments
            //if so open that project, otherwise open the last used
            var args = Environment.GetCommandLineArgs();
            if (args.Length == 2 && ProjectManager.IsProject(args[1]))
            {
                _projectManager.LoadProject(args[1]);
            }
            else if (settings.RecentProjects.Any())
            {
                var mostRecentProject = settings.RecentProjects.OrderByDescending(p => p.LastLoaded).First();
                _projectManager.LoadProject(mostRecentProject.Path);
            }

            _inputStream
                .Throttle(TimeSpan.FromMilliseconds(_responseTimeMs))
                .ObserveOn(NewThreadScheduler.Default)
                .Select(Render)
                .ObserveOn(SynchronizationContext.Current)
                .Subscribe(HandleRenderResults);
            _uiIsReady = true;

            RunBackgroundUpgradeCheck();

            DataContext = this;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RunBackgroundUpgradeCheck()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    _latestVersion = await UpgradeManager.GetLatestVersion();
                    await Task.Delay(TimeSpan.FromHours(24));
                }
            });
        }


        private void MainWindow_OnClosing(object sender, CancelEventArgs e)
        {
            if (ShouldChangesBeLost())
            {
                PersistSettings();
                _inputStream.OnCompleted();
            }
            else e.Cancel = true;
        }


        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            _mainEditWindow.Register();
            //ensure that we get to render the first project loaded at startup
            OnModelChanged(false);
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        #region jumplist

        #endregion

        #region outputs menu

        private void AddOutput(object sender, RoutedEventArgs e) => _outputManager.AddPane();

        private void RemoveOutput(object sender, RoutedEventArgs e) => _outputManager.RemoveLast();


        private void SaveAllOutputs(object sender, RoutedEventArgs e)
        {
            _outputManager.ForAll(p => p.SaveIfLinked());
        }

        #endregion

        #region inputs menu

        private void AddModel(object sender, RoutedEventArgs e) =>
            _modelManager.AddPane();

        private void RemoveModel(object sender, RoutedEventArgs e) => _modelManager.RemoveLast();

        private void ReloadAllInputs(object sender, RoutedEventArgs e)
        {
            _modelManager.ForAll(p => p.LoadIfLinked());
            templateFileBar.LoadIfLinked();
        }

        private void SaveAllInputs(object sender, RoutedEventArgs e)
        {
            _modelManager.ForAll(p => p.SaveIfLinked());
            templateFileBar.SaveIfLinked();
        }

        #endregion

        #region view menu

        public bool LineNumbersOn
        {
            get => _lineNumbersOn;
            set
            {
                _lineNumbersOn = value;
                OnPropertyChanged();
            }
        }

        public double TextSize
        {
            get => _textSize;
            set
            {
                _textSize = value;
                OnPropertyChanged();
            }
        }

        public bool WordWrapOn
        {
            get => _wordWrapOn;
            set
            {
                _wordWrapOn = value;
                OnPropertyChanged();
            }
        }

        private void SmallerFont(object sender, RoutedEventArgs e) => TextSize = Math.Max(TextSize - 2, 10);

        private void LargerFont(object sender, RoutedEventArgs e) => TextSize = Math.Min(TextSize + 2, 36);

        private void ToggleLineNumbers(object sender, RoutedEventArgs e) => LineNumbersOn = !LineNumbersOn;

        private void ToggleWordWrap(object sender, RoutedEventArgs e) => WordWrapOn = !WordWrapOn;

        #endregion

        #region project menu and support

        /// <summary>
        ///     Sets up the output side of the UI when a new project is loaded
        /// </summary>
        public void SetOutputPanes(EngineOutputSet outputControl, bool trim)
        {
            _outputManager.Clear();
            var outputs = outputControl.Outputs;
            if (trim)
                outputs = outputs.Take(1).ToArray();
            foreach (var f in outputs)
            {
                var pane = _outputManager.AddPane();
                pane.Format = f.Format;
                pane.OutputPath = f.Path;
                pane.OutputName = f.Name;
            }

            //ensure there is always at least one output - otherwise things can get confusing for the user
            if (!_outputManager.Panes.Any())
                _outputManager.AddPane();

            _outputManager.FocusFirst();
        }

        public void SetTitle(string path)
        {
#if HASGITVERSION

            var file = Path.GetFileNameWithoutExtension(path);
            var title =
                $"Textrude Interactive {GitVersionInformation.SemVer} : {file}";
            Title = title;
#endif
        }

        public EngineOutputSet CollectOutput()
        {
            return new(
                _outputManager.Panes.Select(b => new OutputPaneModel(b.Format, b.Name, b.OutputPath))
            );
        }

        private bool ShouldChangesBeLost()
        {
            if (_projectManager.IsDirty)
            {
                if (MessageBox.Show(this,
                    "You have unsaved changes in the current project.\nDo you really want to lose them?", "Warning",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return false;
                }
            }

            return true;
        }

        private void NewProject(object sender, RoutedEventArgs e)
        {
            if (ShouldChangesBeLost())
                _projectManager.NewProject();
        }

        private void LoadProject(object sender, RoutedEventArgs e)
        {
            if (ShouldChangesBeLost())
                _projectManager.LoadProject();
        }


        private void SaveProject(object sender, RoutedEventArgs e) => _projectManager.SaveProject();


        private void SaveProjectAs(object sender, RoutedEventArgs e) => _projectManager.SaveProjectAs();

        /// <summary>
        ///     Sets up the input side of the UI when a new project is loaded
        /// </summary>
        public void SetUi(EngineInputSet inputSet, bool trim)
        {
            DefinitionsTextBox.Text = string.Join(Environment.NewLine, inputSet.Definitions);
            _modelManager.Clear();
            foreach (var model in inputSet.Models)
            {
                if (trim && string.IsNullOrWhiteSpace(model.Text))
                    continue;
                var pane = _modelManager.AddPane();
                pane.Format = model.Format;
                pane.Text = model.Text;
                pane.ModelName = model.Name;
                pane.ModelPath = model.Path;
            }

            //ensure we start with at least one model to avoid confusing the user
            if (!_modelManager.Panes.Any())
                _modelManager.AddPane();

            _modelManager.FocusFirst();
            TemplateTextBox.Text = inputSet.Template;
            templateFileBar.PathName = inputSet.TemplatePath;

            IncludesTextBox.Text = string.Join(Environment.NewLine, inputSet.IncludePaths);
        }


        private void ExportInvocation(object sender, RoutedEventArgs e) => _projectManager.ExportProject();

        #endregion

        #region main loop

        private void Avalon1_OnTextChanged(object sender, EventArgs e) => OnModelChanged();


        private void OnModelChanged() => OnModelChanged(true);

        private void OnModelChanged(bool markDirty)
        {
            if (!_uiIsReady)
                return;
            if (markDirty)
                _projectManager.IsDirty = true;
            try
            {
                _inputStream.OnNext(CollectInput());
            }
            catch (Exception exception)
            {
                Errors.Text = exception.Message;
            }
        }

        private TimedOperation<ApplicationEngine> Render(EngineInputSet gi)
        {
            var rte = new RunTimeEnvironment(new FileSystemOperations());
            var engine = new ApplicationEngine(rte);
            var timer = new TimedOperation<ApplicationEngine>(engine);

            foreach (var m in gi.Models)
                engine = engine.WithModel(m.Text, m.Format);
            engine = engine
                .WithEnvironmentVariables()
                .WithDefinitions(gi.Definitions)
                .WithIncludePaths(gi.IncludePaths)
                .WithHelpers()
                .WithTemplate(gi.Template);

            engine.Render();
            return timer;
        }

        private void HandleRenderResults(TimedOperation<ApplicationEngine> timedEngine)
        {
            var elapsedMs = (int) timedEngine.Timer.ElapsedMilliseconds;
            var engine = timedEngine.Value;
            var outputPanes = _outputManager.Panes.ToArray();
            var outputs = engine.GetOutput(outputPanes.Length);
            for (var i = 0; i < Math.Min(outputs.Length, outputPanes.Length); i++)
            {
                outputPanes[i].Text = outputs[i];
            }

            Errors.Text = string.Empty;
#if HASGITVERSION
            if (_latestVersion.Supersedes(GitVersionInformation.SemVer))
            {
                Errors.Text =
                    $"Upgrade to {_latestVersion.Version} available - please visit {UpgradeManager.ReleaseSite}" +
                    Environment.NewLine;
            }
#endif
            Errors.Text += $"Completed: {DateTime.Now.ToLongTimeString()}  Render time: {elapsedMs}ms" +
                           Environment.NewLine;
            if (engine.HasErrors)
            {
                Errors.Foreground = Brushes.OrangeRed;
                Errors.Text += string.Join(Environment.NewLine, engine.Errors);
            }
            else
            {
                Errors.Foreground = Brushes.GreenYellow;
                Errors.Text += "No errors";
            }

            _mainEditWindow.SetCompletion(engine.ModelPaths());
        }


        public EngineInputSet CollectInput()
        {
            var models = _modelManager.Panes
                .Select(m => new ModelText(m.Text, m.Format, m.ModelName, m.ModelPath))
                .ToArray();

            return new EngineInputSet(TemplateTextBox.Text,
                templateFileBar.PathName,
                models,
                DefinitionsTextBox.Text,
                IncludesTextBox.Text);
        }

        #endregion

        #region settings

        /// <summary>
        ///     Loads any persisted settings and applies them
        /// </summary>
        private ApplicationSettings LoadSettings()
        {
            var settings = SettingsManager.ReadSettings();
            LineNumbersOn = settings.LineNumbersOn;
            TextSize = settings.FontSize;
            WordWrapOn = settings.WrapText;
            _responseTimeMs = settings.ResponseTime;
            return settings;
        }

        /// <summary>
        ///     Persist settings
        /// </summary>
        private void PersistSettings()
        {
            //persist settings
            var settings = new ApplicationSettings
            {
                FontSize = TextSize,
                LineNumbersOn = LineNumbersOn,
                WrapText = WordWrapOn,
                ResponseTime = _responseTimeMs,
                //TODO - this is a temporary hack. ProjectManager should actually track the projects that have been used
                //and save them all so we can display them in the menu
                RecentProjects = new[]
                {
                    new RecentlyUsedProject
                    {
                        LastLoaded = DateTime.UtcNow,
                        Path = _projectManager.CurrentProjectPath
                    }
                }
            };
            SettingsManager.WriteSettings(settings);
        }

        #endregion

        #region HelpMenu

        private void OpenBrowserTo(Uri uri)
        {
            var ps = new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true,
                Verb = "open"
            };
            Process.Start(ps);
        }

        private void ShowLanguageRef(object sender, RoutedEventArgs e) =>
            OpenBrowserTo(new Uri("https://github.com/scriban/scriban/blob/master/doc/language.md"));

        private void OpenHome(string path) =>
            OpenBrowserTo(new Uri(HomePage + "/" + path));

        private void ShowAbout(object sender, RoutedEventArgs e) =>
            OpenHome(string.Empty);


        private void NewIssue(object sender, RoutedEventArgs e) =>
            OpenHome("issues/new?assignees=&labels=bug&template=bug_report.md&title=Bug");

        private void NewIdea(object sender, RoutedEventArgs e) =>
            OpenHome("issues/new?assignees=&labels=enhancement&template=feature_request.md&title=Suggestion");

        private void SendASmile(object sender, RoutedEventArgs e) =>
            OpenHome("issues/new?assignees=&labels=smile&template=positive-feedback.md&title=I%20like%20it%21");


        private void Questions(object sender, RoutedEventArgs e) =>
            OpenHome("issues/new?assignees=&labels=question&template=ask-a-question.md&title=Help");

        #endregion
    }
}
