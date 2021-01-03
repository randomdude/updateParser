using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace ConsoleApp1
{
    public class archiveThread : threadWithLogging
    {
        private readonly ConcurrentQueue<string> queue;

        public archiveThread(Program.logString logger, ConcurrentQueue<string> inputQueue) : base(logger)
        {
            this.threadname = "Archive thread";
            queue = inputQueue;
        }

        protected override void threadmain()
        {
            while (true)
            {
                if (queue.IsEmpty)
                {
                    if (exitTime)
                    {
                        if (queue.IsEmpty)
                            break;
                    }
                    pollTimer.WaitOne(100);
                    continue;
                }

                // Get something to archive
                if (!queue.TryDequeue(out string toArchive))
                    continue;

                // If archival is disabled, just delete the file
                if (Program.archivedir == null)
                {
                    deleteWithRetry(toArchive, logString);
                    continue;
                }

                // Was the file used from the archive dir? If so, just leave it there.
                if (toArchive.StartsWith(Program.archivedir))
                    continue;

                // Otherwise, attempt to move the file.
                string tempFile = Path.Combine(Program.tempdir, Path.GetFileName(toArchive));
                string archiveFile = Path.Combine(Program.archivedir, Path.GetFileName(toArchive));
                while (true)
                {
                    try
                    {
                        // TODO: will this handle read-only updates?
                        File.Move(tempFile, archiveFile);
                        break;
                    }
                    catch (Exception e)
                    {
                        logString(e, $"Failed to move {tempFile} to archive as {archiveFile}");
                        Thread.Sleep(TimeSpan.FromSeconds(3));
                    }
                }
            }
        }

        public static void deleteWithRetry(string toDelete, Program.logString logger)
        {
            if (toDelete == null)
                return;

            if (toDelete.StartsWith("\\\\?\\") == false)
                toDelete = "\\\\?\\" + toDelete;

            int fails = 0;
            while (File.Exists(toDelete) || Directory.Exists(toDelete))
            {
                try
                {
                    if (fails == 0)
                    {
                        if (Directory.Exists(toDelete))
                            Directory.Delete(toDelete, true);
                        if (File.Exists(toDelete))
                            File.Delete(toDelete);
                    }
                    else
                    {
                        // Eh, maybe it failed because something was read-only? Try shelling out.
                        wsusUpdate.runAndGetStdout("cmd", $"/c rd /s /q {toDelete}");
                    }
                }
                catch (Exception)
                {
                    if (fails > 0)
                    {
                        if (logger != null)
                            logger($"Failed to delete {toDelete} (attempt {fails}, will retry");
                    }

                    fails++;
                    Thread.Sleep(100);

                    /*
                    // fuck it, some other thread can handle it
                    Thread deletionThread = new Thread((obj) =>
                    {
                        string filename = (string) obj;
                        while (Directory.Exists(filename))
                        {
                            try
                            {
                                Directory.Delete(filename, true);
                            }
                            catch (Exception)
                            {
                                Thread.Sleep(10000);
                            }
                        }
                    });
                    deletionThread.Name = "deletion retry thread";
                    deletionThread.Start(mountpoint);*/
                }
            }
            if (fails > 0)
            {
                if (logger != null)
                 logger($"Succeeded delete of {toDelete} after {fails} reties");
            }
        }

        public void enqueue(string filename)
        {
            queue.Enqueue(filename);
            pollTimer.Set();
        }
    }
}