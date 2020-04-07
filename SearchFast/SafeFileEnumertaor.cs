using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SearchFast
{
    public static class SafeFileEnumerator
    {
        public static IEnumerable<string> EnumerateDirectories(string parentDirectory, string searchPattern, SearchOption searchOpt,
            List<string> exclusions)
        {
            try
            {
                var directories = Enumerable.Empty<string>();
                if (exclusions.Contains(parentDirectory))
                    return directories;
                if (searchOpt == SearchOption.AllDirectories)
                {
                    directories = Directory.EnumerateDirectories(parentDirectory)
                        .SelectMany(x => EnumerateDirectories(x, searchPattern, searchOpt, exclusions));
                }
                return directories.Concat(Directory.EnumerateDirectories(parentDirectory, searchPattern));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Enumerable.Empty<string>();
            }
        }

        public static IEnumerable<string> EnumerateFiles(string path, SearchOption searchOpt, HashSet<string> extList, List<string> exc, int minSize, HashSet<string> fileNames)
        {
            try
            {
                var dirFiles = Enumerable.Empty<string>();
                var dirName = Path.GetFileName(path);
                if (exc.Contains(path.ToLowerInvariant())
                    || exc.Where((x) => x.StartsWith(path.ToLowerInvariant() + "\\")).ToList().Count > 0
                    //|| (!string.IsNullOrWhiteSpace(dirName) && dirName.StartsWith("$"))
                    )
                    return dirFiles;
                if (searchOpt == SearchOption.AllDirectories)
                {
                    dirFiles = Directory.EnumerateDirectories(path)
                                        .SelectMany(x => EnumerateFiles(x, searchOpt, extList, exc, minSize, fileNames));
                }
                return dirFiles.Concat(Directory.EnumerateFiles(path, "*.*")
                    .Where(file => (MatchingExtension(file, extList)
                        && new FileInfo(file).Length / (1024 * 1024) >= minSize)
                        || fileNames.Contains(Path.GetFileName(file), new MyEqualityComparer())));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Enumerable.Empty<string>();
            }
            catch (PathTooLongException ex)
            {
                return Enumerable.Empty<string>();
            }
            catch (FileNotFoundException ex)
            {
                return Enumerable.Empty<string>();
            }
            catch (IOException ex)
            {
                return Enumerable.Empty<string>();
            }
        }
        static bool MatchingExtension(string file, HashSet<string> extList)
        {
            return extList.Count == 0 ? false : extList.Contains(".*") ? true : extList.Any(x => file.EndsWith(x, StringComparison.OrdinalIgnoreCase));
        }
    }

    public class MyEqualityComparer : IEqualityComparer<string>
    {
        public bool Equals(string x, string y)
        {
            return y.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public int GetHashCode(string obj)
        {
            return obj.GetHashCode();
        }
    }
}