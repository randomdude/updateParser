using System;
using System.Diagnostics;

namespace ConsoleApp1
{
    public class mountedDISMImage : IDisposable
    {
        private readonly string _mountpoint;

        public mountedDISMImage(string wimfilename, int imageIndex, string mountpoint)
        {
            _mountpoint = mountpoint;
            doExec("dism",
                $"/mount-image /quiet /imagefile:{wimfilename} /index:{imageIndex} /mountdir:{mountpoint} /ReadOnly");
        }

        private static string doExec(string cmd, string args, bool ignoreFailure = false)
        {
            ProcessStartInfo psi = new ProcessStartInfo(cmd, args);
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            using (Process dism = Process.Start(psi))
            {
                dism.WaitForExit();
                if (dism.ExitCode != 0 && ignoreFailure == false)
                    throw new Exception($"Failed to run {cmd} {args}");

                return dism.StandardOutput.ReadToEnd();
            }
        }

        public void Dispose()
        {
            // todo: retry on fail
            // right now dism sometimes reports failure but actually does the unmount so idklol
            doExec("Dism", $"/Unmount-wim /quiet /mountdir:{_mountpoint} /discard", true);
        }
    }
}