using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using Binding = System.Windows.Data.Binding;

namespace SearchFast
{
    public partial class MainWindow : Window
    {
        string dirName = null;
        string searchText = null;
        string fileNames = null;
        bool? matchCase = false;
        SearchOption searchOption;
        static Guid currentRunId;
        private long? lowestBreakIndex = null;
        bool paused, terminated, processing;
        GridViewColumnHeader _lastHeaderClicked = null;
        ListSortDirection _lastDirection = ListSortDirection.Ascending;
        readonly ObservableCollection<FileInfo> fileList = new ObservableCollection<FileInfo>();
        readonly ObservableCollection<FileSearchResult> fileDetailList = new ObservableCollection<FileSearchResult>();

        public MainWindow()
        {
            InitializeComponent();
            lvFiles.ItemsSource = fileList;
            lvFileDetails.ItemsSource = fileDetailList;
            lvFiles.MouseDoubleClick += LvFiles_MouseDoubleClick;
            lvFiles.SelectionChanged += LvFiles_SelectionChanged;
        }

        private void LvFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            fileDetailList.Clear();
            var fileInfo = lvFiles.SelectedItem as FileInfo;
            if (fileInfo != null)
            {
                if (Contains(fileInfo.Name, searchText, matchCase))
                    fileDetailList.Add(new FileSearchResult { Text = fileInfo.FullName, MatchCase = matchCase.HasValue ? (bool)matchCase.Value : false });
                int count = 0;
                foreach (var line in File.ReadAllLines(fileInfo.FullName))
                {
                    count++;
                    if (Contains(line, searchText, matchCase))
                        fileDetailList.Add(new FileSearchResult { LineNumber = count.ToString(), Text = line, MatchCase = matchCase.HasValue ? (bool)matchCase.Value : false });
                }
            }
        }

        private void LvFiles_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var fileInfo = ((FrameworkElement)e.OriginalSource).DataContext as FileInfo;
            if (fileInfo != null)
            {
                Process.Start(fileInfo.FullName);
            }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            fileNames = textBox.Text;
            dirName = textBox2.Text;
            searchText = textBox1.Text;
            timeTakenLebel.Text = "";
            if (string.IsNullOrWhiteSpace(dirName))
            {
                statusLebel.Text = "Look in directory is required.";
                statusLebel.Foreground = new SolidColorBrush(Colors.Red);
                return;
            }
            statusLebel.Foreground = new SolidColorBrush(Colors.Green);
            statusLebel.Text = "Searching...";

            if (button.Content.ToString() == "Pause")
            {
                paused = true;
                terminated = false;
                processing = false;
                statusLebel.Text = "Search paused";
                button.Content = "Resume";
            }
            else
            {
                if (button.Content.ToString() == "Start")
                {
                    searchOption = chkSubfolders.IsChecked.Value ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    matchCase = chkMatchcase.IsChecked;
                    currentRunId = Guid.NewGuid();
                    fileList.Clear();
                }
                paused = false;
                terminated = false;
                processing = true;
                button.Content = "Pause";
                Task.Factory.StartNew(() => StartProcess(currentRunId));
            }
        }

        private void StartProcess(Guid runId)
        {
            if (!runId.Equals(currentRunId)) return;
            int size = 0;
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
            if (extList.Count == 0 && staticFiles.Count == 0) extList.Add(".*");

            var files = SafeFileEnumerator.EnumerateFiles(dirName, searchOption, extList, exclusionList, size, staticFiles);
            int skip = lowestBreakIndex.HasValue ? (int)lowestBreakIndex.Value : 0;
            Stopwatch sw = Stopwatch.StartNew();
            ParallelLoopResult result = Parallel.ForEach(files.Skip(skip), new ParallelOptions() { MaxDegreeOfParallelism = 4 }, (file1, state) =>
            {
                if (paused)
                    state.Break();
                else if (terminated)
                    state.Stop();
                else
                    try
                    {
                        TryMatch(runId, file1, Path.GetFileName(file1), searchText, matchCase);
                        foreach (var line in File.ReadLines(file1))
                        {
                            if (!runId.Equals(currentRunId)) return;
                            if (paused)
                                state.Break();
                            else if (terminated)
                                state.Stop();
                            else
                            {
                                if (TryMatch(runId, file1, line, searchText, matchCase))
                                    break;
                            }
                        }
                    }
                    catch (IOException ex)
                    {
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                    }
            });
            lowestBreakIndex = result.LowestBreakIteration;
            if (!lowestBreakIndex.HasValue)
            {
                Dispatcher.Invoke(() =>
                {
                    timeTakenLebel.Text = $"Time: {sw.Elapsed.TotalSeconds}s";
                    statusLebel.Text = $"{fileList.Count} file(s).";
                    button.Content = "Start";
                });
            }
            processing = paused = terminated = false;
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            terminated = true;
            processing = false;
            paused = false;
            lowestBreakIndex = null;
            button.Content = "Start";
            statusLebel.Text = "Search canceled";
            timeTakenLebel.Text = "";
        }

        private bool TryMatch(Guid runId, string filePath, string text, string searchText, bool? matchCase)
        {
            bool found = false;
            if (Contains(text, searchText, matchCase))
            {
                found = true;
                if (runId.Equals(currentRunId))
                {
                    var file1 = new FileInfo(filePath);
                    Dispatcher.Invoke(() =>
                    {
                        if (runId.Equals(currentRunId))
                        {
                            fileList.Add(file1);
                        }
                    });
                }
            }
            return found;
        }

        private void GridViewColumnHeaderClickedHandler(object sender, RoutedEventArgs e)
        {
            var headerClicked = e.OriginalSource as GridViewColumnHeader;
            ListSortDirection direction;

            if (headerClicked != null)
            {
                if (headerClicked.Role != GridViewColumnHeaderRole.Padding)
                {
                    if (headerClicked != _lastHeaderClicked)
                    {
                        direction = ListSortDirection.Ascending;
                    }
                    else
                    {
                        if (_lastDirection == ListSortDirection.Ascending)
                        {
                            direction = ListSortDirection.Descending;
                        }
                        else
                        {
                            direction = ListSortDirection.Ascending;
                        }
                    }
                    string header = ((Binding)headerClicked.Column.DisplayMemberBinding).Path.Path;
                    Sort(header, direction);

                    _lastHeaderClicked = headerClicked;
                    _lastDirection = direction;
                }
            }
        }

        private void Sort(string sortBy, ListSortDirection direction)
        {
            ICollectionView dataView = CollectionViewSource.GetDefaultView(lvFiles.ItemsSource);
            dataView.SortDescriptions.Clear();
            SortDescription sd = new SortDescription(sortBy, direction);
            dataView.SortDescriptions.Add(sd);
            dataView.Refresh();
        }

        private void BtnQuestion_Click(object sender, RoutedEventArgs e)
        {
            new InfoWindow().ShowDialog();
        }

        private bool Contains(string source, string searchText, bool? matchCase)
        {
            return matchCase.Value ?
                source.IndexOf(searchText) >= 0 :
                source.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                DialogResult result = dialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                    textBox2.Text = dialog.SelectedPath;
            }
        }
    }

    public class FileSearchResult
    {
        public string LineNumber { get; set; }

        public string Text { get; set; }

        public bool MatchCase { get; set; }
    }
}