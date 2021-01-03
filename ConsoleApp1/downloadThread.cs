using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.UpdateServices.Administration;

namespace ConsoleApp1
{
    public class downloadThread : threadWithLogging
    {
        public class downloadedUpdate
        {
            public updateInfo update;
            public string absolutepath;
        }

        public ConcurrentQueue<downloadedUpdate> outqueue;
        public wsusUpdate currentlydownloading;
        public int maxFinishedDownloads;
        public AutoResetEvent downloadComplete = new AutoResetEvent(false);
        public updateReserverThread updateReserver;

        /// <summary>
        /// We will set this when we need more updates, but none are present in the input queue.
        /// </summary>
        //public AutoResetEvent moreUpdatesRequiredEvent;

        public downloadThread(Program.logString logger, AutoResetEvent pollEvent, ConcurrentQueue<downloadedUpdate> outqueue, updateReserverThread updateReserver) 
            : base(logger, pollEvent)
        {
            threadname = "Download thread";
            this.outqueue = outqueue;
            this.updateReserver = updateReserver;
        }

        protected override void threadmain()
        {
            WebRequest.DefaultWebProxy = null;
            while (true)
            {
                if (updateReserver.allUpdatesExhausted())
                {
                    if (exitTime)
                        break;
                    pollTimer.WaitOne(100);
                    continue;
                }

                // Keep a maximum of this many files on disk
                if (outqueue.Count > maxFinishedDownloads)
                {
                    pollTimer.WaitOne(100);
                    continue;
                }

                // Get an update to download from the input queue
                if (updateReserver.tryDequeueUpdate(out wsusUpdate toDownload) == false)
                    continue;

                currentlydownloading = toDownload;

                string outputfile = null;

                // If the file already exists in the archive or temp dir, use it from that location.
                string tempfile = Path.Combine(Program.tempdir, toDownload.getTemporaryFilename());
                if (File.Exists(tempfile))
                    outputfile = tempfile;

                if (Program.archivedir != null)
                {
                    string archivefile = Path.Combine(Program.archivedir, toDownload.getTemporaryFilename());
                    if (File.Exists(archivefile))
                        outputfile = archivefile;
                }

                if (File.Exists(outputfile))
                {
                    // The file already exists. Let's check its length and hash, just to be sure it's what we want.
                    if (verifyUpdateFile(outputfile, toDownload) == false)
                    {
                        // It's corrupt, or partially downloaded. Delete it and we'll start again.
                        archiveThread.deleteWithRetry(outputfile, OnLogString);
                        outputfile = null;
                    }
                }

                // Now we should download the file if necessary, into the temporary file location.
                while (outputfile == null)
                {
                    try
                    {
                        using (WebClient client = new WebClient())
                        {
                            client.Proxy = null;
                            client.DownloadFile(toDownload.downloadURI, tempfile);
                            currentlydownloading = null;
                            outputfile = tempfile;
                        }
                    }
                    catch (WebException e)
                    {
                        logString(e, $"Download of {toDownload.downloadURI} to {outputfile} failed");
                        Thread.Sleep(TimeSpan.FromSeconds(10));
                    }
                }

                // Finally, pass the downloaded update off to the processing thread queue.
                try
                {
                    downloadedUpdate newlyDownloaded = new downloadedUpdate();
                    newlyDownloaded.absolutepath = outputfile;
                    newlyDownloaded.update = wsusUpdate.parse(toDownload, outputfile);
                    outqueue.Enqueue(newlyDownloaded);
                    downloadComplete.Set();
                }
                catch (Exception e)
                {
                    logString(e, $"Processing of update from {toDownload.downloadURI} as {outputfile} failed");
                }
            }
        }

        private bool verifyUpdateFile(string filename, wsusUpdate toCheckAgainst)
        {
            using (SHA1 hasher = SHA1.Create())
            {
                using (FileStream stream = File.OpenRead(filename))
                {
                    byte[] hashOnDisk = hasher.ComputeHash(stream);
                    if (hashOnDisk.SequenceEqual(toCheckAgainst.fileHashFromWSUS))
                        return true;
                    return false;
                }
            }
        }
    }
}