using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp1
{
    public class wsusUpdate_wim : updateInfo
    {
//        public wsusUpdate parent { get; set; }

        public wsusUpdate_wim(wsusUpdate parent)
        {
            this.parent = parent;
        }

        public wimImage[] getImages(string absoluteFilename)
        {
            string stdout = wsusUpdate.runAndGetStdout("dism", "/get-wiminfo /wimfile:" + absoluteFilename);
            string[] lines = stdout.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            List<wimImage> images = new List<wimImage>();
            wimImage current = null;
            foreach (string line in lines)
            {
                string lineTrimmed = line.Trim();

                if (lineTrimmed.StartsWith("Index : "))
                {
                    if (current != null)
                        images.Add(current);

                    current = new wimImage();
                    current.index = Int32.Parse(line.Substring(7));
                }
                else if (lineTrimmed.StartsWith("Name : "))
                {
                    current.name = lineTrimmed.Substring(7);
                }
                else if (lineTrimmed.StartsWith("Description : "))
                {
                    current.description = lineTrimmed.Substring(14);
                }
                else if (lineTrimmed.StartsWith("Size : "))
                {
                    // The string will be in the form "123,456,768 bytes".
                    lineTrimmed = lineTrimmed.Substring(6).Replace(",", "");
                    lineTrimmed = lineTrimmed.Trim().Split(' ')[0];
                    current.sizeBytes = UInt64.Parse(lineTrimmed);
                }

            }
            if (current != null)
                images.Add(current);

            return images.ToArray();
            //return new wimImage[]  { images.ToArray()[0] };
        }

        class queuedFileCreation
        {
            public readonly string file;
            public readonly string relativePath;

            public queuedFileCreation(string file, string relativePath)
            {
                this.file = file;
                this.relativePath = relativePath;
            }
        }

        public IEnumerable<updateFile> getFilesForImage(string absoluteFilename, int imageIndex)
        {
            string mountpoint = null;
            string convertedPath = null;

            // If the mountpoint already has something mounted in it, umount it first.
            mountpoint = Path.Combine(Program.tempdir, $"mountpoint_{Path.GetFileName(parent.filename)}_{imageIndex}");
            string wiminfostdout = wsusUpdate.runAndGetStdout("dism", "/get-mountedwiminfo");
            string[] activemountpoints = wiminfostdout.Split('\n')
                .Where(x => x.StartsWith("Mount Dir :"))
                .Select(x => x.Substring("Mount Dir :".Length).Trim())
                .ToArray();
            if (activemountpoints.Count(x => x == mountpoint) > 0)
                wsusUpdate.runAndGetStdout("dism", $"/unmount-wim /mountdir:{mountpoint} /discard");
            deleteWithRetry(mountpoint);
            Directory.CreateDirectory(mountpoint);

            try
            {
                // Before we can mount an .esd, we must convert it to a .wim.
                convertedPath = Path.Combine(Program.tempdir, Path.GetFileName(parent.filename) + "_" + imageIndex + ".wim");
                deleteWithRetry(convertedPath);
                wsusUpdate.runAndGetStdout("dism", $"/export-image /quiet /sourceimagefile:{absoluteFilename} " +
                                                   $"/sourceindex:{imageIndex} /DestinationImageFile:{convertedPath} /Compress:Max /CheckIntegrity");

                // Mount the image and examine the files within.
                ConcurrentQueue<queuedFileCreation> createQueue = new ConcurrentQueue<queuedFileCreation>();
                ConcurrentQueue<updateFile> output = new ConcurrentQueue<updateFile>();
                ConcurrentQueue<Tuple<Exception, string>> errors = new ConcurrentQueue<Tuple<Exception, string>>();
                using (mountedDISMImage img = new mountedDISMImage(convertedPath, 1, mountpoint))
                {
                    AutoResetEvent newInfoQueued = new AutoResetEvent(false);
                    AutoResetEvent newFileCreated = new AutoResetEvent(false);
                    bool exitThreads = false;
                    int threadCount = Environment.ProcessorCount;

                    Thread[] hashingThreads = new Thread[threadCount];
                    for (int n = 0; n < threadCount; n++)
                    {
                        hashingThreads[n] = new Thread((() =>
                        {
                            while (true)
                            {
                                if (createQueue.IsEmpty)
                                {
                                    if (exitThreads)
                                    {
                                        if (createQueue.IsEmpty)
                                        {
                                            break;
                                        }
                                    }
                                }

                                if (createQueue.TryDequeue(out queuedFileCreation toCreate) == false)
                                {
                                    newInfoQueued.WaitOne(10);
                                    continue;
                                }

                                try
                                {
                                    output.Enqueue(new updateFile(toCreate.file, toCreate.relativePath));
                                    newFileCreated.Set();
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    // Some files will throw this. :(
                                    if (toCreate.relativePath.Contains("MpScanCache-0.bin") || toCreate.relativePath.Contains("RtBackup"))
                                        continue;

                                    errors.Enqueue(new Tuple<Exception, string>(null, $"Unable to access file {toCreate.relativePath}, saw an UnauthorizedAccessException"));
                                    continue;
                                }
                                catch (Exception e)
                                {
                                    // Should never happen but probably will.
                                    errors.Enqueue(new Tuple<Exception, string>(e, $"Unable to access file {toCreate.relativePath}"));
                                    continue;
                                }

                            }
                        }));
                        hashingThreads[n].Name = $"File examining thread {n}";
                        hashingThreads[n].Start();
                    }

                    IEnumerable<updateFile> files = enumFilesOnDisk<updateFile>(mountpoint,
                        delegate (string file, string relativePath)
                        {
                            createQueue.Enqueue(new queuedFileCreation(file, relativePath));
                            newInfoQueued.Set();
                            return null;
                        });
                    foreach (updateFile updateFile in files)
                    {
                        // ...
                    }
                    exitThreads = true;

                    while (true)
                    {
                        if (createQueue.IsEmpty)
                        {
                            if (output.IsEmpty && errors.IsEmpty)
                            {
                                if (hashingThreads.All(x => x.IsAlive == false))
                                    break;
                            }
                        }

                        if (errors.IsEmpty == false && errors.TryDequeue(out var error))
                        {
                            if (error.Item1 == null)
                                logString(error.Item2);
                            else 
                                logString(error.Item1, error.Item2);
                        }

                        if (output.IsEmpty == true || output.TryDequeue(out updateFile outputFile) == false)
                        {
                            newFileCreated.WaitOne(10);
                            continue;
                        }
                        yield return outputFile;
                    }

                    /*
                    IEnumerable<updateFile> files = enumFilesOnDisk<updateFile>(mountpoint,
                        delegate (string file, string relativePath)
                        {
                            return new updateFile(file, relativePath);
                        });
                    foreach (updateFile updateFile in files)
                        yield return updateFile;*/
                }
            }
            finally
            {
                deleteWithRetry(mountpoint);
                deleteWithRetry(convertedPath);
            }
        }

        public override IEnumerable<updateFile> GetFiles(string absoluteFilename)
        {
            foreach (wimImage image in getImages(absoluteFilename))
            {
                foreach (updateFile updateFile in getFilesForImage(absoluteFilename, image.index))
                {
                    yield return updateFile;
                }
            }
        }

        public override void writeToDB(asyncsqlthread asyncdb, string absoluteFilename)
        {
            using (bulkDBInsert_file_wiminfo bulkxfer = new bulkDBInsert_file_wiminfo(parent, asyncdb))
            {
                foreach (wimImage image in getImages(absoluteFilename))
                {
                    fileSource_wim imageEntry = new fileSource_wim(image);

                    foreach (updateFile updateFile in getFilesForImage(absoluteFilename, image.index))
                    {
                        bulkxfer.add(new file_wimInfo(parent, updateFile, imageEntry));
                    }
                }
            }
        }
    }
}
