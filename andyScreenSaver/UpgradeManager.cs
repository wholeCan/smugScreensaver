using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace andyScreenSaver
{
    internal class UpgradeManager
    {
        //idea is to check for a new download.
        //perform version check (create a checksum of file), and store it somewhere for review.
        //if different, then proceed with upgrade.

        /// <summary>
        ///  test cases:
        ///  1. first time install
        ///  2. open config, check install and decline
        ///  3. open config, check install and accept
        ///  4. open config, decline, then retry.
        /// </summary>

        const int numberOfDaysBeforeChecking = 10;

        private static UpgradeManager instance;
        public static UpgradeManager Instance
        {
            get
            {
                // If the instance is null, create a new instance
                if (instance == null)
                {
                    instance = new UpgradeManager();
                }
                return instance;
            }
        }

        ~UpgradeManager()
        {
            //deleteCurrentInstaller();
        }
        private UpgradeManager() {
            lastUpdate = DateTime.Now;
            //readyForUpgrade();
        }
        bool checkRun = false;
        public bool ReadyForUpgrade
        {
            get
            {
                return readyForUpgrade();
            }       
        }

        DateTime lastUpdate = DateTime.Now;
        private bool readyForUpgrade()
        {
            var timeCheck = lastUpdate.AddMilliseconds(1);
            if (timeCheck > DateTime.Now)
            {//silly throttle
             //   return true;
            }
            var oldChecksum = readChecksumfromFile(InstalledVersionChecksumPath);
            var latestChecksum = downloadLatest();
            checkRun = true;
            lastUpdate = DateTime.Now;

            if (latestChecksum != oldChecksum)
            {
                installed = false;
                return true;
            }
            return false;
        }

        bool installed = false;
        public void PerformUpgrade()
        {
            if (checkRun)
            {
                WriteStringToFile(CalculateSHA256Checksum(InstallerPath), InstalledVersionChecksumPath);
                RunApplicationAsAdmin();
                installed = true;
                System.Threading.Thread.Sleep(500);
            }
        }

        private string? readChecksumfromFile(string filename)
        {
            if (!File.Exists(filename)) { return null; }
            return File.ReadAllText(filename);//.Substring(0, 500);//include max length for safety.
        }

        public void deleteCurrentInstaller()
        {
            if (File.Exists(InstallerPath))
            {
                File.Delete(InstallerPath);
            }
        }

        void RunApplicationAsAdmin()
        {
            if (!File.Exists(InstallerPath))
            {
                Debug.WriteLine("Missing install file");
                return;
            }
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = InstallerPath,
                Verb = "runas", // "runas" indicates that the process should run with elevated privileges
                //running silently doesn't work and is confusing.
               // UseShellExecute = true,
               // CreateNoWindow = false,
               // RedirectStandardOutput = true,
               // RedirectStandardError = true,
              //  Arguments = "/S" 
            };

            Process process = new Process
            {
                StartInfo = startInfo,
            };

            try
            {
                process.Start();
            
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // The exception is thrown if the user clicks "No" on the UAC dialog
                Console.WriteLine($"User declined elevation: {ex.Message}");
            }
        }

        string GetDownloadsFolderPath
        {
            get
            {
                // Get the Downloads folder path
                string downloadsFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                // Append "Downloads" to the path
                downloadsFolderPath = System.IO.Path.Combine(downloadsFolderPath, "Downloads");

                return downloadsFolderPath;
            }
        }

        String InstallerPath
        {
            get
            {
                return GetDownloadsFolderPath + @"\smugAndyLatest.exe";
            }
        }
        String InstalledVersionChecksumPath
        {
            get
            {
                return GetDownloadsFolderPath + @"\smugAndyLatest.md5";
            }
        }
        string InstallerURL
        {
            get
            {
                return "https://github.com/wholeCan/smugScreensaver/blob/main/nsisInstaller/andysScreensaverInstaller_small.exe?raw=true";
            }
        }
        //return checksum for new file
        private string downloadLatest()
        {
            
            using (var client = new WebClient())
            {
                // Download the updated executable
                client.DownloadFile(InstallerURL, InstallerPath);
                string sha256Checksum = CalculateSHA256Checksum(InstallerPath);
                //WriteStringToFile(sha256Checksum, ChecksumFilePath);
                return sha256Checksum;
            }
        }

        static void WriteStringToFile(string content, string filePath)
        {
            // Write the string to the specified file
            File.WriteAllText(filePath, content);
        }

        private static string? CalculateSHA256Checksum(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            using (var sha256 = SHA256.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLower();
                }
            }
        }

    }
}
