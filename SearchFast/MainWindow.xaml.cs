using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SearchFast
{
    public partial class MainWindow : Window
    {
        string dirName = null;
        string searchText = null;
        string fileNames = null;
        bool? matchCase = false;
        ObservableCollection<FileInfo> fileList = new ObservableCollection<FileInfo>();
        ObservableCollection<FileSearchResult> fileDetailList = new ObservableCollection<FileSearchResult>();
        ManualResetEventSlim pauseEvent = new ManualResetEventSlim(false);
        ManualResetEventSlim stopEvent = new ManualResetEventSlim(false);
        ManualResetEventSlim startEvent = new ManualResetEventSlim(false);

        public MainWindow()
        {
            InitializeComponent();
            lvUsers.ItemsSource = fileList;
            lvUsers1.ItemsSource = fileDetailList;
            lvUsers.MouseDoubleClick += LvUsers_MouseDoubleClick;
            lvUsers.SelectionChanged += LvUsers_SelectionChanged;
        }

        private void LvUsers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            fileDetailList.Clear();

            //List<Person> listPerson = new List<Person>();
            //listPerson.Add(new Person("John", "Doe"));
            //listPerson.Add(new Person("James", "Test"));
            //listPerson.Add(new Person("Tester", "Black"));
            //listPerson.Add(new Person("Joan", "Down"));
            //listPerson.Add(new Person("Cole", "Wu"));
            //listPerson.Add(new Person("Test", "Liu"));
            //listPerson.Add(new Person("Jack", "Zhao"));
            //listPerson.Add(new Person("Coach", "Tang"));
            //listPerson.Add(new Person("Rose", "Chou"));
            //lvUsers1.ItemsSource = listPerson;

            var fileInfo = lvUsers.SelectedItem as FileInfo;
            if (fileInfo != null)
            {
                if (fileInfo.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    fileDetailList.Add(new FileSearchResult { Text = fileInfo.FullName });
                int count = 0;
                foreach (var line in File.ReadAllLines(fileInfo.FullName))
                {
                    count++;
                    if (line.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                        fileDetailList.Add(new FileSearchResult { LineNumber = count.ToString(), Text = line });
                }
            }
        }

        private void LvUsers_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var fileInfo = ((FrameworkElement)e.OriginalSource).DataContext as FileInfo;
            if (fileInfo != null)
            {
                Process.Start(fileInfo.FullName);
            }
        }
        static Guid currentRunId;
        private void button_Click(object sender, RoutedEventArgs e)
        {
            fileNames = textBox.Text;
            dirName = textBox2.Text;
            searchText = textBox1.Text;
            matchCase = chkMatchcase.IsChecked;
            timeTakenLebel.Text = "";
            if (string.IsNullOrWhiteSpace(dirName))
            {
                statusLebel.Text = "Look in directory is required.";
                statusLebel.Foreground = new SolidColorBrush(Colors.Red);
                return;
            }
            statusLebel.Foreground = new SolidColorBrush(Colors.Green);
            statusLebel.Text = "Searching...";

            if (startEvent.IsSet && !pauseEvent.IsSet)
            {
                button.Content = "Resume";
                statusLebel.Text = "Search paused";
                stopEvent.Reset();
                startEvent.Reset();
                pauseEvent.Set();
                
            }
            else if (!startEvent.IsSet)
            {
                button.Content = "Pause";
                stopEvent.Reset();
                startEvent.Set();
                if (!pauseEvent.IsSet)
                {
                    fileList.Clear();
                    currentRunId = Guid.NewGuid();
                    Task.Run(() => DoWork(currentRunId));
                }
                pauseEvent.Reset();                
            }
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            statusLebel.Text = "Search canceled";
            timeTakenLebel.Text = "";
            UpdateStopEvents();
        }

        void UpdateStopEvents()
        {
            stopEvent.Set();
            if (!startEvent.IsSet)
                startEvent.Set();            
            startEvent.Reset();
            pauseEvent.Reset();
            
            Dispatcher.Invoke(() =>
            {
                button.Content = "Start";
                //label.Content = "0";
            }
            );
        }

        private void DoWork(Guid runId)
        {
            if (!runId.Equals(currentRunId)) return;
            int sz = 0;
            var exclusionList = new List<string>();
            var extList = new HashSet<string>();
            var staticFiles = new HashSet<string>();
            var fileTypeList = fileNames.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var fileType in fileTypeList)
            {
                var trimmedFileType = fileType.Trim();
                if (trimmedFileType == "*.*")
                {
                    extList.Add(".*");
                    break;
                }
                else if (trimmedFileType.StartsWith("*."))
                {
                    extList.Add(trimmedFileType.Substring(1));
                }
                else
                    staticFiles.Add(trimmedFileType);
            }
            if(extList.Count == 0 && staticFiles.Count == 0) extList.Add(".*");
            Stopwatch sw = new Stopwatch();
            sw.Start();
            //var files = SafeFileEnumerator.EnumerateFiles(dirName, SearchOption.AllDirectories, extList, exclusionList, sz, staticFiles);
            //Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 4 }, (file1, state) =>
            foreach (var file1 in SafeFileEnumerator.EnumerateFiles(dirName, SearchOption.AllDirectories, extList, exclusionList, sz, staticFiles))
            {
                if (!runId.Equals(currentRunId)) return;//state.Stop();
                startEvent.Wait();
                if (stopEvent.IsSet) return;//state.Stop();
                try
                {
                    if (matchCase.Value)
                    {
                        if (Path.GetFileName(file1).Contains(searchText)) AddToFileList(runId, file1);
                    }
                    else
                    {
                        if (Path.GetFileName(file1).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) AddToFileList(runId, file1);
                    }


                    foreach (var line in File.ReadLines(file1))
                    {
                        if (!runId.Equals(currentRunId)) return;//state.Stop();
                        startEvent.Wait();
                        if (stopEvent.IsSet) return;// state.Stop();
                        if (matchCase.Value)
                        {
                            if (line.Contains(searchText))
                            {
                                AddToFileList(runId, file1);
                                break;
                            }
                        }
                        else
                        {
                            if (line.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                AddToFileList(runId, file1);
                                break;
                            }
                        }
                    }
                }
                catch (IOException ex)
                {
                }
                catch (UnauthorizedAccessException ex)
                {
                }
            }
            //);
            if (!stopEvent.IsSet)
            {
                Dispatcher.Invoke(() => { timeTakenLebel.Text = $"Time: {sw.Elapsed.TotalSeconds}s"; });
                Dispatcher.Invoke(() => statusLebel.Text = $"{fileList.Count} record(s).");
            }
            UpdateStopEvents();
        }

        private void AddToFileList(Guid runId, string file)
        {
            if (runId.Equals(currentRunId))
            {
                var file1 = new FileInfo(file);
                Dispatcher.Invoke(() =>
                {
                    if (runId.Equals(currentRunId))
                    {
                        fileList.Add(file1);
                    }
                });
            }
        }

        private void btnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                DialogResult result = dialog.ShowDialog();
                if(result == System.Windows.Forms.DialogResult.OK)
                    textBox2.Text = dialog.SelectedPath;
            }
        }
    }

    public class FileSearchResult
    {
        public string LineNumber { get; set; }

        public string Text { get; set; }
    }

    class Person
    {
        public string FirstName { get; set; }

        public string LastName { get; set; }

        public Person(string fname, string lname)
        {
            FirstName = fname;
            LastName = lname;
        }
    }
}