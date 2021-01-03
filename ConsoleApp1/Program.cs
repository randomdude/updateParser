using Microsoft.UpdateServices.Administration;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PeNet.Structures.MetaDataTables;

namespace ConsoleApp1
{
    public class Program
    {
        public static string connstr = "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true";
        //public static string connstr = "Server=1.2.3.4;Integrated Security=false;user id=updates;password=hunter2";
        public static string dbName = "updateInfo";
        public static string tempdir = "C:\\temp\\";
        public static string archivedir = null; //"S:\\winupdatedata\\updatefiles";
        
        public const bool biggestfirst = false;
        public const int processingThreadCount = 1;
        public const int downloadThreadCount = 1;
        public const int maxPendingDownloads = 1;
        /*
        public const bool biggestfirst = true;
        public const int processingThreadCount = 1;
        public const int downloadThreadCount = 1;
        public const int maxPendingDownloads = 1;*/

        public delegate void logString(string toLog);

        public static void importWSUSFiles()
        {
            using (updateDB db = new updateDB(connstr, dbName))
            {
                int updateIndexInBatch = 0;
                wsusUpdate[] updates = new wsusUpdate[400];
                foreach (IUpdate update in getUpdates())
                {
                    foreach (IInstallableItem item in update.GetInstallableItems())
                    {
                        foreach (UpdateFile f in item.Files)
                        {
                            if (f.Type == FileType.Express || 
                                f.OriginUri.ToString().EndsWith(".txt"))
                            {
                                continue;
                            }
                            wsusUpdate upd = new wsusUpdate(f);
                            updates[updateIndexInBatch++] = upd;
                            if (updateIndexInBatch == updates.Length)
                            {
                                db.insert_noconcurrency(updates);
                                updateIndexInBatch = 0;
                            }
                        }
                    }
                }

                wsusUpdate[] updatesFinalBatch = new wsusUpdate[updateIndexInBatch];
                Array.Copy(updates, updatesFinalBatch, updateIndexInBatch);
                if (updateIndexInBatch == updates.Length)
                    db.insert_noconcurrency(updatesFinalBatch);

                db.removeDuplicateWsusFiles();

            }
        }

        private static updateDB logDB;
        public static void Main(string[] args)
        {
            using (logDB = new updateDB(connstr, dbName))
            {

                ConcurrentQueue<downloadThread.downloadedUpdate> toProcessQueue =
                    new ConcurrentQueue<downloadThread.downloadedUpdate>();
                ConcurrentQueue<string> toArchiveQueue = new ConcurrentQueue<string>();
                AutoResetEvent downloadRequestEvent = new AutoResetEvent(false);
                AutoResetEvent downloadCompleteEvent = new AutoResetEvent(false);

                using (threadLifetimeCollection threads = new threadLifetimeCollection())
                {
                    asyncsqlthread sqlparams = new asyncsqlthread(logger);
                    threads.add(sqlparams.start());

                    updateReserverThread updateGetterThread = new updateReserverThread(logger);
                    threads.add(updateGetterThread.start());

                    archiveThread archiver = new archiveThread(logger, toArchiveQueue);
                    threads.add(archiver.start());

                    downloadThread[] downloadThreads = new downloadThread[downloadThreadCount];
                    for (int i = 0; i < downloadThreads.Length; i++)
                    {
                        downloadThreads[i] =
                            new downloadThread(logger, downloadRequestEvent, toProcessQueue, updateGetterThread);
                        downloadThreads[i].threadname += $" ({i})";

                        downloadThreads[i].outqueue = toProcessQueue;
                        downloadThreads[i].maxFinishedDownloads = maxPendingDownloads;
                        downloadThreads[i].downloadComplete = downloadCompleteEvent;

                        threads.add(downloadThreads[i].start());
                    }

                    processingThread[] processingThreads = new processingThread[processingThreadCount];
                    for (int i = 0; i < processingThreads.Length; i++)
                    {
                        processingThreads[i] = new processingThread(logger, downloadCompleteEvent, toProcessQueue,
                            downloadRequestEvent, archiver, sqlparams);
                        processingThreads[i].threadname += $" ({i})";

                        threads.add(processingThreads[i].start());
                    }

                    while (true)
                    {
                        // have we finished it all?
                        if (updateGetterThread.allUpdatesExhausted())
                        {
                            Console.WriteLine("all done");
                            // Once the update-getter signals completion, it has already passed pending updates to the download threads, so it's safe to
                            // tear down threads without fear of missing updates.
                            foreach (downloadThread dt in downloadThreads)
                                dt.stop();
                            foreach (processingThread pt in processingThreads)
                                pt.stop();
                            archiver.stop();
                            sqlparams.stop();

                            break;
                        }

                        // OK, show some stats.
                        Thread.Sleep(TimeSpan.FromSeconds(1));
                        //Console.Clear();
                        Console.WriteLine(
                            $"Processing queue: {toProcessQueue.Count}, reserved updates {updateGetterThread.claimedUpdates.Count}, " +
                            $"async sql queue {sqlparams.batches.Count} / {sqlparams.batches_wim.Count} / {sqlparams.toMarkComplete.Count}");

                        for (int i = 0; i < downloadThreads.Length; i++)
                        {
                            var current = downloadThreads[i].currentlydownloading;
                            if (current != null)
                            {
                                Console.WriteLine(
                                    $"Download thread {i}: downloading {current.downloadURI} ({current.sizeMB} MB)");
                            }
                            else
                            {
                                Console.WriteLine($"Download thread {i}: idle");
                            }
                        }

                        for (int i = 0; i < processingThreads.Length; i++)
                        {
                            string cur = processingThreads[i].currentlyProcessing;
                            if (cur != null)
                                Console.WriteLine($"Processing thread {i}: processing {cur}");
                            else
                                Console.WriteLine($"Processing thread {i}: idle");
                        }
                    }
                }
            }
        }

        private static void logger(string tolog)
        {
            logDB.logError(tolog);

        }

        private static UpdateCollection getUpdates()
        {
            Console.WriteLine("Getting updates from WSUS..");
            IUpdateServer server = AdminProxy.GetUpdateServer("localhost", false, 8530);
            UpdateScope scope = new UpdateScope();
            UpdateCategoryCollection categories = server.GetUpdateCategories();
            foreach (IUpdateCategory cat in categories)
            {
                if (cat.Description.Contains("10"))
                {
                    Console.WriteLine(cat.Description);
                    scope.Categories.Add(cat);
                }
            }

            int updateCount = server.GetUpdateCount(scope);
            Console.WriteLine($"Updates found: {updateCount}");

            UpdateCollection updates = server.GetUpdates(scope);
            return updates;
        }
    }
}
