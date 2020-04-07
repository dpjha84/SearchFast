using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using Binding = System.Windows.Data.Binding;

namespace SearchFast
{
    public partial class MainWindow : Window
    {
        List<string> dirNames = new List<string>();
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
        readonly ObservableCollection<FileInfoWrapper> fileList = new ObservableCollection<FileInfoWrapper>();
        readonly ObservableCollection<FileSearchResult> fileDetailList = new ObservableCollection<FileSearchResult>();

        public MainWindow()
        {
            InitializeComponent();
            lvFiles.ItemsSource = fileList;
            lvFileDetails.ItemsSource = fileDetailList;
            lvFiles.MouseDoubleClick += LvFiles_MouseDoubleClick;
            lvFiles.SelectionChanged += LvFiles_SelectionChanged;
            textBox2.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private void LvFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            fileDetailList.Clear();
            var fileInfo = lvFiles.SelectedItem as FileInfoWrapper;
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
            var fileInfo = ((FrameworkElement)e.OriginalSource).DataContext as FileInfoWrapper;
            if (fileInfo != null)
            {
                Process.Start(fileInfo.FullName);
            }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            dirNames.Clear();
            ResetLabels(statusLabel, fileCountLabel, timeTakenLabel);
            fileNames = textBox.Text;
            dirName = textBox2.Text;
            searchText = textBox1.Text;
            timeTakenLabel.Text = "";
            var drives = DriveInfo.GetDrives();
            if (string.IsNullOrWhiteSpace(dirName))
            {
                dirNames = DriveInfo.GetDrives().Where(x => x.IsReady).Select(x => x.Name).ToList();
            }
            else
            {
                foreach (var dir in dirName.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Directory.Exists(dir))
                        dirNames.Add(dir);
                }
                if (dirNames.Count == 0)
                {
                    statusLabel.Text = "Invalid Look in directory.";
                    statusLabel.Foreground = new SolidColorBrush(Colors.Red);
                    return;
                }
            }
            statusLabel.Foreground = new SolidColorBrush(Colors.Green);
            statusLabel.Text = "Searching...";
            if (button.Content.ToString() == "Pause")
            {
                paused = true;
                terminated = false;
                processing = false;
                statusLabel.Text = "Search paused";
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

        private void ResetLabels(params TextBlock[] labels)
        {
            foreach (var item in labels)
            {
                item.Text = "";
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

            var files = Enumerable.Empty<string>();
            foreach (var dir in dirNames)
            {
                files = files.Concat(SafeFileEnumerator.EnumerateFiles(dir, searchOption, extList, exclusionList, size, staticFiles));
            }
            
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
                        if (!TryMatch(runId, file1, Path.GetFileName(file1), searchText, matchCase))
                        {
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
                    statusLabel.Text = terminated ? "Search canceled" : "Search completed";
                    timeTakenLabel.Text = $"Time: {sw.Elapsed.TotalSeconds}s";
                    fileCountLabel.Text = $"{fileList.Count} file(s).";
                    button.Content = "Start";
                });
            }
            processing = paused = terminated = false;
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            ResetLabels(statusLabel, timeTakenLabel, fileCountLabel);
            terminated = true;
            processing = false;
            paused = false;
            lowestBreakIndex = null;
            button.Content = "Start";
            statusLabel.Text = "Search canceled";
            timeTakenLabel.Text = "";
        }

        private bool TryMatch(Guid runId, string filePath, string text, string searchText, bool? matchCase)
        {
            bool found = false;
            if (Contains(text, searchText, matchCase))
            {
                found = true;
                if (runId.Equals(currentRunId))
                {
                    var file1 = new FileInfoWrapper(filePath);
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
                    if (header == "Size") header = "Length";
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

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            ResetLabels(statusLabel, timeTakenLabel, fileCountLabel);
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                FileName = "SearchResults.csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Filter = "Comma separated (CSV) (*.csv)|*.csv"
            };
            var result = saveFileDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                try
                {
                    CreateCSVFile(ToDataTable(lvFiles), saveFileDialog.FileName);
                    UpdateStatus("View exported file", saveFileDialog.FileName);
                }
                catch (Exception ex)
                {
                    if (!Directory.Exists("Logs")) Directory.CreateDirectory("Logs");
                    var errorFileName = $"Logs\\ErrorLog_{DateTime.Now.ToString().Replace(":", "-")}.txt";
                    File.WriteAllText(errorFileName, ex.ToString());
                    UpdateStatus("Error in Export", Path.Combine(Environment.CurrentDirectory, errorFileName));
                }
            }
        }

        private void UpdateStatus(string text, string target)
        {
            var hyperlink = new Hyperlink(new Run(text)) { NavigateUri = new Uri(target) };
            hyperlink.RequestNavigate += new RequestNavigateEventHandler(Hyperlink_RequestNavigate);
            statusLabel.Inlines.Clear();
            statusLabel.Inlines.Add(hyperlink);
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(e.Uri.AbsoluteUri);
        }

        private DataTable ToDataTable(System.Windows.Controls.ListView lv)
        {
            DataTable table = new DataTable();
            table.Columns.Add("Name");
            table.Columns.Add("Location");
            table.Columns.Add("Size");
            foreach (var item in lv.Items)
            {
                var file = item as FileInfoWrapper;
                table.Rows.Add(file.Name, file.DirectoryName, file.Size);
            }
            return table;
        }

        public void CreateCSVFile(DataTable dt, string strFilePath)
        {
            using (StreamWriter sw = new StreamWriter(strFilePath, false))
            {
                int iColCount = dt.Columns.Count;
                for (int i = 0; i < iColCount; i++)
                {
                    sw.Write(dt.Columns[i]);
                    if (i < iColCount - 1)
                    {
                        sw.Write(",");
                    }
                }
                sw.Write(sw.NewLine);

                foreach (DataRow dr in dt.Rows)
                {
                    for (int i = 0; i < iColCount; i++)
                    {
                        if (!Convert.IsDBNull(dr[i]))
                        {
                            sw.Write(dr[i].ToString());
                        }
                        if (i < iColCount - 1)
                        {
                            sw.Write(",");
                        }
                    }
                    sw.Write(sw.NewLine);
                }
            }
        }
    }

    public class FileSearchResult
    {
        public string LineNumber { get; set; }

        public string Text { get; set; }

        public bool MatchCase { get; set; }
    }

    public class FileInfoWrapper
    {
        readonly FileInfo fileInfo;
        public FileInfoWrapper(string filePath)
        {
            fileInfo = new FileInfo(filePath);
        }

        public string Name => fileInfo.Name;

        public string FullName => fileInfo.FullName;

        public string DirectoryName => fileInfo.DirectoryName;

        public long Length => fileInfo.Length;

        public string Size => $"{Math.Ceiling((double)Length / 1024)} KB";

        public BitmapSource Icon
        {
            get
            {
                using (var sysicon = System.Drawing.Icon.ExtractAssociatedIcon(fileInfo.FullName))
                {
                    var bmpSrc = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            sysicon.Handle,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                    return bmpSrc;
                }                
            }
        }
    }
}