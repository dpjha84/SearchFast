using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SearchFast
{
    public partial class MainWindow : Window
    {
        string dirName = null;
        string searchText = null;
        string fileNames = null;
        bool workStarted = false;
        ObservableCollection<FileInfo> fileList = new ObservableCollection<FileInfo>();
        ManualResetEventSlim stopEvent = new ManualResetEventSlim(false);

        public MainWindow()
        {
            InitializeComponent();
            lvUsers.ItemsSource = fileList;
            button_Click(null, null);
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            fileNames = textBox.Text;
            dirName = textBox2.Text;
            searchText = textBox1.Text;
            statusLebel.Text = $"Scanning...";
            stopEvent.Set();
            if (!workStarted)
            {
                Task.Run(() => DoWork());
                workStarted = true;
            }
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            statusLebel.Text = $"Stopped";
            stopEvent.Reset();
        }

        private void DoWork()
        {
            Dispatcher.Invoke(() => { fileList.Clear(); });
            int sz = 0;
            var exclusionList = new List<string>();
            var extList = new HashSet<string>();
            if(fileNames.StartsWith("*."))
                extList.Add(fileNames.Substring(2));
            Stopwatch sw = new Stopwatch();
            sw.Start();
            Parallel.ForEach(SafeFileEnumerator.EnumerateFiles(dirName, SearchOption.AllDirectories, extList, exclusionList, sz), new ParallelOptions { MaxDegreeOfParallelism = -1 }, file1 =>
            {
                stopEvent.Wait();
                try
                {
                    if (File.ReadAllText(file1).Contains(searchText))
                    {
                        Dispatcher.Invoke(() =>
                        {                            
                            fileList.Add(new FileInfo(file1));
                        });
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
            );
            Dispatcher.Invoke(() => { timeTakenLebel.Text = $"Time: {sw.Elapsed.TotalSeconds}s"; });
            workStarted = false;
            Dispatcher.Invoke(() => statusLebel.Text = $"{fileList.Count} record(s).");
        }
    }
}