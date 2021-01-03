using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ConsoleApp1
{
    public class updateFile
    {
        public readonly string filelocation;
        public readonly string filename;
        public readonly string fileextension;
        public readonly byte[] hash_sha256;
        public string hash_sha256_string;
        public ulong size;
        public int? pe_timedatestamp;
        public int? pe_sizeOfCode;
        public short? pe_magicType;
        public readonly byte[] contents128b;
        public string rsds_GUID;
        public uint rsds_age;
        public string rsds_filename;
        public string authenticode_certfriendly;
        public string authenticode_certsubj;
        public string FileDescription;
        public string FileVersion;
        public string ProductName;
        public string ProductVersion;
        public string Comments;
        public string CompanyName;

        public string locationAndFilename
        {
            get { return Path.Combine(filelocation, filename); }
        }

        private static int hashesDone = 0;
        private static Stopwatch hashTimer = new Stopwatch();
        private static Stopwatch hashTimerCS = new Stopwatch();

        public updateFile(string file, string filenameAndPath)
        {
            this.filelocation = Path.GetDirectoryName(filenameAndPath);
            this.filename = Path.GetFileName(filenameAndPath);
            this.fileextension = Path.GetExtension(filenameAndPath).TrimStart('.');

            using (BufferedStream stream = new BufferedStream(File.OpenRead(file), 1024 * 1024))
            {
                contents128b = new byte[128];
                stream.Read(contents128b, 0, 128);
            }
            /*
            // prime dat cache
            string[] stdout = wsusUpdate.runAndGetStdout("certutil", $"-hashfile \"{file}\" sha256").Split('\n');
            
            hashTimer.Start();
            // SHA256.ComputeHash is being sluggish and I'm running out of time to get the analysis done so I'm gonna shell out to compute the hash.
            // I haven't got time to work out why (if?) SHA256.ComputeHash isn't running as fast as it should right now. hash-tag yo-lo.
            stdout = wsusUpdate.runAndGetStdout("certutil", $"-hashfile \"{file}\" sha256").Split('\n');
            stdout = stdout.Where(x =>
                x.StartsWith("SHA256 hash of") == false && x.StartsWith("CertUtil") == false && x.Length > 0).ToArray();
            hash_sha256_string = stdout.SingleOrDefault().Trim();
            hash_sha256 = new byte[hash_sha256_string.Length / 2];
            for (int i = 0; i < hash_sha256.Length; ++i)
                hash_sha256[i] = (byte) ((GetHexVal(hash_sha256_string[i << 1]) << 4) +
                                         (GetHexVal(hash_sha256_string[(i << 1) + 1])));
            hashTimer.Stop();
            
            hashTimerCS.Start();*/
            //using (BufferedStream stream = new BufferedStream(File.OpenRead(file), 1024 * 1024))
            using (FileStream stream = File.OpenRead(file))
            {
                using (SHA256 hasher = SHA256.Create())
                {
                    hash_sha256 = hasher.ComputeHash(stream);
                }
            }
            hash_sha256_string = String.Join("", hash_sha256.Select(x => x.ToString("x2")));
/*            hashTimerCS.Stop();

            if (hashesDone++ > 1000)
            {
                Console.WriteLine($"Total time spent hashing: shell out {hashTimer.ElapsedMilliseconds}ms, C# {hashTimerCS.ElapsedMilliseconds}");
                Debugger.Break();
            }*/

            size = (ulong)new FileInfo(file).Length;

            if (size < 1024 * 1024 * 1024 && size > 0x10)
            {
                using (FileStream stream = File.OpenRead(file))
                {
                    pestuff.IMAGE_DOS_HEADER mz = readStruct<pestuff.IMAGE_DOS_HEADER>(stream);
                    if (mz.isValid == false)
                        return;
                    stream.Seek(mz.e_lfanew, SeekOrigin.Begin);

                    pestuff.IMAGE_NT_HEADERS_32_or_64 pe = readStruct<pestuff.IMAGE_NT_HEADERS_32_or_64>(stream);
                    if (pe.isValid == false)
                        return;

                    pe_timedatestamp = (int?)pe.FileHeader.TimeDateStamp;
                    pe_sizeOfCode = (int?)pe.OptionalHeader_SizeOfImage;
                    pe_magicType = (short?)pe.OptionalHeader_MagicType;
                }

                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(file);
                FileDescription = versionInfo.FileDescription;
                FileVersion = versionInfo.FileVersion;
                ProductName = versionInfo.ProductName;
                ProductVersion = versionInfo.ProductVersion;
                Comments = versionInfo.Comments;
                CompanyName = versionInfo.CompanyName;

                /*
                try
                {
                    PeNet.PeFile pe = new PeNet.PeFile(file);
                    try
                    {
                        rsds_GUID = pe.ImageDebugDirectory[0].CvInfoPdb70.Signature.ToString("N");
                        rsds_age = pe.ImageDebugDirectory[0].CvInfoPdb70.Age;
                        rsds_filename = pe.ImageDebugDirectory[0].CvInfoPdb70.PdbFileName;
                    }
                    catch (Exception)
                    {
                        // oh well
                    }

                    try
                    {
                        authenticode_certfriendly = pe.Authenticode.SigningCertificate.FriendlyName;
                        authenticode_certsubj = pe.Authenticode.SigningCertificate.SubjectName.Name;
                    }
                    catch (Exception)
                    {
                        // oh well
                    }
                }
                catch (Exception)
                {
                    // oh well
                }*/
            }
        }

        private T readStruct<T>(FileStream stream) where T : new()
        {
            int structSize = Marshal.SizeOf<T>();
            Byte[] structBytes = new byte[structSize];
            if (stream.Read(structBytes, 0, structSize) != structSize)
                return default(T);

            IntPtr mem = Marshal.AllocHGlobal(structSize);
            try
            {
                Marshal.Copy(structBytes, 0, mem, structSize);
                T toRet = new T();
                return (T) Marshal.PtrToStructure(mem, toRet.GetType());
            }
            finally
            {
                Marshal.FreeHGlobal(mem);
            }
        }


        // see https://stackoverflow.com/questions/321370/how-can-i-convert-a-hex-string-to-a-byte-array
        public static int GetHexVal(char hex)
        {
            int val = (int)hex;
            //For uppercase A-F letters:
            //return val - (val < 58 ? 48 : 55);
            //For lowercase a-f letters:
            //return val - (val < 58 ? 48 : 87);
            //Or the two combined, but a bit slower:
            return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
        }
    }
}