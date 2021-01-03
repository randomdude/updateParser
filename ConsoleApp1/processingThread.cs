using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace ConsoleApp1
{
    public class processingThread : threadWithLogging
    {
        public ConcurrentQueue<downloadThread.downloadedUpdate> queue;
        public string currentlyProcessing;

        public asyncsqlthread asyncSQLParams;
        public AutoResetEvent UpdateProcessingStarted;
        private readonly archiveThread archiver;

        public processingThread(Program.logString logger, AutoResetEvent poller,
            ConcurrentQueue<downloadThread.downloadedUpdate> inputQueue, AutoResetEvent downloadRequestEvent,
            archiveThread archiver,
            asyncsqlthread sqlThread) : base(logger, poller)
        {
            this.threadname = "Update processing thread";
            this.queue = inputQueue;
            this.asyncSQLParams = sqlThread;
            this.archiver = archiver;
            this.UpdateProcessingStarted = downloadRequestEvent;
        }

        protected override void threadmain()
        {
            using (updateDB db = new updateDB(Program.connstr, Program.dbName))
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

                    if (!queue.TryDequeue(out downloadThread.downloadedUpdate updateToProcess) || updateToProcess == null)
                        continue;

                    currentlyProcessing = updateToProcess.update.parent.downloadURI;
                    UpdateProcessingStarted.Set();

                    updateToProcess.update.parent.startTime = DateTime.Now;
                    updateToProcess.update.OnLogString = this.OnLogString;
                    try
                    {
                        updateToProcess.update.writeToDB(asyncSQLParams, updateToProcess.absolutepath);
                    }
                    catch (Exception e)
                    {
                        db.logError(updateToProcess.update.parent, e);
                    }

                    archiver.enqueue(updateToProcess.absolutepath);

                    currentlyProcessing = null;
                }
            }
        }
    }
}