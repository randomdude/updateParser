using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Security.Cryptography.Xml;
using System.Threading;

namespace ConsoleApp1
{
    public abstract class updateInfo
    {
        public wsusUpdate parent { get; set; }

        public abstract IEnumerable<updateFile> GetFiles(string fileDirectory);
        public abstract void writeToDB(asyncsqlthread asyncdb, string absoluteFilename);

        public Program.logString OnLogString { get; set; }

        protected void logString(string toLog)
        {
            OnLogString?.Invoke(toLog);
        }

        protected void logString(Exception e, string toLog)
        {
            OnLogString?.Invoke($"{toLog} : {e.ToString()}");
        }

        protected IEnumerable<T> enumFilesOnDisk<T>(string dir, Func<string, string, T> handler)
        {
            if (dir.StartsWith("\\\\?\\") == false)
                dir = "\\\\?\\" + dir;

            return enumFilesOnDisk<T>(dir, dir, handler);
        }

        private IEnumerable<T> enumFilesOnDisk<T>(string rootdir, string dir, Func<string, string, T> handler)
        {
            // First, enum files. I'm not sure if this can fail if one file is inaccessible.
            string[] filesInDir = null;
            try
            {
                filesInDir = Directory.EnumerateFiles(dir).ToArray();
            }
            catch (Exception e)
            {
                logString(e, $"Unable to enum files in dir {dir}, skipping");
            }

            if (filesInDir != null)
            {
                foreach (string filename in filesInDir)
                {
                    string relativePath = filename.Substring(rootdir.Length + 1);

                    T f;
                    try
                    {
                        f = handler(filename, relativePath);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Some files will throw this. :(
                        if (relativePath.Contains("MpScanCache-0.bin") || relativePath.Contains("RtBackup"))
                            continue;

                        logString($"Unable to access file {relativePath}, saw an UnauthorizedAccessException");
                        continue;
                    }
                    catch (Exception e)
                    {
                        // Should never happen but probably will.
                        logString(e, $"Unable to access file {relativePath}");
                        continue;
                    }

                    yield return f;
                }
            }

            // Now, get a list of directories, being careful to catch exceptions that might happen due to acls on the target or suchlike.
            string[] directories = null;
            try
            {
                directories = Directory.GetDirectories(dir).ToArray();
                // Skip weird stuff which might point outside the update itself.
                directories = directories.Where(x => (File.GetAttributes(x) & FileAttributes.ReparsePoint) == 0)
                    .ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                // Some directories will always give access denied, since they have an ACL set that applies when the WIM is mounted.
                logString($"Skipping location {dir}, saw an UnauthorizedAccessException");
            }
            catch (Exception e)
            {
                // This is unexpected, but lets log it and try to recover anyway.
                logString(e, $"Failed to enumerate directory {dir}");
            }

            // Next, recurse into these directories present.
            if (directories != null)
            {
                foreach (string directory in directories)
                {
                    foreach (T file in enumFilesOnDisk(rootdir, directory, handler))
                        yield return file;
                }
            }
        }

        protected void deleteWithRetry(string toDelete)
        {
            archiveThread.deleteWithRetry(toDelete, OnLogString);
        }
    }
}