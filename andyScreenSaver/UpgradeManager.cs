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

        private UpgradeManager() {
            readyForUpgrade();
            //checkAndPerformUpgrade();
        }
        bool testRun = false;
        public bool ReadyForUpgrade
        {
            get
            {
                return testRun;
            }
            set
            {
                testRun = value;
            }
        }

        private bool readyForUpgrade()
        {

            var lastFileDate = GetFileCreationDate(InstallerPath);
            var oldChecksum = CalculateSHA256Checksum(InstallerPath);
#if(DEBUG) // upgrade test

            var upgradeTest = lastFileDate?.AddSeconds(1);
#else
            var upgradeTest = lastFileDate?.AddDays(numberOfDaysBeforeChecking);
#endif
   //         if (lastFileDate == null || upgradeTest < DateTime.Now)
            {
                var latestChecksum = downloadLatest();
                if (latestChecksum != oldChecksum)
                {
                    ReadyForUpgrade = true;
                    return true;
                }
            }
            return false;
        }

        public void PerformUpgrade()
        {
            if (ReadyForUpgrade)
            {
                RunApplicationAsAdmin(InstallerPath);
           
            }
        }

        public void deleteCurrentInstaller()
        {
            if (File.Exists(InstallerPath))
            {
                File.Delete(InstallerPath);
            }
        }

        static void RunApplicationAsAdmin(string applicationPath)
        {
            if (!File.Exists(applicationPath))
            {
                Debug.WriteLine("Missing install file");
                return;
            }
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = applicationPath,
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


        String InstallerPath
        {
            get
            {
                return Path.GetTempPath() + @"\smugAndyLatest.exe";
            }
        }
        static DateTime? GetFileCreationDate(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }
            // Create a FileInfo object for the specified file path
            FileInfo fileInfo = new FileInfo(filePath);

            // Retrieve the creation date
            return fileInfo.CreationTime;
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
            
            using (WebClient client = new WebClient())
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
