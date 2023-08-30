using Alphaleonis.Win32.Network;
using ControlzEx.Standard;
using Microsoft.Build.Logging.StructuredLogger;
using Noggog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SynthEBD
{
    public class _7ZipInterface
    {
        private readonly IEnvironmentStateProvider _environmentStateProvider;
        private string _sevenZipPath => Path.Combine(_environmentStateProvider.InternalDataPath, "7Zip",
                            Environment.Is64BitProcess ? "x64" : "x86", "7z.exe");
        public _7ZipInterface(IEnvironmentStateProvider environmentStateProvider)
        {
            _environmentStateProvider = environmentStateProvider;
        }

        public bool ExtractArchiveNew(string archivePath, string destinationPath, bool hideWindow, Action<string> updateStr)
        {
            try
            {
                ProcessStartInfo pro = new ProcessStartInfo();
                if (hideWindow)
                {
                    pro.UseShellExecute = false;
                    pro.CreateNoWindow = true;
                    pro.WindowStyle = ProcessWindowStyle.Hidden;
                }
                pro.FileName = _sevenZipPath;
                pro.Arguments = string.Format("x \"{0}\" -y -o\"{1}\"", archivePath, destinationPath);
                if (updateStr != null)
                {
                    pro.WindowStyle = ProcessWindowStyle.Hidden;
                    pro.RedirectStandardOutput = true;
                    pro.RedirectStandardError = true;
                    pro.UseShellExecute = false;
                }
                using (Process process = new Process { StartInfo = pro, EnableRaisingEvents = true })
                {
                    process.Start();

                    // Capture the standard output
                    StringBuilder standardOutputCapture = new StringBuilder();

                    // Asynchronously read the standard output
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                        {
                            updateStr(e.Data);
                            standardOutputCapture.AppendLine(e.Data); // Capture in buffer
                        }
                    };

                    process.BeginOutputReadLine();

                    // Wait for the process to exit
                    process.WaitForExit();

                    // Do something with the captured standard output
                    var redirectedOutput = standardOutputCapture.ToString();
                    if (redirectedOutput.Contains("Can't open as archive"))
                    {
                        CustomMessageBox.DisplayNotificationOK("File Extraction Error", "Extraction of " + archivePath + " appears to have failed with message: " + Environment.NewLine + redirectedOutput.Replace("\r\n", Environment.NewLine));
                        return false;
                    }
                }
            }

            catch (Exception e)
            {
                CustomMessageBox.DisplayNotificationOK("File Extraction Error", "Extraction of " + archivePath + " failed with message: " + Environment.NewLine + ExceptionLogger.GetExceptionStack(e));
                return false;
            }
            return true;
        }

        public List<string> GetArchiveContents(string archivePath)
        {
            ProcessStartInfo pro = new ProcessStartInfo();
            pro.WindowStyle = ProcessWindowStyle.Hidden;
            pro.FileName = _sevenZipPath;
            pro.Arguments = string.Format("l -slt \"{0}\"", archivePath);
            pro.RedirectStandardOutput = true;
            pro.UseShellExecute = false;

            string redirectedOutput = "";
            using (Process process = new Process { StartInfo = pro })
            {
                ConsoleAllocator.ShowConsoleWindow();
                process.Start();

                // Capture the standard output
                StringBuilder standardOutputCapture = new StringBuilder();

                // Asynchronously read the standard output
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        Console.WriteLine(e.Data); // Print to console
                        standardOutputCapture.AppendLine(e.Data); // Capture in buffer
                    }
                };

                process.BeginOutputReadLine();

                // Wait for the process to exit
                process.WaitForExit();

                // Do something with the captured standard output
                redirectedOutput = standardOutputCapture.ToString();
            }
            var output = redirectedOutput.Split(Environment.NewLine).ToList();

            //Process x = Process.Start(pro);
            //x.WaitForExit();
            //var output = x.StandardOutput.ReadToEnd().Split(Environment.NewLine).ToList();

            // remove the path of the archive itself
            for (int i = 0; i < output.Count; i++)
            {
                if (i > 0 && output[i].StartsWith("Type = ") && output[i - 1].StartsWith("Path = "))
                {
                    output.RemoveAt(i - 1);
                }
            }

            var processedOutput = new List<string>(output.Where(x => x.StartsWith("Path = ")).Select(x => x.Replace("Path = ", "")).Where(x => IsFilePathFragment(x)));
            return processedOutput;
        }

        // https://stackoverflow.com/a/31978833
        // temporarily here, will move to its own cs file once confirmed working
        internal static class ConsoleAllocator
        {
            [DllImport(@"kernel32.dll", SetLastError = true)]
            static extern bool AllocConsole();

            [DllImport(@"kernel32.dll")]
            static extern IntPtr GetConsoleWindow();

            [DllImport(@"user32.dll")]
            static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            const int SwHide = 0;
            const int SwShow = 5;


            public static void ShowConsoleWindow()
            {
                var handle = GetConsoleWindow();

                if (handle == IntPtr.Zero)
                {
                    AllocConsole();
                }
                else
                {
                    ShowWindow(handle, SwShow);
                }
            }

            public static void HideConsoleWindow()
            {
                var handle = GetConsoleWindow();

                ShowWindow(handle, SwHide);
            }
        }

        private bool IsFilePathFragment(string input)
        {
            var last = input.Split(Path.DirectorySeparatorChar).Last();
            if (!last.IsNullOrWhitespace())
            {
                var split = last.Split('.');
                if (split.Length > 1)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
