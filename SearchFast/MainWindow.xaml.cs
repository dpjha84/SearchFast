﻿using Microsoft.Win32;
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
        ObservableCollection<FileSearchResult> fileDetailList = new ObservableCollection<FileSearchResult>();
        ManualResetEventSlim stopEvent = new ManualResetEventSlim(false);

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

        private void button_Click(object sender, RoutedEventArgs e)
        {
            fileNames = textBox.Text;
            dirName = textBox2.Text;
            searchText = textBox1.Text;
            statusLebel.Text = $"Searching...";
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
            var staticFiles = new HashSet<string>();
            var fileTypeList = fileNames.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var fileType in fileTypeList)
            {
                var trimmedFileType = fileType.Trim();
                if (trimmedFileType == "*.*")
                {
                    extList.Clear();
                    break;
                }
                else if (trimmedFileType.StartsWith("*."))
                {
                    extList.Add(trimmedFileType.Substring(2));
                }
                else
                    staticFiles.Add(trimmedFileType);
            }
            Stopwatch sw = new Stopwatch();
            sw.Start();
            Parallel.ForEach(SafeFileEnumerator.EnumerateFiles(dirName, SearchOption.AllDirectories, extList, exclusionList, sz, staticFiles), new ParallelOptions { MaxDegreeOfParallelism = -1 }, file1 =>
            {
                stopEvent.Wait();
                try
                {
                    if (Path.GetFileName(file1).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                    || File.ReadAllText(file1).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
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