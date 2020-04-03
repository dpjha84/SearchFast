using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SearchFast
{
    public class FileReader
    {
        protected string filePath;

        public FileReader(string filePath)
        {
            this.filePath = filePath;
        }

        public virtual bool Contains(string searchText)
        {
            return ContainsInText(File.ReadAllText(filePath), searchText);
        }

        protected bool ContainsInText(string sourceText, string searchText)
        {
            return sourceText.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public class PdfFileReader : FileReader
    {
        public PdfFileReader(string filePath) : base(filePath)
        {

        }
        
    }

    public class FileReaderFactory
    {
        public static FileReader Get(string filePath)
        {
            switch(Path.GetExtension(filePath))
            {
                case ".pdf":
                    return new FileReader(filePath);
                default:
                    return new FileReader(filePath);
            }
        }
    }
}
