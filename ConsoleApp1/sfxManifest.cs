using System;
using System.Collections.Generic;
using System.IO;

namespace ConsoleApp1
{
    public class sfxManifest
    {
        public List<fileDelta> deltas = new List<fileDelta>();

        // Manifests aren't quite ini files, so things like Ini-Parser will choke if we try to read them. Because of that, we need to
        // write code to read them ourselves.

        public sfxManifest(string filename)
        {
            bool inDeltaSection = false;
            foreach (string line in File.ReadAllLines(filename))
            {
                string lineTrimmed = line.Trim();
                if (lineTrimmed.ToLower() == "[deltas]")
                {
                    inDeltaSection = true;
                    continue;
                }
                if (!inDeltaSection)
                    continue;

                if (lineTrimmed == "")
                    continue;

                if (lineTrimmed[0] == '[')
                    break;

                lineTrimmed = lineTrimmed.Replace("\"", "");
                string[] elements = lineTrimmed.Split(new[] { '=', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (elements.Length != 3)
                    throw new Exception($"Can't parse delta line '{lineTrimmed}'");

                deltas.Add(new fileDelta() { dest = elements[0].Trim(), patch = elements[1].Trim(), source = elements[2].Trim() });
            }

        }
    }
}