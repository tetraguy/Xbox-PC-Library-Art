
namespace System.Windows
{
    internal class Forms
    {
        private static object? dialogResult;

        public static object GetDialogResult()
        {
            return dialogResult;
        }

        internal static void SetDialogResult(object value)
        {
            dialogResult = value;
        }

        internal class FolderBrowserDialog
        {
            internal readonly string SelectedPath;

            public FolderBrowserDialog()
            {
                SelectedPath = string.Empty;
                Description = string.Empty;
            }

            public string Description { get; internal set; }
            public bool UseDescriptionForTitle { get; internal set; }
            public bool ShowNewFolderButton { get; internal set; }

            internal object ShowDialog()
            {
                throw new NotImplementedException();
            }
        }

        internal class OpenFileDialog
        {
            public string? Title { get; set; }
            public string? Filter { get; set; }
            public bool CheckFileExists { get; set; }
            public bool Multiselect { get; set; }
            public object? FileName { get; internal set; }

            internal object ShowDialog()
            {
                throw new NotImplementedException();
            }
        }
    }
}